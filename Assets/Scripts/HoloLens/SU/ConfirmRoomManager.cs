using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Confirm Room 단계에서 방 닫힘 검증, 열린 gap marker 표시, 최종 ConfirmedRoomRoot 시각화를 담당합니다.
/// WorkflowManager는 상태 전환만 관리하고, ConfirmRoomManager가 자동 벽 + 수동 벽 segment를 기반으로 검증/생성을 수행합니다.
/// </summary>
public class ConfirmRoomManager : MonoBehaviour
{
    private struct Segment2D
    {
        public Vector2 start;
        public Vector2 end;
    }

    private struct WallFootprint2D
    {
        public Vector2 center;
        public Vector2 direction;
        public Vector2 normal;
        public float halfLength;
        public float halfThickness;
    }

    private struct EndpointRef
    {
        public int segmentIndex;
        public bool isStart;
        public Vector2 point;
    }

    [Header("References")]
    public SceneUnderstandingRoomScanner scanner;
    public ManualWallBuilder manualWallBuilder;

    [Tooltip("최종 확정 방 오브젝트들이 생성될 Root입니다. 보통 RoomBuildWorkflowManager의 ConfirmedRoomRoot를 연결합니다.")]
    public Transform confirmedRoomRoot;

    [Header("Furniture Placement")]
    [Tooltip("확정된 벽/바닥/천장 정보를 가구 배치/이동 검증 시스템에 전달합니다.")]
    public ConfirmedRoomGeometryProvider confirmedRoomGeometryProvider;

    [Tooltip("방 확정 성공 시 ConfirmedRoomGeometryProvider를 자동 초기화합니다.")]
    public bool bindFurniturePlacementGeometryOnConfirm = true;

    [Header("Boundary Sources")]
    public bool includeDetectedWallsInClosedCheck = true;
    public bool includeClosureEdgesInClosedCheck = true;
    public bool includeManualWallsInClosedCheck = true;

    [Header("Boundary Segment Cleanup")]
    public bool normalizeBoundarySegmentsBeforeConfirm = true;
    public float endpointSnapDistance = 0.12f;
    public float collinearMergeAngle = 5.0f;
    public float collinearMergeDistance = 0.10f;
    public float boundaryMinSegmentLength = 0.05f;

    [Header("Corner Gap Tolerance")]
    [Tooltip("벽 endpoint가 다른 벽 segment의 중간 근처에 닿아 있는데 endpoint끼리는 맞지 않는 T-junction/코너 오차를 보정합니다.")]
    public bool snapEndpointsToNearbySegments = true;

    [Tooltip("endpoint를 주변 segment 위로 붙일 최대 거리입니다. 자동 인식 벽 모서리가 살짝 어긋나는 경우 이 값 안에서 같은 코너로 봅니다.")]
    public float endpointToSegmentSnapDistance = 0.18f;

    [Tooltip("정규화 후에도 남은 열린 endpoint끼리 거리가 가까우면 검증용 짧은 bridge segment를 추가해 코너 gap을 닫습니다.")]
    public bool bridgeSmallOpenEndpointGaps = true;

    [Tooltip("열린 endpoint 사이를 자동 bridge로 닫을 최대 거리입니다. 문/통로까지 닫지 않도록 너무 크게 두지 않는 것을 권장합니다.")]
    public float openEndpointBridgeDistance = 0.24f;

    [Tooltip("endpoint-to-segment 보정 시 segment를 나누지 않을 endpoint 근처 t 여유값입니다.")]
    public float endpointProjectionSplitEpsilon = 0.03f;

    [Header("User Room Flood Fill Validation")]
    public float gridCellSize = 0.12f;
    public float gridBoundsMargin = 0.75f;
    public float wallBlockDistance = 0.08f;
    public int maxGridCellsPerAxis = 220;
    public int nearestFreeCellSearchRadius = 8;

    [Header("Cube Footprint Flood Fill Validation")]
    [Tooltip("벽을 얇은 선분이 아니라 Cube의 XZ footprint 사각형 장애물로 보고 flood fill을 수행합니다. 모서리 endpoint가 완전히 붙지 않아도 실제 벽 두께/끝단 패딩으로 막히면 닫힌 공간으로 판단합니다.")]
    public bool useCubeFootprintFloodFill = true;

    [Tooltip("닫힘 검증에 사용할 최소 벽 footprint 두께입니다. 실제 ConfirmedRoom 벽 두께, ManualWallBuilder.wallThickness, wallBlockDistance*2보다 작은 경우 자동으로 더 큰 값을 사용합니다.")]
    public float cubeFootprintWallThickness = 0.16f;

    [Tooltip("벽 segment 양 끝을 이 거리만큼 길이 방향으로 확장해서 Cube 끝단이 코너에서 살짝 맞물리는 것으로 판단합니다. 자동 인식 벽끼리 코너가 조금 벌어지는 문제를 완화합니다.")]
    public float cubeFootprintEndPadding = 0.10f;

    [Tooltip("grid cell center와 이동 선분을 footprint와 비교할 때 사각형을 추가로 확장하는 여유값입니다.")]
    public float cubeFootprintCollisionPadding = 0.01f;

    [Tooltip("Cube footprint 검증을 사용할 때도 open endpoint가 하나라도 있으면 실패 처리합니다. 기본값 false이면 open endpoint는 gap marker/debug 용도로만 사용합니다.")]
    public bool openEndpointsBlockCubeFootprintValidation = false;

    [Tooltip("Cube footprint 검증 중에도 작은 open endpoint gap을 bridge segment로 자동 추가합니다. 기본값 false이면 실제 segment에는 인공 bridge를 추가하지 않고, footprint 두께/끝단 패딩으로만 닫힘을 판단합니다.")]
    public bool useAutoBridgeSegmentsForCubeFootprintValidation = false;


    [Header("Confirmed Room Visualization")]
    public Material confirmedRoomWallMaterial;
    public Material confirmedRoomHorizontalMaterial;
    public float confirmedWallThickness = 0.045f;
    public float confirmedFloorYOffset = 0.025f;
    public float confirmedCeilingYOffset = -0.025f;
    public bool useConfirmedHorizontalBoxSlabs = true;
    public float confirmedHorizontalSlabThickness = 0.06f;
    public float confirmedHorizontalSlabPadding = 0.25f;

    [Header("Confirm Room Review")]
    public bool roomReady;
    public string validationStatus;
    public bool finalRoomClosed;
    public bool floodReachedOutside;
    public int selectedCellCount;
    public int confirmedBoundarySegmentCount;
    public int manualWallCount;
    public int openEndpointCount = -1;

    [Header("Debug - Segment Source Counts")]
    [Tooltip("마지막 Confirm Room 검증/미리보기에서 사용한 detected wall segment 개수입니다.")]
    public int lastDetectedWallSourceCount;

    [Tooltip("마지막 Confirm Room 검증/미리보기에서 사용한 closure edge segment 개수입니다.")]
    public int lastClosureEdgeSourceCount;

    [Tooltip("마지막 Confirm Room 검증/미리보기에서 사용한 직접 생성 Manual Wall segment 개수입니다. manualWallRoot가 숨겨져 있어도 포함됩니다.")]
    public int lastManualWallSourceCount;

    [Tooltip("마지막 Confirm Room 미리보기로 생성한 wall cube 개수입니다.")]
    public int lastReviewWallSegmentCount;

    [Tooltip("마지막 정규화에서 endpoint를 주변 segment 위로 붙인 횟수입니다.")]
    public int lastEndpointToSegmentSnapCount;

    [Tooltip("마지막 정규화에서 열린 코너 gap을 닫기 위해 자동 추가한 bridge segment 개수입니다.")]
    public int lastAutoBridgeSegmentCount;

    [Tooltip("마지막 Cube footprint flood fill에 사용한 벽 footprint 개수입니다.")]
    public int lastCubeFootprintCount;

    [Tooltip("마지막 Cube footprint flood fill에서 벽 footprint에 막힌 cell 판정 횟수입니다. Debug 확인용입니다.")]
    public int lastCubeBlockedCellCheckCount;

    [Tooltip("마지막 Cube footprint flood fill에서 이동 경로가 벽 footprint를 가로질러 차단된 횟수입니다. Debug 확인용입니다.")]
    public int lastCubeBlockedMovementCheckCount;

    [Header("Gap Markers")]
    public bool showGapMarkers = true;
    public Transform gapMarkerRoot;
    public Material gapMarkerMaterial;
    public float gapMarkerSize = 0.12f;
    public float gapMarkerYOffset = 0.12f;

    private readonly HashSet<Vector2Int> selectedCells = new HashSet<Vector2Int>();
    private readonly List<Segment2D> confirmedBoundarySegments = new List<Segment2D>();
    private readonly List<Segment2D> normalizedBoundarySegmentsForConfirm = new List<Segment2D>();
    private readonly List<GameObject> confirmedRoomWallObjects = new List<GameObject>();

    private Vector2 gridMin;
    private int gridWidth;
    private int gridHeight;

    private Material runtimeWallMaterial;
    private Material runtimeHorizontalMaterial;
    private Material runtimeGapMarkerMaterial;
    private bool warnedRuntimeMaterialShaderMissing;

    private GameObject confirmedRoomFloorObject;
    private GameObject confirmedRoomCeilingObject;

    public GameObject ConfirmedRoomFloorObject => confirmedRoomFloorObject;
    public GameObject ConfirmedRoomCeilingObject => confirmedRoomCeilingObject;
    public IReadOnlyList<GameObject> ConfirmedRoomWallObjects => confirmedRoomWallObjects;

    public void BindFromWorkflow(RoomBuildWorkflowManager workflow)
    {
        if (workflow == null)
        {
            return;
        }

        // WorkflowManager는 상태 전환만 담당합니다.
        // Confirm Room 검증/시각화 옵션은 ConfirmRoomManager Inspector에서 직접 관리합니다.
        scanner = workflow.scanner;
        manualWallBuilder = workflow.manualWallBuilder;

        EnsureRoots(workflow.transform);
    }

    public void EnsureRoots(Transform fallbackParent = null)
    {
        if (confirmedRoomRoot == null)
        {
            GameObject rootObject = new GameObject("ConfirmedRoomRoot");
            if (fallbackParent != null)
            {
                rootObject.transform.SetParent(fallbackParent, false);
            }
            confirmedRoomRoot = rootObject.transform;
        }

        if (gapMarkerRoot == null)
        {
            GameObject markerRootObject = new GameObject("ConfirmRoomGapMarkers");
            if (fallbackParent != null)
            {
                markerRootObject.transform.SetParent(fallbackParent, false);
            }
            gapMarkerRoot = markerRootObject.transform;
        }
    }

    public bool ValidateRoom()
    {
        ResetValidationData(clearMarkers: true);

        if (scanner == null)
        {
            validationStatus = "Confirm Room blocked. Scanner is null.";
            return false;
        }

        if (scanner.currentRoom == null)
        {
            validationStatus = "Confirm Room blocked. Current room is null.";
            return false;
        }

        if (manualWallBuilder != null && !manualWallBuilder.CanConfirmManualWallWorkflow(out string builderReason))
        {
            validationStatus = builderReason;
            return false;
        }

        List<Segment2D> allSegments = BuildFinalBoundarySegments();

        if (normalizeBoundarySegmentsBeforeConfirm)
        {
            allSegments = NormalizeBoundarySegments(allSegments);
        }

        normalizedBoundarySegmentsForConfirm.AddRange(allSegments);
        manualWallCount = CountManualWalls();

        List<Vector2> openEndpoints = FindOpenEndpointPoints(allSegments);
        openEndpointCount = openEndpoints.Count;
        CreateGapMarkers(openEndpoints);

        if (allSegments.Count < 3)
        {
            floodReachedOutside = true;
            validationStatus = "Confirm Room blocked. Boundary segment count is less than 3.";
            return false;
        }

        // 선분 기반 검증에서는 open endpoint가 곧 실패 조건입니다.
        // Cube footprint 기반 검증에서는 실제 벽 Cube의 두께/끝단 패딩으로 막혀 있으면 닫힌 방으로 인정하므로,
        // open endpoint는 기본적으로 gap marker/debug 용도로만 사용합니다.
        if (openEndpointCount > 0 && (!useCubeFootprintFloodFill || openEndpointsBlockCubeFootprintValidation))
        {
            floodReachedOutside = true;
            validationStatus = $"Confirm Room blocked. Open endpoints:{openEndpointCount}. Close the highlighted gaps first.";
            return false;
        }

        bool floodFillSucceeded = useCubeFootprintFloodFill
            ? TryRunCubeFootprintFloodFill(allSegments)
            : TryRunUserRoomFloodFill(allSegments);

        if (!floodFillSucceeded)
        {
            finalRoomClosed = false;
            validationStatus = "Confirm Room blocked. User position is not inside a valid room area.";
            return false;
        }

        selectedCellCount = selectedCells.Count;
        finalRoomClosed = !floodReachedOutside;

        if (!finalRoomClosed)
        {
            validationStatus = useCubeFootprintFloodFill
                ? "Confirm Room blocked. Cube footprint flood fill escaped outside boundary. Room is not closed."
                : "Confirm Room blocked. Flood fill escaped outside boundary. Room is not closed.";
            return false;
        }

        if (useCubeFootprintFloodFill)
        {
            // Cube footprint 방식에서는 endpoint graph가 아니라 벽 Cube footprint가 닫힘 판정의 기준입니다.
            // 따라서 선택 cell 인접 선분만 다시 추리는 기존 line 기반 boundary 추출 대신,
            // 검증에 사용한 normalized segment 전체를 최종 벽 후보로 유지합니다.
            confirmedBoundarySegments.Clear();
            confirmedBoundarySegments.AddRange(allSegments);
        }
        else
        {
            BuildConfirmedBoundarySegments(allSegments);
        }

        confirmedBoundarySegmentCount = confirmedBoundarySegments.Count;

        if (confirmedBoundarySegmentCount < 3)
        {
            finalRoomClosed = false;
            validationStatus = "Confirm Room blocked. Confirmed boundary segment count is less than 3.";
            return false;
        }

        roomReady = true;
        string algorithm = useCubeFootprintFloodFill ? "cube footprint" : "line segment";
        validationStatus =
            $"Confirm Room ready ({algorithm}). cells:{selectedCellCount}, boundary:{confirmedBoundarySegmentCount}, " +
            $"manual walls:{manualWallCount}, open endpoints:{openEndpointCount}, " +
            $"footprints:{lastCubeFootprintCount}, corner snaps:{lastEndpointToSegmentSnapCount}, bridges:{lastAutoBridgeSegmentCount}";
        return true;
    }


    /// <summary>
    /// Confirm Room 검토 단계에서 사용자가 보게 될 방 미리보기입니다.
    /// 자동 생성 시각화 오브젝트가 아니라, 현재 scanner.currentRoom에 반영된 detected/closure segment와
    /// ManualWallBuilder의 manual wall segment snapshot을 합친 결과를 ConfirmedRoomRoot 아래에 다시 생성합니다.
    /// 따라서 ManualWall 단계에서 잘라낸 자동 벽 수정과 새로 만든 수동 벽이 모두 반영됩니다.
    /// </summary>
    public bool BuildReviewRoomVisualization()
    {
        ClearConfirmedRoomObjectsOnly();

        if (scanner == null || scanner.currentRoom == null)
        {
            return false;
        }

        EnsureRoots(transform);

        float floorY = scanner.currentRoom.floorY + confirmedFloorYOffset;
        float ceilingY = scanner.currentRoom.ceilingY + confirmedCeilingYOffset;

        if (float.IsNaN(floorY) || float.IsNaN(ceilingY) || ceilingY <= floorY)
        {
            return false;
        }

        List<Segment2D> wallSegments = BuildReviewWallSegmentsForVisualization();
        lastReviewWallSegmentCount = wallSegments.Count;

        if (wallSegments.Count == 0)
        {
            return false;
        }

        BuildConfirmedHorizontalBoxSlabsFromSegments(wallSegments, floorY, ceilingY);
        BuildConfirmedWallCubesFromSegments(wallSegments, floorY, ceilingY);

        Debug.Log(
            "[ConfirmRoom] Review visualization built. " +
            $"detected:{lastDetectedWallSourceCount}, closure:{lastClosureEdgeSourceCount}, " +
            $"manual:{lastManualWallSourceCount}, snap:{lastEndpointToSegmentSnapCount}, " +
            $"bridge:{lastAutoBridgeSegmentCount}, preview walls:{confirmedRoomWallObjects.Count}"
        );

        return confirmedRoomWallObjects.Count > 0;
    }

    public bool BuildConfirmedRoomVisualization()
    {
        ClearConfirmedRoomObjectsOnly();

        if (!roomReady)
        {
            validationStatus = string.IsNullOrEmpty(validationStatus)
                ? "Confirm Room blocked. Room validation is not ready."
                : validationStatus;
            return false;
        }

        if (scanner == null || scanner.currentRoom == null)
        {
            validationStatus = "Confirm Room blocked. Current room is null.";
            return false;
        }

        EnsureRoots(transform);

        float floorY = scanner.currentRoom.floorY + confirmedFloorYOffset;
        float ceilingY = scanner.currentRoom.ceilingY + confirmedCeilingYOffset;

        if (float.IsNaN(floorY) || float.IsNaN(ceilingY))
        {
            validationStatus = "Confirm Room blocked. Floor or ceiling height is invalid.";
            return false;
        }

        if (ceilingY <= floorY)
        {
            validationStatus = "Confirm Room blocked. Ceiling height is lower than floor height.";
            return false;
        }

        List<Segment2D> wallSegments = BuildConfirmedWallSegmentsForVisualization();

        if (wallSegments.Count < 3)
        {
            validationStatus = "Confirm Room blocked. Not enough wall segments to visualize.";
            return false;
        }

        // 현재 플로우에서는 바닥/천장을 polygon이 아니라 box slab으로 생성하는 것이 더 안정적입니다.
        BuildConfirmedHorizontalBoxSlabsFromSegments(wallSegments, floorY, ceilingY);
        BuildConfirmedWallCubesFromSegments(wallSegments, floorY, ceilingY);

        validationStatus = $"Confirmed room visualization built. walls:{confirmedRoomWallObjects.Count}";

        bool success =
            confirmedRoomFloorObject != null &&
            confirmedRoomCeilingObject != null &&
            confirmedRoomWallObjects.Count > 0;

        if (success && bindFurniturePlacementGeometryOnConfirm)
        {
            BindConfirmedRoomToFurniturePlacement();
        }

        validationStatus = $"Confirmed room visualization built. walls:{confirmedRoomWallObjects.Count}";
        return success;
    }

    private void BindConfirmedRoomToFurniturePlacement()
    {
        if (confirmedRoomGeometryProvider == null)
        {
            Debug.LogWarning("[ConfirmRoomManager] ConfirmedRoomGeometryProvider is not assigned.");
            return;
        }

        bool bound = confirmedRoomGeometryProvider.SetConfirmedRoom(
            confirmedRoomWallObjects,
            confirmedRoomFloorObject,
            confirmedRoomCeilingObject
        );

        Debug.Log(
            $"[ConfirmRoomManager] Furniture placement geometry bind result:{bound}, " +
            $"walls:{confirmedRoomWallObjects.Count}, " +
            $"floor:{confirmedRoomFloorObject != null}, " +
            $"ceiling:{confirmedRoomCeilingObject != null}"
        );
    }

    public void ClearConfirmedRoomObjectsOnly()
    {
        confirmedRoomFloorObject = null;
        confirmedRoomCeilingObject = null;
        confirmedRoomWallObjects.Clear();

        if (confirmedRoomRoot == null)
        {
            return;
        }

        for (int i = confirmedRoomRoot.childCount - 1; i >= 0; i--)
        {
            Destroy(confirmedRoomRoot.GetChild(i).gameObject);
        }
    }

    public void ClearConfirmedRoomVisualization()
    {
        ClearConfirmedRoomObjectsOnly();
        ClearGapMarkers();
        confirmedBoundarySegments.Clear();
        normalizedBoundarySegmentsForConfirm.Clear();
        selectedCells.Clear();
        roomReady = false;
        finalRoomClosed = false;
        floodReachedOutside = false;
        selectedCellCount = 0;
        confirmedBoundarySegmentCount = 0;
        openEndpointCount = -1;
        validationStatus = string.Empty;
    }

    public void ResetReviewState()
    {
        roomReady = false;
        validationStatus = string.Empty;
        finalRoomClosed = false;
        floodReachedOutside = false;
        selectedCellCount = 0;
        confirmedBoundarySegmentCount = 0;
        manualWallCount = 0;
        openEndpointCount = -1;
        lastDetectedWallSourceCount = 0;
        lastClosureEdgeSourceCount = 0;
        lastManualWallSourceCount = 0;
        lastReviewWallSegmentCount = 0;
        lastEndpointToSegmentSnapCount = 0;
        lastAutoBridgeSegmentCount = 0;
        lastCubeFootprintCount = 0;
        lastCubeBlockedCellCheckCount = 0;
        lastCubeBlockedMovementCheckCount = 0;
        ClearGapMarkers();
    }

    private void ResetValidationData(bool clearMarkers)
    {
        roomReady = false;
        validationStatus = "Confirm Room validation has not run.";
        finalRoomClosed = false;
        floodReachedOutside = false;
        selectedCellCount = 0;
        confirmedBoundarySegmentCount = 0;
        manualWallCount = 0;
        openEndpointCount = -1;
        lastDetectedWallSourceCount = 0;
        lastClosureEdgeSourceCount = 0;
        lastManualWallSourceCount = 0;
        lastReviewWallSegmentCount = 0;
        lastEndpointToSegmentSnapCount = 0;
        lastAutoBridgeSegmentCount = 0;
        lastCubeFootprintCount = 0;
        lastCubeBlockedCellCheckCount = 0;
        lastCubeBlockedMovementCheckCount = 0;
        selectedCells.Clear();
        confirmedBoundarySegments.Clear();
        normalizedBoundarySegmentsForConfirm.Clear();

        if (clearMarkers)
        {
            ClearGapMarkers();
        }
    }

    private List<Segment2D> BuildFinalBoundarySegments()
    {
        List<Segment2D> result = new List<Segment2D>();

        lastDetectedWallSourceCount = 0;
        lastClosureEdgeSourceCount = 0;
        lastManualWallSourceCount = 0;

        if (scanner != null && scanner.currentRoom != null)
        {
            if (includeDetectedWallsInClosedCheck && scanner.currentRoom.detectedWalls != null)
            {
                foreach (WallSegment wall in scanner.currentRoom.detectedWalls)
                {
                    if (Vector2.Distance(wall.start, wall.end) < boundaryMinSegmentLength)
                    {
                        continue;
                    }

                    result.Add(new Segment2D { start = wall.start, end = wall.end });
                    lastDetectedWallSourceCount++;
                }
            }

            if (includeClosureEdgesInClosedCheck && scanner.currentRoom.closureEdges != null)
            {
                foreach (WallSegment edge in scanner.currentRoom.closureEdges)
                {
                    if (Vector2.Distance(edge.start, edge.end) < boundaryMinSegmentLength)
                    {
                        continue;
                    }

                    result.Add(new Segment2D { start = edge.start, end = edge.end });
                    lastClosureEdgeSourceCount++;
                }
            }
        }

        if (includeManualWallsInClosedCheck)
        {
            AddManualWallSegments(result);
        }

        return result;
    }

    private void AddManualWallSegments(List<Segment2D> target)
    {
        if (manualWallBuilder == null)
        {
            return;
        }

        // Confirm Room 단계에서는 manualWallRoot를 숨긴 뒤 검증/미리보기를 만들 수 있습니다.
        // 따라서 activeInHierarchy가 false인 직접 생성 ManualWall도 반드시 포함합니다.
        List<ManualWallBuilder.ManualWallSegmentSnapshot> snapshots =
            manualWallBuilder.GetManualWallSegmentsSnapshot(includeInactiveObjects: true);

        foreach (ManualWallBuilder.ManualWallSegmentSnapshot snapshot in snapshots)
        {
            if (Vector2.Distance(snapshot.start, snapshot.end) < boundaryMinSegmentLength)
            {
                continue;
            }

            target.Add(new Segment2D { start = snapshot.start, end = snapshot.end });
            lastManualWallSourceCount++;
        }
    }

    private int CountManualWalls()
    {
        if (manualWallBuilder == null)
        {
            return 0;
        }

        return manualWallBuilder.GetManualWallSegmentsSnapshot(includeInactiveObjects: true).Count;
    }

    private List<Segment2D> NormalizeBoundarySegments(List<Segment2D> source)
    {
        lastEndpointToSegmentSnapCount = 0;
        lastAutoBridgeSegmentCount = 0;

        List<Segment2D> cleaned = new List<Segment2D>();

        foreach (Segment2D segment in source)
        {
            if (Vector2.Distance(segment.start, segment.end) >= boundaryMinSegmentLength)
            {
                cleaned.Add(segment);
            }
        }

        // 1. 실제로 교차하는 선분은 교차점에서 나눕니다.
        cleaned = SplitSegmentsAtIntersections(cleaned);

        // 2. 자동 인식 벽에서 자주 생기는 "endpoint가 다른 벽 중간 근처에 닿아 있는" 코너 오차를 보정합니다.
        //    이 단계가 없으면 벽은 시각적으로 닫혀 보여도 endpoint graph에서는 열린 endpoint로 남습니다.
        if (snapEndpointsToNearbySegments)
        {
            cleaned = SnapEndpointsToNearbySegmentsAndSplit(cleaned, out lastEndpointToSegmentSnapCount);
        }

        // 3. endpoint끼리 가까운 점은 같은 코너로 합칩니다.
        SnapCloseEndpoints(cleaned);
        RemoveShortSegments(cleaned);

        // 4. 같은 선 위에서 닿거나 겹친 segment를 합칩니다.
        MergeCollinearTouchingSegments(cleaned);
        RemoveShortSegments(cleaned);

        // 5. 그래도 남은 작은 코너 gap은 검증/미리보기용 bridge segment로 닫습니다.
        //    문/통로를 닫지 않도록 openEndpointBridgeDistance는 endpointSnapDistance보다 약간 큰 정도로만 둡니다.
        if (bridgeSmallOpenEndpointGaps && (!useCubeFootprintFloodFill || useAutoBridgeSegmentsForCubeFootprintValidation))
        {
            AddBridgeSegmentsForNearbyOpenEndpoints(cleaned, out lastAutoBridgeSegmentCount);

            if (lastAutoBridgeSegmentCount > 0)
            {
                cleaned = SplitSegmentsAtIntersections(cleaned);
                SnapCloseEndpoints(cleaned);
                RemoveShortSegments(cleaned);
                MergeCollinearTouchingSegments(cleaned);
                RemoveShortSegments(cleaned);
            }
        }

        return cleaned;
    }

    private List<Segment2D> SplitSegmentsAtIntersections(List<Segment2D> source)
    {
        List<List<float>> splitTs = new List<List<float>>();

        for (int i = 0; i < source.Count; i++)
        {
            splitTs.Add(new List<float> { 0.0f, 1.0f });
        }

        for (int i = 0; i < source.Count; i++)
        {
            Segment2D a = source[i];

            for (int j = i + 1; j < source.Count; j++)
            {
                Segment2D b = source[j];

                if (TryGetSegmentIntersectionParameters(a.start, a.end, b.start, b.end, out float ta, out float tb))
                {
                    if (ta > 0.001f && ta < 0.999f)
                    {
                        splitTs[i].Add(ta);
                    }

                    if (tb > 0.001f && tb < 0.999f)
                    {
                        splitTs[j].Add(tb);
                    }
                }
            }
        }

        List<Segment2D> result = new List<Segment2D>();

        for (int i = 0; i < source.Count; i++)
        {
            List<float> ts = splitTs[i];
            ts.Sort();

            for (int j = 0; j < ts.Count - 1; j++)
            {
                float t0 = ts[j];
                float t1 = ts[j + 1];

                if (t1 - t0 < 0.001f)
                {
                    continue;
                }

                Vector2 start = Vector2.Lerp(source[i].start, source[i].end, t0);
                Vector2 end = Vector2.Lerp(source[i].start, source[i].end, t1);

                if (Vector2.Distance(start, end) >= boundaryMinSegmentLength)
                {
                    result.Add(new Segment2D { start = start, end = end });
                }
            }
        }

        return result;
    }

    private void SnapCloseEndpoints(List<Segment2D> segments)
    {
        if (segments == null || segments.Count == 0)
        {
            return;
        }

        List<EndpointRef> endpoints = new List<EndpointRef>();

        for (int i = 0; i < segments.Count; i++)
        {
            endpoints.Add(new EndpointRef { segmentIndex = i, isStart = true, point = segments[i].start });
            endpoints.Add(new EndpointRef { segmentIndex = i, isStart = false, point = segments[i].end });
        }

        int endpointCount = endpoints.Count;
        int[] parent = new int[endpointCount];

        for (int i = 0; i < endpointCount; i++)
        {
            parent[i] = i;
        }

        float snapDistance = Mathf.Max(0.001f, endpointSnapDistance);

        for (int i = 0; i < endpointCount; i++)
        {
            for (int j = i + 1; j < endpointCount; j++)
            {
                if (Vector2.Distance(endpoints[i].point, endpoints[j].point) <= snapDistance)
                {
                    Union(parent, i, j);
                }
            }
        }

        Dictionary<int, List<int>> clusters = new Dictionary<int, List<int>>();

        for (int i = 0; i < endpointCount; i++)
        {
            int root = Find(parent, i);
            if (!clusters.TryGetValue(root, out List<int> cluster))
            {
                cluster = new List<int>();
                clusters.Add(root, cluster);
            }
            cluster.Add(i);
        }

        foreach (KeyValuePair<int, List<int>> pair in clusters)
        {
            if (pair.Value.Count <= 1)
            {
                continue;
            }

            Vector2 sum = Vector2.zero;
            foreach (int endpointIndex in pair.Value)
            {
                sum += endpoints[endpointIndex].point;
            }

            Vector2 snappedPoint = sum / pair.Value.Count;

            foreach (int endpointIndex in pair.Value)
            {
                EndpointRef endpoint = endpoints[endpointIndex];
                Segment2D segment = segments[endpoint.segmentIndex];

                if (endpoint.isStart)
                {
                    segment.start = snappedPoint;
                }
                else
                {
                    segment.end = snappedPoint;
                }

                segments[endpoint.segmentIndex] = segment;
            }
        }
    }

    private void RemoveShortSegments(List<Segment2D> segments)
    {
        for (int i = segments.Count - 1; i >= 0; i--)
        {
            if (Vector2.Distance(segments[i].start, segments[i].end) < boundaryMinSegmentLength)
            {
                segments.RemoveAt(i);
            }
        }
    }

    private void MergeCollinearTouchingSegments(List<Segment2D> segments)
    {
        bool changed = true;
        int guard = 0;

        while (changed && guard < 64)
        {
            changed = false;
            guard++;

            for (int i = 0; i < segments.Count && !changed; i++)
            {
                for (int j = i + 1; j < segments.Count && !changed; j++)
                {
                    if (TryMergeCollinearSegments(segments[i], segments[j], out Segment2D merged))
                    {
                        segments[i] = merged;
                        segments.RemoveAt(j);
                        changed = true;
                    }
                }
            }
        }
    }

    private bool TryMergeCollinearSegments(Segment2D a, Segment2D b, out Segment2D merged)
    {
        merged = default;

        Vector2 aDir = a.end - a.start;
        Vector2 bDir = b.end - b.start;

        if (aDir.sqrMagnitude < 0.0001f || bDir.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        aDir.Normalize();
        bDir.Normalize();

        float angle = Vector2.Angle(aDir, bDir);
        angle = Mathf.Min(angle, Mathf.Abs(180.0f - angle));

        if (angle > collinearMergeAngle)
        {
            return false;
        }

        if (DistancePointToSegment(b.start, a.start, a.end) > collinearMergeDistance &&
            DistancePointToSegment(b.end, a.start, a.end) > collinearMergeDistance &&
            DistancePointToSegment(a.start, b.start, b.end) > collinearMergeDistance &&
            DistancePointToSegment(a.end, b.start, b.end) > collinearMergeDistance)
        {
            return false;
        }

        List<Vector2> points = new List<Vector2> { a.start, a.end, b.start, b.end };
        Vector2 origin = points[0];
        Vector2 dir = aDir;

        float minT = float.MaxValue;
        float maxT = float.MinValue;
        Vector2 minPoint = origin;
        Vector2 maxPoint = origin;

        foreach (Vector2 point in points)
        {
            float t = Vector2.Dot(point - origin, dir);

            if (t < minT)
            {
                minT = t;
                minPoint = point;
            }

            if (t > maxT)
            {
                maxT = t;
                maxPoint = point;
            }
        }

        if (Vector2.Distance(minPoint, maxPoint) < boundaryMinSegmentLength)
        {
            return false;
        }

        merged = new Segment2D { start = minPoint, end = maxPoint };
        return true;
    }

    private List<Vector2> FindOpenEndpointPoints(List<Segment2D> segments)
    {
        List<Vector2> result = new List<Vector2>();
        List<EndpointRef> refs = FindOpenEndpointRefs(segments);

        foreach (EndpointRef endpointRef in refs)
        {
            result.Add(endpointRef.point);
        }

        return result;
    }

    private List<EndpointRef> FindOpenEndpointRefs(List<Segment2D> segments)
    {
        List<EndpointRef> result = new List<EndpointRef>();

        if (segments == null || segments.Count == 0)
        {
            return result;
        }

        List<EndpointRef> endpoints = new List<EndpointRef>();

        for (int i = 0; i < segments.Count; i++)
        {
            if (Vector2.Distance(segments[i].start, segments[i].end) < boundaryMinSegmentLength)
            {
                continue;
            }

            endpoints.Add(new EndpointRef { segmentIndex = i, isStart = true, point = segments[i].start });
            endpoints.Add(new EndpointRef { segmentIndex = i, isStart = false, point = segments[i].end });
        }

        int endpointCount = endpoints.Count;
        if (endpointCount == 0)
        {
            return result;
        }

        float snapDistance = Mathf.Max(0.01f, endpointSnapDistance);
        int[] parent = new int[endpointCount];

        for (int i = 0; i < endpointCount; i++)
        {
            parent[i] = i;
        }

        for (int i = 0; i < endpointCount; i++)
        {
            for (int j = i + 1; j < endpointCount; j++)
            {
                if (Vector2.Distance(endpoints[i].point, endpoints[j].point) <= snapDistance)
                {
                    Union(parent, i, j);
                }
            }
        }

        Dictionary<int, List<int>> clusters = new Dictionary<int, List<int>>();
        for (int i = 0; i < endpointCount; i++)
        {
            int root = Find(parent, i);
            if (!clusters.TryGetValue(root, out List<int> cluster))
            {
                cluster = new List<int>();
                clusters.Add(root, cluster);
            }
            cluster.Add(i);
        }

        foreach (KeyValuePair<int, List<int>> pair in clusters)
        {
            if (pair.Value.Count == 1)
            {
                result.Add(endpoints[pair.Value[0]]);
            }
        }

        return result;
    }

    private List<Segment2D> SnapEndpointsToNearbySegmentsAndSplit(List<Segment2D> source, out int snapCount)
    {
        snapCount = 0;

        if (source == null || source.Count == 0)
        {
            return new List<Segment2D>();
        }

        List<Segment2D> working = new List<Segment2D>(source);
        List<List<float>> splitTs = new List<List<float>>();

        for (int i = 0; i < working.Count; i++)
        {
            splitTs.Add(new List<float> { 0.0f, 1.0f });
        }

        float snapDistance = Mathf.Max(0.001f, endpointToSegmentSnapDistance);
        float splitEpsilon = Mathf.Clamp(endpointProjectionSplitEpsilon, 0.001f, 0.45f);

        for (int segmentIndex = 0; segmentIndex < working.Count; segmentIndex++)
        {
            TrySnapEndpointToNearestSegment(working, splitTs, segmentIndex, isStart: true, snapDistance, splitEpsilon, ref snapCount);
            TrySnapEndpointToNearestSegment(working, splitTs, segmentIndex, isStart: false, snapDistance, splitEpsilon, ref snapCount);
        }

        List<Segment2D> result = new List<Segment2D>();

        for (int i = 0; i < working.Count; i++)
        {
            List<float> ts = splitTs[i];
            ts.Sort();

            for (int tIndex = 0; tIndex < ts.Count - 1; tIndex++)
            {
                float t0 = ts[tIndex];
                float t1 = ts[tIndex + 1];

                if (t1 - t0 < 0.001f)
                {
                    continue;
                }

                Vector2 start = Vector2.Lerp(working[i].start, working[i].end, t0);
                Vector2 end = Vector2.Lerp(working[i].start, working[i].end, t1);

                if (Vector2.Distance(start, end) >= boundaryMinSegmentLength)
                {
                    result.Add(new Segment2D { start = start, end = end });
                }
            }
        }

        return result;
    }

    private bool TrySnapEndpointToNearestSegment(
        List<Segment2D> segments,
        List<List<float>> splitTs,
        int endpointSegmentIndex,
        bool isStart,
        float snapDistance,
        float splitEpsilon,
        ref int snapCount)
    {
        Segment2D endpointSegment = segments[endpointSegmentIndex];
        Vector2 endpoint = isStart ? endpointSegment.start : endpointSegment.end;

        float bestDistance = snapDistance;
        int bestSegmentIndex = -1;
        float bestT = 0.0f;
        Vector2 bestPoint = endpoint;

        for (int i = 0; i < segments.Count; i++)
        {
            if (i == endpointSegmentIndex)
            {
                continue;
            }

            Segment2D candidate = segments[i];
            if (Vector2.Distance(candidate.start, candidate.end) < boundaryMinSegmentLength)
            {
                continue;
            }

            float t = ProjectPointToSegment01(endpoint, candidate.start, candidate.end);
            Vector2 projected = Vector2.Lerp(candidate.start, candidate.end, t);
            float distance = Vector2.Distance(endpoint, projected);

            if (distance <= bestDistance)
            {
                bestDistance = distance;
                bestSegmentIndex = i;
                bestT = t;
                bestPoint = projected;
            }
        }

        if (bestSegmentIndex < 0)
        {
            return false;
        }

        if (Vector2.Distance(endpoint, bestPoint) < 0.0001f)
        {
            return false;
        }

        if (isStart)
        {
            endpointSegment.start = bestPoint;
        }
        else
        {
            endpointSegment.end = bestPoint;
        }

        segments[endpointSegmentIndex] = endpointSegment;
        snapCount++;

        if (bestT > splitEpsilon && bestT < 1.0f - splitEpsilon)
        {
            AddUniqueSplitT(splitTs[bestSegmentIndex], bestT);
        }

        return true;
    }

    private void AddBridgeSegmentsForNearbyOpenEndpoints(List<Segment2D> segments, out int bridgeCount)
    {
        bridgeCount = 0;

        if (segments == null || segments.Count == 0)
        {
            return;
        }

        List<EndpointRef> openEndpoints = FindOpenEndpointRefs(segments);
        bool[] used = new bool[openEndpoints.Count];
        float bridgeDistance = Mathf.Max(endpointSnapDistance, openEndpointBridgeDistance);

        for (int i = 0; i < openEndpoints.Count; i++)
        {
            if (used[i])
            {
                continue;
            }

            int bestIndex = -1;
            float bestDistance = bridgeDistance;

            for (int j = i + 1; j < openEndpoints.Count; j++)
            {
                if (used[j])
                {
                    continue;
                }

                if (openEndpoints[i].segmentIndex == openEndpoints[j].segmentIndex)
                {
                    continue;
                }

                float distance = Vector2.Distance(openEndpoints[i].point, openEndpoints[j].point);
                if (distance <= bestDistance && distance >= boundaryMinSegmentLength)
                {
                    bestDistance = distance;
                    bestIndex = j;
                }
            }

            if (bestIndex < 0)
            {
                continue;
            }

            segments.Add(new Segment2D
            {
                start = openEndpoints[i].point,
                end = openEndpoints[bestIndex].point
            });

            used[i] = true;
            used[bestIndex] = true;
            bridgeCount++;
        }
    }

    private bool TryRunCubeFootprintFloodFill(List<Segment2D> segments)
    {
        selectedCells.Clear();
        lastCubeFootprintCount = 0;
        lastCubeBlockedCellCheckCount = 0;
        lastCubeBlockedMovementCheckCount = 0;

        if (Camera.main == null)
        {
            floodReachedOutside = true;
            return false;
        }

        if (segments == null || segments.Count == 0)
        {
            floodReachedOutside = true;
            return false;
        }

        List<WallFootprint2D> footprints = BuildWallFootprints(segments);
        lastCubeFootprintCount = footprints.Count;

        if (footprints.Count == 0)
        {
            floodReachedOutside = true;
            return false;
        }

        BuildGridBounds(segments);

        Vector3 cameraPosition = Camera.main.transform.position;
        Vector2 seed = new Vector2(cameraPosition.x, cameraPosition.z);

        if (!TryWorldToCell(seed, out Vector2Int startCell))
        {
            floodReachedOutside = true;
            return false;
        }

        if (IsBlockedCellByFootprints(startCell, footprints))
        {
            if (!TryFindNearestFreeCellByFootprints(startCell, footprints, out startCell))
            {
                floodReachedOutside = true;
                return false;
            }
        }

        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        HashSet<Vector2Int> visited = new HashSet<Vector2Int>();

        queue.Enqueue(startCell);
        visited.Add(startCell);
        floodReachedOutside = false;

        Vector2Int[] directions =
        {
            new Vector2Int(1, 0),
            new Vector2Int(-1, 0),
            new Vector2Int(0, 1),
            new Vector2Int(0, -1)
        };

        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();
            selectedCells.Add(current);

            if (IsBorderCell(current))
            {
                floodReachedOutside = true;
            }

            Vector2 currentWorld = CellToWorld(current);

            foreach (Vector2Int direction in directions)
            {
                Vector2Int next = current + direction;

                if (!IsInsideGrid(next))
                {
                    floodReachedOutside = true;
                    continue;
                }

                if (visited.Contains(next))
                {
                    continue;
                }

                if (IsBlockedCellByFootprints(next, footprints))
                {
                    continue;
                }

                Vector2 nextWorld = CellToWorld(next);

                if (MovementCrossesAnyFootprint(currentWorld, nextWorld, footprints))
                {
                    continue;
                }

                visited.Add(next);
                queue.Enqueue(next);
            }
        }

        return selectedCells.Count > 0;
    }

    private List<WallFootprint2D> BuildWallFootprints(List<Segment2D> segments)
    {
        List<WallFootprint2D> footprints = new List<WallFootprint2D>();
        float thickness = GetEffectiveCubeFootprintThickness();
        float halfThickness = Mathf.Max(0.001f, thickness * 0.5f);
        float endPadding = Mathf.Max(0.0f, cubeFootprintEndPadding);

        foreach (Segment2D segment in segments)
        {
            Vector2 delta = segment.end - segment.start;
            float length = delta.magnitude;

            if (length < boundaryMinSegmentLength)
            {
                continue;
            }

            Vector2 direction = delta / length;
            Vector2 normal = new Vector2(direction.y, -direction.x);

            footprints.Add(new WallFootprint2D
            {
                center = (segment.start + segment.end) * 0.5f,
                direction = direction,
                normal = normal,
                halfLength = length * 0.5f + endPadding,
                halfThickness = halfThickness
            });
        }

        return footprints;
    }

    private float GetEffectiveCubeFootprintThickness()
    {
        float thickness = Mathf.Max(0.001f, cubeFootprintWallThickness);
        thickness = Mathf.Max(thickness, confirmedWallThickness);
        thickness = Mathf.Max(thickness, wallBlockDistance * 2.0f);

        if (manualWallBuilder != null)
        {
            thickness = Mathf.Max(thickness, manualWallBuilder.wallThickness);
        }

        return thickness;
    }

    private bool IsBlockedCellByFootprints(Vector2Int cell, List<WallFootprint2D> footprints)
    {
        Vector2 point = CellToWorld(cell);

        foreach (WallFootprint2D footprint in footprints)
        {
            if (IsPointInsideFootprint(point, footprint, cubeFootprintCollisionPadding))
            {
                lastCubeBlockedCellCheckCount++;
                return true;
            }
        }

        return false;
    }

    private bool TryFindNearestFreeCellByFootprints(
        Vector2Int center,
        List<WallFootprint2D> footprints,
        out Vector2Int freeCell)
    {
        for (int radius = 1; radius <= nearestFreeCellSearchRadius; radius++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    Vector2Int candidate = center + new Vector2Int(dx, dy);

                    if (!IsInsideGrid(candidate))
                    {
                        continue;
                    }

                    if (!IsBlockedCellByFootprints(candidate, footprints))
                    {
                        freeCell = candidate;
                        return true;
                    }
                }
            }
        }

        freeCell = default;
        return false;
    }

    private bool MovementCrossesAnyFootprint(
        Vector2 from,
        Vector2 to,
        List<WallFootprint2D> footprints)
    {
        foreach (WallFootprint2D footprint in footprints)
        {
            if (SegmentIntersectsFootprint(from, to, footprint, cubeFootprintCollisionPadding))
            {
                lastCubeBlockedMovementCheckCount++;
                return true;
            }
        }

        return false;
    }

    private static bool IsPointInsideFootprint(
        Vector2 point,
        WallFootprint2D footprint,
        float padding)
    {
        Vector2 relative = point - footprint.center;
        float along = Vector2.Dot(relative, footprint.direction);
        float side = Vector2.Dot(relative, footprint.normal);

        return Mathf.Abs(along) <= footprint.halfLength + padding &&
               Mathf.Abs(side) <= footprint.halfThickness + padding;
    }

    private static bool SegmentIntersectsFootprint(
        Vector2 from,
        Vector2 to,
        WallFootprint2D footprint,
        float padding)
    {
        if (IsPointInsideFootprint(from, footprint, padding) ||
            IsPointInsideFootprint(to, footprint, padding))
        {
            return true;
        }

        GetFootprintCorners(footprint, padding, out Vector2 a, out Vector2 b, out Vector2 c, out Vector2 d);

        return SegmentsIntersect(from, to, a, b) ||
               SegmentsIntersect(from, to, b, c) ||
               SegmentsIntersect(from, to, c, d) ||
               SegmentsIntersect(from, to, d, a);
    }

    private static void GetFootprintCorners(
        WallFootprint2D footprint,
        float padding,
        out Vector2 a,
        out Vector2 b,
        out Vector2 c,
        out Vector2 d)
    {
        Vector2 along = footprint.direction * (footprint.halfLength + padding);
        Vector2 side = footprint.normal * (footprint.halfThickness + padding);

        a = footprint.center + along + side;
        b = footprint.center + along - side;
        c = footprint.center - along - side;
        d = footprint.center - along + side;
    }

    private bool TryRunUserRoomFloodFill(List<Segment2D> segments)
    {
        selectedCells.Clear();

        if (Camera.main == null)
        {
            floodReachedOutside = true;
            return false;
        }

        BuildGridBounds(segments);

        Vector3 cameraPosition = Camera.main.transform.position;
        Vector2 seed = new Vector2(cameraPosition.x, cameraPosition.z);

        if (!TryWorldToCell(seed, out Vector2Int startCell))
        {
            floodReachedOutside = true;
            return false;
        }

        if (IsBlockedCell(startCell, segments))
        {
            if (!TryFindNearestFreeCell(startCell, segments, out startCell))
            {
                floodReachedOutside = true;
                return false;
            }
        }

        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        HashSet<Vector2Int> visited = new HashSet<Vector2Int>();

        queue.Enqueue(startCell);
        visited.Add(startCell);

        floodReachedOutside = false;

        Vector2Int[] directions =
        {
            new Vector2Int(1, 0),
            new Vector2Int(-1, 0),
            new Vector2Int(0, 1),
            new Vector2Int(0, -1)
        };

        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();
            selectedCells.Add(current);

            if (IsBorderCell(current))
            {
                floodReachedOutside = true;
            }

            Vector2 currentWorld = CellToWorld(current);

            foreach (Vector2Int direction in directions)
            {
                Vector2Int next = current + direction;

                if (!IsInsideGrid(next))
                {
                    floodReachedOutside = true;
                    continue;
                }

                if (visited.Contains(next))
                {
                    continue;
                }

                if (IsBlockedCell(next, segments))
                {
                    continue;
                }

                Vector2 nextWorld = CellToWorld(next);

                if (MovementCrossesAnySegment(currentWorld, nextWorld, segments))
                {
                    continue;
                }

                visited.Add(next);
                queue.Enqueue(next);
            }
        }

        return selectedCells.Count > 0;
    }

    private void BuildGridBounds(List<Segment2D> segments)
    {
        float minX = float.MaxValue;
        float maxX = float.MinValue;
        float minZ = float.MaxValue;
        float maxZ = float.MinValue;

        foreach (Segment2D segment in segments)
        {
            minX = Mathf.Min(minX, segment.start.x, segment.end.x);
            maxX = Mathf.Max(maxX, segment.start.x, segment.end.x);
            minZ = Mathf.Min(minZ, segment.start.y, segment.end.y);
            maxZ = Mathf.Max(maxZ, segment.start.y, segment.end.y);
        }

        minX -= gridBoundsMargin;
        maxX += gridBoundsMargin;
        minZ -= gridBoundsMargin;
        maxZ += gridBoundsMargin;

        gridWidth = Mathf.CeilToInt((maxX - minX) / gridCellSize);
        gridHeight = Mathf.CeilToInt((maxZ - minZ) / gridCellSize);

        gridWidth = Mathf.Clamp(gridWidth, 1, maxGridCellsPerAxis);
        gridHeight = Mathf.Clamp(gridHeight, 1, maxGridCellsPerAxis);

        gridMin = new Vector2(minX, minZ);
    }

    private bool TryWorldToCell(Vector2 world, out Vector2Int cell)
    {
        int x = Mathf.FloorToInt((world.x - gridMin.x) / gridCellSize);
        int y = Mathf.FloorToInt((world.y - gridMin.y) / gridCellSize);

        cell = new Vector2Int(x, y);
        return IsInsideGrid(cell);
    }

    private Vector2 CellToWorld(Vector2Int cell)
    {
        return new Vector2(
            gridMin.x + (cell.x + 0.5f) * gridCellSize,
            gridMin.y + (cell.y + 0.5f) * gridCellSize
        );
    }

    private bool IsInsideGrid(Vector2Int cell)
    {
        return cell.x >= 0 && cell.y >= 0 && cell.x < gridWidth && cell.y < gridHeight;
    }

    private bool IsBorderCell(Vector2Int cell)
    {
        return cell.x == 0 || cell.y == 0 || cell.x == gridWidth - 1 || cell.y == gridHeight - 1;
    }

    private bool IsBlockedCell(Vector2Int cell, List<Segment2D> segments)
    {
        Vector2 point = CellToWorld(cell);

        foreach (Segment2D segment in segments)
        {
            if (DistancePointToSegment(point, segment.start, segment.end) <= wallBlockDistance)
            {
                return true;
            }
        }

        return false;
    }

    private bool TryFindNearestFreeCell(Vector2Int center, List<Segment2D> segments, out Vector2Int freeCell)
    {
        for (int radius = 1; radius <= nearestFreeCellSearchRadius; radius++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    Vector2Int candidate = center + new Vector2Int(dx, dy);

                    if (!IsInsideGrid(candidate))
                    {
                        continue;
                    }

                    if (!IsBlockedCell(candidate, segments))
                    {
                        freeCell = candidate;
                        return true;
                    }
                }
            }
        }

        freeCell = default;
        return false;
    }

    private bool MovementCrossesAnySegment(Vector2 from, Vector2 to, List<Segment2D> segments)
    {
        foreach (Segment2D segment in segments)
        {
            if (SegmentsIntersect(from, to, segment.start, segment.end))
            {
                return true;
            }
        }

        return false;
    }

    private void BuildConfirmedBoundarySegments(List<Segment2D> allSegments)
    {
        confirmedBoundarySegments.Clear();

        foreach (Segment2D segment in allSegments)
        {
            if (IsSegmentAdjacentToSelectedRoom(segment))
            {
                confirmedBoundarySegments.Add(segment);
            }
        }
    }

    private bool IsSegmentAdjacentToSelectedRoom(Segment2D segment)
    {
        float length = Vector2.Distance(segment.start, segment.end);
        int sampleCount = Mathf.Max(2, Mathf.CeilToInt(length / gridCellSize));
        Vector2 dir = (segment.end - segment.start).normalized;

        if (dir.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        Vector2 normal = new Vector2(-dir.y, dir.x);

        for (int i = 0; i <= sampleCount; i++)
        {
            float t = i / (float)sampleCount;
            Vector2 p = Vector2.Lerp(segment.start, segment.end, t);
            Vector2 sideA = p + normal * gridCellSize * 0.75f;
            Vector2 sideB = p - normal * gridCellSize * 0.75f;

            bool aSelected = TryWorldToCell(sideA, out Vector2Int cellA) && selectedCells.Contains(cellA);
            bool bSelected = TryWorldToCell(sideB, out Vector2Int cellB) && selectedCells.Contains(cellB);

            if (aSelected || bSelected)
            {
                return true;
            }
        }

        return false;
    }


    private List<Segment2D> BuildReviewWallSegmentsForVisualization()
    {
        List<Segment2D> result = new List<Segment2D>();

        if (normalizedBoundarySegmentsForConfirm.Count == 0)
        {
            List<Segment2D> allSegments = BuildFinalBoundarySegments();
            if (normalizeBoundarySegmentsBeforeConfirm)
            {
                allSegments = NormalizeBoundarySegments(allSegments);
            }

            foreach (Segment2D segment in allSegments)
            {
                AddUniqueCandidateSegment(result, segment);
            }

            return result;
        }

        foreach (Segment2D segment in normalizedBoundarySegmentsForConfirm)
        {
            AddUniqueCandidateSegment(result, segment);
        }

        return result;
    }

    private List<Segment2D> BuildConfirmedWallSegmentsForVisualization()
    {
        List<Segment2D> result = new List<Segment2D>();

        foreach (Segment2D segment in confirmedBoundarySegments)
        {
            AddUniqueCandidateSegment(result, segment);
        }

        foreach (Segment2D segment in normalizedBoundarySegmentsForConfirm)
        {
            AddUniqueCandidateSegment(result, segment);
        }

        return result;
    }

    private void AddUniqueCandidateSegment(List<Segment2D> target, Segment2D segment)
    {
        if (Vector2.Distance(segment.start, segment.end) < boundaryMinSegmentLength)
        {
            return;
        }

        if (!IsSegmentAlreadyCovered(target, segment))
        {
            target.Add(segment);
        }
    }

    private bool IsSegmentAlreadyCovered(List<Segment2D> segments, Segment2D candidate)
    {
        foreach (Segment2D segment in segments)
        {
            bool sameDirection = Vector2.Distance(segment.start, candidate.start) <= endpointSnapDistance &&
                                 Vector2.Distance(segment.end, candidate.end) <= endpointSnapDistance;
            bool reversed = Vector2.Distance(segment.start, candidate.end) <= endpointSnapDistance &&
                            Vector2.Distance(segment.end, candidate.start) <= endpointSnapDistance;

            if (sameDirection || reversed)
            {
                return true;
            }
        }

        return false;
    }

    private void BuildConfirmedHorizontalBoxSlabsFromSegments(List<Segment2D> segments, float floorY, float ceilingY)
    {
        if (!TryGetBoundsFromSegments(segments, out Vector2 min, out Vector2 max))
        {
            return;
        }

        min -= Vector2.one * confirmedHorizontalSlabPadding;
        max += Vector2.one * confirmedHorizontalSlabPadding;

        Vector2 center = (min + max) * 0.5f;
        Vector2 size = max - min;

        CreateConfirmedHorizontalBoxSlab(
            "ConfirmedRoom_Floor",
            new Vector3(center.x, floorY - confirmedHorizontalSlabThickness * 0.5f, center.y),
            new Vector3(size.x, confirmedHorizontalSlabThickness, size.y),
            true
        );

        CreateConfirmedHorizontalBoxSlab(
            "ConfirmedRoom_Ceiling",
            new Vector3(center.x, ceilingY + confirmedHorizontalSlabThickness * 0.5f, center.y),
            new Vector3(size.x, confirmedHorizontalSlabThickness, size.y),
            false
        );
    }

    private bool TryGetBoundsFromSegments(List<Segment2D> segments, out Vector2 min, out Vector2 max)
    {
        min = new Vector2(float.MaxValue, float.MaxValue);
        max = new Vector2(float.MinValue, float.MinValue);

        if (segments == null || segments.Count == 0)
        {
            return false;
        }

        foreach (Segment2D segment in segments)
        {
            min.x = Mathf.Min(min.x, segment.start.x, segment.end.x);
            min.y = Mathf.Min(min.y, segment.start.y, segment.end.y);
            max.x = Mathf.Max(max.x, segment.start.x, segment.end.x);
            max.y = Mathf.Max(max.y, segment.start.y, segment.end.y);
        }

        return true;
    }

    private void CreateConfirmedHorizontalBoxSlab(string objectName, Vector3 position, Vector3 scale, bool isFloor)
    {
        GameObject slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
        slab.name = objectName;
        slab.transform.position = position;
        slab.transform.rotation = Quaternion.identity;
        slab.transform.localScale = scale;
        slab.transform.SetParent(confirmedRoomRoot, true);

        MeshRenderer renderer = slab.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = GetHorizontalMaterial();
        }

        BoxCollider boxCollider = slab.GetComponent<BoxCollider>();
        if (boxCollider == null)
        {
            slab.AddComponent<BoxCollider>();
        }

        if (isFloor)
        {
            confirmedRoomFloorObject = slab;
        }
        else
        {
            confirmedRoomCeilingObject = slab;
        }
    }

    private void BuildConfirmedWallCubesFromSegments(List<Segment2D> segments, float floorY, float ceilingY)
    {
        foreach (Segment2D segment in segments)
        {
            CreateConfirmedWallCube(segment.start, segment.end, floorY, ceilingY);
        }
    }

    private void CreateConfirmedWallCube(Vector2 start, Vector2 end, float floorY, float ceilingY)
    {
        float length = Vector2.Distance(start, end);

        if (length < boundaryMinSegmentLength)
        {
            return;
        }

        float height = Mathf.Max(0.1f, ceilingY - floorY);
        Vector2 mid = (start + end) * 0.5f;
        Vector2 dir = (end - start).normalized;

        if (dir.sqrMagnitude < 0.0001f)
        {
            return;
        }

        float yaw = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;

        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "ConfirmedRoom_Wall";
        wall.transform.position = new Vector3(mid.x, floorY + height * 0.5f, mid.y);
        wall.transform.rotation = Quaternion.Euler(0.0f, yaw, 0.0f);
        wall.transform.localScale = new Vector3(confirmedWallThickness, height, length);
        wall.transform.SetParent(confirmedRoomRoot, true);

        MeshRenderer renderer = wall.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = GetWallMaterial();
        }

        BoxCollider boxCollider = wall.GetComponent<BoxCollider>();
        if (boxCollider == null)
        {
            wall.AddComponent<BoxCollider>();
        }

        confirmedRoomWallObjects.Add(wall);
    }

    private void CreateGapMarkers(List<Vector2> openEndpoints)
    {
        ClearGapMarkers();

        if (!showGapMarkers || openEndpoints == null || openEndpoints.Count == 0)
        {
            return;
        }

        EnsureRoots(transform);

        float y = 0.0f;

        if (scanner != null && scanner.currentRoom != null && !float.IsNaN(scanner.currentRoom.floorY))
        {
            y = scanner.currentRoom.floorY + gapMarkerYOffset;
        }
        else if (Camera.main != null)
        {
            y = Camera.main.transform.position.y - 1.2f;
        }

        float markerSize = Mathf.Max(0.02f, gapMarkerSize);

        for (int i = 0; i < openEndpoints.Count; i++)
        {
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = $"ConfirmRoom_OpenEndpoint_{i:00}";
            marker.transform.SetParent(gapMarkerRoot, false);
            marker.transform.position = new Vector3(openEndpoints[i].x, y, openEndpoints[i].y);
            marker.transform.localScale = Vector3.one * markerSize;

            Collider collider = marker.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }

            Renderer renderer = marker.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = GetGapMarkerMaterial();
            }
        }
    }

    public void ClearGapMarkers()
    {
        if (gapMarkerRoot == null)
        {
            return;
        }

        for (int i = gapMarkerRoot.childCount - 1; i >= 0; i--)
        {
            Destroy(gapMarkerRoot.GetChild(i).gameObject);
        }
    }

    private Material GetWallMaterial()
    {
        if (confirmedRoomWallMaterial != null)
        {
            return confirmedRoomWallMaterial;
        }

        if (runtimeWallMaterial == null)
        {
            runtimeWallMaterial = CreateRuntimeMaterial(
                "Runtime_ConfirmedRoom_Wall_Material",
                new Color(0.1f, 0.75f, 1.0f, 0.45f)
            );
        }

        return runtimeWallMaterial;
    }

    private Material GetHorizontalMaterial()
    {
        if (confirmedRoomHorizontalMaterial != null)
        {
            return confirmedRoomHorizontalMaterial;
        }

        if (runtimeHorizontalMaterial == null)
        {
            runtimeHorizontalMaterial = CreateRuntimeMaterial(
                "Runtime_ConfirmedRoom_Horizontal_Material",
                new Color(0.1f, 1.0f, 0.45f, 0.35f)
            );
        }

        return runtimeHorizontalMaterial;
    }

    private Material GetGapMarkerMaterial()
    {
        if (gapMarkerMaterial != null)
        {
            return gapMarkerMaterial;
        }

        if (runtimeGapMarkerMaterial == null)
        {
            runtimeGapMarkerMaterial = CreateRuntimeMaterial(
                "Runtime_ConfirmRoom_GapMarker_Material",
                new Color(1.0f, 0.12f, 0.05f, 0.85f)
            );
        }

        return runtimeGapMarkerMaterial;
    }

    private Material CreateRuntimeMaterial(string materialName, Color color)
    {
        Shader shader = FindSupportedShader();

        if (shader == null)
        {
            if (!warnedRuntimeMaterialShaderMissing)
            {
                warnedRuntimeMaterialShaderMissing = true;
                Debug.LogWarning("[ConfirmRoom] Runtime material shader is missing. Primitive default material will be used.");
            }

            return null;
        }

        Material material = new Material(shader)
        {
            name = materialName,
            color = color
        };

        return material;
    }

    private Shader FindSupportedShader()
    {
        string[] shaderNames =
        {
            "Graphics Tools/Standard",
            "Mixed Reality Toolkit/Standard",
            "Universal Render Pipeline/Unlit",
            "Universal Render Pipeline/Lit",
            "Unlit/Color",
            "Standard"
        };

        foreach (string shaderName in shaderNames)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader != null)
            {
                return shader;
            }
        }

        return null;
    }

    private static float ProjectPointToSegment01(Vector2 point, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float abSqr = ab.sqrMagnitude;

        if (abSqr < 0.0001f)
        {
            return 0.0f;
        }

        return Mathf.Clamp01(Vector2.Dot(point - a, ab) / abSqr);
    }

    private static void AddUniqueSplitT(List<float> splitTs, float t)
    {
        for (int i = 0; i < splitTs.Count; i++)
        {
            if (Mathf.Abs(splitTs[i] - t) < 0.001f)
            {
                return;
            }
        }

        splitTs.Add(t);
    }

    private static int Find(int[] parent, int index)
    {
        if (parent[index] != index)
        {
            parent[index] = Find(parent, parent[index]);
        }

        return parent[index];
    }

    private static void Union(int[] parent, int a, int b)
    {
        int rootA = Find(parent, a);
        int rootB = Find(parent, b);

        if (rootA != rootB)
        {
            parent[rootB] = rootA;
        }
    }

    private static float DistancePointToSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float abSqr = ab.sqrMagnitude;

        if (abSqr < 0.0001f)
        {
            return Vector2.Distance(point, a);
        }

        float t = Vector2.Dot(point - a, ab) / abSqr;
        t = Mathf.Clamp01(t);
        Vector2 projection = a + ab * t;
        return Vector2.Distance(point, projection);
    }

    private static bool SegmentsIntersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        float o1 = Orientation(a, b, c);
        float o2 = Orientation(a, b, d);
        float o3 = Orientation(c, d, a);
        float o4 = Orientation(c, d, b);

        if (o1 * o2 < 0.0f && o3 * o4 < 0.0f)
        {
            return true;
        }

        const float epsilon = 0.0001f;

        if (Mathf.Abs(o1) < epsilon && IsPointOnSegment(c, a, b)) return true;
        if (Mathf.Abs(o2) < epsilon && IsPointOnSegment(d, a, b)) return true;
        if (Mathf.Abs(o3) < epsilon && IsPointOnSegment(a, c, d)) return true;
        if (Mathf.Abs(o4) < epsilon && IsPointOnSegment(b, c, d)) return true;

        return false;
    }

    private static bool TryGetSegmentIntersectionParameters(
        Vector2 a,
        Vector2 b,
        Vector2 c,
        Vector2 d,
        out float ta,
        out float tb)
    {
        ta = 0.0f;
        tb = 0.0f;

        Vector2 r = b - a;
        Vector2 s = d - c;
        float denominator = Cross(r, s);

        if (Mathf.Abs(denominator) < 0.0001f)
        {
            return false;
        }

        Vector2 diff = c - a;
        ta = Cross(diff, s) / denominator;
        tb = Cross(diff, r) / denominator;

        return ta >= -0.0001f && ta <= 1.0001f && tb >= -0.0001f && tb <= 1.0001f;
    }

    private static float Cross(Vector2 a, Vector2 b)
    {
        return a.x * b.y - a.y * b.x;
    }

    private static float Orientation(Vector2 a, Vector2 b, Vector2 c)
    {
        return Cross(b - a, c - a);
    }

    private static bool IsPointOnSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        return point.x >= Mathf.Min(a.x, b.x) - 0.0001f &&
               point.x <= Mathf.Max(a.x, b.x) + 0.0001f &&
               point.y >= Mathf.Min(a.y, b.y) - 0.0001f &&
               point.y <= Mathf.Max(a.y, b.y) + 0.0001f;
    }
}

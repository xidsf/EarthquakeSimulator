using System.Collections.Generic;
using MixedReality.Toolkit.UX;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

public class RoomBuildWorkflowManager : MonoBehaviour
{
    public enum WorkflowState
    {
        RoomBuild,
        ManualWallGenerate,
        ManualWallConfirmed,
        FurnitureRecognition
    }

    private struct Segment2D
    {
        public Vector2 start;
        public Vector2 end;
    }

    private struct EndpointRef
    {
        public int segmentIndex;
        public bool isStart;
        public Vector2 point;
    }

    private struct GridBoundaryEdge
    {
        public Vector2Int a;
        public Vector2Int b;
    }

    [Header("References")]
    public SceneUnderstandingRoomScanner scanner;
    public ManualWallBuilder manualWallBuilder;

    [Header("UI Groups")]
    public GameObject roomBuildButtonGroup;
    public GameObject manualWallButtonGroup;
    public GameObject confirmRoomButtonGroup;
    public GameObject furnitureRecognitionButtonGroup;

    [Header("Workflow Buttons")]
    public PressableButton switchToManualWallButton;
    public PressableButton returnToRoomBuildButton;
    public PressableButton confirmManualWallsButton;
    public PressableButton backToManualWallButton;
    public PressableButton confirmRoomButton;

    [Header("Status Text")]
    public TMP_Text workflowStatusText;

    [Header("Switch Conditions")]
    public bool autoLockRoomWhenSwitchingToManual = true;
    public bool stopScanningWhenSwitchingToManual = true;
    public bool rebuildSelectableSurfacesWhenSwitching = true;

    [Tooltip("ManualWallGenerate 상태에서 SceneUnderstanding raw/detected mesh가 비동기 갱신으로 다시 나타나도 계속 숨깁니다.")]
    public bool keepAutoRoomVisualsHiddenWhileManual = true;

    public bool clearManualWallsWhenReturningToRoomBuild = true;

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

    [Header("User Room Flood Fill Validation")]
    public float gridCellSize = 0.12f;
    public float gridBoundsMargin = 0.75f;
    public float wallBlockDistance = 0.08f;
    public int maxGridCellsPerAxis = 220;
    public int nearestFreeCellSearchRadius = 8;

    [Header("Confirmed Room Visualization")]
    public Transform confirmedRoomRoot;

    [Tooltip("비워두면 런타임에 자동 생성됩니다.")]
    public Material confirmedRoomWallMaterial;

    [Tooltip("비워두면 런타임에 자동 생성됩니다. 바닥과 천장이 같이 사용합니다.")]
    public Material confirmedRoomHorizontalMaterial;

    public float confirmedWallThickness = 0.045f;
    public float confirmedFloorYOffset = 0.025f;
    public float confirmedCeilingYOffset = -0.025f;

    [Tooltip("Confirmed Room의 바닥/천장을 polygon mesh가 아니라 얇은 Cube + BoxCollider slab으로 생성합니다.")]
    public bool useConfirmedHorizontalBoxSlabs = true;

    [Tooltip("Confirmed Room 바닥/천장 slab의 두께입니다. Floor는 이 두께만큼 아래로, Ceiling은 이 두께만큼 위로 생성되어 방 내부 높이를 침범하지 않습니다.")]
    public float confirmedHorizontalSlabThickness = 0.06f;

    [Tooltip("벽 segment들의 XZ bounds보다 바닥/천장 slab을 이 거리만큼 더 크게 만듭니다. 벽과 바닥/천장이 살짝 어긋나도 빈틈이 보이지 않게 하기 위한 값입니다.")]
    public float confirmedHorizontalSlabPadding = 0.25f;

    [Tooltip("기존 polygon mesh 기반 바닥/천장 fallback에 MeshCollider를 생성합니다. useConfirmedHorizontalBoxSlabs가 켜져 있으면 일반적으로 사용되지 않습니다.")]
    public bool addConfirmedRoomMeshColliders = true;

    [Tooltip("segment polygon 생성에 실패했을 때 grid cell 전체가 아니라 선택 영역의 외곽선만 추출해 하나의 polygon mesh로 만듭니다.")]
    public bool useSelectedCellBoundaryPolygonFallback = true;

    [Tooltip("마지막 Confirm horizontal mesh가 grid fallback까지 내려갔는지 확인하는 debug 값입니다.")]
    public bool usedGridHorizontalFallback;

    [Tooltip("segment loop 생성이 실패했을 때 flood fill selected cell 외곽선 polygon으로 Confirm mesh를 생성했는지 확인하는 debug 값입니다.")]
    public bool usedSelectedCellBoundaryPolygonFallback;

    [Tooltip("selected cell 외곽선 fallback polygon을 단순화하는 거리입니다. 너무 낮으면 계단형이 남고, 너무 높으면 방 모양이 과하게 단순화됩니다.")]
    public float selectedCellFallbackSimplifyTolerance = 0.18f;

    [Tooltip("selected cell fallback polygon vertex를 가까운 boundary segment로 붙이는 최대 거리입니다.")]
    public float selectedCellFallbackSnapDistance = 0.24f;

    [Tooltip("selected cell fallback polygon vertex를 detected/manual boundary segment 위로 보정합니다.")]
    public bool snapSelectedCellFallbackPolygonToBoundary = true;

    [Tooltip("Confirm Room 상태에서 기존 raw mesh / wall segmentation 표시를 숨깁니다.")]
    public bool hideAutoRoomVisualsInConfirmRoom = true;

    [Tooltip("Confirm Room 상태에서 사용자가 직접 만든 Manual Wall 원본 표시와 collider를 숨깁니다. 최종 방은 ConfirmedRoomRoot로만 표시됩니다.")]
    public bool hideManualWallVisualsInConfirmRoom = true;

    [Tooltip("Confirm Room에서 Manual Wall로 돌아갈 때 기존 raw mesh / wall segmentation 표시를 다시 켭니다.")]
    public bool restoreAutoRoomVisualsWhenBackToManual = true;

    [Header("Debug")]
    public WorkflowState currentState = WorkflowState.RoomBuild;
    public bool finalRoomClosed;
    public bool floodReachedOutside;
    public int selectedCellCount;
    public int confirmedBoundarySegmentCount;
    public int manualWallCount;
    public int openEndpointCount = -1;
    public string status;

    private readonly HashSet<Vector2Int> selectedCells = new HashSet<Vector2Int>();
    private readonly List<Segment2D> confirmedBoundarySegments = new List<Segment2D>();
    private readonly List<Segment2D> normalizedBoundarySegmentsForConfirm = new List<Segment2D>();

    private Vector2 gridMin;
    private int gridWidth;
    private int gridHeight;

    private Material runtimeWallMaterial;
    private Material runtimeHorizontalMaterial;

    private UnityAction switchToManualWallAction;
    private UnityAction returnToRoomBuildAction;
    private UnityAction confirmManualWallsAction;
    private UnityAction backToManualWallAction;
    private UnityAction confirmRoomAction;

    private void Awake()
    {
        if (confirmedRoomRoot == null)
        {
            confirmedRoomRoot = new GameObject("ConfirmedRoomRoot").transform;
        }

        EnsureDefaultMaterials();
    }

    private void OnEnable()
    {
        CreateActions();
        RegisterButtons();
        ApplyState(currentState);
    }

    private void OnDisable()
    {
        UnregisterButtons();
    }

    private void LateUpdate()
    {
        // Scene Understanding ComputeAsync가 ManualWallGenerate 진입 직후 늦게 끝나면
        // Raw Scene Understanding Objects / Reconstructed Room이 다시 생성될 수 있습니다.
        // Manual 단계에서는 상호작용 가능한 selectable surface만 보여야 하므로 매 프레임 방어적으로 숨깁니다.
        if (currentState == WorkflowState.ManualWallGenerate && keepAutoRoomVisualsHiddenWhileManual)
        {
            SetAutoRoomVisualsActive(false);
        }

        if (currentState == WorkflowState.ManualWallConfirmed && hideAutoRoomVisualsInConfirmRoom)
        {
            SetAutoRoomVisualsActive(false);
        }
    }

    private void CreateActions()
    {
        switchToManualWallAction ??= SwitchToManualWallGeneration;
        returnToRoomBuildAction ??= ReturnToRoomBuild;
        confirmManualWallsAction ??= ConfirmManualWalls;
        backToManualWallAction ??= BackToManualWallGeneration;
        confirmRoomAction ??= ConfirmRoomAndStartFurnitureRecognitionPlaceholder;
    }

    private void RegisterButtons()
    {
        AddClick(switchToManualWallButton, switchToManualWallAction);
        AddClick(returnToRoomBuildButton, returnToRoomBuildAction);
        AddClick(confirmManualWallsButton, confirmManualWallsAction);
        AddClick(backToManualWallButton, backToManualWallAction);
        AddClick(confirmRoomButton, confirmRoomAction);
    }

    private void UnregisterButtons()
    {
        RemoveClick(switchToManualWallButton, switchToManualWallAction);
        RemoveClick(returnToRoomBuildButton, returnToRoomBuildAction);
        RemoveClick(confirmManualWallsButton, confirmManualWallsAction);
        RemoveClick(backToManualWallButton, backToManualWallAction);
        RemoveClick(confirmRoomButton, confirmRoomAction);
    }

    private static void AddClick(PressableButton button, UnityAction action)
    {
        if (button == null || action == null)
        {
            return;
        }

        button.OnClicked.RemoveListener(action);
        button.OnClicked.AddListener(action);
    }

    private static void RemoveClick(PressableButton button, UnityAction action)
    {
        if (button == null || action == null)
        {
            return;
        }

        button.OnClicked.RemoveListener(action);
    }

    public void SwitchToManualWallGeneration()
    {
        if (scanner == null)
        {
            SetStatus("Cannot switch to Manual Wall. Scanner is null.");
            return;
        }

        if (scanner.currentRoom == null)
        {
            SetStatus("Cannot switch to Manual Wall. Build room first.");
            return;
        }

        if (autoLockRoomWhenSwitchingToManual)
        {
            scanner.LockRoom();
        }

        if (!scanner.lockRoom)
        {
            SetStatus("Room must be locked before Manual Wall generation.");
            return;
        }

        if (stopScanningWhenSwitchingToManual)
        {
            scanner.StopScanning();
        }

        ClearConfirmedRoomVisualization();
        // ManualWallGenerate 단계에서는 raw Scene Understanding mesh / 자동 벽 cube를 숨기고,
        // 상호작용 가능한 selectable surface와 사용자가 만든 Manual Wall만 표시합니다.
        SetAutoRoomVisualsActive(false);
        SetManualWallVisualsActive(true);

        if (manualWallBuilder != null)
        {
            manualWallBuilder.SetManualCreateMode();

            if (rebuildSelectableSurfacesWhenSwitching)
            {
                manualWallBuilder.RebuildSelectableSurfaces();
            }
        }

        // RebuildSelectableSurfaces 직후 또는 아직 진행 중이던 SU 갱신 직후 자동 mesh가 다시 보이는 것을 방지합니다.
        SetAutoRoomVisualsActive(false);

        finalRoomClosed = false;
        floodReachedOutside = false;
        selectedCellCount = 0;
        confirmedBoundarySegmentCount = 0;
        openEndpointCount = -1;
        manualWallCount = CountManualWalls();

        SetStatus("Switched to Manual Wall generation.");
        ApplyState(WorkflowState.ManualWallGenerate);
    }

    public void ReturnToRoomBuild()
    {
        if (manualWallBuilder != null)
        {
            manualWallBuilder.SetNoneMode();
            manualWallBuilder.ClearSelectableSurfaces();

            if (clearManualWallsWhenReturningToRoomBuild)
            {
                manualWallBuilder.ClearManualWalls();
            }
        }

        ClearConfirmedRoomVisualization();
        SetAutoRoomVisualsActive(true);
        SetManualWallVisualsActive(true);

        if (scanner != null)
        {
            scanner.UnlockRoom();
        }

        finalRoomClosed = false;
        floodReachedOutside = false;
        selectedCellCount = 0;
        confirmedBoundarySegmentCount = 0;
        openEndpointCount = -1;
        manualWallCount = CountManualWalls();

        SetStatus("Returned to RoomBuild. Manual edit state cleared.");
        ApplyState(WorkflowState.RoomBuild);
    }

    public void ConfirmManualWalls()
    {
        if (scanner == null || scanner.currentRoom == null)
        {
            SetStatus("Cannot confirm manual walls. Current room is null.");
            return;
        }

        List<Segment2D> allSegments = BuildFinalBoundarySegments();

        if (normalizeBoundarySegmentsBeforeConfirm)
        {
            allSegments = NormalizeBoundarySegments(allSegments);
        }

        normalizedBoundarySegmentsForConfirm.Clear();
        normalizedBoundarySegmentsForConfirm.AddRange(allSegments);

        if (allSegments.Count < 3)
        {
            finalRoomClosed = false;
            floodReachedOutside = true;
            SetStatus("Cannot confirm. Boundary segment count is less than 3.");
            ApplyState(WorkflowState.ManualWallGenerate);
            return;
        }

        if (!TryRunUserRoomFloodFill(allSegments))
        {
            finalRoomClosed = false;
            SetStatus("Cannot confirm. User position is not inside a valid room area.");
            ApplyState(WorkflowState.ManualWallGenerate);
            return;
        }

        finalRoomClosed = !floodReachedOutside;

        if (!finalRoomClosed)
        {
            SetStatus("Cannot confirm. Flood fill escaped outside boundary. Room is not closed.");
            ApplyState(WorkflowState.ManualWallGenerate);
            return;
        }

        BuildConfirmedBoundarySegments(allSegments);

        if (!BuildConfirmedRoomVisualization())
        {
            finalRoomClosed = false;
            SetStatus("Cannot confirm. Final boundary polygon mesh construction failed. Check open endpoints or disconnected manual walls.");
            ApplyState(WorkflowState.ManualWallGenerate);
            return;
        }

        if (manualWallBuilder != null)
        {
            manualWallBuilder.SetNoneMode();
            manualWallBuilder.ClearSelectableSurfaces();
        }

        if (hideManualWallVisualsInConfirmRoom)
        {
            SetManualWallVisualsActive(false);
        }

        if (hideAutoRoomVisualsInConfirmRoom)
        {
            SetAutoRoomVisualsActive(false);
        }

        manualWallCount = CountManualWalls();
        selectedCellCount = selectedCells.Count;
        confirmedBoundarySegmentCount = confirmedBoundarySegments.Count;
        openEndpointCount = 0;

        SetStatus(
            $"Manual walls confirmed. user room closed:true, cells:{selectedCellCount}, boundary:{confirmedBoundarySegmentCount}, manual walls:{manualWallCount}"
        );

        ApplyState(WorkflowState.ManualWallConfirmed);
    }

    public void BackToManualWallGeneration()
    {
        if (currentState != WorkflowState.ManualWallConfirmed)
        {
            SetStatus("Back to Manual Wall is only available from Confirm Room state.");
            return;
        }

        ClearConfirmedRoomVisualization();

        if (restoreAutoRoomVisualsWhenBackToManual)
        {
            // ManualWallGenerate로 돌아올 때도 사용자에게는 selectable surface만 보여줍니다.
            SetAutoRoomVisualsActive(false);
        }

        SetManualWallVisualsActive(true);

        if (manualWallBuilder != null)
        {
            manualWallBuilder.SetManualCreateMode();
            manualWallBuilder.RebuildSelectableSurfaces();
        }

        finalRoomClosed = false;
        floodReachedOutside = false;
        selectedCellCount = 0;
        confirmedBoundarySegmentCount = 0;
        openEndpointCount = -1;
        manualWallCount = CountManualWalls();

        SetStatus("Returned to Manual Wall generation. Existing manual walls are kept.");
        ApplyState(WorkflowState.ManualWallGenerate);
    }

    public void ConfirmRoomAndStartFurnitureRecognitionPlaceholder()
    {
        if (currentState != WorkflowState.ManualWallConfirmed)
        {
            SetStatus("Confirm Room blocked. Confirm manual walls first.");
            return;
        }

        if (!finalRoomClosed)
        {
            SetStatus("Confirm Room blocked. Final room is not closed.");
            return;
        }

        ApplyState(WorkflowState.FurnitureRecognition);

        // TODO:
        // 가구 인식 기능이 준비되면 여기서 호출.
        SetStatus("Furniture recognition placeholder. Not implemented yet.");
    }

    private void ApplyState(WorkflowState nextState)
    {
        currentState = nextState;

        if (roomBuildButtonGroup != null)
        {
            roomBuildButtonGroup.SetActive(nextState == WorkflowState.RoomBuild);
        }

        if (manualWallButtonGroup != null)
        {
            manualWallButtonGroup.SetActive(nextState == WorkflowState.ManualWallGenerate);
        }

        if (confirmRoomButtonGroup != null)
        {
            confirmRoomButtonGroup.SetActive(nextState == WorkflowState.ManualWallConfirmed);
        }

        if (furnitureRecognitionButtonGroup != null)
        {
            furnitureRecognitionButtonGroup.SetActive(nextState == WorkflowState.FurnitureRecognition);
        }

        UpdateStatusText();
    }

    private List<Segment2D> BuildFinalBoundarySegments()
    {
        List<Segment2D> result = new List<Segment2D>();

        if (scanner != null && scanner.currentRoom != null)
        {
            if (includeDetectedWallsInClosedCheck)
            {
                foreach (WallSegment wall in scanner.currentRoom.detectedWalls)
                {
                    result.Add(new Segment2D
                    {
                        start = wall.start,
                        end = wall.end
                    });
                }
            }

            if (includeClosureEdgesInClosedCheck)
            {
                foreach (WallSegment edge in scanner.currentRoom.closureEdges)
                {
                    result.Add(new Segment2D
                    {
                        start = edge.start,
                        end = edge.end
                    });
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

        // Transform 이름/계층에 의존하지 않고 ManualWallBuilder가 관리하는 실제 segment 데이터를 사용합니다.
        // 자르기 후 조각이 생기거나 이름이 바뀌어도 Confirm 대상에서 누락되지 않게 하기 위함입니다.
        List<ManualWallBuilder.ManualWallSegmentSnapshot> snapshots =
            manualWallBuilder.GetManualWallSegmentsSnapshot();

        foreach (ManualWallBuilder.ManualWallSegmentSnapshot snapshot in snapshots)
        {
            if (Vector2.Distance(snapshot.start, snapshot.end) < boundaryMinSegmentLength)
            {
                continue;
            }

            target.Add(new Segment2D
            {
                start = snapshot.start,
                end = snapshot.end
            });
        }
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
        return cell.x >= 0 &&
               cell.y >= 0 &&
               cell.x < gridWidth &&
               cell.y < gridHeight;
    }

    private bool IsBorderCell(Vector2Int cell)
    {
        return cell.x == 0 ||
               cell.y == 0 ||
               cell.x == gridWidth - 1 ||
               cell.y == gridHeight - 1;
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

    private bool TryFindNearestFreeCell(
        Vector2Int start,
        List<Segment2D> segments,
        out Vector2Int result)
    {
        for (int r = 1; r <= nearestFreeCellSearchRadius; r++)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                for (int dy = -r; dy <= r; dy++)
                {
                    Vector2Int cell = start + new Vector2Int(dx, dy);

                    if (!IsInsideGrid(cell))
                    {
                        continue;
                    }

                    if (!IsBlockedCell(cell, segments))
                    {
                        result = cell;
                        return true;
                    }
                }
            }
        }

        result = start;
        return false;
    }

    private bool MovementCrossesAnySegment(
        Vector2 from,
        Vector2 to,
        List<Segment2D> segments)
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

            bool aSelected = TryWorldToCell(sideA, out Vector2Int cellA) &&
                             selectedCells.Contains(cellA);

            bool bSelected = TryWorldToCell(sideB, out Vector2Int cellB) &&
                             selectedCells.Contains(cellB);

            if (aSelected || bSelected)
            {
                return true;
            }
        }

        return false;
    }

    private bool BuildConfirmedRoomVisualization()
    {
        ClearConfirmedRoomObjectsOnly();

        if (scanner == null || scanner.currentRoom == null)
        {
            return false;
        }

        if (confirmedRoomRoot == null)
        {
            confirmedRoomRoot = new GameObject("ConfirmedRoomRoot").transform;
        }

        usedGridHorizontalFallback = false;
        usedSelectedCellBoundaryPolygonFallback = false;

        List<Segment2D> wallSegments = BuildConfirmedWallSegmentsForVisualization();

        if (wallSegments == null || wallSegments.Count == 0)
        {
            return false;
        }

        float floorY = scanner.currentRoom.floorY + confirmedFloorYOffset;
        float ceilingY = scanner.currentRoom.ceilingY + confirmedCeilingYOffset;

        if (useConfirmedHorizontalBoxSlabs)
        {
            BuildConfirmedHorizontalBoxSlabsFromSegments(wallSegments, floorY, ceilingY);
        }
        else
        {
            if (!BuildConfirmedPolygonHorizontalSurfaces(wallSegments, floorY, ceilingY))
            {
                return false;
            }
        }

        BuildConfirmedWallCubesFromSegments(wallSegments, floorY, ceilingY);

        return true;
    }

    private bool BuildConfirmedPolygonHorizontalSurfaces(
        List<Segment2D> wallSegments,
        float floorY,
        float ceilingY)
    {
        List<Vector2> polygon = null;
        List<int> polygonTriangles = null;

        bool hasSegmentPolygon = false;

        if (TryBuildBoundaryPolygonFromSegments(wallSegments, out List<Vector2> segmentPolygon))
        {
            hasSegmentPolygon = TryPreparePolygonForMesh(
                segmentPolygon,
                out polygon,
                out polygonTriangles
            );
        }

        if (!hasSegmentPolygon)
        {
            bool hasFallbackPolygon = false;

            if (useSelectedCellBoundaryPolygonFallback)
            {
                hasFallbackPolygon = TryBuildSelectedCellFallbackPolygon(
                    out polygon,
                    out polygonTriangles
                );
            }

            if (!hasFallbackPolygon)
            {
                return false;
            }

            usedSelectedCellBoundaryPolygonFallback = true;
        }

        BuildPolygonHorizontalSurface("ConfirmedRoom_Floor", floorY, normalUp: true, polygon, polygonTriangles);
        BuildPolygonHorizontalSurface("ConfirmedRoom_Ceiling", ceilingY, normalUp: false, polygon, polygonTriangles);

        return true;
    }

    private void BuildConfirmedHorizontalBoxSlabsFromSegments(
        List<Segment2D> segments,
        float floorY,
        float ceilingY)
    {
        if (!TryCalculateSegmentBounds(segments, out Vector2 min, out Vector2 max))
        {
            return;
        }

        float padding = Mathf.Max(0.0f, confirmedHorizontalSlabPadding);
        float thickness = Mathf.Max(0.01f, confirmedHorizontalSlabThickness);

        min -= Vector2.one * padding;
        max += Vector2.one * padding;

        Vector2 center = (min + max) * 0.5f;
        Vector2 size = max - min;

        // Floor slab은 top face가 floorY에 오도록 아래쪽으로 배치한다.
        CreateConfirmedHorizontalBoxSlab(
            "ConfirmedRoom_Floor",
            new Vector3(center.x, floorY - thickness * 0.5f, center.y),
            new Vector3(Mathf.Max(0.01f, size.x), thickness, Mathf.Max(0.01f, size.y))
        );

        // Ceiling slab은 bottom face가 ceilingY에 오도록 위쪽으로 배치한다.
        CreateConfirmedHorizontalBoxSlab(
            "ConfirmedRoom_Ceiling",
            new Vector3(center.x, ceilingY + thickness * 0.5f, center.y),
            new Vector3(Mathf.Max(0.01f, size.x), thickness, Mathf.Max(0.01f, size.y))
        );
    }

    private bool TryCalculateSegmentBounds(
        List<Segment2D> segments,
        out Vector2 min,
        out Vector2 max)
    {
        min = new Vector2(float.MaxValue, float.MaxValue);
        max = new Vector2(float.MinValue, float.MinValue);

        if (segments == null || segments.Count == 0)
        {
            return false;
        }

        bool hasPoint = false;

        foreach (Segment2D segment in segments)
        {
            IncludePointInBounds(segment.start, ref min, ref max, ref hasPoint);
            IncludePointInBounds(segment.end, ref min, ref max, ref hasPoint);
        }

        return hasPoint &&
               max.x > min.x + 0.001f &&
               max.y > min.y + 0.001f;
    }

    private void IncludePointInBounds(
        Vector2 point,
        ref Vector2 min,
        ref Vector2 max,
        ref bool hasPoint)
    {
        min.x = Mathf.Min(min.x, point.x);
        min.y = Mathf.Min(min.y, point.y);
        max.x = Mathf.Max(max.x, point.x);
        max.y = Mathf.Max(max.y, point.y);
        hasPoint = true;
    }

    private void CreateConfirmedHorizontalBoxSlab(
        string objectName,
        Vector3 center,
        Vector3 scale)
    {
        GameObject slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
        slab.name = objectName;
        slab.transform.SetParent(confirmedRoomRoot, true);
        slab.transform.position = center;
        slab.transform.rotation = Quaternion.identity;
        slab.transform.localScale = scale;

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
    }

    private List<Segment2D> BuildConfirmedWallSegmentsForVisualization()
    {
        List<Segment2D> result = new List<Segment2D>();

        foreach (Segment2D segment in confirmedBoundarySegments)
        {
            AddUniqueCandidateSegment(result, segment);
        }

        // selected cell 인접 판정이 과하게 보수적으로 동작해 일부 벽이 빠지는 경우를 대비해
        // confirm 직전 정규화된 boundary segment 중 현재 polygon 경계와 맞닿는 segment도 벽 cube로 유지한다.
        foreach (Segment2D segment in normalizedBoundarySegmentsForConfirm)
        {
            if (IsSegmentAlreadyCovered(result, segment))
            {
                continue;
            }

            if (IsSegmentAdjacentToSelectedRoom(segment))
            {
                AddUniqueCandidateSegment(result, segment);
            }
        }

        return result;
    }

    private bool IsSegmentAlreadyCovered(List<Segment2D> segments, Segment2D candidate)
    {
        foreach (Segment2D segment in segments)
        {
            bool sameDirection =
                Vector2.Distance(segment.start, candidate.start) <= endpointSnapDistance &&
                Vector2.Distance(segment.end, candidate.end) <= endpointSnapDistance;

            bool reverseDirection =
                Vector2.Distance(segment.start, candidate.end) <= endpointSnapDistance &&
                Vector2.Distance(segment.end, candidate.start) <= endpointSnapDistance;

            if (sameDirection || reverseDirection)
            {
                return true;
            }
        }

        return false;
    }

    private bool TryPreparePolygonForMesh(
        List<Vector2> sourcePolygon,
        out List<Vector2> polygon,
        out List<int> polygonTriangles)
    {
        polygon = sourcePolygon != null
            ? new List<Vector2>(sourcePolygon)
            : new List<Vector2>();

        polygonTriangles = new List<int>();

        SimplifyPolygonInPlace(polygon, Mathf.Max(boundaryMinSegmentLength, endpointSnapDistance * 0.25f));

        if (polygon.Count < 3)
        {
            return false;
        }

        if (SignedArea(polygon) < 0.0f)
        {
            polygon.Reverse();
        }

        return TryTriangulatePolygon(polygon, out polygonTriangles);
    }

    private bool TryBuildSelectedCellFallbackPolygon(
        out List<Vector2> polygon,
        out List<int> polygonTriangles)
    {
        polygon = new List<Vector2>();
        polygonTriangles = new List<int>();

        if (!TryBuildSelectedCellBoundaryPolygon(out List<Vector2> selectedCellPolygon))
        {
            return false;
        }

        SimplifyPolygonDouglasPeuckerClosed(
            selectedCellPolygon,
            Mathf.Max(gridCellSize * 0.5f, selectedCellFallbackSimplifyTolerance)
        );

        if (snapSelectedCellFallbackPolygonToBoundary)
        {
            SnapPolygonVerticesToBoundarySegments(
                selectedCellPolygon,
                normalizedBoundarySegmentsForConfirm,
                selectedCellFallbackSnapDistance
            );
        }

        SimplifyPolygonInPlace(selectedCellPolygon, Mathf.Max(boundaryMinSegmentLength, gridCellSize * 0.25f));

        return TryPreparePolygonForMesh(selectedCellPolygon, out polygon, out polygonTriangles);
    }

    private void BuildPolygonHorizontalSurface(
        string objectName,
        float y,
        bool normalUp,
        List<Vector2> polygon,
        List<int> polygonTriangles)
    {
        Mesh mesh = new Mesh();

        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();

        foreach (Vector2 p in polygon)
        {
            vertices.Add(new Vector3(p.x, y, p.y));
        }

        for (int i = 0; i < polygonTriangles.Count; i += 3)
        {
            int a = polygonTriangles[i];
            int b = polygonTriangles[i + 1];
            int c = polygonTriangles[i + 2];

            // XZ 2D 좌표에서 CCW인 삼각형은 Unity의 X/Y/Z 공간에서 -Y normal이 됩니다.
            // 바닥 normalUp은 winding을 반대로 넣고, 천장 normalDown은 그대로 넣습니다.
            if (normalUp)
            {
                triangles.Add(a);
                triangles.Add(c);
                triangles.Add(b);
            }
            else
            {
                triangles.Add(a);
                triangles.Add(b);
                triangles.Add(c);
            }
        }

        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        GameObject surface = new GameObject(objectName);
        surface.transform.SetParent(confirmedRoomRoot, true);

        MeshFilter filter = surface.AddComponent<MeshFilter>();
        MeshRenderer renderer = surface.AddComponent<MeshRenderer>();

        filter.sharedMesh = mesh;
        renderer.sharedMaterial = GetHorizontalMaterial();

        if (addConfirmedRoomMeshColliders)
        {
            MeshCollider meshCollider = surface.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = mesh;
        }
    }


    private void BuildConfirmedWallCubesFromSegments(List<Segment2D> segments, float floorY, float ceilingY)
    {
        if (segments == null || segments.Count == 0)
        {
            return;
        }

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

        wall.transform.position = new Vector3(
            mid.x,
            floorY + height * 0.5f,
            mid.y
        );

        wall.transform.rotation = Quaternion.Euler(0.0f, yaw, 0.0f);
        wall.transform.localScale = new Vector3(
            confirmedWallThickness,
            height,
            length
        );

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
    }

    private void BuildGridHorizontalSurface(string objectName, float y, bool normalUp)
    {
        if (selectedCells.Count == 0)
        {
            return;
        }

        Mesh mesh = new Mesh();

        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();

        foreach (Vector2Int cell in selectedCells)
        {
            Vector2 center = CellToWorld(cell);
            float h = gridCellSize * 0.5f;

            int startIndex = vertices.Count;

            vertices.Add(new Vector3(center.x - h, y, center.y - h));
            vertices.Add(new Vector3(center.x - h, y, center.y + h));
            vertices.Add(new Vector3(center.x + h, y, center.y + h));
            vertices.Add(new Vector3(center.x + h, y, center.y - h));

            if (normalUp)
            {
                triangles.Add(startIndex);
                triangles.Add(startIndex + 1);
                triangles.Add(startIndex + 2);

                triangles.Add(startIndex);
                triangles.Add(startIndex + 2);
                triangles.Add(startIndex + 3);
            }
            else
            {
                triangles.Add(startIndex);
                triangles.Add(startIndex + 2);
                triangles.Add(startIndex + 1);

                triangles.Add(startIndex);
                triangles.Add(startIndex + 3);
                triangles.Add(startIndex + 2);
            }
        }

        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        GameObject surface = new GameObject(objectName);
        surface.transform.SetParent(confirmedRoomRoot, true);

        MeshFilter filter = surface.AddComponent<MeshFilter>();
        MeshRenderer renderer = surface.AddComponent<MeshRenderer>();

        filter.sharedMesh = mesh;
        renderer.sharedMaterial = GetHorizontalMaterial();

        if (addConfirmedRoomMeshColliders)
        {
            MeshCollider meshCollider = surface.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = mesh;
        }
    }

    private void SnapPolygonVerticesToBoundarySegments(
        List<Vector2> polygon,
        List<Segment2D> boundarySegments,
        float maxDistance)
    {
        if (polygon == null || boundarySegments == null || maxDistance <= 0.0f)
        {
            return;
        }

        for (int i = 0; i < polygon.Count; i++)
        {
            Vector2 p = polygon[i];
            Vector2 best = p;
            float bestDistance = maxDistance;

            foreach (Segment2D segment in boundarySegments)
            {
                if (Vector2.Distance(segment.start, segment.end) < boundaryMinSegmentLength)
                {
                    continue;
                }

                Vector2 projected = ClosestPointOnSegment(p, segment.start, segment.end);
                float distance = Vector2.Distance(p, projected);

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = projected;
                }
            }

            polygon[i] = best;
        }
    }

    private void SimplifyPolygonDouglasPeuckerClosed(List<Vector2> polygon, float tolerance)
    {
        if (polygon == null || polygon.Count < 5 || tolerance <= 0.0f)
        {
            return;
        }

        int firstIndex = 0;
        int secondIndex = 0;
        float maxDistance = 0.0f;

        for (int i = 0; i < polygon.Count; i++)
        {
            for (int j = i + 1; j < polygon.Count; j++)
            {
                float distance = (polygon[i] - polygon[j]).sqrMagnitude;

                if (distance > maxDistance)
                {
                    maxDistance = distance;
                    firstIndex = i;
                    secondIndex = j;
                }
            }
        }

        if (firstIndex == secondIndex)
        {
            return;
        }

        List<Vector2> chainA = BuildCircularChain(polygon, firstIndex, secondIndex);
        List<Vector2> chainB = BuildCircularChain(polygon, secondIndex, firstIndex);

        List<Vector2> simplifiedA = SimplifyOpenPolylineDouglasPeucker(chainA, tolerance);
        List<Vector2> simplifiedB = SimplifyOpenPolylineDouglasPeucker(chainB, tolerance);

        List<Vector2> combined = new List<Vector2>();
        combined.AddRange(simplifiedA);

        for (int i = 1; i < simplifiedB.Count - 1; i++)
        {
            combined.Add(simplifiedB[i]);
        }

        if (combined.Count >= 3)
        {
            polygon.Clear();
            polygon.AddRange(combined);
        }
    }

    private static List<Vector2> BuildCircularChain(List<Vector2> polygon, int startIndex, int endIndex)
    {
        List<Vector2> chain = new List<Vector2>();

        int index = startIndex;
        int guard = 0;

        while (guard <= polygon.Count)
        {
            chain.Add(polygon[index]);

            if (index == endIndex)
            {
                break;
            }

            index = (index + 1) % polygon.Count;
            guard++;
        }

        return chain;
    }

    private static List<Vector2> SimplifyOpenPolylineDouglasPeucker(List<Vector2> points, float tolerance)
    {
        if (points == null || points.Count <= 2)
        {
            return points != null ? new List<Vector2>(points) : new List<Vector2>();
        }

        float maxDistance = 0.0f;
        int index = -1;

        Vector2 start = points[0];
        Vector2 end = points[points.Count - 1];

        for (int i = 1; i < points.Count - 1; i++)
        {
            float distance = DistancePointToSegment(points[i], start, end);

            if (distance > maxDistance)
            {
                maxDistance = distance;
                index = i;
            }
        }

        if (maxDistance <= tolerance || index < 0)
        {
            return new List<Vector2> { start, end };
        }

        List<Vector2> left = SimplifyOpenPolylineDouglasPeucker(points.GetRange(0, index + 1), tolerance);
        List<Vector2> right = SimplifyOpenPolylineDouglasPeucker(points.GetRange(index, points.Count - index), tolerance);

        left.RemoveAt(left.Count - 1);
        left.AddRange(right);
        return left;
    }

    private bool TryBuildSelectedCellBoundaryPolygon(out List<Vector2> polygon)
    {
        polygon = new List<Vector2>();

        if (selectedCells.Count == 0)
        {
            return false;
        }

        List<GridBoundaryEdge> edges = new List<GridBoundaryEdge>();

        foreach (Vector2Int cell in selectedCells)
        {
            Vector2Int left = cell + new Vector2Int(-1, 0);
            Vector2Int right = cell + new Vector2Int(1, 0);
            Vector2Int down = cell + new Vector2Int(0, -1);
            Vector2Int up = cell + new Vector2Int(0, 1);

            Vector2Int bl = new Vector2Int(cell.x, cell.y);
            Vector2Int br = new Vector2Int(cell.x + 1, cell.y);
            Vector2Int tr = new Vector2Int(cell.x + 1, cell.y + 1);
            Vector2Int tl = new Vector2Int(cell.x, cell.y + 1);

            // CCW 방향으로 boundary edge를 만든다. interior가 항상 edge의 왼쪽에 오도록 한다.
            if (!selectedCells.Contains(down))
            {
                edges.Add(new GridBoundaryEdge { a = bl, b = br });
            }

            if (!selectedCells.Contains(right))
            {
                edges.Add(new GridBoundaryEdge { a = br, b = tr });
            }

            if (!selectedCells.Contains(up))
            {
                edges.Add(new GridBoundaryEdge { a = tr, b = tl });
            }

            if (!selectedCells.Contains(left))
            {
                edges.Add(new GridBoundaryEdge { a = tl, b = bl });
            }
        }

        if (edges.Count < 3)
        {
            return false;
        }

        bool[] used = new bool[edges.Count];
        List<Vector2Int> bestLoop = null;
        float bestArea = 0.0f;

        for (int startIndex = 0; startIndex < edges.Count; startIndex++)
        {
            if (used[startIndex])
            {
                continue;
            }

            List<Vector2Int> loop = new List<Vector2Int>();
            GridBoundaryEdge first = edges[startIndex];
            used[startIndex] = true;

            loop.Add(first.a);
            loop.Add(first.b);

            Vector2Int loopStart = first.a;
            Vector2Int current = first.b;

            int guard = 0;

            while (current != loopStart && guard < edges.Count + 4)
            {
                guard++;

                int nextIndex = -1;
                bool reverse = false;

                for (int i = 0; i < edges.Count; i++)
                {
                    if (used[i])
                    {
                        continue;
                    }

                    if (edges[i].a == current)
                    {
                        nextIndex = i;
                        reverse = false;
                        break;
                    }

                    if (edges[i].b == current)
                    {
                        nextIndex = i;
                        reverse = true;
                        break;
                    }
                }

                if (nextIndex < 0)
                {
                    break;
                }

                used[nextIndex] = true;
                GridBoundaryEdge next = edges[nextIndex];
                current = reverse ? next.a : next.b;
                loop.Add(current);
            }

            if (loop.Count < 4 || loop[loop.Count - 1] != loopStart)
            {
                continue;
            }

            loop.RemoveAt(loop.Count - 1);

            List<Vector2> worldLoop = new List<Vector2>();

            foreach (Vector2Int v in loop)
            {
                worldLoop.Add(GridVertexToWorld(v));
            }

            SimplifyPolygonInPlace(worldLoop, gridCellSize * 0.25f);

            if (worldLoop.Count < 3)
            {
                continue;
            }

            float area = Mathf.Abs(SignedArea(worldLoop));

            if (area > bestArea)
            {
                bestArea = area;
                bestLoop = loop;
                polygon = worldLoop;
            }
        }

        if (bestLoop == null || polygon.Count < 3)
        {
            return false;
        }

        if (SignedArea(polygon) < 0.0f)
        {
            polygon.Reverse();
        }

        return true;
    }

    private Vector2 GridVertexToWorld(Vector2Int vertex)
    {
        return new Vector2(
            gridMin.x + vertex.x * gridCellSize,
            gridMin.y + vertex.y * gridCellSize
        );
    }

    private bool TryBuildBoundaryPolygonFromSegments(List<Segment2D> sourceSegments, out List<Vector2> polygon)
    {
        polygon = new List<Vector2>();

        List<Segment2D> segments = new List<Segment2D>();

        if (sourceSegments != null)
        {
            foreach (Segment2D segment in sourceSegments)
            {
                AddUniqueCandidateSegment(segments, segment);
            }
        }

        if (segments.Count < 3)
        {
            return false;
        }

        float joinDistance = Mathf.Max(endpointSnapDistance * 1.5f, gridCellSize * 1.5f, 0.18f);
        Vector2 referencePoint = GetUserReferencePoint2D();

        List<Vector2> bestContainingLoop = null;
        float bestContainingArea = 0.0f;
        List<Vector2> bestAnyLoop = null;
        float bestAnyArea = 0.0f;

        for (int i = 0; i < segments.Count; i++)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                bool reverseFirst = pass == 1;

                if (!TryTraceClosedLoopFromSegment(
                        segments,
                        i,
                        reverseFirst,
                        joinDistance,
                        out List<Vector2> candidate))
                {
                    continue;
                }

                SimplifyPolygonInPlace(candidate, joinDistance * 0.5f);

                if (candidate.Count < 3)
                {
                    continue;
                }

                float area = Mathf.Abs(SignedArea(candidate));

                if (area <= 0.0001f)
                {
                    continue;
                }

                if (SignedArea(candidate) < 0.0f)
                {
                    candidate.Reverse();
                }

                if (area > bestAnyArea)
                {
                    bestAnyArea = area;
                    bestAnyLoop = candidate;
                }

                if (IsPointInsidePolygon(referencePoint, candidate) && area > bestContainingArea)
                {
                    bestContainingArea = area;
                    bestContainingLoop = candidate;
                }
            }
        }

        if (bestContainingLoop != null)
        {
            polygon = bestContainingLoop;
            return true;
        }

        if (bestAnyLoop != null)
        {
            polygon = bestAnyLoop;
            return true;
        }

        return false;
    }

    private bool TryBuildConfirmedBoundaryPolygon(out List<Vector2> polygon)
    {
        polygon = new List<Vector2>();

        List<Segment2D> segments = new List<Segment2D>();

        foreach (Segment2D segment in confirmedBoundarySegments)
        {
            AddUniqueCandidateSegment(segments, segment);
        }

        // selected cell 인접 판정에서 빠진 Manual Wall이 있을 수 있으므로,
        // polygon 생성 후보에는 confirm 직전 정규화된 전체 boundary도 함께 넣습니다.
        foreach (Segment2D segment in normalizedBoundarySegmentsForConfirm)
        {
            AddUniqueCandidateSegment(segments, segment);
        }

        if (segments.Count < 3)
        {
            return false;
        }

        float joinDistance = Mathf.Max(endpointSnapDistance * 1.5f, gridCellSize * 1.5f, 0.18f);
        Vector2 referencePoint = GetUserReferencePoint2D();

        List<Vector2> bestContainingLoop = null;
        float bestContainingArea = 0.0f;
        List<Vector2> bestAnyLoop = null;
        float bestAnyArea = 0.0f;

        for (int i = 0; i < segments.Count; i++)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                bool reverseFirst = pass == 1;

                if (!TryTraceClosedLoopFromSegment(
                        segments,
                        i,
                        reverseFirst,
                        joinDistance,
                        out List<Vector2> candidate))
                {
                    continue;
                }

                SimplifyPolygonInPlace(candidate, joinDistance * 0.5f);

                if (candidate.Count < 3)
                {
                    continue;
                }

                float area = Mathf.Abs(SignedArea(candidate));

                if (area <= 0.0001f)
                {
                    continue;
                }

                if (SignedArea(candidate) < 0.0f)
                {
                    candidate.Reverse();
                }

                if (area > bestAnyArea)
                {
                    bestAnyArea = area;
                    bestAnyLoop = candidate;
                }

                if (IsPointInsidePolygon(referencePoint, candidate) && area > bestContainingArea)
                {
                    bestContainingArea = area;
                    bestContainingLoop = candidate;
                }
            }
        }

        if (bestContainingLoop != null)
        {
            polygon = bestContainingLoop;
            return true;
        }

        if (bestAnyLoop != null)
        {
            polygon = bestAnyLoop;
            return true;
        }

        return false;
    }

    private void AddUniqueCandidateSegment(List<Segment2D> target, Segment2D segment)
    {
        if (Vector2.Distance(segment.start, segment.end) < boundaryMinSegmentLength)
        {
            return;
        }

        for (int i = 0; i < target.Count; i++)
        {
            Segment2D existing = target[i];

            bool sameDirection = Vector2.Distance(existing.start, segment.start) <= endpointSnapDistance &&
                                 Vector2.Distance(existing.end, segment.end) <= endpointSnapDistance;

            bool reverseDirection = Vector2.Distance(existing.start, segment.end) <= endpointSnapDistance &&
                                    Vector2.Distance(existing.end, segment.start) <= endpointSnapDistance;

            if (sameDirection || reverseDirection)
            {
                return;
            }
        }

        target.Add(segment);
    }

    private bool TryTraceClosedLoopFromSegment(
        List<Segment2D> segments,
        int startIndex,
        bool reverseFirst,
        float joinDistance,
        out List<Vector2> polygon)
    {
        polygon = new List<Vector2>();

        if (segments == null || startIndex < 0 || startIndex >= segments.Count)
        {
            return false;
        }

        bool[] used = new bool[segments.Count];
        Segment2D first = segments[startIndex];
        used[startIndex] = true;

        Vector2 loopStart = reverseFirst ? first.end : first.start;
        Vector2 currentEnd = reverseFirst ? first.start : first.end;

        polygon.Add(loopStart);
        polygon.Add(currentEnd);

        for (int step = 0; step < segments.Count + 4; step++)
        {
            if (Vector2.Distance(currentEnd, loopStart) <= joinDistance && polygon.Count >= 4)
            {
                polygon[polygon.Count - 1] = loopStart;
                polygon.RemoveAt(polygon.Count - 1);
                return polygon.Count >= 3;
            }

            int bestIndex = -1;
            bool bestReverse = false;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < segments.Count; i++)
            {
                if (used[i])
                {
                    continue;
                }

                float dStart = Vector2.Distance(currentEnd, segments[i].start);
                float dEnd = Vector2.Distance(currentEnd, segments[i].end);

                if (dStart < bestDistance)
                {
                    bestDistance = dStart;
                    bestIndex = i;
                    bestReverse = false;
                }

                if (dEnd < bestDistance)
                {
                    bestDistance = dEnd;
                    bestIndex = i;
                    bestReverse = true;
                }
            }

            if (bestIndex < 0 || bestDistance > joinDistance)
            {
                return false;
            }

            used[bestIndex] = true;
            Segment2D next = segments[bestIndex];
            currentEnd = bestReverse ? next.start : next.end;
            polygon.Add(currentEnd);
        }

        return false;
    }

    private Vector2 GetUserReferencePoint2D()
    {
        if (Camera.main != null)
        {
            Vector3 cameraPosition = Camera.main.transform.position;
            return new Vector2(cameraPosition.x, cameraPosition.z);
        }

        if (selectedCells.Count > 0)
        {
            Vector2 sum = Vector2.zero;

            foreach (Vector2Int cell in selectedCells)
            {
                sum += CellToWorld(cell);
            }

            return sum / selectedCells.Count;
        }

        return Vector2.zero;
    }

    private static bool IsPointInsidePolygon(Vector2 point, List<Vector2> polygon)
    {
        if (polygon == null || polygon.Count < 3)
        {
            return false;
        }

        bool inside = false;

        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            Vector2 pi = polygon[i];
            Vector2 pj = polygon[j];

            float denominator = pj.y - pi.y;

            if (Mathf.Abs(denominator) < 0.000001f)
            {
                continue;
            }

            bool intersects = ((pi.y > point.y) != (pj.y > point.y)) &&
                              (point.x < (pj.x - pi.x) * (point.y - pi.y) /
                               denominator + pi.x);

            if (intersects)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    private void SimplifyPolygonInPlace(List<Vector2> polygon, float duplicateDistance)
    {
        for (int i = polygon.Count - 1; i >= 0; i--)
        {
            int next = (i + 1) % polygon.Count;

            if (Vector2.Distance(polygon[i], polygon[next]) <= duplicateDistance)
            {
                polygon.RemoveAt(next == 0 ? i : next);
            }
        }

        for (int i = polygon.Count - 1; i >= 0; i--)
        {
            int prev = (i - 1 + polygon.Count) % polygon.Count;
            int next = (i + 1) % polygon.Count;

            Vector2 a = (polygon[i] - polygon[prev]).normalized;
            Vector2 b = (polygon[next] - polygon[i]).normalized;

            if (Mathf.Abs(Cross(a, b)) <= 0.02f && Vector2.Dot(a, b) > 0.0f)
            {
                polygon.RemoveAt(i);
            }
        }
    }

    private bool TryTriangulatePolygon(List<Vector2> polygon, out List<int> triangles)
    {
        triangles = new List<int>();

        if (polygon == null || polygon.Count < 3)
        {
            return false;
        }

        List<int> indices = new List<int>();

        for (int i = 0; i < polygon.Count; i++)
        {
            indices.Add(i);
        }

        int guard = 0;

        while (indices.Count > 3 && guard < polygon.Count * polygon.Count)
        {
            guard++;
            bool clipped = false;

            for (int i = 0; i < indices.Count; i++)
            {
                int prevIndex = indices[(i - 1 + indices.Count) % indices.Count];
                int currentIndex = indices[i];
                int nextIndex = indices[(i + 1) % indices.Count];

                Vector2 a = polygon[prevIndex];
                Vector2 b = polygon[currentIndex];
                Vector2 c = polygon[nextIndex];

                if (!IsConvex(a, b, c))
                {
                    continue;
                }

                bool containsPoint = false;

                for (int j = 0; j < indices.Count; j++)
                {
                    int testIndex = indices[j];

                    if (testIndex == prevIndex || testIndex == currentIndex || testIndex == nextIndex)
                    {
                        continue;
                    }

                    if (IsPointInTriangle(polygon[testIndex], a, b, c))
                    {
                        containsPoint = true;
                        break;
                    }
                }

                if (containsPoint)
                {
                    continue;
                }

                triangles.Add(prevIndex);
                triangles.Add(currentIndex);
                triangles.Add(nextIndex);

                indices.RemoveAt(i);
                clipped = true;
                break;
            }

            if (!clipped)
            {
                return false;
            }
        }

        if (indices.Count == 3)
        {
            triangles.Add(indices[0]);
            triangles.Add(indices[1]);
            triangles.Add(indices[2]);
        }

        return triangles.Count >= 3;
    }

    private static bool IsConvex(Vector2 a, Vector2 b, Vector2 c)
    {
        return Cross(b - a, c - b) > 0.000001f;
    }

    private static bool IsPointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float c1 = Cross(b - a, p - a);
        float c2 = Cross(c - b, p - b);
        float c3 = Cross(a - c, p - c);

        bool hasNegative = c1 < 0.0f || c2 < 0.0f || c3 < 0.0f;
        bool hasPositive = c1 > 0.0f || c2 > 0.0f || c3 > 0.0f;

        return !(hasNegative && hasPositive);
    }

    private static float SignedArea(List<Vector2> polygon)
    {
        float area = 0.0f;

        for (int i = 0; i < polygon.Count; i++)
        {
            Vector2 a = polygon[i];
            Vector2 b = polygon[(i + 1) % polygon.Count];
            area += a.x * b.y - b.x * a.y;
        }

        return area * 0.5f;
    }

    private void ClearConfirmedRoomObjectsOnly()
    {
        if (confirmedRoomRoot == null)
        {
            return;
        }

        for (int i = confirmedRoomRoot.childCount - 1; i >= 0; i--)
        {
            Destroy(confirmedRoomRoot.GetChild(i).gameObject);
        }
    }

    private void ClearConfirmedRoomVisualization()
    {
        ClearConfirmedRoomObjectsOnly();
        confirmedBoundarySegments.Clear();
        normalizedBoundarySegmentsForConfirm.Clear();
        selectedCells.Clear();
    }

    private void SetAutoRoomVisualsActive(bool active)
    {
        if (scanner == null || scanner.sceneRoot == null)
        {
            return;
        }

        for (int i = 0; i < scanner.sceneRoot.childCount; i++)
        {
            Transform child = scanner.sceneRoot.GetChild(i);

            if (child == null)
            {
                continue;
            }

            if (child.name == "Raw Scene Understanding Objects" ||
                child.name == "Reconstructed Room")
            {
                child.gameObject.SetActive(active);
            }
        }
    }

    private void SetManualWallVisualsActive(bool active)
    {
        if (manualWallBuilder == null || manualWallBuilder.manualWallRoot == null)
        {
            return;
        }

        Renderer[] renderers =
            manualWallBuilder.manualWallRoot.GetComponentsInChildren<Renderer>(true);

        foreach (Renderer renderer in renderers)
        {
            if (renderer != null)
            {
                renderer.enabled = active;
            }
        }

        Collider[] colliders =
            manualWallBuilder.manualWallRoot.GetComponentsInChildren<Collider>(true);

        foreach (Collider collider in colliders)
        {
            if (collider != null)
            {
                collider.enabled = active;
            }
        }
    }

    private int CountManualWalls()
    {
        if (manualWallBuilder == null)
        {
            return 0;
        }

        return manualWallBuilder.GetManualWallSegmentsSnapshot().Count;
    }

    private List<Segment2D> NormalizeBoundarySegments(List<Segment2D> source)
    {
        List<Segment2D> cleaned = new List<Segment2D>();

        foreach (Segment2D segment in source)
        {
            if (Vector2.Distance(segment.start, segment.end) >= boundaryMinSegmentLength)
            {
                cleaned.Add(segment);
            }
        }

        cleaned = SplitSegmentsAtIntersections(cleaned);
        SnapCloseEndpoints(cleaned);
        MergeCollinearTouchingSegments(cleaned);
        RemoveShortSegments(cleaned);

        return cleaned;
    }

    private List<Segment2D> SplitSegmentsAtIntersections(List<Segment2D> source)
    {
        List<List<float>> splitValues = new List<List<float>>();

        for (int i = 0; i < source.Count; i++)
        {
            splitValues.Add(new List<float> { 0.0f, 1.0f });
        }

        for (int i = 0; i < source.Count; i++)
        {
            Segment2D a = source[i];

            for (int j = i + 1; j < source.Count; j++)
            {
                Segment2D b = source[j];

                if (TryGetSegmentIntersectionPoint(
                        a.start,
                        a.end,
                        b.start,
                        b.end,
                        out Vector2 intersection,
                        out float ta,
                        out float tb))
                {
                    AddSplitValue(splitValues[i], ta);
                    AddSplitValue(splitValues[j], tb);
                }

                TryAddEndpointProjectionSplit(a.start, b.start, b.end, splitValues[j]);
                TryAddEndpointProjectionSplit(a.end, b.start, b.end, splitValues[j]);
                TryAddEndpointProjectionSplit(b.start, a.start, a.end, splitValues[i]);
                TryAddEndpointProjectionSplit(b.end, a.start, a.end, splitValues[i]);
            }
        }

        List<Segment2D> result = new List<Segment2D>();

        for (int i = 0; i < source.Count; i++)
        {
            splitValues[i].Sort();

            for (int k = 0; k < splitValues[i].Count - 1; k++)
            {
                float t0 = splitValues[i][k];
                float t1 = splitValues[i][k + 1];

                if (t1 - t0 < 0.0001f)
                {
                    continue;
                }

                Vector2 start = Vector2.Lerp(source[i].start, source[i].end, t0);
                Vector2 end = Vector2.Lerp(source[i].start, source[i].end, t1);

                if (Vector2.Distance(start, end) < boundaryMinSegmentLength)
                {
                    continue;
                }

                result.Add(new Segment2D
                {
                    start = start,
                    end = end
                });
            }
        }

        return result;
    }

    private void SnapCloseEndpoints(List<Segment2D> segments)
    {
        List<EndpointRef> endpoints = new List<EndpointRef>();

        for (int i = 0; i < segments.Count; i++)
        {
            endpoints.Add(new EndpointRef
            {
                segmentIndex = i,
                isStart = true,
                point = segments[i].start
            });

            endpoints.Add(new EndpointRef
            {
                segmentIndex = i,
                isStart = false,
                point = segments[i].end
            });
        }

        bool[] visited = new bool[endpoints.Count];

        for (int i = 0; i < endpoints.Count; i++)
        {
            if (visited[i])
            {
                continue;
            }

            Vector2 sum = endpoints[i].point;
            int count = 1;
            visited[i] = true;

            for (int j = i + 1; j < endpoints.Count; j++)
            {
                if (visited[j])
                {
                    continue;
                }

                if (Vector2.Distance(endpoints[i].point, endpoints[j].point) <= endpointSnapDistance)
                {
                    sum += endpoints[j].point;
                    count++;
                    visited[j] = true;
                }
            }

            Vector2 snapped = sum / count;

            for (int j = 0; j < endpoints.Count; j++)
            {
                if (Vector2.Distance(endpoints[j].point, endpoints[i].point) > endpointSnapDistance)
                {
                    continue;
                }

                Segment2D segment = segments[endpoints[j].segmentIndex];

                if (endpoints[j].isStart)
                {
                    segment.start = snapped;
                }
                else
                {
                    segment.end = snapped;
                }

                segments[endpoints[j].segmentIndex] = segment;
            }
        }
    }

    private void MergeCollinearTouchingSegments(List<Segment2D> segments)
    {
        bool changed = true;

        while (changed)
        {
            changed = false;

            for (int i = 0; i < segments.Count && !changed; i++)
            {
                for (int j = i + 1; j < segments.Count; j++)
                {
                    if (TryMergeCollinearSegments(segments[i], segments[j], out Segment2D merged))
                    {
                        segments[i] = merged;
                        segments.RemoveAt(j);
                        changed = true;
                        break;
                    }
                }
            }
        }
    }

    private bool TryMergeCollinearSegments(Segment2D a, Segment2D b, out Segment2D merged)
    {
        merged = default;

        Vector2 dirA = a.end - a.start;
        Vector2 dirB = b.end - b.start;
        float lenA = dirA.magnitude;
        float lenB = dirB.magnitude;

        if (lenA < boundaryMinSegmentLength || lenB < boundaryMinSegmentLength)
        {
            return false;
        }

        dirA /= lenA;
        dirB /= lenB;

        float minDot = Mathf.Cos(collinearMergeAngle * Mathf.Deg2Rad);

        if (Mathf.Abs(Vector2.Dot(dirA, dirB)) < minDot)
        {
            return false;
        }

        if (DistancePointToInfiniteLine(b.start, a.start, a.end) > collinearMergeDistance ||
            DistancePointToInfiniteLine(b.end, a.start, a.end) > collinearMergeDistance)
        {
            return false;
        }

        float a0 = 0.0f;
        float a1 = lenA;
        float b0 = Vector2.Dot(b.start - a.start, dirA);
        float b1 = Vector2.Dot(b.end - a.start, dirA);

        if (b0 > b1)
        {
            float tmp = b0;
            b0 = b1;
            b1 = tmp;
        }

        float gap = 0.0f;

        if (a1 < b0)
        {
            gap = b0 - a1;
        }
        else if (b1 < a0)
        {
            gap = a0 - b1;
        }

        if (gap > collinearMergeDistance)
        {
            return false;
        }

        float min = Mathf.Min(a0, b0);
        float max = Mathf.Max(a1, b1);

        if (max - min < boundaryMinSegmentLength)
        {
            return false;
        }

        merged = new Segment2D
        {
            start = a.start + dirA * min,
            end = a.start + dirA * max
        };

        return true;
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

    private void TryAddEndpointProjectionSplit(
        Vector2 point,
        Vector2 segmentStart,
        Vector2 segmentEnd,
        List<float> splitValues)
    {
        if (TryProjectPointToSegmentT(
                point,
                segmentStart,
                segmentEnd,
                endpointSnapDistance,
                out float t))
        {
            AddSplitValue(splitValues, t);
        }
    }

    private static void AddSplitValue(List<float> values, float value)
    {
        if (value <= 0.0001f || value >= 0.9999f)
        {
            return;
        }

        foreach (float existing in values)
        {
            if (Mathf.Abs(existing - value) <= 0.0001f)
            {
                return;
            }
        }

        values.Add(value);
    }

    private static bool TryProjectPointToSegmentT(
        Vector2 point,
        Vector2 segmentStart,
        Vector2 segmentEnd,
        float maxDistance,
        out float t)
    {
        Vector2 ab = segmentEnd - segmentStart;
        float abSqr = ab.sqrMagnitude;

        if (abSqr < 0.000001f)
        {
            t = 0.0f;
            return false;
        }

        t = Vector2.Dot(point - segmentStart, ab) / abSqr;

        if (t <= 0.0001f || t >= 0.9999f)
        {
            return false;
        }

        Vector2 projected = segmentStart + ab * t;
        return Vector2.Distance(point, projected) <= maxDistance;
    }

    private static bool TryGetSegmentIntersectionPoint(
        Vector2 a,
        Vector2 b,
        Vector2 c,
        Vector2 d,
        out Vector2 intersection,
        out float ta,
        out float tb)
    {
        intersection = default;
        ta = 0.0f;
        tb = 0.0f;

        Vector2 r = b - a;
        Vector2 s = d - c;
        float denominator = Cross(r, s);

        if (Mathf.Abs(denominator) < 0.000001f)
        {
            return false;
        }

        Vector2 cMinusA = c - a;
        ta = Cross(cMinusA, s) / denominator;
        tb = Cross(cMinusA, r) / denominator;

        if (ta < -0.0001f || ta > 1.0001f ||
            tb < -0.0001f || tb > 1.0001f)
        {
            return false;
        }

        ta = Mathf.Clamp01(ta);
        tb = Mathf.Clamp01(tb);
        intersection = a + r * ta;
        return true;
    }

    private static Vector2 ClosestPointOnSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float abSqr = ab.sqrMagnitude;

        if (abSqr < 0.000001f)
        {
            return a;
        }

        float t = Vector2.Dot(p - a, ab) / abSqr;
        t = Mathf.Clamp01(t);
        return a + ab * t;
    }

    private static float DistancePointToInfiniteLine(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float len = ab.magnitude;

        if (len < 0.000001f)
        {
            return Vector2.Distance(p, a);
        }

        return Mathf.Abs(Cross(ab, p - a)) / len;
    }

    private static float Cross(Vector2 a, Vector2 b)
    {
        return a.x * b.y - a.y * b.x;
    }

    private void SetStatus(string message)
    {
        status = message;
        Debug.Log($"[Workflow] {status}");
        UpdateStatusText();
    }

    private void UpdateStatusText()
    {
        if (workflowStatusText == null)
        {
            return;
        }

        bool roomExists = scanner != null && scanner.currentRoom != null;
        bool locked = scanner != null && scanner.lockRoom;

        workflowStatusText.text =
            "=== Workflow ===\n" +
            $"state: {currentState}\n" +
            $"room exists: {roomExists}\n" +
            $"room locked: {locked}\n" +
            $"manual walls: {manualWallCount}\n" +
            $"final room closed: {finalRoomClosed}\n" +
            $"flood reached outside: {floodReachedOutside}\n" +
            $"selected cells: {selectedCellCount}\n" +
            $"confirmed boundary: {confirmedBoundarySegmentCount}\n" +
            $"cell polygon fallback: {usedSelectedCellBoundaryPolygonFallback}\n" +
            $"status: {status}";
    }

    private void EnsureDefaultMaterials()
    {
        if (runtimeWallMaterial == null)
        {
            runtimeWallMaterial = CreateRuntimeMaterial(
                "Runtime_ConfirmedRoom_Wall_Material",
                new Color(0.1f, 0.75f, 1.0f, 0.45f)
            );
        }

        if (runtimeHorizontalMaterial == null)
        {
            runtimeHorizontalMaterial = CreateRuntimeMaterial(
                "Runtime_ConfirmedRoom_Horizontal_Material",
                new Color(0.2f, 1.0f, 0.55f, 0.28f)
            );
        }
    }

    private Material GetWallMaterial()
    {
        if (confirmedRoomWallMaterial != null)
        {
            return confirmedRoomWallMaterial;
        }

        EnsureDefaultMaterials();
        return runtimeWallMaterial;
    }

    private Material GetHorizontalMaterial()
    {
        if (confirmedRoomHorizontalMaterial != null)
        {
            return confirmedRoomHorizontalMaterial;
        }

        EnsureDefaultMaterials();
        return runtimeHorizontalMaterial;
    }

    private static Material CreateRuntimeMaterial(string materialName, Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");

        if (shader == null)
        {
            shader = Shader.Find("Unlit/Color");
        }

        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        Material material = new Material(shader);
        material.name = materialName;
        material.hideFlags = HideFlags.DontSave;

        material.color = color;

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }

        return material;
    }

    private static bool SegmentsIntersect(
        Vector2 a,
        Vector2 b,
        Vector2 c,
        Vector2 d)
    {
        float d1 = Direction(c, d, a);
        float d2 = Direction(c, d, b);
        float d3 = Direction(a, b, c);
        float d4 = Direction(a, b, d);

        if (((d1 > 0.0f && d2 < 0.0f) || (d1 < 0.0f && d2 > 0.0f)) &&
            ((d3 > 0.0f && d4 < 0.0f) || (d3 < 0.0f && d4 > 0.0f)))
        {
            return true;
        }

        return false;
    }

    private static float Direction(Vector2 a, Vector2 b, Vector2 c)
    {
        return (c.x - a.x) * (b.y - a.y) -
               (b.x - a.x) * (c.y - a.y);
    }

    private static float DistancePointToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float abSqr = ab.sqrMagnitude;

        if (abSqr < 0.000001f)
        {
            return Vector2.Distance(p, a);
        }

        float t = Vector2.Dot(p - a, ab) / abSqr;
        t = Mathf.Clamp01(t);

        Vector2 projected = a + ab * t;
        return Vector2.Distance(p, projected);
    }
}
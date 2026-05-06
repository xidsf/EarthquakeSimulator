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

    [Header("References")]
    public SceneUnderstandingRoomScanner scanner;
    public ManualWallBuilder manualWallBuilder;
    public SceneUnderstandingMeshObjExporter meshObjExporter;

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
    public PressableButton exportMeshAfterManualWallButton;
    public PressableButton confirmRoomButton;

    [Header("Status Text")]
    public TMP_Text workflowStatusText;

    [Header("Switch Conditions")]
    public bool autoLockRoomWhenSwitchingToManual = true;
    public bool stopScanningWhenSwitchingToManual = true;
    public bool rebuildSelectableSurfacesWhenSwitching = true;
    public bool clearManualWallsWhenReturningToRoomBuild = true;

    [Header("Boundary Sources")]
    public bool includeDetectedWallsInClosedCheck = true;
    public bool includeClosureEdgesInClosedCheck = true;
    public bool includeManualWallsInClosedCheck = true;

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

    [Tooltip("Confirm Room 상태에서 기존 raw mesh / wall segmentation 표시를 숨깁니다.")]
    public bool hideAutoRoomVisualsInConfirmRoom = true;

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

    private Vector2 gridMin;
    private int gridWidth;
    private int gridHeight;

    private Material runtimeWallMaterial;
    private Material runtimeHorizontalMaterial;

    private UnityAction switchToManualWallAction;
    private UnityAction returnToRoomBuildAction;
    private UnityAction confirmManualWallsAction;
    private UnityAction backToManualWallAction;
    private UnityAction exportMeshAfterManualWallAction;
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

    private void CreateActions()
    {
        switchToManualWallAction ??= SwitchToManualWallGeneration;
        returnToRoomBuildAction ??= ReturnToRoomBuild;
        confirmManualWallsAction ??= ConfirmManualWalls;
        backToManualWallAction ??= BackToManualWallGeneration;
        exportMeshAfterManualWallAction ??= ExportMeshAfterManualWall;
        confirmRoomAction ??= ConfirmRoomAndStartFurnitureRecognitionPlaceholder;
    }

    private void RegisterButtons()
    {
        AddClick(switchToManualWallButton, switchToManualWallAction);
        AddClick(returnToRoomBuildButton, returnToRoomBuildAction);
        AddClick(confirmManualWallsButton, confirmManualWallsAction);
        AddClick(backToManualWallButton, backToManualWallAction);
        AddClick(exportMeshAfterManualWallButton, exportMeshAfterManualWallAction);
        AddClick(confirmRoomButton, confirmRoomAction);
    }

    private void UnregisterButtons()
    {
        RemoveClick(switchToManualWallButton, switchToManualWallAction);
        RemoveClick(returnToRoomBuildButton, returnToRoomBuildAction);
        RemoveClick(confirmManualWallsButton, confirmManualWallsAction);
        RemoveClick(backToManualWallButton, backToManualWallAction);
        RemoveClick(exportMeshAfterManualWallButton, exportMeshAfterManualWallAction);
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
        SetAutoRoomVisualsActive(true);

        if (manualWallBuilder != null)
        {
            manualWallBuilder.SetNoneMode();

            if (rebuildSelectableSurfacesWhenSwitching)
            {
                manualWallBuilder.RebuildSelectableSurfaces();
            }
        }

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
        BuildConfirmedRoomVisualization();

        if (manualWallBuilder != null)
        {
            manualWallBuilder.SetNoneMode();
            manualWallBuilder.ClearSelectableSurfaces();
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
            SetAutoRoomVisualsActive(true);
        }

        if (manualWallBuilder != null)
        {
            manualWallBuilder.SetNoneMode();
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

    public void ExportMeshAfterManualWall()
    {
        if (currentState != WorkflowState.ManualWallConfirmed)
        {
            SetStatus("Export blocked. Confirm manual walls first.");
            return;
        }

        if (!finalRoomClosed)
        {
            SetStatus("Export blocked. Final room is not closed.");
            return;
        }

        if (meshObjExporter == null)
        {
            SetStatus("Export failed. Mesh exporter is null.");
            return;
        }

        meshObjExporter.ExportBothObjForEditorTest();
        SetStatus("Test OBJ export requested after manual wall confirmation.");
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
        if (manualWallBuilder == null || manualWallBuilder.manualWallRoot == null)
        {
            return;
        }

        foreach (Transform child in manualWallBuilder.manualWallRoot)
        {
            if (child == null)
            {
                continue;
            }

            if (!child.gameObject.activeInHierarchy)
            {
                continue;
            }

            if (!child.name.StartsWith("ManualWall"))
            {
                continue;
            }

            float length = child.lossyScale.z;
            Vector3 center = child.position;
            Vector3 forward = child.forward;

            Vector3 a = center - forward * length * 0.5f;
            Vector3 b = center + forward * length * 0.5f;

            target.Add(new Segment2D
            {
                start = new Vector2(a.x, a.z),
                end = new Vector2(b.x, b.z)
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

    private void BuildConfirmedRoomVisualization()
    {
        ClearConfirmedRoomVisualization();

        if (confirmedRoomRoot == null)
        {
            confirmedRoomRoot = new GameObject("ConfirmedRoomRoot").transform;
        }

        BuildConfirmedHorizontalSurface("ConfirmedRoom_Floor", scanner.currentRoom.floorY + confirmedFloorYOffset, normalUp: true);
        BuildConfirmedHorizontalSurface("ConfirmedRoom_Ceiling", scanner.currentRoom.ceilingY + confirmedCeilingYOffset, normalUp: false);

        foreach (Segment2D segment in confirmedBoundarySegments)
        {
            CreateConfirmedWallObject(segment);
        }
    }

    private void BuildConfirmedHorizontalSurface(string objectName, float y, bool normalUp)
    {
        if (scanner == null || scanner.currentRoom == null)
        {
            return;
        }

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
    }

    private void CreateConfirmedWallObject(Segment2D segment)
    {
        if (scanner == null || scanner.currentRoom == null)
        {
            return;
        }

        float floorY = scanner.currentRoom.floorY;
        float ceilingY = scanner.currentRoom.ceilingY;
        float height = Mathf.Max(0.1f, ceilingY - floorY);

        Vector2 mid = (segment.start + segment.end) * 0.5f;
        float length = Vector2.Distance(segment.start, segment.end);

        if (length < 0.05f)
        {
            return;
        }

        Vector2 dir = (segment.end - segment.start).normalized;
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
    }

    private void ClearConfirmedRoomVisualization()
    {
        if (confirmedRoomRoot == null)
        {
            return;
        }

        for (int i = confirmedRoomRoot.childCount - 1; i >= 0; i--)
        {
            Destroy(confirmedRoomRoot.GetChild(i).gameObject);
        }

        confirmedBoundarySegments.Clear();
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

    private int CountManualWalls()
    {
        if (manualWallBuilder == null || manualWallBuilder.manualWallRoot == null)
        {
            return 0;
        }

        int count = 0;

        foreach (Transform child in manualWallBuilder.manualWallRoot)
        {
            if (child != null &&
                child.gameObject.activeInHierarchy &&
                child.name.StartsWith("ManualWall"))
            {
                count++;
            }
        }

        return count;
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
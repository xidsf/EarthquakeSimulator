using System;
using System.Collections.Generic;
using MixedReality.Toolkit.UX;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

public class ManualWallBuilder : MonoBehaviour
{
    public enum BuildMode
    {
        None,
        FloorTwoPoint,
        WallPerpendicular
    }

    public enum SelectableSurfaceKind
    {
        Floor,
        DetectedWall,
        ManualWall
    }

    [Serializable]
    public struct SurfaceSelection
    {
        public SelectableSurfaceKind kind;
        public Vector3 point;
        public Vector3 normal;
        public GameObject surfaceObject;
    }

    private struct ManualWallSegmentData
    {
        public Vector2 start;
        public Vector2 end;
        public GameObject wallObject;
    }

    [Header("References")]
    public SceneUnderstandingRoomScanner scanner;

    [Tooltip("생성된 수동 벽들이 들어갈 루트. 비워두면 자동 생성됨.")]
    public Transform manualWallRoot;

    [Tooltip("손 Ray가 선택할 floor/wall collider들이 들어갈 루트. 비워두면 자동 생성됨.")]
    public Transform selectableSurfaceRoot;

    [Header("Hand Ray Preview")]
    [Tooltip("MRTK XR Rig 안의 손 Ray XRRayInteractor들을 넣어주세요. 비워두면 자동 탐색을 시도합니다.")]
    public XRRayInteractor[] handRayInteractors;

    [Tooltip("첫 번째 점 선택 후 두 번째 점 후보까지 이어지는 선.")]
    public LineRenderer previewLine;

    public Material previewLineMaterial;
    public float previewLineWidth = 0.015f;
    public bool keepPreviewAtLastValidPointWhenRayLost = true;
    public float rayLostHoldSeconds = 1.0f;

    [Header("Materials")]
    public Material manualWallMaterial;
    public Material selectableFloorMaterial;
    public Material selectableWallMaterial;
    public Material markerMaterial;

    [Header("Manual Wall Settings")]
    public float wallThickness = 0.06f;
    public float defaultPerpendicularWallLength = 1.0f;
    public float minWallLength = 0.2f;
    public bool invertPerpendicularDirection = false;

    [Header("Perpendicular Wall Extend")]
    public bool extendPerpendicularWallToNextWall = true;
    public bool includeClosureEdgesForPerpendicularExtend = true;
    public bool includeManualWallsForPerpendicularExtend = true;
    public float maxPerpendicularWallLength = 5.0f;
    public float perpendicularWallEndPadding = 0.02f;
    public float minPerpendicularIntersectionDistance = 0.15f;

    [Header("Selectable Surface Settings")]
    public bool showSelectableSurfaces = true;
    public float selectableFloorThickness = 0.03f;
    public float selectableWallThickness = 0.05f;
    public float floorMargin = 0.3f;

    [Header("MRTK Pressable Buttons")]
    public PressableButton rebuildSelectableSurfacesButton;
    public PressableButton floorTwoPointModeButton;
    public PressableButton wallPerpendicularModeButton;
    public PressableButton invertDirectionButton;
    public PressableButton undoLastWallButton;
    public PressableButton clearManualWallsButton;
    public PressableButton toggleSelectableSurfaceVisibleButton;
    public PressableButton clearSelectableSurfacesButton;

    [Header("Debug")]
    public BuildMode currentMode = BuildMode.None;
    public string status;
    public bool hasFirstFloorPoint;
    public bool previewRayHasValidHit;
    public Vector3 lastPreviewPoint;

    private Vector3? firstFloorPoint;
    private GameObject firstPointMarker;

    private readonly List<GameObject> manualWalls = new List<GameObject>();
    private readonly List<ManualWallSegmentData> manualWallSegments = new List<ManualWallSegmentData>();
    private readonly List<GameObject> selectableSurfaces = new List<GameObject>();

    private float lastValidPreviewPointTime;

    private UnityAction rebuildSelectableSurfacesAction;
    private UnityAction floorTwoPointModeAction;
    private UnityAction wallPerpendicularModeAction;
    private UnityAction invertDirectionAction;
    private UnityAction undoLastWallAction;
    private UnityAction clearManualWallsAction;
    private UnityAction exitManualModeAction;
    private UnityAction toggleSelectableSurfaceVisibleAction;
    private UnityAction clearSelectableSurfacesAction;

    private void Awake()
    {
        if (manualWallRoot == null)
        {
            manualWallRoot = new GameObject("ManualWallRoot").transform;
        }

        if (selectableSurfaceRoot == null)
        {
            selectableSurfaceRoot = new GameObject("SelectableSurfaceRoot").transform;
        }

        SetupPreviewLine();

        if (handRayInteractors == null || handRayInteractors.Length == 0)
        {
            handRayInteractors = handRayInteractors = FindObjectsByType<XRRayInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        }
    }

    private void OnEnable()
    {
        CreateButtonActions();
        RegisterButtons();
    }

    private void OnDisable()
    {
        UnregisterButtons();
    }

    private void Update()
    {
        UpdateFloorTwoPointPreview();
    }

    private void CreateButtonActions()
    {
        rebuildSelectableSurfacesAction ??= RebuildSelectableSurfaces;
        floorTwoPointModeAction ??= SetFloorTwoPointMode;
        wallPerpendicularModeAction ??= SetWallPerpendicularMode;
        invertDirectionAction ??= ToggleInvertDirection;
        undoLastWallAction ??= UndoLastManualWall;
        clearManualWallsAction ??= ClearManualWalls;
        toggleSelectableSurfaceVisibleAction ??= ToggleSelectableSurfaceVisible;
        clearSelectableSurfacesAction ??= ClearSelectableSurfaces;
    }

    private void RegisterButtons()
    {
        AddClick(rebuildSelectableSurfacesButton, rebuildSelectableSurfacesAction);
        AddClick(floorTwoPointModeButton, floorTwoPointModeAction);
        AddClick(wallPerpendicularModeButton, wallPerpendicularModeAction);
        AddClick(invertDirectionButton, invertDirectionAction);
        AddClick(undoLastWallButton, undoLastWallAction);
        AddClick(clearManualWallsButton, clearManualWallsAction);
        AddClick(toggleSelectableSurfaceVisibleButton, toggleSelectableSurfaceVisibleAction);
        AddClick(clearSelectableSurfacesButton, clearSelectableSurfacesAction);
    }

    private void UnregisterButtons()
    {
        RemoveClick(rebuildSelectableSurfacesButton, rebuildSelectableSurfacesAction);
        RemoveClick(floorTwoPointModeButton, floorTwoPointModeAction);
        RemoveClick(wallPerpendicularModeButton, wallPerpendicularModeAction);
        RemoveClick(invertDirectionButton, invertDirectionAction);
        RemoveClick(undoLastWallButton, undoLastWallAction);
        RemoveClick(clearManualWallsButton, clearManualWallsAction);
        RemoveClick(toggleSelectableSurfaceVisibleButton, toggleSelectableSurfaceVisibleAction);
        RemoveClick(clearSelectableSurfacesButton, clearSelectableSurfacesAction);
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

    public void SetFloorTwoPointMode()
    {
        currentMode = BuildMode.FloorTwoPoint;
        firstFloorPoint = null;
        hasFirstFloorPoint = false;
        ClearFirstPointMarker();
        SetPreviewLineVisible(false);

        status = "FloorTwoPoint mode. Select first floor point with hand ray.";
        Debug.Log($"[ManualWall] {status}");
    }

    public void SetWallPerpendicularMode()
    {
        currentMode = BuildMode.WallPerpendicular;
        firstFloorPoint = null;
        hasFirstFloorPoint = false;
        ClearFirstPointMarker();
        SetPreviewLineVisible(false);

        status = "WallPerpendicular mode. Select a wall with hand ray.";
        Debug.Log($"[ManualWall] {status}");
    }

    public void SetNoneMode()
    {
        currentMode = BuildMode.None;
        firstFloorPoint = null;
        hasFirstFloorPoint = false;
        ClearFirstPointMarker();
        SetPreviewLineVisible(false);

        status = "Manual wall mode disabled.";
        Debug.Log($"[ManualWall] {status}");
    }

    public void ToggleInvertDirection()
    {
        invertPerpendicularDirection = !invertPerpendicularDirection;

        status = $"invertPerpendicularDirection: {invertPerpendicularDirection}";
        Debug.Log($"[ManualWall] {status}");
    }

    public void ToggleSelectableSurfaceVisible()
    {
        showSelectableSurfaces = !showSelectableSurfaces;

        foreach (GameObject surface in selectableSurfaces)
        {
            if (surface == null)
            {
                continue;
            }

            MeshRenderer renderer = surface.GetComponent<MeshRenderer>();

            if (renderer != null)
            {
                renderer.enabled = showSelectableSurfaces;
            }
        }

        status = $"showSelectableSurfaces: {showSelectableSurfaces}. Colliders remain active.";
        Debug.Log($"[ManualWall] {status}");
    }

    public void RebuildSelectableSurfaces()
    {
        ClearSelectableSurfaces();

        if (scanner == null || scanner.currentRoom == null)
        {
            status = "Cannot rebuild selectable surfaces. Scanner or currentRoom is null.";
            Debug.LogWarning($"[ManualWall] {status}");
            return;
        }

        BuildSelectableFloor();
        BuildSelectableDetectedWalls();

        status = $"Selectable surfaces rebuilt. count:{selectableSurfaces.Count}";
        Debug.Log($"[ManualWall] {status}");
    }

    public void ClearSelectableSurfaces()
    {
        foreach (GameObject surface in selectableSurfaces)
        {
            if (surface != null)
            {
                Destroy(surface);
            }
        }

        selectableSurfaces.Clear();

        status = "Selectable helper surfaces cleared. Manual walls were not removed.";
        Debug.Log($"[ManualWall] {status}");
    }

    public void HandleSurfaceSelected(SurfaceSelection selection)
    {
        if (currentMode == BuildMode.None)
        {
            status = "Surface selected, but no build mode is active.";
            Debug.Log($"[ManualWall] {status}");
            return;
        }

        if (currentMode == BuildMode.FloorTwoPoint)
        {
            HandleFloorTwoPointSelection(selection);
        }
        else if (currentMode == BuildMode.WallPerpendicular)
        {
            HandleWallPerpendicularSelection(selection);
        }
    }

    public void CreateManualWallFromWorldPoints(Vector3 startWorld, Vector3 endWorld)
    {
        if (scanner == null || scanner.currentRoom == null)
        {
            status = "Cannot create wall. Scanner or currentRoom is null.";
            Debug.LogWarning($"[ManualWall] {status}");
            return;
        }

        float floorY = scanner.currentRoom.floorY;
        float ceilingY = scanner.currentRoom.ceilingY;
        float height = ceilingY - floorY;

        if (height <= 0.1f)
        {
            status = $"Invalid room height: {height:F2}";
            Debug.LogWarning($"[ManualWall] {status}");
            return;
        }

        Vector3 start = new Vector3(startWorld.x, floorY, startWorld.z);
        Vector3 end = new Vector3(endWorld.x, floorY, endWorld.z);

        Vector3 delta = end - start;
        delta.y = 0.0f;

        float length = delta.magnitude;

        if (length < minWallLength)
        {
            status = $"Wall too short: {length:F2}";
            Debug.LogWarning($"[ManualWall] {status}");
            return;
        }

        Vector3 direction = delta.normalized;
        Vector3 center = (start + end) * 0.5f;
        center.y = floorY + height * 0.5f;

        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "ManualWall";

        wall.transform.position = center;

        float yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        wall.transform.rotation = Quaternion.Euler(0.0f, yaw, 0.0f);

        wall.transform.localScale = new Vector3(
            wallThickness,
            height,
            length
        );

        if (manualWallMaterial != null)
        {
            MeshRenderer renderer = wall.GetComponent<MeshRenderer>();

            if (renderer != null)
            {
                renderer.sharedMaterial = manualWallMaterial;
            }
        }

        if (manualWallRoot != null)
        {
            wall.transform.SetParent(manualWallRoot, true);
        }

        manualWalls.Add(wall);
        manualWallSegments.Add(new ManualWallSegmentData
        {
            start = new Vector2(start.x, start.z),
            end = new Vector2(end.x, end.z),
            wallObject = wall
        });

        AddSelectableSurface(
            wall,
            SelectableSurfaceKind.ManualWall,
            manualWallMaterial,
            visible: true
        );

        status = $"Manual wall created. length:{length:F2}, height:{height:F2}";
        Debug.Log($"[ManualWall] {status}");
    }

    public void UndoLastManualWall()
    {
        if (manualWalls.Count == 0)
        {
            status = "No manual wall to undo.";
            Debug.Log($"[ManualWall] {status}");
            return;
        }

        int lastIndex = manualWalls.Count - 1;
        GameObject last = manualWalls[lastIndex];

        manualWalls.RemoveAt(lastIndex);

        if (manualWallSegments.Count > lastIndex)
        {
            manualWallSegments.RemoveAt(lastIndex);
        }
        else if (manualWallSegments.Count > 0)
        {
            manualWallSegments.RemoveAt(manualWallSegments.Count - 1);
        }

        if (last != null)
        {
            Destroy(last);
        }

        status = "Undo last manual wall.";
        Debug.Log($"[ManualWall] {status}");
    }

    public void ClearManualWalls()
    {
        foreach (GameObject wall in manualWalls)
        {
            if (wall != null)
            {
                Destroy(wall);
            }
        }

        manualWalls.Clear();
        manualWallSegments.Clear();

        status = "Manual walls cleared.";
        Debug.Log($"[ManualWall] {status}");
    }

    private void HandleFloorTwoPointSelection(SurfaceSelection selection)
    {
        if (selection.kind != SelectableSurfaceKind.Floor)
        {
            status = "FloorTwoPoint mode requires selecting the floor.";
            Debug.Log($"[ManualWall] {status}");
            return;
        }

        Vector3 point = selection.point;

        if (!firstFloorPoint.HasValue)
        {
            firstFloorPoint = point;
            hasFirstFloorPoint = true;
            lastPreviewPoint = point;
            lastValidPreviewPointTime = Time.time;

            CreateFirstPointMarker(point);
            SetPreviewLineVisible(true);
            UpdatePreviewLine(point, point);

            status = "First floor point selected. Move hand ray to preview, then select second floor point.";
            Debug.Log($"[ManualWall] {status}");
            return;
        }

        CreateManualWallFromWorldPoints(firstFloorPoint.Value, point);

        firstFloorPoint = null;
        hasFirstFloorPoint = false;
        ClearFirstPointMarker();
        SetPreviewLineVisible(false);

        status = "Floor two-point wall completed.";
        Debug.Log($"[ManualWall] {status}");
    }

    private void HandleWallPerpendicularSelection(SurfaceSelection selection)
    {
        if (selection.kind != SelectableSurfaceKind.DetectedWall &&
            selection.kind != SelectableSurfaceKind.ManualWall)
        {
            status = "WallPerpendicular mode requires selecting a wall.";
            Debug.Log($"[ManualWall] {status}");
            return;
        }

        Vector3 normal = selection.normal;
        normal.y = 0.0f;

        if (normal.sqrMagnitude < 0.001f && selection.surfaceObject != null)
        {
            normal = selection.surfaceObject.transform.right;
            normal.y = 0.0f;
        }

        if (normal.sqrMagnitude < 0.001f)
        {
            status = "Selected wall normal is invalid.";
            Debug.LogWarning($"[ManualWall] {status}");
            return;
        }

        normal.Normalize();

        if (invertPerpendicularDirection)
        {
            normal = -normal;
        }

        Vector3 start = selection.point;
        Vector3 end;

        if (extendPerpendicularWallToNextWall &&
            TryFindPerpendicularEndPoint(start, normal, out Vector3 extendedEnd))
        {
            end = extendedEnd;
        }
        else
        {
            end = start + normal * defaultPerpendicularWallLength;
        }

        CreateManualWallFromWorldPoints(start, end);

        status = "Perpendicular manual wall created.";
        Debug.Log($"[ManualWall] {status}");
    }

    private bool TryFindPerpendicularEndPoint(Vector3 startWorld, Vector3 directionWorld, out Vector3 endWorld)
    {
        endWorld = startWorld + directionWorld.normalized * defaultPerpendicularWallLength;

        if (scanner == null || scanner.currentRoom == null)
        {
            return false;
        }

        Vector2 origin = new Vector2(startWorld.x, startWorld.z);
        Vector2 dir = new Vector2(directionWorld.x, directionWorld.z);

        if (dir.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        dir.Normalize();

        float nearestDistance = float.MaxValue;

        foreach (WallSegment wall in scanner.currentRoom.detectedWalls)
        {
            if (TryRaySegmentIntersection(
                    origin,
                    dir,
                    wall.start,
                    wall.end,
                    minPerpendicularIntersectionDistance,
                    maxPerpendicularWallLength,
                    out float distance))
            {
                nearestDistance = Mathf.Min(nearestDistance, distance);
            }
        }

        if (includeClosureEdgesForPerpendicularExtend)
        {
            foreach (WallSegment edge in scanner.currentRoom.closureEdges)
            {
                if (TryRaySegmentIntersection(
                        origin,
                        dir,
                        edge.start,
                        edge.end,
                        minPerpendicularIntersectionDistance,
                        maxPerpendicularWallLength,
                        out float distance))
                {
                    nearestDistance = Mathf.Min(nearestDistance, distance);
                }
            }
        }

        if (includeManualWallsForPerpendicularExtend)
        {
            foreach (ManualWallSegmentData manual in manualWallSegments)
            {
                if (manual.wallObject == null)
                {
                    continue;
                }

                if (TryRaySegmentIntersection(
                        origin,
                        dir,
                        manual.start,
                        manual.end,
                        minPerpendicularIntersectionDistance,
                        maxPerpendicularWallLength,
                        out float distance))
                {
                    nearestDistance = Mathf.Min(nearestDistance, distance);
                }
            }
        }

        if (nearestDistance == float.MaxValue)
        {
            return false;
        }

        float finalDistance = Mathf.Max(minWallLength, nearestDistance - perpendicularWallEndPadding);
        Vector2 end2 = origin + dir * finalDistance;

        endWorld = new Vector3(end2.x, startWorld.y, end2.y);
        return true;
    }

    private static bool TryRaySegmentIntersection(
        Vector2 rayOrigin,
        Vector2 rayDirection,
        Vector2 segmentA,
        Vector2 segmentB,
        float minDistance,
        float maxDistance,
        out float distance)
    {
        distance = 0.0f;

        Vector2 v1 = rayOrigin - segmentA;
        Vector2 v2 = segmentB - segmentA;
        Vector2 v3 = new Vector2(-rayDirection.y, rayDirection.x);

        float dot = Vector2.Dot(v2, v3);

        if (Mathf.Abs(dot) < 0.000001f)
        {
            return false;
        }

        float t1 = Cross(v2, v1) / dot;
        float t2 = Vector2.Dot(v1, v3) / dot;

        if (t1 < minDistance || t1 > maxDistance)
        {
            return false;
        }

        if (t2 < 0.0f || t2 > 1.0f)
        {
            return false;
        }

        distance = t1;
        return true;
    }

    private static float Cross(Vector2 a, Vector2 b)
    {
        return a.x * b.y - a.y * b.x;
    }

    private void BuildSelectableFloor()
    {
        List<Vector2> points = new List<Vector2>();

        foreach (WallSegment wall in scanner.currentRoom.detectedWalls)
        {
            points.Add(wall.start);
            points.Add(wall.end);
        }

        foreach (WallSegment edge in scanner.currentRoom.closureEdges)
        {
            points.Add(edge.start);
            points.Add(edge.end);
        }

        if (points.Count == 0)
        {
            status = "Cannot build selectable floor. No boundary points.";
            Debug.LogWarning($"[ManualWall] {status}");
            return;
        }

        float minX = float.MaxValue;
        float maxX = float.MinValue;
        float minZ = float.MaxValue;
        float maxZ = float.MinValue;

        foreach (Vector2 p in points)
        {
            minX = Mathf.Min(minX, p.x);
            maxX = Mathf.Max(maxX, p.x);
            minZ = Mathf.Min(minZ, p.y);
            maxZ = Mathf.Max(maxZ, p.y);
        }

        minX -= floorMargin;
        maxX += floorMargin;
        minZ -= floorMargin;
        maxZ += floorMargin;

        float width = Mathf.Max(0.1f, maxX - minX);
        float depth = Mathf.Max(0.1f, maxZ - minZ);
        float floorY = scanner.currentRoom.floorY;

        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "SelectableFloor";

        floor.transform.position = new Vector3(
            (minX + maxX) * 0.5f,
            floorY - selectableFloorThickness * 0.5f,
            (minZ + maxZ) * 0.5f
        );

        floor.transform.rotation = Quaternion.identity;
        floor.transform.localScale = new Vector3(
            width,
            selectableFloorThickness,
            depth
        );

        if (selectableSurfaceRoot != null)
        {
            floor.transform.SetParent(selectableSurfaceRoot, true);
        }

        AddSelectableSurface(
            floor,
            SelectableSurfaceKind.Floor,
            selectableFloorMaterial,
            showSelectableSurfaces
        );

        selectableSurfaces.Add(floor);
    }

    private void BuildSelectableDetectedWalls()
    {
        if (scanner == null || scanner.currentRoom == null)
        {
            return;
        }

        foreach (WallSegment wall in scanner.currentRoom.detectedWalls)
        {
            CreateSelectableWallSurface(wall);
        }
    }

    private void CreateSelectableWallSurface(WallSegment wall)
    {
        float floorY = scanner.currentRoom.floorY;
        float ceilingY = scanner.currentRoom.ceilingY;
        float height = Mathf.Max(0.1f, ceilingY - floorY);

        Vector2 mid2 = (wall.start + wall.end) * 0.5f;
        float length = Vector2.Distance(wall.start, wall.end);

        if (length < minWallLength)
        {
            return;
        }

        Vector2 dir2 = (wall.end - wall.start).normalized;
        float yaw = Mathf.Atan2(dir2.x, dir2.y) * Mathf.Rad2Deg;

        GameObject wallSurface = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wallSurface.name = "SelectableDetectedWall";

        wallSurface.transform.position = new Vector3(
            mid2.x,
            floorY + height * 0.5f,
            mid2.y
        );

        wallSurface.transform.rotation = Quaternion.Euler(0.0f, yaw, 0.0f);
        wallSurface.transform.localScale = new Vector3(
            selectableWallThickness,
            height,
            length
        );

        if (selectableSurfaceRoot != null)
        {
            wallSurface.transform.SetParent(selectableSurfaceRoot, true);
        }

        AddSelectableSurface(
            wallSurface,
            SelectableSurfaceKind.DetectedWall,
            selectableWallMaterial,
            showSelectableSurfaces
        );

        selectableSurfaces.Add(wallSurface);
    }

    private void AddSelectableSurface(
        GameObject target,
        SelectableSurfaceKind kind,
        Material material,
        bool visible)
    {
        if (target == null)
        {
            return;
        }

        MeshRenderer renderer = target.GetComponent<MeshRenderer>();

        if (renderer != null)
        {
            renderer.enabled = visible;

            if (material != null)
            {
                renderer.sharedMaterial = material;
            }
        }

        Collider collider = target.GetComponent<Collider>();

        if (collider == null)
        {
            collider = target.AddComponent<BoxCollider>();
        }

        MixedReality.Toolkit.StatefulInteractable interactable =
            target.GetComponent<MixedReality.Toolkit.StatefulInteractable>();

        if (interactable == null)
        {
            interactable = target.AddComponent<MixedReality.Toolkit.StatefulInteractable>();
        }

        ManualWallSelectableSurface selectable =
            target.GetComponent<ManualWallSelectableSurface>();

        if (selectable == null)
        {
            selectable = target.AddComponent<ManualWallSelectableSurface>();
        }

        selectable.builder = this;
        selectable.surfaceKind = kind;
    }

    private void SetupPreviewLine()
    {
        if (previewLine == null)
        {
            GameObject lineObject = new GameObject("ManualWall_PreviewLine");
            lineObject.transform.SetParent(transform, false);
            previewLine = lineObject.AddComponent<LineRenderer>();
        }

        previewLine.positionCount = 2;
        previewLine.useWorldSpace = true;
        previewLine.startWidth = previewLineWidth;
        previewLine.endWidth = previewLineWidth;

        if (previewLineMaterial != null)
        {
            previewLine.sharedMaterial = previewLineMaterial;
        }

        SetPreviewLineVisible(false);
    }

    private void UpdateFloorTwoPointPreview()
    {
        if (currentMode != BuildMode.FloorTwoPoint || !firstFloorPoint.HasValue)
        {
            SetPreviewLineVisible(false);
            return;
        }

        if (TryGetCurrentFloorRayHit(out Vector3 currentPoint))
        {
            previewRayHasValidHit = true;
            lastPreviewPoint = currentPoint;
            lastValidPreviewPointTime = Time.time;
            UpdatePreviewLine(firstFloorPoint.Value, currentPoint);
            SetPreviewLineVisible(true);
            return;
        }

        previewRayHasValidHit = false;

        if (keepPreviewAtLastValidPointWhenRayLost &&
            Time.time - lastValidPreviewPointTime <= rayLostHoldSeconds)
        {
            UpdatePreviewLine(firstFloorPoint.Value, lastPreviewPoint);
            SetPreviewLineVisible(true);
            return;
        }

        if (keepPreviewAtLastValidPointWhenRayLost)
        {
            UpdatePreviewLine(firstFloorPoint.Value, lastPreviewPoint);
            SetPreviewLineVisible(true);
        }
        else
        {
            SetPreviewLineVisible(false);
        }
    }

    private bool TryGetCurrentFloorRayHit(out Vector3 point)
    {
        point = default;

        if (handRayInteractors == null || handRayInteractors.Length == 0)
        {
            return false;
        }

        foreach (XRRayInteractor rayInteractor in handRayInteractors)
        {
            if (rayInteractor == null || !rayInteractor.isActiveAndEnabled)
            {
                continue;
            }

            if (!rayInteractor.TryGetCurrent3DRaycastHit(out RaycastHit hit))
            {
                continue;
            }

            ManualWallSelectableSurface surface =
                hit.collider != null
                    ? hit.collider.GetComponentInParent<ManualWallSelectableSurface>()
                    : null;

            if (surface == null ||
                surface.surfaceKind != SelectableSurfaceKind.Floor)
            {
                continue;
            }

            point = hit.point;
            return true;
        }

        return false;
    }

    private void UpdatePreviewLine(Vector3 start, Vector3 end)
    {
        if (previewLine == null)
        {
            return;
        }

        float y = scanner != null && scanner.currentRoom != null
            ? scanner.currentRoom.floorY + 0.03f
            : start.y + 0.03f;

        Vector3 startPoint = new Vector3(start.x, y, start.z);
        Vector3 endPoint = new Vector3(end.x, y, end.z);

        previewLine.SetPosition(0, startPoint);
        previewLine.SetPosition(1, endPoint);
    }

    private void SetPreviewLineVisible(bool visible)
    {
        if (previewLine != null)
        {
            previewLine.enabled = visible;
        }
    }

    private void CreateFirstPointMarker(Vector3 position)
    {
        ClearFirstPointMarker();

        firstPointMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        firstPointMarker.name = "ManualWall_FirstPointMarker";
        firstPointMarker.transform.position = position;
        firstPointMarker.transform.localScale = Vector3.one * 0.08f;

        Collider col = firstPointMarker.GetComponent<Collider>();

        if (col != null)
        {
            Destroy(col);
        }

        if (markerMaterial != null)
        {
            MeshRenderer renderer = firstPointMarker.GetComponent<MeshRenderer>();

            if (renderer != null)
            {
                renderer.sharedMaterial = markerMaterial;
            }
        }
    }

    private void ClearFirstPointMarker()
    {
        if (firstPointMarker != null)
        {
            Destroy(firstPointMarker);
            firstPointMarker = null;
        }
    }
}
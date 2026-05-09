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
        ManualWallCreate,
        WallCut
    }

    public enum SelectableSurfaceKind
    {
        Floor,
        DetectedWall,
        ManualWall
    }


    private enum LastWallCreationType
    {
        None,
        TwoPoint,
        WallCornerPerpendicular,
        WallMiddlePerpendicular
    }

    public enum SelectableWallSourceKind
    {
        Unknown,
        DetectedWall,
        ClosureEdge,
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

    [Serializable]
    public struct ManualWallSegmentSnapshot
    {
        public Vector2 start;
        public Vector2 end;
        public GameObject wallObject;
    }

    private struct ManualWallSegmentData
    {
        public Vector2 start;
        public Vector2 end;
        public GameObject wallObject;
    }

    private class SelectableWallSegmentData : MonoBehaviour
    {
        public Vector2 start;
        public Vector2 end;
        public SelectableWallSourceKind sourceKind = SelectableWallSourceKind.Unknown;
        public int sourceIndex = -1;
    }

    private struct WallBuildCandidate
    {
        public Vector2 anchor;
        public Vector2 direction;
        public Vector2 end;
        public float distance;
        public bool hitTarget;
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

    [Tooltip("방금 생성된 벽을 구분하기 위한 Material. 비워두면 런타임 Material을 자동 생성합니다.")]
    public Material lastCreatedWallMaterial;

    [Header("Manual Wall Settings")]
    public float wallThickness = 0.06f;

    public float minWallLength = 0.2f;

    [Header("Unified Manual Wall Interaction")]

    [Tooltip("벽 endpoint에서 이 거리 이내를 선택하면 모서리 선택으로 판단합니다.")]
    public float wallCornerSelectDistance = 0.18f;

    [Tooltip("2점 벽 생성 시 endpoint가 기존 벽/모서리와 가까우면 붙이는 거리입니다.")]
    public float snapPointToWallDistance = 0.22f;

    [Tooltip("2점 벽 생성 시 기존 벽의 endpoint에 더 가까우면 endpoint로 우선 snap합니다.")]
    public float snapPointToCornerDistance = 0.28f;

    [Tooltip("수직벽 회전 버튼 1회당 회전 각도입니다. 현재 UX 요구에 맞춰 90도를 기본으로 둡니다.")]
    public float rotateLastWallDegrees = 90.0f;

    [Header("Perpendicular Wall Selection")]
    [Tooltip("교차점 탐색 시작점을 원본 벽에서 살짝 떼어 자기 자신과 바로 교차하는 것을 방지합니다. 생성 벽 자체는 원본 anchor에서 시작합니다.")]
    public float perpendicularStartOffsetFromSourceWall = 0.03f;

    [Header("Perpendicular Wall Extend")]
    public bool extendPerpendicularWallToNextWall = true;
    public bool includeClosureEdgesForPerpendicularExtend = true;
    public bool includeManualWallsForPerpendicularExtend = true;
    public float maxPerpendicularWallLength = 5.0f;
    public float perpendicularWallEndPadding = 0.02f;
    public float minPerpendicularIntersectionDistance = 0.15f;

    [Tooltip("방금 생성한 수직벽을 회전할 때 벽 교차를 찾기 위한 최소 거리입니다. 회전 후 가까운 벽을 놓치지 않도록 일반 생성보다 작게 둡니다.")]
    public float rotatedWallIntersectionMinDistance = 0.02f;

    [Tooltip("방금 생성한 수직벽을 회전했을 때 닿는 벽이 없으면 이 최대 길이까지 확장합니다. ㄱ자 미인식 상황에서는 긴 후보 벽으로 두고, 필요 시 Cut Mode에서 제거합니다.")]
    public bool extendRotatedWallToMaxLengthWhenNoHit = true;

    [Header("Wall Cut Mode")]
    [Tooltip("벽 자르기 모드입니다. ON 상태에서 벽 선택 -> 자를 방향 선택 순서로 동작합니다.")]
    public bool wallCutModeActive = false;

    [Tooltip("자르기 대상 벽을 선택했을 때 표시할 Material입니다. 비워두면 런타임 Material을 자동 생성합니다.")]
    public Material wallCutTargetMaterial;

    [Tooltip("자르기 후 남은 벽이 이 길이보다 짧으면 벽 전체를 삭제합니다.")]
    public float minWallLengthAfterCut = 0.2f;

    [Header("Selectable Surface Settings")]
    public bool showSelectableSurfaces = true;
    public float selectableFloorThickness = 0.03f;
    public float selectableWallThickness = 0.05f;
    public float floorMargin = 0.3f;

    [Header("MRTK Pressable Buttons")]
    public PressableButton rebuildSelectableSurfacesButton;

    [Tooltip("수동 벽 생성 <-> 벽 자르기 모드를 전환하는 단일 토글 버튼입니다.")]
    public PressableButton manualWallModeToggleButton;

    [Tooltip("방금 생성한 수직벽을 anchor 기준으로 90도 회전하는 버튼입니다.")]
    public PressableButton rotateLastWallButton;

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
    public bool hasLastGeneratedWall;
    public string lastGeneratedWallType;
    public bool hasCutTarget;
    public string cutModeStatus;

    private Vector3? firstFloorPoint;
    private GameObject firstPointMarker;

    private readonly List<GameObject> manualWalls = new List<GameObject>();
    private readonly List<ManualWallSegmentData> manualWallSegments = new List<ManualWallSegmentData>();
    private readonly List<GameObject> selectableSurfaces = new List<GameObject>();

    private float lastValidPreviewPointTime;

    private GameObject lastGeneratedWall;
    private int lastGeneratedWallIndex = -1;
    private LastWallCreationType lastWallCreationType = LastWallCreationType.None;
    private Vector2 lastWallAnchor;
    private Vector2 lastWallDirection;
    private Material runtimeLastCreatedWallMaterial;
    private Material runtimeWallCutTargetMaterial;
    private bool warnedRuntimeMaterialShaderMissing;
    private int lastManualWallModeToggleFrame = -1;

    private int cutTargetWallIndex = -1;
    private GameObject cutTargetWall;
    private Vector2 cutPoint;
    private Material cutTargetOriginalMaterial;
    private SelectableWallSourceKind cutTargetSourceKind = SelectableWallSourceKind.Unknown;

    private UnityAction rebuildSelectableSurfacesAction;
    private UnityAction manualWallModeToggleAction;
    private UnityAction rotateLastWallAction;
    private UnityAction undoLastWallAction;
    private UnityAction clearManualWallsAction;
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

        EnsureRuntimeMaterials();
        SetupPreviewLine();

        if (handRayInteractors == null || handRayInteractors.Length == 0)
        {
            handRayInteractors = FindObjectsByType<XRRayInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None);
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
        UpdateTwoPointPreview();
    }

    private void CreateButtonActions()
    {
        rebuildSelectableSurfacesAction ??= RebuildSelectableSurfaces;
        manualWallModeToggleAction ??= ToggleManualWallEditMode;
        rotateLastWallAction ??= RotateLastGeneratedPerpendicularWall;
        undoLastWallAction ??= UndoLastManualWall;
        clearManualWallsAction ??= ClearManualWalls;
        toggleSelectableSurfaceVisibleAction ??= ToggleSelectableSurfaceVisible;
        clearSelectableSurfacesAction ??= ClearSelectableSurfaces;
    }

    private void RegisterButtons()
    {
        AddClick(rebuildSelectableSurfacesButton, rebuildSelectableSurfacesAction);
        AddClick(manualWallModeToggleButton, manualWallModeToggleAction);
        AddClick(rotateLastWallButton, rotateLastWallAction);
        AddClick(undoLastWallButton, undoLastWallAction);
        AddClick(clearManualWallsButton, clearManualWallsAction);
        AddClick(toggleSelectableSurfaceVisibleButton, toggleSelectableSurfaceVisibleAction);
        AddClick(clearSelectableSurfacesButton, clearSelectableSurfacesAction);
    }

    private void UnregisterButtons()
    {
        RemoveClick(rebuildSelectableSurfacesButton, rebuildSelectableSurfacesAction);
        RemoveClick(manualWallModeToggleButton, manualWallModeToggleAction);
        RemoveClick(rotateLastWallButton, rotateLastWallAction);
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


    public void SetManualCreateMode()
    {
        wallCutModeActive = false;
        currentMode = BuildMode.ManualWallCreate;
        ClearCutTargetSelection();
        ClearPendingFirstPoint();
        SetPreviewLineVisible(false);

        status = "Manual wall create mode. Select floor for two-point wall, or select wall for wall-based generation.";
        cutModeStatus = "Wall cut mode OFF.";
        Debug.Log($"[ManualWall] {status}");
    }

    public void SetNoneMode()
    {
        currentMode = BuildMode.None;
        wallCutModeActive = false;
        ClearPendingFirstPoint();
        ClearCutTargetSelection();
        SetPreviewLineVisible(false);

        status = "Manual wall mode disabled.";
        cutModeStatus = "Wall cut mode OFF.";
        Debug.Log($"[ManualWall] {status}");
    }

    public void ToggleManualWallEditMode()
    {
        // PressableButton이 같은 프레임에 select/click을 중복 발생시키거나 Inspector Persistent Listener와 겹치는 경우를 방어합니다.
        if (lastManualWallModeToggleFrame == Time.frameCount)
        {
            Debug.LogWarning("[ManualWall] Ignored duplicated manual wall mode toggle in the same frame.");
            return;
        }

        lastManualWallModeToggleFrame = Time.frameCount;

        if (currentMode == BuildMode.WallCut || wallCutModeActive)
        {
            SetManualCreateMode();
        }
        else
        {
            SetWallCutMode(true);
        }
    }


    public void SetWallCutMode(bool active)
    {
        if (!active)
        {
            SetManualCreateMode();
            return;
        }

        wallCutModeActive = true;
        currentMode = BuildMode.WallCut;
        ClearPendingFirstPoint();
        SetPreviewLineVisible(false);
        ClearCutTargetSelection();

        cutModeStatus = "Wall cut mode ON. Select a manual or detected wall at the cut position.";
        status = cutModeStatus;
        Debug.Log($"[ManualWall] {status}");
    }


    public void RotateLastGeneratedPerpendicularWall()
    {
        if (!IsLastWallRotatable())
        {
            status = "No recently generated perpendicular wall to rotate.";
            Debug.Log($"[ManualWall] {status}");
            return;
        }

        Vector2 rotatedDirection = Rotate2D(lastWallDirection, rotateLastWallDegrees);

        if (rotatedDirection.sqrMagnitude < 0.0001f)
        {
            status = "Cannot rotate last wall. Direction is invalid.";
            Debug.LogWarning($"[ManualWall] {status}");
            return;
        }

        rotatedDirection.Normalize();

        float previousLength = GetManualWallLength(lastGeneratedWallIndex);
        float fallbackLength = extendRotatedWallToMaxLengthWhenNoHit
            ? maxPerpendicularWallLength
            : Mathf.Max(minWallLength, previousLength);

        float searchMaxDistance = Mathf.Max(maxPerpendicularWallLength, fallbackLength);
        float searchMinDistance = Mathf.Max(0.001f, rotatedWallIntersectionMinDistance);

        WallBuildCandidate candidate = BuildCandidateFromAnchorAndDirection(
            lastWallAnchor,
            rotatedDirection,
            allowTargetSearch: true,
            fallbackLength: fallbackLength,
            searchMaxDistance: searchMaxDistance,
            searchMinDistance: searchMinDistance
        );

        Vector3 startWorld = ToFloorWorld(lastWallAnchor);
        Vector3 endWorld = ToFloorWorld(candidate.end);

        if (!UpdateManualWallGeometry(lastGeneratedWallIndex, startWorld, endWorld))
        {
            status = "Failed to rotate last generated wall.";
            Debug.LogWarning($"[ManualWall] {status}");
            return;
        }

        lastWallDirection = rotatedDirection;
        SetWallMaterial(lastGeneratedWall, GetLastCreatedWallMaterial());

        string hitText = candidate.hitTarget
            ? "trimmed to nearest wall"
            : $"extended to fallback length:{fallbackLength:F2}";

        status = $"Rotated last perpendicular wall by {rotateLastWallDegrees:F0} degrees, {hitText}.";
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
        if (currentMode == BuildMode.WallCut || wallCutModeActive)
        {
            HandleWallCutSelection(selection);
            return;
        }

        if (!IsCreateModeActive())
        {
            status = "Surface selected, but manual wall create mode is not active.";
            Debug.Log($"[ManualWall] {status}");
            return;
        }

        if (selection.kind == SelectableSurfaceKind.Floor)
        {
            HandleFloorSelection(selection);
            return;
        }

        if (selection.kind == SelectableSurfaceKind.DetectedWall ||
            selection.kind == SelectableSurfaceKind.ManualWall)
        {
            HandleWallSelection(selection);
            return;
        }

        status = "Unsupported selectable surface.";
        Debug.Log($"[ManualWall] {status}");
    }

    private bool IsCreateModeActive()
    {
        return currentMode == BuildMode.ManualWallCreate;
    }

    public void CreateManualWallFromWorldPoints(Vector3 startWorld, Vector3 endWorld)
    {
        Vector3 adjustedStart = startWorld;
        Vector3 adjustedEnd = endWorld;

        SnapWallEndpoints(ref adjustedStart, ref adjustedEnd);

        CreateManualWallInternal(
            adjustedStart,
            adjustedEnd,
            LastWallCreationType.TwoPoint,
            new Vector2(adjustedStart.x, adjustedStart.z),
            GetDirection2D(adjustedStart, adjustedEnd),
            highlightAsLastCreated: true
        );
    }

    public void UndoLastManualWall()
    {
        if ((currentMode == BuildMode.WallCut || wallCutModeActive) && hasCutTarget)
        {
            ClearCutTargetSelection();
            status = "Canceled pending wall cut target.";
            Debug.Log($"[ManualWall] {status}");
            return;
        }

        if (firstFloorPoint.HasValue)
        {
            ClearPendingFirstPoint();
            SetPreviewLineVisible(false);

            status = "Canceled first floor point.";
            Debug.Log($"[ManualWall] {status}");
            return;
        }

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

        if (lastGeneratedWallIndex == lastIndex)
        {
            ClearLastGeneratedWallState();
        }
        else if (lastGeneratedWallIndex > lastIndex)
        {
            lastGeneratedWallIndex--;
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
        ClearLastGeneratedWallState();
        ClearPendingFirstPoint();
        ClearCutTargetSelection();

        status = "Manual walls cleared.";
        Debug.Log($"[ManualWall] {status}");
    }

    private void HandleWallCutSelection(SurfaceSelection selection)
    {
        if (!hasCutTarget)
        {
            SelectWallCutTarget(selection);
            return;
        }

        ApplyWallCut(selection);
    }

    private void SelectWallCutTarget(SurfaceSelection selection)
    {
        if (selection.kind != SelectableSurfaceKind.ManualWall &&
            selection.kind != SelectableSurfaceKind.DetectedWall)
        {
            status = "Wall cut mode requires selecting a wall first.";
            cutModeStatus = status;
            Debug.Log($"[ManualWall] {status}");
            return;
        }

        if (selection.kind == SelectableSurfaceKind.ManualWall)
        {
            SelectManualWallCutTarget(selection);
            return;
        }

        SelectSceneWallCutTarget(selection);
    }

    private void SelectManualWallCutTarget(SurfaceSelection selection)
    {
        if (!TryFindManualWallIndex(selection.surfaceObject, out int index))
        {
            status = "Selected wall is not registered as a manual wall.";
            cutModeStatus = status;
            Debug.LogWarning($"[ManualWall] {status}");
            return;
        }

        ManualWallSegmentData segment = manualWallSegments[index];
        Vector3 projected = ProjectPointToWallSegment(selection.point, segment.start, segment.end);

        cutTargetWallIndex = index;
        cutTargetSourceKind = SelectableWallSourceKind.ManualWall;
        cutTargetWall = manualWalls[index];
        cutPoint = new Vector2(projected.x, projected.z);
        cutTargetOriginalMaterial = GetWallMaterial(cutTargetWall);
        hasCutTarget = true;

        SetWallMaterial(cutTargetWall, GetWallCutTargetMaterial());

        status = "Manual wall cut target selected. Select the side/area to remove.";
        cutModeStatus = status;
        Debug.Log($"[ManualWall] {status}");
    }

    private void SelectSceneWallCutTarget(SurfaceSelection selection)
    {
        if (!TryGetSelectedWallData(selection, out SelectableWallSegmentData data))
        {
            status = "Selected detected wall has no segment data.";
            cutModeStatus = status;
            Debug.LogWarning($"[ManualWall] {status}");
            return;
        }

        SelectableWallSourceKind sourceKind = data.sourceKind;
        int sourceIndex = data.sourceIndex;

        if (sourceKind == SelectableWallSourceKind.Unknown || sourceIndex < 0)
        {
            if (!TryFindSceneWallIndex(data.start, data.end, out sourceKind, out sourceIndex))
            {
                status = "Selected detected wall is not registered in current room.";
                cutModeStatus = status;
                Debug.LogWarning($"[ManualWall] {status}");
                return;
            }
        }

        if (sourceKind != SelectableWallSourceKind.DetectedWall &&
            sourceKind != SelectableWallSourceKind.ClosureEdge)
        {
            status = "Wall cut mode cannot use this wall source as a detected wall target.";
            cutModeStatus = status;
            Debug.LogWarning($"[ManualWall] {status}");
            return;
        }

        if (!TryGetSceneWallSegment(sourceKind, sourceIndex, out WallSegment segment))
        {
            status = "Selected detected wall no longer exists.";
            cutModeStatus = status;
            Debug.LogWarning($"[ManualWall] {status}");
            return;
        }

        Vector3 projected = ProjectPointToWallSegment(selection.point, segment.start, segment.end);

        cutTargetWallIndex = sourceIndex;
        cutTargetSourceKind = sourceKind;
        cutTargetWall = selection.surfaceObject;
        cutPoint = new Vector2(projected.x, projected.z);
        cutTargetOriginalMaterial = GetWallMaterial(cutTargetWall);
        hasCutTarget = true;

        SetWallMaterial(cutTargetWall, GetWallCutTargetMaterial());

        status = "Detected wall cut target selected. Select the side/area to remove.";
        cutModeStatus = status;
        Debug.Log($"[ManualWall] {status}");
    }

    private void ApplyWallCut(SurfaceSelection areaSelection)
    {
        if (cutTargetSourceKind == SelectableWallSourceKind.ManualWall)
        {
            ApplyManualWallCut(areaSelection);
            return;
        }

        if (cutTargetSourceKind == SelectableWallSourceKind.DetectedWall ||
            cutTargetSourceKind == SelectableWallSourceKind.ClosureEdge)
        {
            ApplySceneWallCut(areaSelection);
            return;
        }

        ClearCutTargetSelection();
        status = "Cut target is no longer valid.";
        cutModeStatus = status;
        Debug.LogWarning($"[ManualWall] {status}");
    }

    private void ApplyManualWallCut(SurfaceSelection areaSelection)
    {
        if (cutTargetWallIndex < 0 || cutTargetWallIndex >= manualWallSegments.Count)
        {
            ClearCutTargetSelection();
            status = "Cut target is no longer valid.";
            cutModeStatus = status;
            Debug.LogWarning($"[ManualWall] {status}");
            return;
        }

        ManualWallSegmentData segment = manualWallSegments[cutTargetWallIndex];

        if (segment.wallObject == null)
        {
            ClearCutTargetSelection();
            status = "Cut target object was destroyed.";
            cutModeStatus = status;
            Debug.LogWarning($"[ManualWall] {status}");
            return;
        }

        if (!TryCalculateCutResult(segment.start, segment.end, areaSelection, out Vector2 newStart, out Vector2 newEnd, out float remainingLength))
        {
            DeleteManualWallAt(cutTargetWallIndex);
            ClearCutTargetSelection();
            status = "Cut target was too short and removed.";
            cutModeStatus = status;
            Debug.Log($"[ManualWall] {status}");
            return;
        }

        if (remainingLength < Mathf.Max(minWallLength, minWallLengthAfterCut))
        {
            DeleteManualWallAt(cutTargetWallIndex);
            ClearCutTargetSelection();
            status = "Remaining wall was too short. Wall removed.";
            cutModeStatus = status;
            Debug.Log($"[ManualWall] {status}");
            return;
        }

        GameObject target = segment.wallObject;
        Material restoreMaterial = cutTargetOriginalMaterial != null ? cutTargetOriginalMaterial : manualWallMaterial;

        UpdateManualWallGeometry(
            cutTargetWallIndex,
            ToFloorWorld(newStart),
            ToFloorWorld(newEnd)
        );

        SetWallMaterial(target, restoreMaterial);

        if (target == lastGeneratedWall)
        {
            ClearLastGeneratedWallState();
        }

        ClearCutTargetSelection(restoreMaterialAlreadyApplied: true);

        status = $"Manual wall cut applied. remaining length:{remainingLength:F2}";
        cutModeStatus = status;
        Debug.Log($"[ManualWall] {status}");
    }

    private void ApplySceneWallCut(SurfaceSelection areaSelection)
    {
        if (!TryGetSceneWallSegment(cutTargetSourceKind, cutTargetWallIndex, out WallSegment segment))
        {
            ClearCutTargetSelection();
            status = "Detected wall cut target is no longer valid.";
            cutModeStatus = status;
            Debug.LogWarning($"[ManualWall] {status}");
            return;
        }

        if (!TryCalculateCutResult(segment.start, segment.end, areaSelection, out Vector2 newStart, out Vector2 newEnd, out float remainingLength))
        {
            RemoveSceneWallSegment(cutTargetSourceKind, cutTargetWallIndex);
            ClearCutTargetSelection(restoreMaterialAlreadyApplied: true);
            RebuildSelectableSurfaces();
            status = "Detected wall cut target was too short and removed.";
            cutModeStatus = status;
            Debug.Log($"[ManualWall] {status}");
            return;
        }

        if (remainingLength < Mathf.Max(minWallLength, minWallLengthAfterCut))
        {
            RemoveSceneWallSegment(cutTargetSourceKind, cutTargetWallIndex);
            ClearCutTargetSelection(restoreMaterialAlreadyApplied: true);
            RebuildSelectableSurfaces();
            status = "Remaining detected wall was too short. Wall removed.";
            cutModeStatus = status;
            Debug.Log($"[ManualWall] {status}");
            return;
        }

        UpdateSceneWallSegment(cutTargetSourceKind, cutTargetWallIndex, newStart, newEnd);

        ClearCutTargetSelection(restoreMaterialAlreadyApplied: true);
        RebuildSelectableSurfaces();

        status = $"Detected wall cut applied. remaining length:{remainingLength:F2}";
        cutModeStatus = status;
        Debug.Log($"[ManualWall] {status}");
    }

    private bool TryCalculateCutResult(
        Vector2 segmentStart,
        Vector2 segmentEnd,
        SurfaceSelection areaSelection,
        out Vector2 newStart,
        out Vector2 newEnd,
        out float remainingLength)
    {
        newStart = segmentStart;
        newEnd = segmentEnd;
        remainingLength = 0.0f;

        Vector2 areaPoint = GetSelectionPoint2D(areaSelection);
        Vector2 dir = segmentEnd - segmentStart;

        if (dir.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        dir.Normalize();

        float cutT = Vector2.Dot(cutPoint - segmentStart, dir);
        float areaT = Vector2.Dot(areaPoint - segmentStart, dir);
        float totalLength = Vector2.Distance(segmentStart, segmentEnd);

        cutT = Mathf.Clamp(cutT, 0.0f, totalLength);

        if (areaT < cutT)
        {
            newStart = cutPoint;
        }
        else
        {
            newEnd = cutPoint;
        }

        remainingLength = Vector2.Distance(newStart, newEnd);
        return true;
    }

    private Vector2 GetSelectionPoint2D(SurfaceSelection selection)
    {
        if ((selection.kind == SelectableSurfaceKind.DetectedWall ||
             selection.kind == SelectableSurfaceKind.ManualWall) &&
            TryGetSelectedWallSegment(selection, out Vector2 wallStart, out Vector2 wallEnd))
        {
            Vector3 projected = ProjectPointToWallSegment(selection.point, wallStart, wallEnd);
            return new Vector2(projected.x, projected.z);
        }

        return new Vector2(selection.point.x, selection.point.z);
    }

    private bool TryFindManualWallIndex(GameObject selectedObject, out int index)
    {
        index = -1;

        if (selectedObject == null)
        {
            return false;
        }

        for (int i = 0; i < manualWalls.Count; i++)
        {
            GameObject wall = manualWalls[i];

            if (wall == null)
            {
                continue;
            }

            if (wall == selectedObject || selectedObject.transform.IsChildOf(wall.transform))
            {
                index = i;
                return true;
            }
        }

        return false;
    }

    private void DeleteManualWallAt(int index)
    {
        if (index < 0 || index >= manualWalls.Count)
        {
            return;
        }

        GameObject target = manualWalls[index];
        manualWalls.RemoveAt(index);

        if (index < manualWallSegments.Count)
        {
            manualWallSegments.RemoveAt(index);
        }

        if (target != null)
        {
            Destroy(target);
        }

        if (lastGeneratedWallIndex == index)
        {
            ClearLastGeneratedWallState();
        }
        else if (lastGeneratedWallIndex > index)
        {
            lastGeneratedWallIndex--;
        }

        if (cutTargetSourceKind == SelectableWallSourceKind.ManualWall)
        {
            if (cutTargetWallIndex == index)
            {
                cutTargetWallIndex = -1;
                cutTargetWall = null;
                cutTargetSourceKind = SelectableWallSourceKind.Unknown;
                hasCutTarget = false;
            }
            else if (cutTargetWallIndex > index)
            {
                cutTargetWallIndex--;
            }
        }

        RefreshManualWallSourceIndices();
    }

    private void RefreshManualWallSourceIndices()
    {
        for (int i = 0; i < manualWalls.Count; i++)
        {
            GameObject wall = manualWalls[i];

            if (wall == null || i >= manualWallSegments.Count)
            {
                continue;
            }

            SetSelectableWallSegmentData(
                wall,
                manualWallSegments[i].start,
                manualWallSegments[i].end,
                SelectableWallSourceKind.ManualWall,
                i
            );
        }
    }

    private void ClearCutTargetSelection(bool restoreMaterialAlreadyApplied = false)
    {
        if (!restoreMaterialAlreadyApplied && cutTargetWall != null)
        {
            SetWallMaterial(
                cutTargetWall,
                cutTargetOriginalMaterial != null ? cutTargetOriginalMaterial : manualWallMaterial
            );
        }

        cutTargetWallIndex = -1;
        cutTargetWall = null;
        cutPoint = Vector2.zero;
        cutTargetOriginalMaterial = null;
        cutTargetSourceKind = SelectableWallSourceKind.Unknown;
        hasCutTarget = false;
    }

    private void HandleFloorSelection(SurfaceSelection selection)
    {
        Vector3 point = ToFloorWorld(selection.point);

        if (!firstFloorPoint.HasValue)
        {
            firstFloorPoint = point;
            hasFirstFloorPoint = true;
            lastPreviewPoint = point;
            lastValidPreviewPointTime = Time.time;

            CreateFirstPointMarker(point);
            SetPreviewLineVisible(true);
            UpdatePreviewLine(point, point);

            status = "First floor point selected. Select floor or wall as second point.";
            Debug.Log($"[ManualWall] {status}");
            return;
        }

        Vector3 start = firstFloorPoint.Value;
        Vector3 end = point;
        SnapWallEndpoints(ref start, ref end);

        CreateManualWallInternal(
            start,
            end,
            LastWallCreationType.TwoPoint,
            new Vector2(start.x, start.z),
            GetDirection2D(start, end),
            highlightAsLastCreated: true
        );

        ClearPendingFirstPoint();
        SetPreviewLineVisible(false);

        status = "Floor two-point wall completed.";
        Debug.Log($"[ManualWall] {status}");
    }

    private void HandleWallSelection(SurfaceSelection selection)
    {
        if (!TryGetSelectedWallSegment(selection, out Vector2 wallStart, out Vector2 wallEnd))
        {
            status = "Selected wall segment data is missing.";
            Debug.LogWarning($"[ManualWall] {status}");
            return;
        }

        Vector3 projectedWorld = ProjectPointToWallSegment(selection.point, wallStart, wallEnd);

        if (firstFloorPoint.HasValue)
        {
            Vector3 start = firstFloorPoint.Value;
            Vector3 end = ToFloorWorld(projectedWorld);
            SnapWallEndpoints(ref start, ref end);

            CreateManualWallInternal(
                start,
                end,
                LastWallCreationType.TwoPoint,
                new Vector2(start.x, start.z),
                GetDirection2D(start, end),
                highlightAsLastCreated: true
            );

            ClearPendingFirstPoint();
            SetPreviewLineVisible(false);

            status = "Wall completed by using selected wall point as second floor point.";
            Debug.Log($"[ManualWall] {status}");
            return;
        }

        Vector2 wallDir = wallEnd - wallStart;

        if (wallDir.sqrMagnitude < 0.0001f)
        {
            status = "Selected wall segment is too short.";
            Debug.LogWarning($"[ManualWall] {status}");
            return;
        }

        wallDir.Normalize();

        Vector2 projected = new Vector2(projectedWorld.x, projectedWorld.z);
        Vector2 anchor;
        LastWallCreationType creationType;

        float distanceToStart = Vector2.Distance(projected, wallStart);
        float distanceToEnd = Vector2.Distance(projected, wallEnd);

        if (Mathf.Min(distanceToStart, distanceToEnd) <= wallCornerSelectDistance)
        {
            anchor = distanceToStart <= distanceToEnd ? wallStart : wallEnd;
            creationType = LastWallCreationType.WallCornerPerpendicular;
        }
        else
        {
            anchor = projected;
            creationType = LastWallCreationType.WallMiddlePerpendicular;
        }

        Vector2 normal = new Vector2(-wallDir.y, wallDir.x).normalized;
        WallBuildCandidate candidate = ChooseBestCandidate(anchor, normal);

        CreateManualWallInternal(
            ToFloorWorld(candidate.anchor),
            ToFloorWorld(candidate.end),
            creationType,
            candidate.anchor,
            candidate.direction,
            highlightAsLastCreated: true
        );

        string hitText = candidate.hitTarget ? "target wall found" : "fallback max length";
        status = $"Wall-based manual wall created. type:{creationType}, {hitText}";
        Debug.Log($"[ManualWall] {status}");
    }

    public List<ManualWallSegmentSnapshot> GetManualWallSegmentsSnapshot()
    {
        List<ManualWallSegmentSnapshot> result = new List<ManualWallSegmentSnapshot>();

        for (int i = 0; i < manualWallSegments.Count; i++)
        {
            ManualWallSegmentData segment = manualWallSegments[i];

            if (segment.wallObject == null)
            {
                continue;
            }

            if (!segment.wallObject.activeInHierarchy)
            {
                continue;
            }

            if (Vector2.Distance(segment.start, segment.end) < minWallLength)
            {
                continue;
            }

            result.Add(new ManualWallSegmentSnapshot
            {
                start = segment.start,
                end = segment.end,
                wallObject = segment.wallObject
            });
        }

        return result;
    }

    private GameObject CreateManualWallInternal(
        Vector3 startWorld,
        Vector3 endWorld,
        LastWallCreationType creationType,
        Vector2 anchor,
        Vector2 direction,
        bool highlightAsLastCreated)
    {
        if (scanner == null || scanner.currentRoom == null)
        {
            status = "Cannot create wall. Scanner or currentRoom is null.";
            Debug.LogWarning($"[ManualWall] {status}");
            return null;
        }

        float floorY = scanner.currentRoom.floorY;
        float ceilingY = scanner.currentRoom.ceilingY;
        float height = ceilingY - floorY;

        if (height <= 0.1f)
        {
            status = $"Invalid room height: {height:F2}";
            Debug.LogWarning($"[ManualWall] {status}");
            return null;
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
            return null;
        }

        ResetPreviousLastCreatedMaterial();

        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "ManualWall";

        ApplyWallTransform(wall, start, end, height);
        SetWallMaterial(wall, highlightAsLastCreated ? GetLastCreatedWallMaterial() : manualWallMaterial);

        if (manualWallRoot != null)
        {
            wall.transform.SetParent(manualWallRoot, true);
        }

        SetSelectableWallSegmentData(wall, new Vector2(start.x, start.z), new Vector2(end.x, end.z), SelectableWallSourceKind.ManualWall, manualWalls.Count);

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
            highlightAsLastCreated ? GetLastCreatedWallMaterial() : manualWallMaterial,
            visible: true
        );

        if (highlightAsLastCreated)
        {
            lastGeneratedWall = wall;
            lastGeneratedWallIndex = manualWalls.Count - 1;
            lastWallCreationType = creationType;
            lastWallAnchor = anchor;
            lastWallDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : GetDirection2D(start, end);
            hasLastGeneratedWall = true;
            lastGeneratedWallType = creationType.ToString();
        }

        status = $"Manual wall created. length:{length:F2}, height:{height:F2}, type:{creationType}";
        Debug.Log($"[ManualWall] {status}");

        return wall;
    }

    private bool UpdateManualWallGeometry(int index, Vector3 startWorld, Vector3 endWorld)
    {
        if (index < 0 || index >= manualWalls.Count)
        {
            return false;
        }

        GameObject wall = manualWalls[index];

        if (wall == null || scanner == null || scanner.currentRoom == null)
        {
            return false;
        }

        float floorY = scanner.currentRoom.floorY;
        float ceilingY = scanner.currentRoom.ceilingY;
        float height = Mathf.Max(0.1f, ceilingY - floorY);

        Vector3 start = new Vector3(startWorld.x, floorY, startWorld.z);
        Vector3 end = new Vector3(endWorld.x, floorY, endWorld.z);

        Vector3 delta = end - start;
        delta.y = 0.0f;

        if (delta.magnitude < minWallLength)
        {
            return false;
        }

        ApplyWallTransform(wall, start, end, height);

        Vector2 start2 = new Vector2(start.x, start.z);
        Vector2 end2 = new Vector2(end.x, end.z);

        SetSelectableWallSegmentData(wall, start2, end2, SelectableWallSourceKind.ManualWall, index);

        ManualWallSegmentData data = manualWallSegments[index];
        data.start = start2;
        data.end = end2;
        data.wallObject = wall;
        manualWallSegments[index] = data;

        return true;
    }

    private void ApplyWallTransform(GameObject wall, Vector3 start, Vector3 end, float height)
    {
        Vector3 delta = end - start;
        delta.y = 0.0f;

        float length = Mathf.Max(minWallLength, delta.magnitude);
        Vector3 direction = delta.normalized;
        Vector3 center = (start + end) * 0.5f;
        center.y = start.y + height * 0.5f;

        wall.transform.position = center;

        float yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        wall.transform.rotation = Quaternion.Euler(0.0f, yaw, 0.0f);

        wall.transform.localScale = new Vector3(
            wallThickness,
            height,
            length
        );
    }

    private void SetSelectableWallSegmentData(
        GameObject wall,
        Vector2 start,
        Vector2 end,
        SelectableWallSourceKind sourceKind = SelectableWallSourceKind.Unknown,
        int sourceIndex = -1)
    {
        if (wall == null)
        {
            return;
        }

        SelectableWallSegmentData data = wall.GetComponent<SelectableWallSegmentData>();

        if (data == null)
        {
            data = wall.AddComponent<SelectableWallSegmentData>();
        }

        data.start = start;
        data.end = end;
        data.sourceKind = sourceKind;
        data.sourceIndex = sourceIndex;
    }

    private void ResetPreviousLastCreatedMaterial()
    {
        if (lastGeneratedWall != null)
        {
            SetWallMaterial(lastGeneratedWall, manualWallMaterial);
        }
    }

    private void ClearLastGeneratedWallState()
    {
        lastGeneratedWall = null;
        lastGeneratedWallIndex = -1;
        lastWallCreationType = LastWallCreationType.None;
        lastWallAnchor = Vector2.zero;
        lastWallDirection = Vector2.zero;
        hasLastGeneratedWall = false;
        lastGeneratedWallType = LastWallCreationType.None.ToString();
    }

    private bool IsLastWallRotatable()
    {
        if (lastGeneratedWall == null || lastGeneratedWallIndex < 0)
        {
            return false;
        }

        if (lastWallCreationType != LastWallCreationType.WallCornerPerpendicular &&
            lastWallCreationType != LastWallCreationType.WallMiddlePerpendicular)
        {
            return false;
        }

        if (lastGeneratedWallIndex >= manualWalls.Count)
        {
            return false;
        }

        return manualWalls[lastGeneratedWallIndex] == lastGeneratedWall;
    }

    private float GetManualWallLength(int index)
    {
        if (index < 0 || index >= manualWallSegments.Count)
        {
            return maxPerpendicularWallLength;
        }

        ManualWallSegmentData segment = manualWallSegments[index];
        return Mathf.Max(minWallLength, Vector2.Distance(segment.start, segment.end));
    }

    private WallBuildCandidate ChooseBestCandidate(Vector2 anchor, Vector2 normal)
    {
        Vector2 positive = normal.sqrMagnitude > 0.0001f ? normal.normalized : Vector2.right;
        Vector2 negative = -positive;

        WallBuildCandidate positiveCandidate = BuildCandidateFromAnchorAndDirection(anchor, positive, allowTargetSearch: true);
        WallBuildCandidate negativeCandidate = BuildCandidateFromAnchorAndDirection(anchor, negative, allowTargetSearch: true);

        if (positiveCandidate.hitTarget && negativeCandidate.hitTarget)
        {
            return positiveCandidate.distance <= negativeCandidate.distance
                ? positiveCandidate
                : negativeCandidate;
        }

        if (positiveCandidate.hitTarget)
        {
            return positiveCandidate;
        }

        if (negativeCandidate.hitTarget)
        {
            return negativeCandidate;
        }

        return positiveCandidate;
    }

    private WallBuildCandidate BuildCandidateFromAnchorAndDirection(
        Vector2 anchor,
        Vector2 direction,
        bool allowTargetSearch,
        float fallbackLength = -1.0f,
        float searchMaxDistance = -1.0f,
        float searchMinDistance = -1.0f)
    {
        Vector2 dir = direction.sqrMagnitude > 0.0001f
            ? direction.normalized
            : Vector2.right;

        float resolvedFallbackLength = fallbackLength > 0.0f
            ? fallbackLength
            : maxPerpendicularWallLength;

        float resolvedSearchMaxDistance = searchMaxDistance > 0.0f
            ? searchMaxDistance
            : maxPerpendicularWallLength;

        float resolvedSearchMinDistance = searchMinDistance > 0.0f
            ? searchMinDistance
            : minPerpendicularIntersectionDistance;

        WallBuildCandidate candidate = new WallBuildCandidate
        {
            anchor = anchor,
            direction = dir,
            end = anchor + dir * resolvedFallbackLength,
            distance = resolvedFallbackLength,
            hitTarget = false
        };

        if (!allowTargetSearch || !extendPerpendicularWallToNextWall)
        {
            return candidate;
        }

        Vector2 searchOrigin = anchor + dir * perpendicularStartOffsetFromSourceWall;

        if (TryFindNearestWallHit(
                searchOrigin,
                dir,
                resolvedSearchMinDistance,
                resolvedSearchMaxDistance,
                out Vector2 hitPoint,
                out float distance))
        {
            Vector2 correctedEnd = hitPoint - dir * perpendicularWallEndPadding;

            if (Vector2.Distance(anchor, correctedEnd) >= minWallLength)
            {
                candidate.end = correctedEnd;
                candidate.distance = Vector2.Distance(anchor, correctedEnd);
                candidate.hitTarget = true;
            }
        }

        return candidate;
    }

    private bool TryFindNearestWallHit(
        Vector2 origin,
        Vector2 direction,
        float minDistance,
        float maxDistance,
        out Vector2 hitPoint,
        out float hitDistance)
    {
        hitPoint = default;
        hitDistance = float.MaxValue;

        if (scanner == null || scanner.currentRoom == null)
        {
            return false;
        }

        Vector2 dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;
        bool found = false;

        foreach (WallSegment wall in scanner.currentRoom.detectedWalls)
        {
            if (TryRaySegmentIntersection(origin, dir, wall.start, wall.end, minDistance, maxDistance, out float distance))
            {
                if (distance < hitDistance)
                {
                    hitDistance = distance;
                    hitPoint = origin + dir * distance;
                    found = true;
                }
            }
        }

        if (includeClosureEdgesForPerpendicularExtend)
        {
            foreach (WallSegment edge in scanner.currentRoom.closureEdges)
            {
                if (TryRaySegmentIntersection(origin, dir, edge.start, edge.end, minDistance, maxDistance, out float distance))
                {
                    if (distance < hitDistance)
                    {
                        hitDistance = distance;
                        hitPoint = origin + dir * distance;
                        found = true;
                    }
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

                if (manual.wallObject == lastGeneratedWall)
                {
                    continue;
                }

                if (TryRaySegmentIntersection(origin, dir, manual.start, manual.end, minDistance, maxDistance, out float distance))
                {
                    if (distance < hitDistance)
                    {
                        hitDistance = distance;
                        hitPoint = origin + dir * distance;
                        found = true;
                    }
                }
            }
        }

        return found;
    }

    private void SnapWallEndpoints(ref Vector3 startWorld, ref Vector3 endWorld)
    {
        Vector2 start = new Vector2(startWorld.x, startWorld.z);
        Vector2 end = new Vector2(endWorld.x, endWorld.z);

        if (TryFindSnapPoint(start, out Vector2 snappedStart))
        {
            start = snappedStart;
        }

        if (TryFindSnapPoint(end, out Vector2 snappedEnd))
        {
            end = snappedEnd;
        }

        startWorld = ToFloorWorld(start);
        endWorld = ToFloorWorld(end);
    }

    private bool TryFindSnapPoint(Vector2 point, out Vector2 snapped)
    {
        snapped = point;

        float bestDistance = float.MaxValue;
        Vector2 bestPoint = point;

        foreach (WallSegment wall in GetAllSceneWallSegments())
        {
            TryConsiderSnapCandidate(point, wall.start, snapPointToCornerDistance, ref bestDistance, ref bestPoint);
            TryConsiderSnapCandidate(point, wall.end, snapPointToCornerDistance, ref bestDistance, ref bestPoint);

            Vector2 projected = ClosestPointOnSegment(point, wall.start, wall.end);
            TryConsiderSnapCandidate(point, projected, snapPointToWallDistance, ref bestDistance, ref bestPoint);
        }

        foreach (ManualWallSegmentData manual in manualWallSegments)
        {
            if (manual.wallObject == null)
            {
                continue;
            }

            TryConsiderSnapCandidate(point, manual.start, snapPointToCornerDistance, ref bestDistance, ref bestPoint);
            TryConsiderSnapCandidate(point, manual.end, snapPointToCornerDistance, ref bestDistance, ref bestPoint);

            Vector2 projected = ClosestPointOnSegment(point, manual.start, manual.end);
            TryConsiderSnapCandidate(point, projected, snapPointToWallDistance, ref bestDistance, ref bestPoint);
        }

        if (bestDistance < float.MaxValue)
        {
            snapped = bestPoint;
            return true;
        }

        return false;
    }

    private void TryConsiderSnapCandidate(
        Vector2 source,
        Vector2 candidate,
        float threshold,
        ref float bestDistance,
        ref Vector2 bestPoint)
    {
        float distance = Vector2.Distance(source, candidate);

        if (distance <= threshold && distance < bestDistance)
        {
            bestDistance = distance;
            bestPoint = candidate;
        }
    }

    private IEnumerable<WallSegment> GetAllSceneWallSegments()
    {
        if (scanner == null || scanner.currentRoom == null)
        {
            yield break;
        }

        foreach (WallSegment wall in scanner.currentRoom.detectedWalls)
        {
            yield return wall;
        }

        if (includeClosureEdgesForPerpendicularExtend)
        {
            foreach (WallSegment edge in scanner.currentRoom.closureEdges)
            {
                yield return edge;
            }
        }
    }

    private bool TryGetSelectedWallData(SurfaceSelection selection, out SelectableWallSegmentData data)
    {
        data = null;

        if (selection.surfaceObject == null)
        {
            return false;
        }

        data = selection.surfaceObject.GetComponent<SelectableWallSegmentData>();

        if (data == null)
        {
            data = selection.surfaceObject.GetComponentInParent<SelectableWallSegmentData>();
        }

        return data != null && Vector2.Distance(data.start, data.end) >= minWallLength;
    }

    private bool TryFindSceneWallIndex(
        Vector2 start,
        Vector2 end,
        out SelectableWallSourceKind sourceKind,
        out int sourceIndex)
    {
        sourceKind = SelectableWallSourceKind.Unknown;
        sourceIndex = -1;

        if (scanner == null || scanner.currentRoom == null)
        {
            return false;
        }

        if (TryFindWallIndexInList(scanner.currentRoom.detectedWalls, start, end, out sourceIndex))
        {
            sourceKind = SelectableWallSourceKind.DetectedWall;
            return true;
        }

        if (TryFindWallIndexInList(scanner.currentRoom.closureEdges, start, end, out sourceIndex))
        {
            sourceKind = SelectableWallSourceKind.ClosureEdge;
            return true;
        }

        return false;
    }

    private bool TryFindWallIndexInList(List<WallSegment> walls, Vector2 start, Vector2 end, out int index)
    {
        index = -1;

        if (walls == null)
        {
            return false;
        }

        for (int i = 0; i < walls.Count; i++)
        {
            WallSegment wall = walls[i];

            bool sameDirection = Vector2.Distance(wall.start, start) <= 0.02f &&
                                 Vector2.Distance(wall.end, end) <= 0.02f;

            bool oppositeDirection = Vector2.Distance(wall.start, end) <= 0.02f &&
                                     Vector2.Distance(wall.end, start) <= 0.02f;

            if (sameDirection || oppositeDirection)
            {
                index = i;
                return true;
            }
        }

        return false;
    }

    private bool TryGetSceneWallSegment(SelectableWallSourceKind sourceKind, int sourceIndex, out WallSegment segment)
    {
        segment = default;

        if (scanner == null || scanner.currentRoom == null || sourceIndex < 0)
        {
            return false;
        }

        if (sourceKind == SelectableWallSourceKind.DetectedWall &&
            sourceIndex < scanner.currentRoom.detectedWalls.Count)
        {
            segment = scanner.currentRoom.detectedWalls[sourceIndex];
            return true;
        }

        if (sourceKind == SelectableWallSourceKind.ClosureEdge &&
            sourceIndex < scanner.currentRoom.closureEdges.Count)
        {
            segment = scanner.currentRoom.closureEdges[sourceIndex];
            return true;
        }

        return false;
    }

    private void UpdateSceneWallSegment(SelectableWallSourceKind sourceKind, int sourceIndex, Vector2 newStart, Vector2 newEnd)
    {
        if (!TryGetSceneWallSegment(sourceKind, sourceIndex, out WallSegment segment))
        {
            return;
        }

        segment.start = newStart;
        segment.end = newEnd;
        segment.center = (newStart + newEnd) * 0.5f;

        if (sourceKind == SelectableWallSourceKind.DetectedWall)
        {
            scanner.currentRoom.detectedWalls[sourceIndex] = segment;
        }
        else if (sourceKind == SelectableWallSourceKind.ClosureEdge)
        {
            scanner.currentRoom.closureEdges[sourceIndex] = segment;
        }
    }

    private void RemoveSceneWallSegment(SelectableWallSourceKind sourceKind, int sourceIndex)
    {
        if (scanner == null || scanner.currentRoom == null || sourceIndex < 0)
        {
            return;
        }

        if (sourceKind == SelectableWallSourceKind.DetectedWall &&
            sourceIndex < scanner.currentRoom.detectedWalls.Count)
        {
            scanner.currentRoom.detectedWalls.RemoveAt(sourceIndex);
        }
        else if (sourceKind == SelectableWallSourceKind.ClosureEdge &&
                 sourceIndex < scanner.currentRoom.closureEdges.Count)
        {
            scanner.currentRoom.closureEdges.RemoveAt(sourceIndex);
        }
    }

    private bool TryGetSelectedWallSegment(
        SurfaceSelection selection,
        out Vector2 start,
        out Vector2 end)
    {
        start = default;
        end = default;

        if (selection.surfaceObject == null)
        {
            return false;
        }

        if (TryGetSelectedWallData(selection, out SelectableWallSegmentData data))
        {
            start = data.start;
            end = data.end;
            return true;
        }

        Transform t = selection.surfaceObject.transform;
        float length = t.lossyScale.z;

        Vector3 a = t.position - t.forward * length * 0.5f;
        Vector3 b = t.position + t.forward * length * 0.5f;

        start = new Vector2(a.x, a.z);
        end = new Vector2(b.x, b.z);

        return Vector2.Distance(start, end) >= minWallLength;
    }

    private Vector3 ProjectPointToWallSegment(Vector3 point, Vector2 segmentA, Vector2 segmentB)
    {
        Vector2 p = new Vector2(point.x, point.z);
        Vector2 projected = ClosestPointOnSegment(p, segmentA, segmentB);
        return new Vector3(projected.x, GetFloorY(point.y), projected.y);
    }

    private static Vector2 ClosestPointOnSegment(Vector2 point, Vector2 segmentA, Vector2 segmentB)
    {
        Vector2 ab = segmentB - segmentA;
        float denominator = Mathf.Max(ab.sqrMagnitude, 0.000001f);
        float t = Vector2.Dot(point - segmentA, ab) / denominator;
        t = Mathf.Clamp01(t);
        return segmentA + ab * t;
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

        Vector2 dir = rayDirection.sqrMagnitude > 0.0001f
            ? rayDirection.normalized
            : Vector2.right;

        Vector2 v1 = rayOrigin - segmentA;
        Vector2 v2 = segmentB - segmentA;
        Vector2 v3 = new Vector2(-dir.y, dir.x);

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

    private static Vector2 Rotate2D(Vector2 vector, float degrees)
    {
        float rad = degrees * Mathf.Deg2Rad;
        float cos = Mathf.Cos(rad);
        float sin = Mathf.Sin(rad);

        return new Vector2(
            vector.x * cos - vector.y * sin,
            vector.x * sin + vector.y * cos
        );
    }

    private Vector2 GetDirection2D(Vector3 startWorld, Vector3 endWorld)
    {
        Vector2 start = new Vector2(startWorld.x, startWorld.z);
        Vector2 end = new Vector2(endWorld.x, endWorld.z);
        Vector2 direction = end - start;

        return direction.sqrMagnitude > 0.0001f
            ? direction.normalized
            : Vector2.right;
    }

    private Vector3 ToFloorWorld(Vector2 point)
    {
        return new Vector3(point.x, GetFloorY(0.0f), point.y);
    }

    private Vector3 ToFloorWorld(Vector3 point)
    {
        return new Vector3(point.x, GetFloorY(point.y), point.z);
    }

    private float GetFloorY(float fallback)
    {
        if (scanner != null && scanner.currentRoom != null)
        {
            return scanner.currentRoom.floorY;
        }

        return fallback;
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

        for (int i = 0; i < scanner.currentRoom.detectedWalls.Count; i++)
        {
            CreateSelectableWallSurface(
                scanner.currentRoom.detectedWalls[i],
                SelectableWallSourceKind.DetectedWall,
                i,
                "SelectableDetectedWall"
            );
        }

        if (includeClosureEdgesForPerpendicularExtend)
        {
            for (int i = 0; i < scanner.currentRoom.closureEdges.Count; i++)
            {
                CreateSelectableWallSurface(
                    scanner.currentRoom.closureEdges[i],
                    SelectableWallSourceKind.ClosureEdge,
                    i,
                    "SelectableClosureEdge"
                );
            }
        }
    }

    private void CreateSelectableWallSurface(
        WallSegment wall,
        SelectableWallSourceKind sourceKind,
        int sourceIndex,
        string objectName)
    {
        float floorY = scanner.currentRoom.floorY;
        float ceilingY = scanner.currentRoom.ceilingY;
        float height = Mathf.Max(0.1f, ceilingY - floorY);

        float length = Vector2.Distance(wall.start, wall.end);

        if (length < minWallLength)
        {
            return;
        }

        GameObject wallSurface = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wallSurface.name = objectName;

        ApplySelectableWallTransform(wallSurface, wall.start, wall.end, height);

        SetSelectableWallSegmentData(wallSurface, wall.start, wall.end, sourceKind, sourceIndex);

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

    private void ApplySelectableWallTransform(GameObject wallSurface, Vector2 start, Vector2 end, float height)
    {
        Vector2 mid2 = (start + end) * 0.5f;
        float length = Mathf.Max(minWallLength, Vector2.Distance(start, end));
        Vector2 dir2 = (end - start).sqrMagnitude > 0.0001f
            ? (end - start).normalized
            : Vector2.up;

        float yaw = Mathf.Atan2(dir2.x, dir2.y) * Mathf.Rad2Deg;

        wallSurface.transform.position = new Vector3(
            mid2.x,
            GetFloorY(0.0f) + height * 0.5f,
            mid2.y
        );

        wallSurface.transform.rotation = Quaternion.Euler(0.0f, yaw, 0.0f);

        wallSurface.transform.localScale = new Vector3(
            selectableWallThickness,
            height,
            length
        );
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

        ManualWallSelectableSurface selectable = target.GetComponent<ManualWallSelectableSurface>();

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

    private void UpdateTwoPointPreview()
    {
        if (!IsCreateModeActive() || !firstFloorPoint.HasValue)
        {
            SetPreviewLineVisible(false);
            return;
        }

        if (TryGetCurrentManualTargetPoint(out Vector3 currentPoint))
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

    private bool TryGetCurrentManualTargetPoint(out Vector3 point)
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

            if (surface == null)
            {
                continue;
            }

            if (surface.surfaceKind == SelectableSurfaceKind.Floor)
            {
                point = ToFloorWorld(hit.point);
                return true;
            }

            SurfaceSelection selection = new SurfaceSelection
            {
                kind = surface.surfaceKind,
                point = hit.point,
                normal = hit.normal,
                surfaceObject = surface.gameObject
            };

            if (TryGetSelectedWallSegment(selection, out Vector2 wallStart, out Vector2 wallEnd))
            {
                point = ProjectPointToWallSegment(hit.point, wallStart, wallEnd);
                return true;
            }
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
        firstPointMarker.transform.position = ToFloorWorld(position);
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

    private void ClearPendingFirstPoint()
    {
        firstFloorPoint = null;
        hasFirstFloorPoint = false;
        ClearFirstPointMarker();
    }

    private void ClearFirstPointMarker()
    {
        if (firstPointMarker != null)
        {
            Destroy(firstPointMarker);
            firstPointMarker = null;
        }
    }

    private void SetWallMaterial(GameObject wall, Material material)
    {
        if (wall == null)
        {
            return;
        }

        MeshRenderer renderer = wall.GetComponent<MeshRenderer>();

        if (renderer != null && material != null)
        {
            renderer.sharedMaterial = material;
        }
    }

    private Material GetWallMaterial(GameObject wall)
    {
        if (wall == null)
        {
            return null;
        }

        MeshRenderer renderer = wall.GetComponent<MeshRenderer>();
        return renderer != null ? renderer.sharedMaterial : null;
    }

    private Material GetWallCutTargetMaterial()
    {
        if (wallCutTargetMaterial != null)
        {
            return wallCutTargetMaterial;
        }

        EnsureRuntimeMaterials();
        return runtimeWallCutTargetMaterial;
    }

    private Material GetLastCreatedWallMaterial()
    {
        if (lastCreatedWallMaterial != null)
        {
            return lastCreatedWallMaterial;
        }

        EnsureRuntimeMaterials();
        return runtimeLastCreatedWallMaterial;
    }

    private void EnsureRuntimeMaterials()
    {
        if (runtimeLastCreatedWallMaterial == null)
        {
            runtimeLastCreatedWallMaterial = CreateRuntimeMaterial(
                "Runtime_LastCreatedManualWall_Material",
                new Color(1.0f, 0.78f, 0.15f, 0.75f),
                lastCreatedWallMaterial
            );
        }

        if (runtimeWallCutTargetMaterial == null)
        {
            runtimeWallCutTargetMaterial = CreateRuntimeMaterial(
                "Runtime_WallCutTarget_Material",
                new Color(1.0f, 0.25f, 0.15f, 0.75f),
                wallCutTargetMaterial
            );
        }
    }

    private Material CreateRuntimeMaterial(string materialName, Color color, Material preferredTemplate)
    {
        Material template = preferredTemplate;

        if (template == null)
        {
            template = manualWallMaterial;
        }

        if (template == null)
        {
            template = selectableWallMaterial;
        }

        if (template == null)
        {
            template = markerMaterial;
        }

        if (template != null && template.shader != null)
        {
            Material cloned = new Material(template);
            cloned.name = materialName;
            cloned.hideFlags = HideFlags.DontSave;
            ApplyRuntimeMaterialColor(cloned, color);
            return cloned;
        }

        Shader shader = FindRuntimeCompatibleShader();

        if (shader == null)
        {
            if (!warnedRuntimeMaterialShaderMissing)
            {
                warnedRuntimeMaterialShaderMissing = true;
                Debug.LogWarning(
                    "[ManualWall] Runtime highlight material was not created because no compatible shader was found in this build. " +
                    "Assign Last Created Wall Material and Wall Cut Target Material in the Inspector to enable highlight colors."
                );
            }

            return null;
        }

        Material material = new Material(shader);
        material.name = materialName;
        material.hideFlags = HideFlags.DontSave;
        ApplyRuntimeMaterialColor(material, color);
        return material;
    }

    private static Shader FindRuntimeCompatibleShader()
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

    private static void ApplyRuntimeMaterialColor(Material material, Color color)
    {
        if (material == null)
        {
            return;
        }

        material.color = color;

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }
    }
}

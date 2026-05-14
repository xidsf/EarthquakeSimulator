using System;
using System.Collections.Generic;
using MixedReality.Toolkit.UX;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

public partial class ManualWallBuilder : MonoBehaviour
{
    public enum BuildMode
    {
        None,
        ManualWallCreate,
        WallCut,
        WallDelete,
        WallExtend
    }

    public enum SelectableSurfaceKind
    {
        Floor,
        DetectedWall,
        ActiveWallCandidate,
        IgnoredWallCandidate,
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
        ActiveWallCandidate,
        IgnoredWallCandidate,
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

    private enum EditUndoActionKind
    {
        None,
        ManualWallCreated,
        ManualWallCutModified,
        ManualWallCutRemoved,
        SceneWallCutModified,
        SceneWallCutRemoved,
        ActiveCandidatePromoted,
        ActiveCandidateRemoved
    }

    private struct EditUndoAction
    {
        public int actionId;
        public EditUndoActionKind kind;
        public SelectableWallSourceKind sourceKind;
        public int index;
        public Vector2 start;
        public Vector2 end;
        public WallSegment sceneSegment;
        public GameObject wallObject;
        public Material material;
    }

    private struct PendingWallExtendTarget
    {
        public bool hasTarget;
        public SelectableWallSourceKind sourceKind;
        public int sourceIndex;
        public GameObject wallObject;
        public Vector2 start;
        public Vector2 end;
    }

    private struct ManualWallCreationGuardRecord
    {
        public float time;
        public GameObject wallObject;
    }

    private struct WallCutGuardRecord
    {
        public float time;
        public EditUndoAction action;
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
    public Material selectableActiveWallCandidateMaterial;
    public Material selectableIgnoredWallCandidateMaterial;
    public Material markerMaterial;

    [Tooltip("방금 생성된 벽을 구분하기 위한 Material. 비워두면 런타임 Material을 자동 생성합니다.")]
    public Material lastCreatedWallMaterial;

    [Header("Manual Wall Settings")]
    public float wallThickness = 0.06f;

    public float minWallLength = 0.2f;

    [Header("Manual Wall Burst Guard")]
    [Tooltip("HoloLens 입력/인식 오류로 짧은 시간 안에 벽이 연속 생성되는 경우, 해당 구간에 생성된 벽들을 자동 취소합니다.")]
    public bool enableManualWallBurstGuard = true;

    [Tooltip("이 시간 범위 안의 수동 벽 생성 횟수를 감시합니다.")]
    public float manualWallBurstIntervalSeconds = 2.0f;

    [Tooltip("감시 시간 안에 이 횟수 이상 벽이 생성되면 burst로 판단하고, 해당 구간의 벽 생성을 모두 취소합니다.")]
    public int manualWallBurstCreationLimit = 5;

    [Tooltip("burst 감지 후 이 시간 동안 추가 수동 벽 생성을 무시합니다. 같은 입력이 계속 유지될 때 즉시 재발하는 것을 막기 위한 값입니다.")]
    public float manualWallBurstSuppressSeconds = 2.0f;

    [Tooltip("생성 폭주로 취소한 벽 개수입니다. Debug 확인용입니다.")]
    public int burstCanceledManualWallCount;

    [Tooltip("최근 burst guard 감시 창 안에 들어온 수동 벽 생성 개수입니다. Debug 확인용입니다.")]
    public int recentManualWallCreationCount;

    [Tooltip("마지막 burst guard 메시지입니다. Debug 확인용입니다.")]
    public string lastManualWallBurstGuardMessage;

    [Tooltip("최근 burst guard 감시 창 안에 들어온 벽 자르기 완료 횟수입니다. Debug 확인용입니다.")]
    public int recentWallCutOperationCount;

    [Tooltip("연속 자르기 burst로 자동 되돌린 cut 작업 개수입니다. Debug 확인용입니다.")]
    public int burstCanceledWallCutOperationCount;

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

    [Tooltip("Closure Edge도 Manual Wall 단계에서 선택 가능한 벽으로 만들고, 수직 벽 연장/스냅 대상에 포함합니다. 자동 벽 사이의 빈 구간을 임시 벽처럼 막기 위한 옵션입니다.")]
    public bool includeClosureEdgesAsManualWallSelectable = true;
    public bool includeManualWallsForPerpendicularExtend = true;
    public float maxPerpendicularWallLength = 5.0f;
    public bool enableLongPerpendicularConnect = true;
    public float perpendicularAutoConnectDistance = 4.0f;
    public float perpendicularLongConnectDistance = 8.0f;
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

    [Tooltip("자르기 대상 선택 후 실제 cut 위치를 세로 라인으로 표시합니다. 이 오브젝트는 입력/충돌에는 사용되지 않습니다.")]
    public bool showWallCutLinePreview = true;

    [Tooltip("자르기 라인 표시용 Material입니다. 비워두면 Wall Cut Target Material 또는 런타임 Material을 사용합니다.")]
    public Material wallCutLineMaterial;

    [Tooltip("자르기 라인 표시용 세로 박스의 벽 방향 길이입니다.")]
    public float wallCutLineWidth = 0.035f;

    [Tooltip("자르기 라인 표시용 세로 박스의 두께 방향 깊이입니다. 벽보다 살짝 두껍게 두면 잘 보입니다.")]
    public float wallCutLineDepth = 0.09f;

    [Tooltip("자르기 라인이 바닥/천장을 살짝 넘어서 보이도록 더하는 높이입니다.")]
    public float wallCutLineHeightPadding = 0.04f;

    [Header("Selectable Surface Settings")]
    public bool showSelectableSurfaces = true;
    public bool showSelectableDetectedWalls = true;
    public bool showSelectableActiveWallCandidates = false;
    public bool showSelectableIgnoredWallCandidates = false;
    public bool forceShowSelectableSurfacesOnRebuild = true;
    public float selectableFloorThickness = 0.03f;
    public float selectableWallThickness = 0.05f;
    public float floorMargin = 0.3f;

    [Header("Two Point Wall Auto Connect")]
    public bool extendTwoPointWallToNearbyWalls = true;
    public float twoPointAutoConnectDistance = 0.25f;
    public float twoPointAutoConnectMaxAngleDegrees = 8.0f;

    [Header("Wall Extend Mode")]
    public float wallExtendMaxDistance = 3.0f;
    public float wallExtendMinDelta = 0.05f;
    public float wallExtendTargetPadding = 0.02f;
    public float wallExtendTargetPointTolerance = 0.25f;

    [Header("MRTK Pressable Buttons - Legacy Direct Binding")]
    [Tooltip("이전 버전처럼 ManualWallBuilder가 버튼 listener를 직접 등록할지 여부입니다. 새 구조에서는 ManualWallPanelController가 버튼을 연결하므로 기본값은 false입니다.")]
    public bool registerLegacyButtonListeners = false;

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
    public int lastSelectableSurfaceCount;
    public int lastSelectableDetectedWallCount;
    public int lastSelectableActiveWallCandidateCount;
    public int lastSelectableIgnoredWallCandidateCount;
    public int lastSelectableClosureEdgeCount;
    public bool hasFirstFloorPoint;
    public bool previewRayHasValidHit;
    public Vector3 lastPreviewPoint;
    public bool hasLastGeneratedWall;
    public string lastGeneratedWallType;
    public bool hasCutTarget;
    public bool hasWallExtendTarget;
    public string cutModeStatus;

    private Vector3? firstFloorPoint;
    private GameObject firstPointMarker;

    private readonly List<GameObject> manualWalls = new List<GameObject>();
    private readonly List<ManualWallSegmentData> manualWallSegments = new List<ManualWallSegmentData>();
    private readonly List<GameObject> selectableSurfaces = new List<GameObject>();
    private readonly Stack<EditUndoAction> editUndoStack = new Stack<EditUndoAction>();
    private readonly List<ManualWallCreationGuardRecord> recentManualWallCreations = new List<ManualWallCreationGuardRecord>();
    private readonly List<WallCutGuardRecord> recentWallCutOperations = new List<WallCutGuardRecord>();

    private float lastValidPreviewPointTime;
    private float manualWallCreationSuppressedUntilTime;
    private float wallCutSuppressedUntilTime;
    private int suppressedManualWallCreationAttemptCount;
    private int suppressedWallCutAttemptCount;
    private int nextEditUndoActionId = 1;

    private GameObject lastGeneratedWall;
    private int lastGeneratedWallIndex = -1;
    private LastWallCreationType lastWallCreationType = LastWallCreationType.None;
    private Vector2 lastWallAnchor;
    private Vector2 lastWallDirection;
    private Material runtimeLastCreatedWallMaterial;
    private Material runtimeWallCutTargetMaterial;
    private Material runtimeWallCutLineMaterial;
    private bool warnedRuntimeMaterialShaderMissing;
    private int lastManualWallModeToggleFrame = -1;

    private GameObject cutLinePreviewObject;

    private int cutTargetWallIndex = -1;
    private GameObject cutTargetWall;
    private Vector2 cutPoint;
    private Material cutTargetOriginalMaterial;
    private SelectableWallSourceKind cutTargetSourceKind = SelectableWallSourceKind.Unknown;
    private PendingWallExtendTarget pendingWallExtendTarget;

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
        if (registerLegacyButtonListeners)
        {
            CreateButtonActions();
            RegisterButtons();
        }
    }

    private void OnDisable()
    {
        if (registerLegacyButtonListeners)
        {
            UnregisterButtons();
        }
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
        PressableButtonClickGuard.AddClick(button, action);
    }


    private static void RemoveClick(PressableButton button, UnityAction action)
    {
        PressableButtonClickGuard.RemoveClick(button, action);
    }


    public void SetManualCreateMode()
    {
        wallCutModeActive = false;
        currentMode = BuildMode.ManualWallCreate;
        ClearCutTargetSelection();
        ClearWallExtendTargetSelection();
        ClearPendingFirstPoint();
        SetPreviewLineVisible(false);
        ResetManualWallBurstGuardWindow();

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
        ClearWallExtendTargetSelection();
        SetPreviewLineVisible(false);
        ResetManualWallBurstGuardWindow();

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
            SetNoneMode();
            return;
        }

        wallCutModeActive = true;
        currentMode = BuildMode.WallCut;
        ClearPendingFirstPoint();
        SetPreviewLineVisible(false);
        ClearCutTargetSelection();
        ClearWallExtendTargetSelection();
        ResetManualWallBurstGuardWindow();

        cutModeStatus = "Wall cut mode ON. Select a manual or detected wall at the cut position.";
        status = cutModeStatus;
        Debug.Log($"[ManualWall] {status}");
    }

    public void SetWallDeleteMode(bool active)
    {
        if (!active)
        {
            SetNoneMode();
            return;
        }

        wallCutModeActive = false;
        currentMode = BuildMode.WallDelete;
        ClearPendingFirstPoint();
        SetPreviewLineVisible(false);
        ClearCutTargetSelection();
        ClearWallExtendTargetSelection();
        ResetManualWallBurstGuardWindow();

        status = "Wall delete mode ON. Select a manual, detected, or active candidate wall to delete.";
        cutModeStatus = status;
        Debug.Log($"[ManualWall] {status}");
    }

    public void SetWallExtendMode(bool active)
    {
        if (!active)
        {
            SetNoneMode();
            return;
        }

        wallCutModeActive = false;
        currentMode = BuildMode.WallExtend;
        ClearPendingFirstPoint();
        SetPreviewLineVisible(false);
        ClearCutTargetSelection();
        ClearWallExtendTargetSelection();
        ResetManualWallBurstGuardWindow();

        status = "Wall extend mode ON. Select a wall, then select the wall or point to connect to.";
        cutModeStatus = status;
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

        float searchMaxDistance = Mathf.Max(GetPerpendicularSearchMaxDistance(), fallbackLength);
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

    public bool CanRotateLastGeneratedPerpendicularWall()
    {
        return IsLastWallRotatable();
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

    public void ToggleDetectedWallsVisible()
    {
        showSelectableDetectedWalls = !showSelectableDetectedWalls;
        RebuildSelectableSurfaces();
    }

    public void ToggleActiveCandidatesVisible()
    {
        showSelectableActiveWallCandidates = !showSelectableActiveWallCandidates;
        RebuildSelectableSurfaces();
    }

    public void ToggleIgnoredWallsVisible()
    {
        showSelectableIgnoredWallCandidates = !showSelectableIgnoredWallCandidates;
        RebuildSelectableSurfaces();
    }

    public void RebuildSelectableSurfaces()
    {
        ClearSelectableSurfaces();
        EnsureSelectableSurfaceRootActive();

        if (forceShowSelectableSurfacesOnRebuild)
        {
            showSelectableSurfaces = true;
        }

        lastSelectableSurfaceCount = 0;
        lastSelectableDetectedWallCount = 0;
        lastSelectableActiveWallCandidateCount = 0;
        lastSelectableIgnoredWallCandidateCount = 0;
        lastSelectableClosureEdgeCount = 0;

        if (scanner == null || scanner.currentRoom == null)
        {
            status = "Cannot rebuild selectable surfaces. Scanner or currentRoom is null.";
            Debug.LogWarning($"[ManualWall] {status}");
            return;
        }

        BuildSelectableFloor();
        BuildSelectableDetectedWalls();

        lastSelectableSurfaceCount = selectableSurfaces.Count;
        status =
            $"Selectable surfaces rebuilt. total:{lastSelectableSurfaceCount}, " +
            $"detected:{lastSelectableDetectedWallCount}, active:{lastSelectableActiveWallCandidateCount}, ignored:{lastSelectableIgnoredWallCandidateCount}, closure:{lastSelectableClosureEdgeCount}, " +
            $"sourceDetected:{scanner.currentRoom.detectedWalls.Count}, visible:{showSelectableSurfaces}";
        Debug.Log($"[ManualWall] {status}");
    }

    private void EnsureSelectableSurfaceRootActive()
    {
        if (selectableSurfaceRoot != null)
        {
            selectableSurfaceRoot.gameObject.SetActive(true);
        }
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

        if (selectableSurfaceRoot != null)
        {
            for (int i = selectableSurfaceRoot.childCount - 1; i >= 0; i--)
            {
                Transform child = selectableSurfaceRoot.GetChild(i);

                if (child != null)
                {
                    Destroy(child.gameObject);
                }
            }
        }

        lastSelectableSurfaceCount = 0;
        lastSelectableDetectedWallCount = 0;
        lastSelectableActiveWallCandidateCount = 0;
        lastSelectableIgnoredWallCandidateCount = 0;
        lastSelectableClosureEdgeCount = 0;

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

        if (currentMode == BuildMode.WallDelete)
        {
            HandleWallDeleteSelection(selection);
            return;
        }

        if (currentMode == BuildMode.WallExtend)
        {
            HandleWallExtendSelection(selection);
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
            selection.kind == SelectableSurfaceKind.ActiveWallCandidate ||
            selection.kind == SelectableSurfaceKind.IgnoredWallCandidate ||
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

        if (currentMode == BuildMode.WallExtend && hasWallExtendTarget)
        {
            ClearWallExtendTargetSelection();
            status = "Canceled pending wall extend target.";
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

        if (TryUndoLastEditOperation())
        {
            return;
        }

        if (manualWalls.Count == 0)
        {
            status = "No manual wall or wall cut operation to undo.";
            Debug.Log($"[ManualWall] {status}");
            return;
        }

        int lastIndex = manualWalls.Count - 1;
        DeleteManualWallAt(lastIndex);

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
        editUndoStack.Clear();
        ResetManualWallBurstGuardWindow();
        ClearLastGeneratedWallState();
        ClearPendingFirstPoint();
        ClearCutTargetSelection();
        ClearWallExtendTargetSelection();
        ClearCutLinePreviewObject();

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

        ShowCutLinePreview(segment.start, segment.end, cutPoint);

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
            status = $"Wall cut mode cannot use this wall source as a detected wall target. source:{sourceKind}, index:{sourceIndex}";
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

        ShowCutLinePreview(segment.start, segment.end, cutPoint);

        status = "Detected wall cut target selected. Select the side/area to remove.";
        cutModeStatus = status;
        Debug.Log($"[ManualWall] {status}");
    }

    private void ApplyWallCut(SurfaceSelection areaSelection)
    {
        if (IsWallEditTemporarilySuppressed())
        {
            ClearCutTargetSelection(restoreMaterialAlreadyApplied: true);
            return;
        }

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

    private void HandleWallDeleteSelection(SurfaceSelection selection)
    {
        if (IsWallEditTemporarilySuppressed())
        {
            return;
        }

        if (selection.kind == SelectableSurfaceKind.Floor ||
            selection.kind == SelectableSurfaceKind.IgnoredWallCandidate)
        {
            status = "Wall delete mode requires selecting a detected, closure edge, active candidate, or manual wall.";
            cutModeStatus = status;
            Debug.Log($"[ManualWall] {status}");
            return;
        }

        if (selection.kind == SelectableSurfaceKind.ManualWall)
        {
            DeleteSelectedManualWall(selection);
            return;
        }

        DeleteSelectedSceneWall(selection);
    }

    private void DeleteSelectedManualWall(SurfaceSelection selection)
    {
        if (!TryFindManualWallIndex(selection.surfaceObject, out int index) ||
            index < 0 ||
            index >= manualWallSegments.Count)
        {
            status = "Selected wall is not registered as a manual wall.";
            cutModeStatus = status;
            Debug.LogWarning($"[ManualWall] {status}");
            return;
        }

        ManualWallSegmentData segment = manualWallSegments[index];
        GameObject target = segment.wallObject;
        Material material = GetWallMaterial(target);

        EditUndoAction deleteUndoAction = PushManualWallCutRemovedUndo(index, segment.start, segment.end, target, material);
        DeleteManualWallAt(index);

        if (!RegisterWallEditOperationForBurstGuard(deleteUndoAction))
        {
            return;
        }

        status = "Manual wall deleted.";
        cutModeStatus = status;
        Debug.Log($"[ManualWall] {status}");
    }

    private void DeleteSelectedSceneWall(SurfaceSelection selection)
    {
        if (!TryGetSelectedWallData(selection, out SelectableWallSegmentData data) ||
            !IsSceneWallDeletionSource(data.sourceKind))
        {
            status = "Selected wall cannot be deleted in Manual Wall edit mode.";
            cutModeStatus = status;
            Debug.LogWarning($"[ManualWall] {status}");
            return;
        }

        if (!TryGetSceneWallSegment(data.sourceKind, data.sourceIndex, out WallSegment segment))
        {
            status = "Selected wall no longer exists.";
            cutModeStatus = status;
            Debug.LogWarning($"[ManualWall] {status}");
            return;
        }

        EditUndoAction deleteUndoAction;

        if (data.sourceKind == SelectableWallSourceKind.ActiveWallCandidate)
        {
            deleteUndoAction = PushActiveCandidateRemovedUndo(data.sourceIndex, segment);
        }
        else
        {
            deleteUndoAction = PushSceneWallCutRemovedUndo(data.sourceKind, data.sourceIndex, segment);
        }

        RemoveSceneWallSegment(data.sourceKind, data.sourceIndex);
        RebuildSelectableSurfaces();

        if (!RegisterWallEditOperationForBurstGuard(deleteUndoAction))
        {
            return;
        }

        status = GetSceneWallDeletedStatus(data.sourceKind);
        cutModeStatus = status;
        Debug.Log($"[ManualWall] {status}");
    }

    private static bool IsSceneWallDeletionSource(SelectableWallSourceKind sourceKind)
    {
        return sourceKind == SelectableWallSourceKind.DetectedWall ||
               sourceKind == SelectableWallSourceKind.ActiveWallCandidate ||
               sourceKind == SelectableWallSourceKind.ClosureEdge;
    }

    private static string GetSceneWallDeletedStatus(SelectableWallSourceKind sourceKind)
    {
        if (sourceKind == SelectableWallSourceKind.ActiveWallCandidate)
        {
            return "Active wall candidate deleted.";
        }

        if (sourceKind == SelectableWallSourceKind.ClosureEdge)
        {
            return "Closure edge deleted.";
        }

        return "Detected wall deleted.";
    }

    private void HandleWallExtendSelection(SurfaceSelection selection)
    {
        if (!pendingWallExtendTarget.hasTarget)
        {
            SelectWallExtendTarget(selection);
            return;
        }

        ApplyWallExtend(selection);
    }

    private void SelectWallExtendTarget(SurfaceSelection selection)
    {
        if (selection.kind != SelectableSurfaceKind.ManualWall &&
            selection.kind != SelectableSurfaceKind.DetectedWall)
        {
            status = "Wall extend mode requires selecting a manual or detected wall first.";
            cutModeStatus = status;
            Debug.Log($"[ManualWall] {status}");
            return;
        }

        if (!TryGetEditableWallFromSelection(selection, out SelectableWallSourceKind sourceKind, out int sourceIndex, out Vector2 start, out Vector2 end, out GameObject wallObject))
        {
            status = "Selected wall cannot be extended.";
            cutModeStatus = status;
            Debug.LogWarning($"[ManualWall] {status}");
            return;
        }

        pendingWallExtendTarget = new PendingWallExtendTarget
        {
            hasTarget = true,
            sourceKind = sourceKind,
            sourceIndex = sourceIndex,
            wallObject = wallObject,
            start = start,
            end = end
        };

        hasWallExtendTarget = true;
        status = "Wall extend target selected. Select a wall or point to connect to.";
        cutModeStatus = status;
        Debug.Log($"[ManualWall] {status}");
    }

    private void ApplyWallExtend(SurfaceSelection selection)
    {
        if (IsWallEditTemporarilySuppressed())
        {
            ClearWallExtendTargetSelection();
            return;
        }

        PendingWallExtendTarget target = pendingWallExtendTarget;

        if (!target.hasTarget)
        {
            return;
        }

        if (!TryCalculateWallExtendResult(target.start, target.end, selection, out Vector2 newStart, out Vector2 newEnd, out float addedLength))
        {
            ClearWallExtendTargetSelection();
            status = "Wall extend canceled. The selected target cannot be connected by a straight extension.";
            cutModeStatus = status;
            Debug.Log($"[ManualWall] {status}");
            return;
        }

        EditUndoAction extendUndoAction = default;
        bool hasExtendUndoAction = false;

        if (target.sourceKind == SelectableWallSourceKind.ManualWall)
        {
            int index = ResolveManualWallIndex(target.sourceIndex, target.wallObject);
            if (index < 0)
            {
                ClearWallExtendTargetSelection();
                status = "Wall extend target is no longer valid.";
                cutModeStatus = status;
                Debug.LogWarning($"[ManualWall] {status}");
                return;
            }

            Material material = GetWallMaterial(manualWalls[index]);
            extendUndoAction = PushManualWallCutModifiedUndo(index, target.start, target.end, manualWalls[index], material);
            hasExtendUndoAction = true;
            UpdateManualWallGeometry(index, ToFloorWorld(newStart), ToFloorWorld(newEnd));
        }
        else if (target.sourceKind == SelectableWallSourceKind.DetectedWall)
        {
            if (!TryGetSceneWallSegment(target.sourceKind, target.sourceIndex, out WallSegment oldSegment))
            {
                ClearWallExtendTargetSelection();
                status = "Wall extend target is no longer valid.";
                cutModeStatus = status;
                Debug.LogWarning($"[ManualWall] {status}");
                return;
            }

            extendUndoAction = PushSceneWallCutModifiedUndo(target.sourceKind, target.sourceIndex, oldSegment);
            hasExtendUndoAction = true;
            UpdateSceneWallSegment(target.sourceKind, target.sourceIndex, newStart, newEnd);
            RebuildSelectableSurfaces();
        }

        ClearWallExtendTargetSelection();

        if (hasExtendUndoAction && !RegisterWallEditOperationForBurstGuard(extendUndoAction))
        {
            return;
        }

        status = $"Wall extended. added length:{addedLength:F2}";
        cutModeStatus = status;
        Debug.Log($"[ManualWall] {status}");
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

        int targetIndex = cutTargetWallIndex;
        ManualWallSegmentData segment = manualWallSegments[targetIndex];

        if (segment.wallObject == null)
        {
            ClearCutTargetSelection();
            status = "Cut target object was destroyed.";
            cutModeStatus = status;
            Debug.LogWarning($"[ManualWall] {status}");
            return;
        }

        Vector2 oldStart = segment.start;
        Vector2 oldEnd = segment.end;
        GameObject target = segment.wallObject;
        Material originalMaterial = cutTargetOriginalMaterial != null
            ? cutTargetOriginalMaterial
            : GetWallMaterial(target);

        if (!TryCalculateCutResult(oldStart, oldEnd, areaSelection, out Vector2 newStart, out Vector2 newEnd, out float remainingLength))
        {
            EditUndoAction removedInvalidManualCutUndoAction = PushManualWallCutRemovedUndo(targetIndex, oldStart, oldEnd, target, originalMaterial);
            DeleteManualWallAt(targetIndex);
            ClearCutTargetSelection(restoreMaterialAlreadyApplied: true);

            if (!RegisterWallEditOperationForBurstGuard(removedInvalidManualCutUndoAction))
            {
                return;
            }

            status = "Cut target was too short and removed.";
            cutModeStatus = status;
            Debug.Log($"[ManualWall] {status}");
            return;
        }

        if (remainingLength < Mathf.Max(minWallLength, minWallLengthAfterCut))
        {
            EditUndoAction removedShortManualCutUndoAction = PushManualWallCutRemovedUndo(targetIndex, oldStart, oldEnd, target, originalMaterial);
            DeleteManualWallAt(targetIndex);
            ClearCutTargetSelection(restoreMaterialAlreadyApplied: true);

            if (!RegisterWallEditOperationForBurstGuard(removedShortManualCutUndoAction))
            {
                return;
            }

            status = "Remaining wall was too short. Wall removed.";
            cutModeStatus = status;
            Debug.Log($"[ManualWall] {status}");
            return;
        }

        if (!UpdateManualWallGeometry(
                targetIndex,
                ToFloorWorld(newStart),
                ToFloorWorld(newEnd)))
        {
            ClearCutTargetSelection();
            status = "Failed to apply manual wall cut.";
            cutModeStatus = status;
            Debug.LogWarning($"[ManualWall] {status}");
            return;
        }

        EditUndoAction modifiedManualCutUndoAction = PushManualWallCutModifiedUndo(targetIndex, oldStart, oldEnd, target, originalMaterial);
        SetWallMaterial(target, originalMaterial != null ? originalMaterial : manualWallMaterial);

        if (target == lastGeneratedWall)
        {
            ClearLastGeneratedWallState();
        }

        ClearCutTargetSelection(restoreMaterialAlreadyApplied: true);

        if (!RegisterWallEditOperationForBurstGuard(modifiedManualCutUndoAction))
        {
            return;
        }

        status = $"Manual wall cut applied. remaining length:{remainingLength:F2}";
        cutModeStatus = status;
        Debug.Log($"[ManualWall] {status}");
    }


    private void ApplySceneWallCut(SurfaceSelection areaSelection)
    {
        SelectableWallSourceKind sourceKind = cutTargetSourceKind;
        int sourceIndex = cutTargetWallIndex;

        if (!TryGetSceneWallSegment(sourceKind, sourceIndex, out WallSegment segment))
        {
            ClearCutTargetSelection();
            status = "Detected wall cut target is no longer valid.";
            cutModeStatus = status;
            Debug.LogWarning($"[ManualWall] {status}");
            return;
        }

        WallSegment oldSegment = segment;

        if (!TryCalculateCutResult(segment.start, segment.end, areaSelection, out Vector2 newStart, out Vector2 newEnd, out float remainingLength))
        {
            EditUndoAction removedInvalidSceneCutUndoAction = PushSceneWallCutRemovedUndo(sourceKind, sourceIndex, oldSegment);
            RemoveSceneWallSegment(sourceKind, sourceIndex);
            ClearCutTargetSelection(restoreMaterialAlreadyApplied: true);
            RebuildSelectableSurfaces();

            if (!RegisterWallEditOperationForBurstGuard(removedInvalidSceneCutUndoAction))
            {
                return;
            }

            status = "Detected wall cut target was too short and removed.";
            cutModeStatus = status;
            Debug.Log($"[ManualWall] {status}");
            return;
        }

        if (remainingLength < Mathf.Max(minWallLength, minWallLengthAfterCut))
        {
            EditUndoAction removedShortSceneCutUndoAction = PushSceneWallCutRemovedUndo(sourceKind, sourceIndex, oldSegment);
            RemoveSceneWallSegment(sourceKind, sourceIndex);
            ClearCutTargetSelection(restoreMaterialAlreadyApplied: true);
            RebuildSelectableSurfaces();

            if (!RegisterWallEditOperationForBurstGuard(removedShortSceneCutUndoAction))
            {
                return;
            }

            status = "Remaining detected wall was too short. Wall removed.";
            cutModeStatus = status;
            Debug.Log($"[ManualWall] {status}");
            return;
        }

        UpdateSceneWallSegment(sourceKind, sourceIndex, newStart, newEnd);
        EditUndoAction modifiedSceneCutUndoAction = PushSceneWallCutModifiedUndo(sourceKind, sourceIndex, oldSegment);

        ClearCutTargetSelection(restoreMaterialAlreadyApplied: true);
        RebuildSelectableSurfaces();

        if (!RegisterWallEditOperationForBurstGuard(modifiedSceneCutUndoAction))
        {
            return;
        }

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

    private bool TryGetEditableWallFromSelection(
        SurfaceSelection selection,
        out SelectableWallSourceKind sourceKind,
        out int sourceIndex,
        out Vector2 start,
        out Vector2 end,
        out GameObject wallObject)
    {
        sourceKind = SelectableWallSourceKind.Unknown;
        sourceIndex = -1;
        start = default;
        end = default;
        wallObject = null;

        if (selection.kind == SelectableSurfaceKind.ManualWall)
        {
            if (!TryFindManualWallIndex(selection.surfaceObject, out int manualIndex) ||
                manualIndex < 0 ||
                manualIndex >= manualWallSegments.Count)
            {
                return false;
            }

            ManualWallSegmentData segment = manualWallSegments[manualIndex];
            sourceKind = SelectableWallSourceKind.ManualWall;
            sourceIndex = manualIndex;
            start = segment.start;
            end = segment.end;
            wallObject = segment.wallObject;
            return true;
        }

        if (!TryGetSelectedWallData(selection, out SelectableWallSegmentData data) ||
            data.sourceKind != SelectableWallSourceKind.DetectedWall)
        {
            return false;
        }

        if (!TryGetSceneWallSegment(data.sourceKind, data.sourceIndex, out WallSegment sceneSegment))
        {
            return false;
        }

        sourceKind = data.sourceKind;
        sourceIndex = data.sourceIndex;
        start = sceneSegment.start;
        end = sceneSegment.end;
        wallObject = selection.surfaceObject;
        return true;
    }

    private bool TryCalculateWallExtendResult(
        Vector2 sourceStart,
        Vector2 sourceEnd,
        SurfaceSelection targetSelection,
        out Vector2 newStart,
        out Vector2 newEnd,
        out float addedLength)
    {
        newStart = sourceStart;
        newEnd = sourceEnd;
        addedLength = 0.0f;

        Vector2 direction = sourceEnd - sourceStart;

        if (direction.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        direction.Normalize();

        bool startFound = TryFindExtendHit(sourceStart, -direction, targetSelection, out Vector2 startHit, out float startDistance);
        bool endFound = TryFindExtendHit(sourceEnd, direction, targetSelection, out Vector2 endHit, out float endDistance);

        if (!startFound && !endFound)
        {
            return false;
        }

        if (startFound && (!endFound || startDistance <= endDistance))
        {
            if (Vector2.Distance(startHit, sourceEnd) < minWallLength)
            {
                return false;
            }

            newStart = startHit;
            addedLength = Vector2.Distance(sourceStart, newStart);
            return addedLength >= wallExtendMinDelta;
        }

        if (Vector2.Distance(sourceStart, endHit) < minWallLength)
        {
            return false;
        }

        newEnd = endHit;
        addedLength = Vector2.Distance(sourceEnd, newEnd);
        return addedLength >= wallExtendMinDelta;
    }

    private bool TryFindExtendHit(
        Vector2 origin,
        Vector2 direction,
        SurfaceSelection targetSelection,
        out Vector2 hitPoint,
        out float hitDistance)
    {
        hitPoint = default;
        hitDistance = float.MaxValue;

        Vector2 dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;
        float maxDistance = Mathf.Max(wallExtendMinDelta, wallExtendMaxDistance);

        if ((targetSelection.kind == SelectableSurfaceKind.DetectedWall ||
             targetSelection.kind == SelectableSurfaceKind.ManualWall ||
             targetSelection.kind == SelectableSurfaceKind.ActiveWallCandidate) &&
            TryGetSelectedWallSegment(targetSelection, out Vector2 targetStart, out Vector2 targetEnd))
        {
            if (TryRaySegmentIntersection(origin, dir, targetStart, targetEnd, wallExtendMinDelta, maxDistance, out float distance))
            {
                hitDistance = distance;
                hitPoint = origin + dir * Mathf.Max(0.0f, distance - wallExtendTargetPadding);
                return true;
            }

            return false;
        }

        Vector2 targetPoint = GetSelectionPoint2D(targetSelection);
        float pointDistance = Vector2.Dot(targetPoint - origin, dir);

        if (pointDistance < wallExtendMinDelta || pointDistance > maxDistance)
        {
            return false;
        }

        Vector2 projectedPoint = origin + dir * pointDistance;
        float lateralDistance = Vector2.Distance(targetPoint, projectedPoint);

        if (lateralDistance > wallExtendTargetPointTolerance)
        {
            return false;
        }

        hitDistance = pointDistance;
        hitPoint = projectedPoint;
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


    private bool IsManualWallCreationTemporarilySuppressed()
    {
        if (!enableManualWallBurstGuard)
        {
            return false;
        }

        if (Time.time >= manualWallCreationSuppressedUntilTime)
        {
            suppressedManualWallCreationAttemptCount = 0;
            return false;
        }

        suppressedManualWallCreationAttemptCount++;

        int logStep = Mathf.Max(1, manualWallBurstCreationLimit);

        if (suppressedManualWallCreationAttemptCount % logStep == 0)
        {
            lastManualWallBurstGuardMessage =
                $"Manual wall creation is temporarily blocked after burst detection. attempts:{suppressedManualWallCreationAttemptCount}, wait:{manualWallCreationSuppressedUntilTime - Time.time:F1}s";
            status = lastManualWallBurstGuardMessage;
            Debug.LogError($"[ManualWall][BurstGuard] {lastManualWallBurstGuardMessage}");
        }

        return true;
    }

    private bool IsWallEditTemporarilySuppressed()
    {
        if (!enableManualWallBurstGuard)
        {
            return false;
        }

        if (Time.time >= wallCutSuppressedUntilTime)
        {
            suppressedWallCutAttemptCount = 0;
            return false;
        }

        suppressedWallCutAttemptCount++;

        int logStep = Mathf.Max(1, manualWallBurstCreationLimit);

        if (suppressedWallCutAttemptCount % logStep == 0)
        {
            lastManualWallBurstGuardMessage =
                $"Wall edit is temporarily blocked after burst detection. attempts:{suppressedWallCutAttemptCount}, wait:{wallCutSuppressedUntilTime - Time.time:F1}s";
            status = lastManualWallBurstGuardMessage;
            cutModeStatus = status;
            Debug.LogError($"[ManualWall][BurstGuard] {lastManualWallBurstGuardMessage}");
        }

        return true;
    }

    private bool RegisterManualWallCreationForBurstGuard(GameObject wallObject)
    {
        if (!enableManualWallBurstGuard)
        {
            recentManualWallCreationCount = 0;
            return true;
        }

        float now = Time.time;
        float interval = Mathf.Max(0.1f, manualWallBurstIntervalSeconds);

        for (int i = recentManualWallCreations.Count - 1; i >= 0; i--)
        {
            ManualWallCreationGuardRecord record = recentManualWallCreations[i];

            if (record.wallObject == null || now - record.time > interval)
            {
                recentManualWallCreations.RemoveAt(i);
            }
        }

        recentManualWallCreations.Add(new ManualWallCreationGuardRecord
        {
            time = now,
            wallObject = wallObject
        });

        recentManualWallCreationCount = recentManualWallCreations.Count;

        int limit = Mathf.Max(1, manualWallBurstCreationLimit);

        if (recentManualWallCreations.Count < limit)
        {
            return true;
        }

        CancelRecentManualWallCreationBurst(now);
        return false;
    }

    private void CancelRecentManualWallCreationBurst(float now)
    {
        List<GameObject> wallsToCancel = new List<GameObject>();

        foreach (ManualWallCreationGuardRecord record in recentManualWallCreations)
        {
            if (record.wallObject != null)
            {
                wallsToCancel.Add(record.wallObject);
            }
        }

        RemoveUndoActionsForManualWallObjects(wallsToCancel);

        int removedCount = 0;

        for (int i = wallsToCancel.Count - 1; i >= 0; i--)
        {
            GameObject wall = wallsToCancel[i];

            if (wall == null)
            {
                continue;
            }

            int index = manualWalls.IndexOf(wall);

            if (index < 0)
            {
                continue;
            }

            DeleteManualWallAt(index);
            removedCount++;
        }

        burstCanceledManualWallCount += removedCount;
        recentManualWallCreations.Clear();
        recentManualWallCreationCount = 0;
        manualWallCreationSuppressedUntilTime = now + Mathf.Max(0.0f, manualWallBurstSuppressSeconds);
        suppressedManualWallCreationAttemptCount = 0;

        ClearPendingFirstPoint();
        SetPreviewLineVisible(false);
        ClearCutTargetSelection();

        lastManualWallBurstGuardMessage =
            $"Manual wall burst detected. Canceled {removedCount} walls created within {manualWallBurstIntervalSeconds:F1}s. Creation is blocked for {manualWallBurstSuppressSeconds:F1}s.";

        status = lastManualWallBurstGuardMessage;
        cutModeStatus = status;
        Debug.LogError($"[ManualWall][BurstGuard] {lastManualWallBurstGuardMessage}");
    }

    private bool RegisterWallEditOperationForBurstGuard(EditUndoAction action)
    {
        if (!enableManualWallBurstGuard)
        {
            recentWallCutOperationCount = 0;
            return true;
        }

        float now = Time.time;
        float interval = Mathf.Max(0.1f, manualWallBurstIntervalSeconds);

        for (int i = recentWallCutOperations.Count - 1; i >= 0; i--)
        {
            WallCutGuardRecord record = recentWallCutOperations[i];

            if (now - record.time > interval)
            {
                recentWallCutOperations.RemoveAt(i);
            }
        }

        recentWallCutOperations.Add(new WallCutGuardRecord
        {
            time = now,
            action = action
        });

        recentWallCutOperationCount = recentWallCutOperations.Count;

        int limit = Mathf.Max(1, manualWallBurstCreationLimit);

        if (recentWallCutOperations.Count < limit)
        {
            return true;
        }

        CancelRecentWallEditBurst(now);
        return false;
    }

    private void CancelRecentWallEditBurst(float now)
    {
        HashSet<int> actionIdsToRemove = new HashSet<int>();
        int revertedCount = 0;

        for (int i = recentWallCutOperations.Count - 1; i >= 0; i--)
        {
            EditUndoAction action = recentWallCutOperations[i].action;

            if (action.actionId > 0)
            {
                actionIdsToRemove.Add(action.actionId);
            }

            if (TryUndoEditActionSilently(action))
            {
                revertedCount++;
            }
        }

        RemoveUndoActionsByIds(actionIdsToRemove);

        burstCanceledWallCutOperationCount += revertedCount;
        recentWallCutOperations.Clear();
        recentWallCutOperationCount = 0;
        wallCutSuppressedUntilTime = now + Mathf.Max(0.0f, manualWallBurstSuppressSeconds);
        suppressedWallCutAttemptCount = 0;

        ClearPendingFirstPoint();
        SetPreviewLineVisible(false);
        ClearCutTargetSelection(restoreMaterialAlreadyApplied: true);
        ClearWallExtendTargetSelection();
        RebuildSelectableSurfaces();

        lastManualWallBurstGuardMessage =
            $"Wall edit burst detected. Reverted {revertedCount} edits applied within {manualWallBurstIntervalSeconds:F1}s. Wall editing is blocked for {manualWallBurstSuppressSeconds:F1}s.";

        status = lastManualWallBurstGuardMessage;
        cutModeStatus = status;
        Debug.LogError($"[ManualWall][BurstGuard] {lastManualWallBurstGuardMessage}");
    }

    private bool TryUndoEditActionSilently(EditUndoAction action)
    {
        switch (action.kind)
        {
            case EditUndoActionKind.ManualWallCutModified:
                return TryUndoManualWallCutModified(action);

            case EditUndoActionKind.ManualWallCutRemoved:
                return TryUndoManualWallCutRemoved(action);

            case EditUndoActionKind.SceneWallCutModified:
                SetSceneWallSegment(action.sourceKind, action.index, action.sceneSegment);
                return true;

            case EditUndoActionKind.SceneWallCutRemoved:
                InsertSceneWallSegment(action.sourceKind, action.index, action.sceneSegment);
                return true;

            case EditUndoActionKind.ActiveCandidatePromoted:
                return TryUndoActiveCandidatePromoted(action);

            case EditUndoActionKind.ActiveCandidateRemoved:
                InsertSceneWallSegment(SelectableWallSourceKind.ActiveWallCandidate, action.index, action.sceneSegment);
                return true;
        }

        return false;
    }

    private void RemoveUndoActionsByIds(HashSet<int> actionIds)
    {
        if (actionIds == null || actionIds.Count == 0 || editUndoStack.Count == 0)
        {
            return;
        }

        List<EditUndoAction> keptActions = new List<EditUndoAction>();

        while (editUndoStack.Count > 0)
        {
            EditUndoAction action = editUndoStack.Pop();

            if (action.actionId <= 0 || !actionIds.Contains(action.actionId))
            {
                keptActions.Add(action);
            }
        }

        for (int i = keptActions.Count - 1; i >= 0; i--)
        {
            editUndoStack.Push(keptActions[i]);
        }
    }

    private void RemoveUndoActionsForManualWallObjects(List<GameObject> targetWalls)
    {
        if (targetWalls == null || targetWalls.Count == 0 || editUndoStack.Count == 0)
        {
            return;
        }

        HashSet<int> targetInstanceIds = new HashSet<int>();

        foreach (GameObject wall in targetWalls)
        {
            if (wall != null)
            {
                targetInstanceIds.Add(wall.GetInstanceID());
            }
        }

        if (targetInstanceIds.Count == 0)
        {
            return;
        }

        List<EditUndoAction> keptActions = new List<EditUndoAction>();

        while (editUndoStack.Count > 0)
        {
            EditUndoAction action = editUndoStack.Pop();

            bool removeAction =
                action.wallObject != null &&
                targetInstanceIds.Contains(action.wallObject.GetInstanceID());

            if (!removeAction)
            {
                keptActions.Add(action);
            }
        }

        for (int i = keptActions.Count - 1; i >= 0; i--)
        {
            editUndoStack.Push(keptActions[i]);
        }
    }

    private void ResetManualWallBurstGuardWindow()
    {
        recentManualWallCreations.Clear();
        recentWallCutOperations.Clear();
        recentManualWallCreationCount = 0;
        recentWallCutOperationCount = 0;
        manualWallCreationSuppressedUntilTime = 0.0f;
        wallCutSuppressedUntilTime = 0.0f;
        suppressedManualWallCreationAttemptCount = 0;
        suppressedWallCutAttemptCount = 0;
    }


    private void PushManualWallCreatedUndo(int index, GameObject wallObject)
    {
        editUndoStack.Push(new EditUndoAction
        {
            kind = EditUndoActionKind.ManualWallCreated,
            sourceKind = SelectableWallSourceKind.ManualWall,
            index = index,
            wallObject = wallObject
        });
    }

    private EditUndoAction PushManualWallCutModifiedUndo(int index, Vector2 oldStart, Vector2 oldEnd, GameObject wallObject, Material material)
    {
        EditUndoAction action = new EditUndoAction
        {
            actionId = nextEditUndoActionId++,
            kind = EditUndoActionKind.ManualWallCutModified,
            sourceKind = SelectableWallSourceKind.ManualWall,
            index = index,
            start = oldStart,
            end = oldEnd,
            wallObject = wallObject,
            material = material
        };

        editUndoStack.Push(action);
        return action;
    }

    private EditUndoAction PushManualWallCutRemovedUndo(int index, Vector2 oldStart, Vector2 oldEnd, GameObject wallObject, Material material)
    {
        EditUndoAction action = new EditUndoAction
        {
            actionId = nextEditUndoActionId++,
            kind = EditUndoActionKind.ManualWallCutRemoved,
            sourceKind = SelectableWallSourceKind.ManualWall,
            index = index,
            start = oldStart,
            end = oldEnd,
            wallObject = wallObject,
            material = material
        };

        editUndoStack.Push(action);
        return action;
    }

    private EditUndoAction PushSceneWallCutModifiedUndo(SelectableWallSourceKind sourceKind, int sourceIndex, WallSegment oldSegment)
    {
        EditUndoAction action = new EditUndoAction
        {
            actionId = nextEditUndoActionId++,
            kind = EditUndoActionKind.SceneWallCutModified,
            sourceKind = sourceKind,
            index = sourceIndex,
            sceneSegment = oldSegment
        };

        editUndoStack.Push(action);
        return action;
    }

    private EditUndoAction PushSceneWallCutRemovedUndo(SelectableWallSourceKind sourceKind, int sourceIndex, WallSegment oldSegment)
    {
        EditUndoAction action = new EditUndoAction
        {
            actionId = nextEditUndoActionId++,
            kind = EditUndoActionKind.SceneWallCutRemoved,
            sourceKind = sourceKind,
            index = sourceIndex,
            sceneSegment = oldSegment
        };

        editUndoStack.Push(action);
        return action;
    }

    private EditUndoAction PushActiveCandidatePromotedUndo(int activeCandidateIndex, WallSegment promotedSegment)
    {
        EditUndoAction action = new EditUndoAction
        {
            actionId = nextEditUndoActionId++,
            kind = EditUndoActionKind.ActiveCandidatePromoted,
            sourceKind = SelectableWallSourceKind.ActiveWallCandidate,
            index = activeCandidateIndex,
            sceneSegment = promotedSegment
        };

        editUndoStack.Push(action);
        return action;
    }

    private EditUndoAction PushActiveCandidateRemovedUndo(int activeCandidateIndex, WallSegment removedSegment)
    {
        EditUndoAction action = new EditUndoAction
        {
            actionId = nextEditUndoActionId++,
            kind = EditUndoActionKind.ActiveCandidateRemoved,
            sourceKind = SelectableWallSourceKind.ActiveWallCandidate,
            index = activeCandidateIndex,
            sceneSegment = removedSegment
        };

        editUndoStack.Push(action);
        return action;
    }

    private bool TryUndoLastEditOperation()
    {
        while (editUndoStack.Count > 0)
        {
            EditUndoAction action = editUndoStack.Pop();

            switch (action.kind)
            {
                case EditUndoActionKind.ManualWallCreated:
                    if (TryUndoManualWallCreated(action))
                    {
                        status = "Undo manual wall creation.";
                        cutModeStatus = status;
                        Debug.Log($"[ManualWall] {status}");
                        return true;
                    }
                    break;

                case EditUndoActionKind.ManualWallCutModified:
                    if (TryUndoManualWallCutModified(action))
                    {
                        status = "Undo manual wall cut.";
                        cutModeStatus = status;
                        Debug.Log($"[ManualWall] {status}");
                        return true;
                    }
                    break;

                case EditUndoActionKind.ManualWallCutRemoved:
                    if (TryUndoManualWallCutRemoved(action))
                    {
                        status = "Undo removed manual wall cut.";
                        cutModeStatus = status;
                        Debug.Log($"[ManualWall] {status}");
                        return true;
                    }
                    break;

                case EditUndoActionKind.SceneWallCutModified:
                    SetSceneWallSegment(action.sourceKind, action.index, action.sceneSegment);
                    RebuildSelectableSurfaces();
                    status = "Undo detected wall cut.";
                    cutModeStatus = status;
                    Debug.Log($"[ManualWall] {status}");
                    return true;

                case EditUndoActionKind.SceneWallCutRemoved:
                    InsertSceneWallSegment(action.sourceKind, action.index, action.sceneSegment);
                    RebuildSelectableSurfaces();
                    status = "Undo removed detected wall cut.";
                    cutModeStatus = status;
                    Debug.Log($"[ManualWall] {status}");
                    return true;

                case EditUndoActionKind.ActiveCandidatePromoted:
                    if (TryUndoActiveCandidatePromoted(action))
                    {
                        RebuildSelectableSurfaces();
                        status = "Undo active candidate promotion.";
                        cutModeStatus = status;
                        Debug.Log($"[ManualWall] {status}");
                        return true;
                    }
                    break;

                case EditUndoActionKind.ActiveCandidateRemoved:
                    InsertSceneWallSegment(SelectableWallSourceKind.ActiveWallCandidate, action.index, action.sceneSegment);
                    RebuildSelectableSurfaces();
                    status = "Undo removed active candidate.";
                    cutModeStatus = status;
                    Debug.Log($"[ManualWall] {status}");
                    return true;
            }
        }

        return false;
    }

    private bool TryUndoManualWallCreated(EditUndoAction action)
    {
        int index = -1;

        if (action.wallObject != null)
        {
            index = manualWalls.IndexOf(action.wallObject);
        }

        if (index < 0 && action.index >= 0 && action.index < manualWalls.Count)
        {
            index = action.index;
        }

        if (index < 0)
        {
            return false;
        }

        DeleteManualWallAt(index);
        return true;
    }

    private bool TryUndoManualWallCutModified(EditUndoAction action)
    {
        int index = ResolveManualWallIndex(action.index, action.wallObject);

        if (index < 0)
        {
            return false;
        }

        bool restored = UpdateManualWallGeometry(
            index,
            ToFloorWorld(action.start),
            ToFloorWorld(action.end)
        );

        if (!restored)
        {
            return false;
        }

        SetWallMaterial(manualWalls[index], action.material != null ? action.material : manualWallMaterial);
        RefreshManualWallSourceIndices();
        return true;
    }

    private bool TryUndoManualWallCutRemoved(EditUndoAction action)
    {
        GameObject restored = InsertManualWallAt(
            action.index,
            action.start,
            action.end,
            action.material != null ? action.material : manualWallMaterial
        );

        return restored != null;
    }

    private bool TryUndoActiveCandidatePromoted(EditUndoAction action)
    {
        if (scanner == null || scanner.currentRoom == null)
        {
            return false;
        }

        if (!RemoveMatchingSceneWallSegment(scanner.currentRoom.detectedWalls, action.sceneSegment))
        {
            return false;
        }

        WallSegment candidate = action.sceneSegment;
        candidate.candidateState = WallCandidateState.ActiveCandidate;
        candidate.qualityReason = "promotion undone";
        InsertSceneWallSegment(SelectableWallSourceKind.ActiveWallCandidate, action.index, candidate);
        return true;
    }

    private bool RemoveMatchingSceneWallSegment(List<WallSegment> walls, WallSegment target)
    {
        if (walls == null)
        {
            return false;
        }

        for (int i = 0; i < walls.Count; i++)
        {
            if (AreWallSegmentsEquivalent(walls[i], target))
            {
                walls.RemoveAt(i);
                return true;
            }
        }

        return false;
    }

    private bool AreWallSegmentsEquivalent(WallSegment a, WallSegment b)
    {
        return (Vector2.Distance(a.start, b.start) <= 0.02f &&
                Vector2.Distance(a.end, b.end) <= 0.02f) ||
               (Vector2.Distance(a.start, b.end) <= 0.02f &&
                Vector2.Distance(a.end, b.start) <= 0.02f);
    }

    private int ResolveManualWallIndex(int preferredIndex, GameObject wallObject)
    {
        if (wallObject != null)
        {
            int index = manualWalls.IndexOf(wallObject);

            if (index >= 0)
            {
                return index;
            }
        }

        if (preferredIndex >= 0 && preferredIndex < manualWalls.Count)
        {
            return preferredIndex;
        }

        return -1;
    }

    private GameObject InsertManualWallAt(int index, Vector2 start2, Vector2 end2, Material material)
    {
        if (scanner == null || scanner.currentRoom == null)
        {
            return null;
        }

        float floorY = scanner.currentRoom.floorY;
        float ceilingY = scanner.currentRoom.ceilingY;
        float height = Mathf.Max(0.1f, ceilingY - floorY);

        Vector3 start = new Vector3(start2.x, floorY, start2.y);
        Vector3 end = new Vector3(end2.x, floorY, end2.y);

        if (Vector3.Distance(start, end) < minWallLength)
        {
            return null;
        }

        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "ManualWall";

        ApplyWallTransform(wall, start, end, height);
        SetWallMaterial(wall, material != null ? material : manualWallMaterial);

        if (manualWallRoot != null)
        {
            wall.transform.SetParent(manualWallRoot, true);
        }

        int insertIndex = Mathf.Clamp(index, 0, manualWalls.Count);

        manualWalls.Insert(insertIndex, wall);
        manualWallSegments.Insert(insertIndex, new ManualWallSegmentData
        {
            start = start2,
            end = end2,
            wallObject = wall
        });

        AddSelectableSurface(
            wall,
            SelectableSurfaceKind.ManualWall,
            material != null ? material : manualWallMaterial,
            visible: true
        );

        RefreshManualWallSourceIndices();
        return wall;
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

        ClearCutLinePreviewObject();

        cutTargetWallIndex = -1;
        cutTargetWall = null;
        cutPoint = Vector2.zero;
        cutTargetOriginalMaterial = null;
        cutTargetSourceKind = SelectableWallSourceKind.Unknown;
        hasCutTarget = false;
    }

    private void ClearWallExtendTargetSelection()
    {
        pendingWallExtendTarget = default;
        hasWallExtendTarget = false;
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

        GameObject createdWall = CreateManualWallInternal(
            start,
            end,
            LastWallCreationType.TwoPoint,
            new Vector2(start.x, start.z),
            GetDirection2D(start, end),
            highlightAsLastCreated: true
        );

        ClearPendingFirstPoint();
        SetPreviewLineVisible(false);

        if (createdWall == null)
        {
            return;
        }

        status = "Floor two-point wall completed.";
        Debug.Log($"[ManualWall] {status}");
    }

    private void HandleWallSelection(SurfaceSelection selection)
    {
        if (selection.kind == SelectableSurfaceKind.IgnoredWallCandidate)
        {
            status = "Ignored wall candidates are display-only and cannot be edited.";
            Debug.Log($"[ManualWall] {status}");
            return;
        }

        if (selection.kind == SelectableSurfaceKind.ActiveWallCandidate)
        {
            PromoteActiveCandidateToDetectedWall(selection);
            return;
        }

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

            GameObject wallFromFirstPoint = CreateManualWallInternal(
                start,
                end,
                LastWallCreationType.TwoPoint,
                new Vector2(start.x, start.z),
                GetDirection2D(start, end),
                highlightAsLastCreated: true
            );

            ClearPendingFirstPoint();
            SetPreviewLineVisible(false);

            if (wallFromFirstPoint == null)
            {
                return;
            }

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

        GameObject createdWall = CreateManualWallInternal(
            ToFloorWorld(candidate.anchor),
            ToFloorWorld(candidate.end),
            creationType,
            candidate.anchor,
            candidate.direction,
            highlightAsLastCreated: true
        );

        if (createdWall == null)
        {
            return;
        }

        string hitText = candidate.hitTarget ? "target wall found" : "fallback max length";
        status = $"Wall-based manual wall created. type:{creationType}, {hitText}";
        Debug.Log($"[ManualWall] {status}");
    }

    private void PromoteActiveCandidateToDetectedWall(SurfaceSelection selection)
    {
        if (!TryGetSelectedWallData(selection, out SelectableWallSegmentData data) ||
            data.sourceKind != SelectableWallSourceKind.ActiveWallCandidate ||
            data.sourceIndex < 0)
        {
            status = "Selected active candidate is not registered.";
            Debug.LogWarning($"[ManualWall] {status}");
            return;
        }

        if (!TryGetSceneWallSegment(data.sourceKind, data.sourceIndex, out WallSegment segment))
        {
            status = "Selected active candidate no longer exists.";
            Debug.LogWarning($"[ManualWall] {status}");
            return;
        }

        RemoveSceneWallSegment(SelectableWallSourceKind.ActiveWallCandidate, data.sourceIndex);
        segment.candidateState = WallCandidateState.Accepted;
        segment.qualityReason = "promoted by user";
        scanner.currentRoom.detectedWalls.Add(segment);

        PushActiveCandidatePromotedUndo(data.sourceIndex, segment);
        RebuildSelectableSurfaces();

        status = "Active wall candidate promoted to detected wall.";
        Debug.Log($"[ManualWall] {status}");
    }

    /// <summary>
    /// ManualWallBuilder가 실제로 관리 중인 수동 벽 segment를 반환합니다.
    /// Confirm Room 단계에서는 manualWallRoot를 숨긴 상태에서도 검증/미리보기에 수동 벽을 포함해야 하므로
    /// 기본값은 inactive wall도 포함하도록 둡니다.
    /// </summary>
    public List<ManualWallSegmentSnapshot> GetManualWallSegmentsSnapshot(bool includeInactiveObjects = true)
    {
        List<ManualWallSegmentSnapshot> result = new List<ManualWallSegmentSnapshot>();

        for (int i = 0; i < manualWallSegments.Count; i++)
        {
            ManualWallSegmentData segment = manualWallSegments[i];

            // Destroy된 벽은 Unity의 fake-null 비교로 걸러집니다.
            if (segment.wallObject == null)
            {
                continue;
            }

            // Confirm Room 진입 시 manualWallRoot를 숨기면 activeInHierarchy가 false가 됩니다.
            // 이 경우에도 직접 생성한 벽은 최종 방 후보에 포함되어야 합니다.
            if (!includeInactiveObjects && !segment.wallObject.activeInHierarchy)
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
        if (IsManualWallCreationTemporarilySuppressed())
        {
            return null;
        }

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

        if (creationType == LastWallCreationType.TwoPoint && extendTwoPointWallToNearbyWalls)
        {
            TryAutoExtendTwoPointWallEndpoints(ref start, ref end);
        }

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

        PushManualWallCreatedUndo(manualWalls.Count - 1, wall);

        if (!RegisterManualWallCreationForBurstGuard(wall))
        {
            return null;
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

    private void TryAutoExtendTwoPointWallEndpoints(ref Vector3 startWorld, ref Vector3 endWorld)
    {
        Vector2 start = new Vector2(startWorld.x, startWorld.z);
        Vector2 end = new Vector2(endWorld.x, endWorld.z);
        Vector2 dir = end - start;

        if (dir.sqrMagnitude < 0.0001f)
        {
            return;
        }

        dir.Normalize();

        float maxDistance = Mathf.Max(0.0f, twoPointAutoConnectDistance);

        if (maxDistance <= 0.0f)
        {
            return;
        }

        if (TryFindNearestWallHit(start, -dir, wallExtendMinDelta, maxDistance, out Vector2 startHit, out _))
        {
            start = startHit;
        }

        if (TryFindNearestWallHit(end, dir, wallExtendMinDelta, maxDistance, out Vector2 endHit, out _))
        {
            end = endHit;
        }

        startWorld = ToFloorWorld(start);
        endWorld = ToFloorWorld(end);
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
            : Mathf.Max(maxPerpendicularWallLength, perpendicularAutoConnectDistance);

        float resolvedSearchMaxDistance = searchMaxDistance > 0.0f
            ? searchMaxDistance
            : GetPerpendicularSearchMaxDistance();

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

    private float GetPerpendicularSearchMaxDistance()
    {
        float baseDistance = Mathf.Max(maxPerpendicularWallLength, perpendicularAutoConnectDistance);

        if (!enableLongPerpendicularConnect)
        {
            return baseDistance;
        }

        return Mathf.Max(baseDistance, perpendicularLongConnectDistance);
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

        if (includeClosureEdgesForPerpendicularExtend || includeClosureEdgesAsManualWallSelectable)
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

        if (includeClosureEdgesForPerpendicularExtend || includeClosureEdgesAsManualWallSelectable)
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

        if (sourceKind == SelectableWallSourceKind.ActiveWallCandidate &&
            scanner.currentRoom.activeWallCandidates != null &&
            sourceIndex < scanner.currentRoom.activeWallCandidates.Count)
        {
            segment = scanner.currentRoom.activeWallCandidates[sourceIndex];
            return true;
        }

        if (sourceKind == SelectableWallSourceKind.IgnoredWallCandidate &&
            scanner.currentRoom.ignoredWallCandidates != null &&
            sourceIndex < scanner.currentRoom.ignoredWallCandidates.Count)
        {
            segment = scanner.currentRoom.ignoredWallCandidates[sourceIndex];
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
        else if (sourceKind == SelectableWallSourceKind.ActiveWallCandidate)
        {
            scanner.currentRoom.activeWallCandidates[sourceIndex] = segment;
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
        else if (sourceKind == SelectableWallSourceKind.ActiveWallCandidate &&
                 scanner.currentRoom.activeWallCandidates != null &&
                 sourceIndex < scanner.currentRoom.activeWallCandidates.Count)
        {
            scanner.currentRoom.activeWallCandidates.RemoveAt(sourceIndex);
        }
        else if (sourceKind == SelectableWallSourceKind.ClosureEdge &&
                 sourceIndex < scanner.currentRoom.closureEdges.Count)
        {
            scanner.currentRoom.closureEdges.RemoveAt(sourceIndex);
        }
    }


    private void SetSceneWallSegment(SelectableWallSourceKind sourceKind, int sourceIndex, WallSegment segment)
    {
        if (scanner == null || scanner.currentRoom == null || sourceIndex < 0)
        {
            return;
        }

        if (sourceKind == SelectableWallSourceKind.DetectedWall &&
            sourceIndex < scanner.currentRoom.detectedWalls.Count)
        {
            scanner.currentRoom.detectedWalls[sourceIndex] = segment;
        }
        else if (sourceKind == SelectableWallSourceKind.ActiveWallCandidate &&
                 scanner.currentRoom.activeWallCandidates != null &&
                 sourceIndex < scanner.currentRoom.activeWallCandidates.Count)
        {
            scanner.currentRoom.activeWallCandidates[sourceIndex] = segment;
        }
        else if (sourceKind == SelectableWallSourceKind.ClosureEdge &&
                 sourceIndex < scanner.currentRoom.closureEdges.Count)
        {
            scanner.currentRoom.closureEdges[sourceIndex] = segment;
        }
    }

    private void InsertSceneWallSegment(SelectableWallSourceKind sourceKind, int sourceIndex, WallSegment segment)
    {
        if (scanner == null || scanner.currentRoom == null)
        {
            return;
        }

        if (sourceKind == SelectableWallSourceKind.DetectedWall)
        {
            int index = Mathf.Clamp(sourceIndex, 0, scanner.currentRoom.detectedWalls.Count);
            scanner.currentRoom.detectedWalls.Insert(index, segment);
        }
        else if (sourceKind == SelectableWallSourceKind.ActiveWallCandidate)
        {
            int index = Mathf.Clamp(sourceIndex, 0, scanner.currentRoom.activeWallCandidates.Count);
            scanner.currentRoom.activeWallCandidates.Insert(index, segment);
        }
        else if (sourceKind == SelectableWallSourceKind.ClosureEdge)
        {
            int index = Mathf.Clamp(sourceIndex, 0, scanner.currentRoom.closureEdges.Count);
            scanner.currentRoom.closureEdges.Insert(index, segment);
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

        if (showSelectableDetectedWalls)
        {
            for (int i = 0; i < scanner.currentRoom.detectedWalls.Count; i++)
            {
                if (CreateSelectableWallSurface(
                    scanner.currentRoom.detectedWalls[i],
                    SelectableSurfaceKind.DetectedWall,
                    SelectableWallSourceKind.DetectedWall,
                    i,
                    "SelectableDetectedWall",
                    selectableWallMaterial,
                    showSelectableSurfaces
                ))
                {
                    lastSelectableDetectedWallCount++;
                }
            }
        }

        if (showSelectableActiveWallCandidates && scanner.currentRoom.activeWallCandidates != null)
        {
            for (int i = 0; i < scanner.currentRoom.activeWallCandidates.Count; i++)
            {
                if (CreateSelectableWallSurface(
                    scanner.currentRoom.activeWallCandidates[i],
                    SelectableSurfaceKind.ActiveWallCandidate,
                    SelectableWallSourceKind.ActiveWallCandidate,
                    i,
                    "SelectableActiveWallCandidate",
                    GetSelectableActiveWallCandidateMaterial(),
                    showSelectableSurfaces
                ))
                {
                    lastSelectableActiveWallCandidateCount++;
                }
            }
        }

        if (showSelectableIgnoredWallCandidates && scanner.currentRoom.ignoredWallCandidates != null)
        {
            for (int i = 0; i < scanner.currentRoom.ignoredWallCandidates.Count; i++)
            {
                if (CreateSelectableWallSurface(
                    scanner.currentRoom.ignoredWallCandidates[i],
                    SelectableSurfaceKind.IgnoredWallCandidate,
                    SelectableWallSourceKind.IgnoredWallCandidate,
                    i,
                    "SelectableIgnoredWallCandidate",
                    GetSelectableIgnoredWallCandidateMaterial(),
                    showSelectableSurfaces
                ))
                {
                    lastSelectableIgnoredWallCandidateCount++;
                }
            }
        }

        if (includeClosureEdgesForPerpendicularExtend || includeClosureEdgesAsManualWallSelectable)
        {
            for (int i = 0; i < scanner.currentRoom.closureEdges.Count; i++)
            {
                if (CreateSelectableWallSurface(
                    scanner.currentRoom.closureEdges[i],
                    SelectableSurfaceKind.DetectedWall,
                    SelectableWallSourceKind.ClosureEdge,
                    i,
                    "SelectableClosureEdge",
                    selectableWallMaterial,
                    showSelectableSurfaces
                ))
                {
                    lastSelectableClosureEdgeCount++;
                }
            }
        }

    }

    private bool CreateSelectableWallSurface(
        WallSegment wall,
        SelectableSurfaceKind surfaceKind,
        SelectableWallSourceKind sourceKind,
        int sourceIndex,
        string objectName,
        Material material,
        bool visible)
    {
        float floorY = scanner.currentRoom.floorY;
        float ceilingY = scanner.currentRoom.ceilingY;
        float height = Mathf.Max(0.1f, ceilingY - floorY);

        float length = Vector2.Distance(wall.start, wall.end);

        if (length < minWallLength)
        {
            return false;
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
            surfaceKind,
            material,
            visible
        );

        selectableSurfaces.Add(wallSurface);
        return true;
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

        collider.enabled = kind != SelectableSurfaceKind.IgnoredWallCandidate;

        MixedReality.Toolkit.StatefulInteractable interactable =
            target.GetComponent<MixedReality.Toolkit.StatefulInteractable>();

        if (interactable == null)
        {
            interactable = target.AddComponent<MixedReality.Toolkit.StatefulInteractable>();
        }

        interactable.enabled = kind != SelectableSurfaceKind.IgnoredWallCandidate;

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


    private void ShowCutLinePreview(Vector2 segmentStart, Vector2 segmentEnd, Vector2 cutPosition)
    {
        ClearCutLinePreviewObject();

        if (!showWallCutLinePreview || scanner == null || scanner.currentRoom == null)
        {
            return;
        }

        Vector2 dir = segmentEnd - segmentStart;

        if (dir.sqrMagnitude < 0.0001f)
        {
            return;
        }

        dir.Normalize();

        float floorY = scanner.currentRoom.floorY;
        float ceilingY = scanner.currentRoom.ceilingY;
        float height = Mathf.Max(0.1f, ceilingY - floorY) + Mathf.Max(0.0f, wallCutLineHeightPadding);
        float centerY = floorY + (ceilingY - floorY) * 0.5f;

        cutLinePreviewObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cutLinePreviewObject.name = "ManualWall_CutLinePreview";

        cutLinePreviewObject.transform.position = new Vector3(cutPosition.x, centerY, cutPosition.y);
        float yaw = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;
        cutLinePreviewObject.transform.rotation = Quaternion.Euler(0.0f, yaw, 0.0f);
        cutLinePreviewObject.transform.localScale = new Vector3(
            Mathf.Max(wallCutLineDepth, wallThickness * 1.25f),
            height,
            Mathf.Max(0.01f, wallCutLineWidth)
        );

        if (manualWallRoot != null)
        {
            cutLinePreviewObject.transform.SetParent(manualWallRoot, true);
        }
        else
        {
            cutLinePreviewObject.transform.SetParent(transform, true);
        }

        MeshRenderer renderer = cutLinePreviewObject.GetComponent<MeshRenderer>();

        if (renderer != null)
        {
            Material material = GetCutLinePreviewMaterial();

            if (material != null)
            {
                renderer.sharedMaterial = material;
            }
        }

        Collider collider = cutLinePreviewObject.GetComponent<Collider>();

        if (collider != null)
        {
            collider.isTrigger = true;
            collider.enabled = false;
        }
    }

    private void ClearCutLinePreviewObject()
    {
        if (cutLinePreviewObject != null)
        {
            Destroy(cutLinePreviewObject);
            cutLinePreviewObject = null;
        }
    }

    private Material GetCutLinePreviewMaterial()
    {
        if (wallCutLineMaterial != null)
        {
            return wallCutLineMaterial;
        }

        if (wallCutTargetMaterial != null)
        {
            return wallCutTargetMaterial;
        }

        if (runtimeWallCutLineMaterial == null)
        {
            runtimeWallCutLineMaterial = CreateRuntimeMaterial(
                "Runtime_WallCutLinePreview_Material",
                new Color(1.0f, 0.12f, 0.05f, 0.95f),
                wallCutLineMaterial != null ? wallCutLineMaterial : wallCutTargetMaterial
            );
        }

        return runtimeWallCutLineMaterial != null ? runtimeWallCutLineMaterial : GetWallCutTargetMaterial();
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

    private Material GetSelectableActiveWallCandidateMaterial()
    {
        if (selectableActiveWallCandidateMaterial != null)
        {
            return selectableActiveWallCandidateMaterial;
        }

        if (scanner != null && scanner.activeWallCandidateMaterial != null)
        {
            return scanner.activeWallCandidateMaterial;
        }

        return selectableWallMaterial;
    }

    private Material GetSelectableIgnoredWallCandidateMaterial()
    {
        if (selectableIgnoredWallCandidateMaterial != null)
        {
            return selectableIgnoredWallCandidateMaterial;
        }

        if (scanner != null && scanner.ignoredWallCandidateMaterial != null)
        {
            return scanner.ignoredWallCandidateMaterial;
        }

        return selectableWallMaterial;
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

        if (runtimeWallCutLineMaterial == null)
        {
            runtimeWallCutLineMaterial = CreateRuntimeMaterial(
                "Runtime_WallCutLinePreview_Material",
                new Color(1.0f, 0.12f, 0.05f, 0.95f),
                wallCutLineMaterial != null ? wallCutLineMaterial : wallCutTargetMaterial
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

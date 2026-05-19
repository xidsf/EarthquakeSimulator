using System.Collections;
using System.Collections.Generic;
using MixedReality.Toolkit.UX;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;

/// <summary>
/// 수정된 가구 배치 패널 컨트롤러입니다.
/// 주요 변경 사항:
/// 1. 수동 확정(Confirm) 버튼 제거 및 로직 삭제 (자동 확정 대응)
/// 2. 동일 가구의 다중 배치 허용 (배치 후 버튼 잠금 해제)
/// 3. 모든 가구를 배치해야 한다는 강제 조건 삭제 (사용자의 자율성 부여)
/// 4. UIManager의 중앙 통제 로딩 UI 연동 (서버 데이터 수신 시 사용)
/// </summary>
public class FurniturePlacementPanelController : WorkflowPanelControllerBase
{
    private const string ToggleButtonBackplateOuterGeometryName = "UX.Button.BackplateOuterGeometry";

    [Header("Furniture Placement References")]
    public FurniturePlacementManager furniturePlacementManager;
    public FurnitureMoveModeController moveModeController;
    public ConfirmedRoomGeometryProvider roomGeometryProvider;
    public FurnitureServerPlacementPipeline serverPlacementPipeline;
    [Tooltip("촬영 세션 정보를 읽어 가구 생성 버튼 활성화 조건을 판단합니다.")]
    public FurnitureCaptureManager furnitureCaptureManager;

    [Header("User Buttons")]
    public PressableButton userPlaceFurnitureFromServerButton;
    public PressableButton userSelectNextServerFurnitureButton;
    public PressableButton userSelectPreviousServerFurnitureButton;
    public PressableButton userPlaceSelectedServerFurnitureButton;
    public PressableButton userToggleMoveModeButton;
    public PressableButton userToggleRotateModeButton;
    public PressableButton userToggleScaleModeButton;
    public PressableButton userToggleFurnitureFixedButton;
    [FormerlySerializedAs("userToggleWallMountedButton")]
    public PressableButton userToggleFloorContactlessButton;
    public PressableButton userSnapSelectedFurnitureButton;
    public PressableButton userDeleteSelectedFurnitureButton;
    public PressableButton userScaleUpButton;
    public PressableButton userScaleDownButton;
    public PressableButton userScaleMoreButton;
    public PressableButton userScaleXUpButton;
    public PressableButton userScaleXDownButton;
    public PressableButton userScaleYUpButton;
    public PressableButton userScaleYDownButton;
    public PressableButton userScaleZUpButton;
    public PressableButton userScaleZDownButton;
    [Tooltip("x/y/z 축별 스케일 버튼들의 공통 상위 GameObject. 더보기 토글 시 이 오브젝트를 통째로 활성/비활성합니다.")]
    public GameObject userScaleAxisButtonsRoot;
    public PressableButton userCompleteFurniturePlacementButton;

    // [수정됨] 내부 System Message UI 변수 및 코루틴 삭제 (UIManager에서 중앙 통제)

    [Header("Server Furniture List UI")]
    public TMP_Text userSelectedServerFurnitureText;
    public TMP_Text userServerFurnitureListText;
    public TMP_Text userServerFurnitureStatusText;
    public Transform userSelectedServerFurniturePreviewAnchor;
    public bool loadSelectedServerFurniturePreview = true;
    public bool suppressSelectedServerFurniturePreviewOnDevice = false;
    public float selectedServerFurniturePreviewScale = 0.03f;

    [Tooltip("true이면 리스트의 모든 가구를 최소 한 번씩 배치해야 완료 버튼이 활성화됩니다.")]
    public bool requireAllServerFurniturePlacedBeforeComplete = false;

    [Header("Server Furniture Request Guard")]
    [Min(1.0f)] public float serverFurnitureRequestTimeoutSeconds = 600.0f;

    [Header("Mode Text")]
    [SerializeField] private string moveModeStatusText = "이동 모드 활성화. 가구를 잡아 위치를 조정하세요.";
    [SerializeField] private string rotateModeStatusText = "회전 모드 활성화. 가구를 Y축 기준으로 회전시키세요.";
    [SerializeField] private string scaleModeStatusText = "크기 조절 모드 활성화. +/- 버튼으로 크기를 조절하세요.";

    [Tooltip("버튼 한 번 누를 때 적용되는 균일 스케일 배수입니다. 작게할땐 1/이 값이 적용됩니다.")]
    [SerializeField] private float scaleStepFactor = 1.1f;

    [Tooltip("스케일 +/- 버튼을 길게 누르고 있을 때 반복 적용되는 간격(초)입니다.")]
    [SerializeField] private float scaleRepeatIntervalSeconds = 0.3f;

    [Tooltip("x/y/z 축별 미세 조정 버튼 한 번에 적용되는 배수입니다 (미세하므로 균일 배수보다 작게 권장).")]
    [SerializeField] private float axisScaleStepFactor = 1.05f;

    [Header("Toggle Button Visuals")]
    [Tooltip("Move/Rotate 토글 버튼이 On일 때 적용할 머티리얼입니다. 비워두면 시각 변경 없이 동작합니다.")]
    public Material toggleOnMaterial;
    [Tooltip("가구 고정 버튼이 On일 때 적용할 별도 머티리얼입니다.")]
    public Material fixedToggleOnMaterial;
    [Tooltip("벽걸이(바닥 띄움) 버튼이 On일 때 적용할 별도 머티리얼입니다.")]
    [FormerlySerializedAs("wallMountedToggleOnMaterial")]
    public Material floorContactlessToggleOnMaterial;

    [Header("Debug Buttons - Optional")]
    public PressableButton debugBindRoomGeometryButton;
    public PressableButton debugClearPlacedFurnitureButton;
    public PressableButton debugSelectNextFurnitureButton;
    public PressableButton debugSelectPreviousFurnitureButton;

    private UnityAction userPlaceFurnitureFromServerAction;
    private UnityAction userSelectNextServerFurnitureAction;
    private UnityAction userSelectPreviousServerFurnitureAction;
    private UnityAction userPlaceSelectedServerFurnitureAction;
    private UnityAction userToggleMoveModeAction;
    private UnityAction userToggleRotateModeAction;
    private UnityAction userToggleScaleModeAction;
    private UnityAction userToggleFurnitureFixedAction;
    private UnityAction userToggleFloorContactlessAction;
    private UnityAction userSnapSelectedFurnitureAction;
    private UnityAction userDeleteSelectedFurnitureAction;
    private bool scaleUpHeldActive;
    private float scaleUpNextRepeatTime;
    private bool scaleDownHeldActive;
    private float scaleDownNextRepeatTime;
    private bool scaleMoreExpanded;
    private UnityAction userScaleMoreAction;
    private UnityAction userScaleXUpAction;
    private UnityAction userScaleXDownAction;
    private UnityAction userScaleYUpAction;
    private UnityAction userScaleYDownAction;
    private UnityAction userScaleZUpAction;
    private UnityAction userScaleZDownAction;
    private UnityAction userCompleteFurniturePlacementAction;

    private UnityAction debugBindRoomGeometryAction;
    private UnityAction debugClearPlacedFurnitureAction;
    private UnityAction debugSelectNextFurnitureAction;
    private UnityAction debugSelectPreviousFurnitureAction;

    private FurnitureServerPlacementPipeline subscribedServerPlacementPipeline;
    private FurnitureMoveModeController subscribedMoveModeController;
    private GameObject selectedServerFurniturePreviewObject;
    private int selectedServerFurniturePreviewRequestId;
    private bool serverFurnitureRequestLocked;
    private float serverFurnitureRequestStartedRealtime = -1.0f;
    private readonly Dictionary<Renderer, Material[]> originalToggleButtonMaterials = new Dictionary<Renderer, Material[]>();
    private readonly Dictionary<PressableButton, bool> toggleButtonVisualStates = new Dictionary<PressableButton, bool>();

    protected override void Awake()
    {
        base.Awake();
        EnsureFurniturePlacementReferences();
    }

    private void OnEnable()
    {
        EnsureCommonReferences();
        EnsureFurniturePlacementReferences();
        TryBindRoomGeometryFromWorkflow();
        CreateActions();
        RegisterButtons();
        SubscribeServerPlacementPipelineEvents();
        SubscribeMoveModeControllerEvents();
        RefreshServerFurnitureListView();
        UpdateManipulationModeVisuals();
    }

    private void OnDisable()
    {
        UnsubscribeServerPlacementPipelineEvents();
        UnsubscribeMoveModeControllerEvents();
        UnregisterButtons();
        RestoreToggleButtonMaterials();
        ClearSelectedServerFurniturePreview();
        uiManager?.HideLoading(); // 패널 꺼질 때 안전하게 로딩창 끄기
    }

    private void Update()
    {
        UpdateScaleButtonHoldRepeat();

        if (!serverFurnitureRequestLocked) return;

        if (HasServerFurnitureResponse())
        {
            ClearServerFurnitureRequestLock("server response received");
            RefreshServerFurnitureListView();
            return;
        }

        if (HasServerFurnitureRequestTimedOut())
        {
            if (serverPlacementPipeline != null && serverPlacementPipeline.IsRunning)
                serverPlacementPipeline.StopRunningFlow();

            ClearServerFurnitureRequestLock("request timed out");
            RefreshServerFurnitureListView();
        }
    }

    private void EnsureFurniturePlacementReferences()
    {
        if (!autoFindReferences) return;
        if (furniturePlacementManager == null) furniturePlacementManager = FindFirst<FurniturePlacementManager>();
        if (moveModeController == null) moveModeController = FindFirst<FurnitureMoveModeController>();
        if (roomGeometryProvider == null) roomGeometryProvider = FindFirst<ConfirmedRoomGeometryProvider>();
        if (serverPlacementPipeline == null) serverPlacementPipeline = FindFirst<FurnitureServerPlacementPipeline>();
        if (furnitureCaptureManager == null) furnitureCaptureManager = FindFirst<FurnitureCaptureManager>();
    }

    private void SubscribeServerPlacementPipelineEvents()
    {
        if (subscribedServerPlacementPipeline == serverPlacementPipeline) return;
        UnsubscribeServerPlacementPipelineEvents();
        if (serverPlacementPipeline == null) return;

        subscribedServerPlacementPipeline = serverPlacementPipeline;
        subscribedServerPlacementPipeline.ResultListChanged += OnServerResultListChanged;
        subscribedServerPlacementPipeline.PendingSelectionChanged += OnServerPendingSelectionChanged;
        subscribedServerPlacementPipeline.PendingObjectLoadedForPlacement += OnServerPendingObjectLoadedForPlacement;
        subscribedServerPlacementPipeline.PendingObjectConfirmed += OnServerPendingObjectConfirmed;
    }

    private void UnsubscribeServerPlacementPipelineEvents()
    {
        if (subscribedServerPlacementPipeline == null) return;
        subscribedServerPlacementPipeline.ResultListChanged -= OnServerResultListChanged;
        subscribedServerPlacementPipeline.PendingSelectionChanged -= OnServerPendingSelectionChanged;
        subscribedServerPlacementPipeline.PendingObjectLoadedForPlacement -= OnServerPendingObjectLoadedForPlacement;
        subscribedServerPlacementPipeline.PendingObjectConfirmed -= OnServerPendingObjectConfirmed;
        subscribedServerPlacementPipeline = null;
    }

    private void SubscribeMoveModeControllerEvents()
    {
        if (subscribedMoveModeController == moveModeController) return;
        UnsubscribeMoveModeControllerEvents();
        if (moveModeController == null) return;

        subscribedMoveModeController = moveModeController;
        subscribedMoveModeController.MoveModeChanged += OnMoveModeChanged;
        subscribedMoveModeController.SelectionChanged += OnMoveModeSelectionChanged;
    }

    private void UnsubscribeMoveModeControllerEvents()
    {
        if (subscribedMoveModeController == null) return;
        subscribedMoveModeController.MoveModeChanged -= OnMoveModeChanged;
        subscribedMoveModeController.SelectionChanged -= OnMoveModeSelectionChanged;
        subscribedMoveModeController = null;
    }

    private void CreateActions()
    {
        userPlaceFurnitureFromServerAction ??= OnClickPlaceFurnitureFromServer;
        userSelectNextServerFurnitureAction ??= OnClickSelectNextServerFurniture;
        userSelectPreviousServerFurnitureAction ??= OnClickSelectPreviousServerFurniture;
        userPlaceSelectedServerFurnitureAction ??= OnClickPlaceSelectedServerFurniture;
        userToggleMoveModeAction ??= OnClickToggleMoveMode;
        userToggleRotateModeAction ??= OnClickToggleRotateMode;
        userToggleScaleModeAction ??= OnClickToggleScaleMode;
        userToggleFurnitureFixedAction ??= OnClickToggleFurnitureFixed;
        userToggleFloorContactlessAction ??= OnClickToggleFloorContactless;
        userSnapSelectedFurnitureAction ??= OnClickSnapSelectedFurniture;
        userDeleteSelectedFurnitureAction ??= OnClickDeleteSelectedFurniture;
        userScaleMoreAction ??= OnClickScaleMore;
        userScaleXUpAction ??= OnClickScaleXUp;
        userScaleXDownAction ??= OnClickScaleXDown;
        userScaleYUpAction ??= OnClickScaleYUp;
        userScaleYDownAction ??= OnClickScaleYDown;
        userScaleZUpAction ??= OnClickScaleZUp;
        userScaleZDownAction ??= OnClickScaleZDown;
        userCompleteFurniturePlacementAction ??= OnClickCompleteFurniturePlacement;

        debugBindRoomGeometryAction ??= OnClickDebugBindRoomGeometry;
        debugClearPlacedFurnitureAction ??= OnClickDebugClearPlacedFurniture;
        debugSelectNextFurnitureAction ??= OnClickDebugSelectNextFurniture;
        debugSelectPreviousFurnitureAction ??= OnClickDebugSelectPreviousFurniture;
    }

    private void RegisterButtons()
    {
        AddClick(userPlaceFurnitureFromServerButton, userPlaceFurnitureFromServerAction);
        AddClick(userSelectNextServerFurnitureButton, userSelectNextServerFurnitureAction);
        AddClick(userSelectPreviousServerFurnitureButton, userSelectPreviousServerFurnitureAction);
        AddClick(userPlaceSelectedServerFurnitureButton, userPlaceSelectedServerFurnitureAction);
        AddClick(userToggleMoveModeButton, userToggleMoveModeAction);
        AddClick(userToggleRotateModeButton, userToggleRotateModeAction);
        AddClick(userToggleScaleModeButton, userToggleScaleModeAction);
        AddClick(userToggleFurnitureFixedButton, userToggleFurnitureFixedAction);
        AddClick(userToggleFloorContactlessButton, userToggleFloorContactlessAction);
        AddClick(userSnapSelectedFurnitureButton, userSnapSelectedFurnitureAction);
        AddClick(userDeleteSelectedFurnitureButton, userDeleteSelectedFurnitureAction);
        AddClick(userScaleMoreButton, userScaleMoreAction);
        AddClick(userScaleXUpButton, userScaleXUpAction);
        AddClick(userScaleXDownButton, userScaleXDownAction);
        AddClick(userScaleYUpButton, userScaleYUpAction);
        AddClick(userScaleYDownButton, userScaleYDownAction);
        AddClick(userScaleZUpButton, userScaleZUpAction);
        AddClick(userScaleZDownButton, userScaleZDownAction);
        AddClick(userCompleteFurniturePlacementButton, userCompleteFurniturePlacementAction);

        AddClick(debugBindRoomGeometryButton, debugBindRoomGeometryAction);
        AddClick(debugClearPlacedFurnitureButton, debugClearPlacedFurnitureAction);
        AddClick(debugSelectNextFurnitureButton, debugSelectNextFurnitureAction);
        AddClick(debugSelectPreviousFurnitureButton, debugSelectPreviousFurnitureAction);
    }

    private void UnregisterButtons()
    {
        RemoveClick(userPlaceFurnitureFromServerButton, userPlaceFurnitureFromServerAction);
        RemoveClick(userSelectNextServerFurnitureButton, userSelectNextServerFurnitureAction);
        RemoveClick(userSelectPreviousServerFurnitureButton, userSelectPreviousServerFurnitureAction);
        RemoveClick(userPlaceSelectedServerFurnitureButton, userPlaceSelectedServerFurnitureAction);
        RemoveClick(userToggleMoveModeButton, userToggleMoveModeAction);
        RemoveClick(userToggleRotateModeButton, userToggleRotateModeAction);
        RemoveClick(userToggleScaleModeButton, userToggleScaleModeAction);
        RemoveClick(userToggleFurnitureFixedButton, userToggleFurnitureFixedAction);
        RemoveClick(userToggleFloorContactlessButton, userToggleFloorContactlessAction);
        RemoveClick(userSnapSelectedFurnitureButton, userSnapSelectedFurnitureAction);
        RemoveClick(userDeleteSelectedFurnitureButton, userDeleteSelectedFurnitureAction);
        RemoveClick(userScaleMoreButton, userScaleMoreAction);
        RemoveClick(userScaleXUpButton, userScaleXUpAction);
        RemoveClick(userScaleXDownButton, userScaleXDownAction);
        RemoveClick(userScaleYUpButton, userScaleYUpAction);
        RemoveClick(userScaleYDownButton, userScaleYDownAction);
        RemoveClick(userScaleZUpButton, userScaleZUpAction);
        RemoveClick(userScaleZDownButton, userScaleZDownAction);
        RemoveClick(userCompleteFurniturePlacementButton, userCompleteFurniturePlacementAction);

        RemoveClick(debugBindRoomGeometryButton, debugBindRoomGeometryAction);
        RemoveClick(debugClearPlacedFurnitureButton, debugClearPlacedFurnitureAction);
        RemoveClick(debugSelectNextFurnitureButton, debugSelectNextFurnitureAction);
        RemoveClick(debugSelectPreviousFurnitureButton, debugSelectPreviousFurnitureAction);
    }

    private void OnServerResultListChanged(FurnitureServerPlacementPipeline pipeline)
    {
        if (HasServerFurnitureResponse())
        {
            ClearServerFurnitureRequestLock("server response received");
            uiManager?.HideLoading(); // 데이터 수신 완료 시 로딩창 끄기
        }
        RefreshServerFurnitureListView();
    }

    private void OnServerPendingSelectionChanged(int index, FurnitureServerResultObject selectedObject)
    {
        RefreshServerFurnitureListView();
        UpdateSelectedServerFurniturePreview(selectedObject);
    }

    private void OnServerPendingObjectLoadedForPlacement(int index, FurnitureServerResultObject selectedObject, GameObject placedObject)
    {
        RefreshServerFurnitureListView();
        SetServerFurnitureStatus($"{BuildFurnitureName(selectedObject, index)}의 위치를 조정하세요.");
    }

    private void OnServerPendingObjectConfirmed(int index, FurnitureServerResultObject selectedObject)
    {
        RefreshServerFurnitureListView();
    }

    private void OnMoveModeChanged(bool enabled)
    {
        UpdateManipulationModeVisuals();
        RefreshServerFurnitureListView();
    }

    private void OnMoveModeSelectionChanged(PlacedFurniture selectedFurniture)
    {
        UpdateManipulationModeVisuals();
        RefreshServerFurnitureListView();
    }

    private void RefreshServerFurnitureListView()
    {
        EnsureFurniturePlacementReferences();
        RefreshFurnitureActionButtonState();
        UpdateManipulationModeVisuals();

        if (serverPlacementPipeline == null || serverPlacementPipeline.PendingObjectCount == 0)
        {
            SetSelectedFurnitureText("불러온 가구가 없습니다.");
            SetFurnitureListText(string.Empty);
            SetServerFurnitureStatus(IsServerFurnitureRequestLocked()
                ? BuildServerFurnitureRequestLockStatus()
                : serverPlacementPipeline != null ? serverPlacementPipeline.LastStatus : string.Empty);
            ClearSelectedServerFurniturePreview();
            return;
        }

        int selectedIndex = serverPlacementPipeline.SelectedPendingObjectIndex;
        FurnitureServerResultObject selectedObject = serverPlacementPipeline.SelectedPendingObject;
        int count = serverPlacementPipeline.PendingObjectCount;
        int placed = serverPlacementPipeline.GetPlacedPendingObjectCount();

        if (selectedObject != null)
        {
            string selectedLine = $"{selectedIndex + 1}/{count}  {BuildFurnitureName(selectedObject, selectedIndex)}";
            SetSelectedFurnitureText(selectedLine);
        }
        else
        {
            SetSelectedFurnitureText($"가구 리스트 준비됨. 배치됨: {placed}/{count}");
        }

        string listText = string.Empty;
        for (int i = 0; i < count; i++)
        {
            FurnitureServerResultObject obj = serverPlacementPipeline.GetPendingObject(i);
            string marker = i == selectedIndex ? "> " : "  ";
            string state = serverPlacementPipeline.IsPendingObjectLoadedForPlacement(i) ? " [배치중]" : "";
            listText += $"{marker}{i + 1}. {BuildFurnitureName(obj, i)}{state}\n";
        }

        SetFurnitureListText(listText.TrimEnd());
        SetServerFurnitureStatus(BuildCurrentPlacementStatus(placed, count));
    }

    private void RefreshFurnitureActionButtonState()
    {
        bool isRunning = serverPlacementPipeline != null && serverPlacementPipeline.IsRunning;
        int count = serverPlacementPipeline != null ? serverPlacementPipeline.PendingObjectCount : 0;
        bool hasServerFurniture = count > 0;
        bool hasSelectedFurniture = hasServerFurniture && serverPlacementPipeline.SelectedPendingObject != null;

        bool allPlaced = serverPlacementPipeline != null && serverPlacementPipeline.AreAllPendingObjectsPlaced();
        bool hasPlacedSelection = moveModeController != null && moveModeController.SelectedFurniture != null;
        PlacedFurniture selectedFurniture = moveModeController != null ? moveModeController.SelectedFurniture : null;
        bool selectedFurnitureFixed = selectedFurniture != null && selectedFurniture.IsFixed;
        bool moveModeOn = moveModeController != null
            && moveModeController.CurrentMode == FurnitureManipulationMode.Move;
        bool canUseManualSnap = hasPlacedSelection && !isRunning && !selectedFurnitureFixed && moveModeOn;
        bool canDeleteSelectedFurniture = hasPlacedSelection && !isRunning;

        SetButtonVisible(userPlaceFurnitureFromServerButton, !hasServerFurniture);
        SetButtonEnabled(userPlaceFurnitureFromServerButton, !isRunning && !IsServerFurnitureRequestLocked());
        SetButtonEnabled(userSelectNextServerFurnitureButton, hasServerFurniture && !isRunning);
        SetButtonEnabled(userSelectPreviousServerFurnitureButton, hasServerFurniture && !isRunning);

        SetButtonEnabled(userPlaceSelectedServerFurnitureButton, hasSelectedFurniture && !isRunning);

        SetButtonVisible(userToggleMoveModeButton, hasPlacedSelection && !isRunning);
        SetButtonVisible(userToggleRotateModeButton, hasPlacedSelection && !isRunning);
        SetButtonVisible(userToggleScaleModeButton, hasPlacedSelection && !isRunning);
        SetButtonVisible(userToggleFurnitureFixedButton, hasPlacedSelection && !isRunning);
        SetButtonVisible(userToggleFloorContactlessButton, hasPlacedSelection && !isRunning);
        SetButtonVisible(userSnapSelectedFurnitureButton, canUseManualSnap);
        SetButtonVisible(userDeleteSelectedFurnitureButton, canDeleteSelectedFurniture);
        SetButtonEnabled(userToggleMoveModeButton, hasPlacedSelection && !isRunning && !selectedFurnitureFixed);
        SetButtonEnabled(userToggleRotateModeButton, hasPlacedSelection && !isRunning && !selectedFurnitureFixed);
        SetButtonEnabled(userToggleScaleModeButton, hasPlacedSelection && !isRunning && !selectedFurnitureFixed);
        SetButtonEnabled(userToggleFurnitureFixedButton, hasPlacedSelection && !isRunning);
        SetButtonEnabled(userToggleFloorContactlessButton, hasPlacedSelection && !isRunning && !selectedFurnitureFixed);
        SetButtonEnabled(userSnapSelectedFurnitureButton, canUseManualSnap);
        SetButtonEnabled(userDeleteSelectedFurnitureButton, canDeleteSelectedFurniture);

        bool scaleModeOn = moveModeController != null
            && moveModeController.CurrentMode == FurnitureManipulationMode.Scale;

        if (!scaleModeOn)
        {
            scaleMoreExpanded = false;
        }

        bool baseScaleVisible = hasPlacedSelection && !isRunning && !selectedFurnitureFixed && scaleModeOn;
        bool axisScaleVisible = baseScaleVisible && scaleMoreExpanded;

        SetButtonVisible(userScaleUpButton, baseScaleVisible);
        SetButtonVisible(userScaleDownButton, baseScaleVisible);
        SetButtonVisible(userScaleMoreButton, baseScaleVisible);
        if (userScaleAxisButtonsRoot != null) userScaleAxisButtonsRoot.SetActive(axisScaleVisible);

        bool canComplete = hasServerFurniture && !isRunning;
        if (requireAllServerFurniturePlacedBeforeComplete)
        {
            canComplete = canComplete && allPlaced;
        }

        SetButtonEnabled(userCompleteFurniturePlacementButton, canComplete);
    }

    private void UpdateManipulationModeVisuals()
    {
        FurnitureManipulationMode mode = moveModeController != null ? moveModeController.CurrentMode : FurnitureManipulationMode.None;
        UpdateToggleButtonVisuals(mode);
    }

    private void UpdateToggleButtonVisuals(FurnitureManipulationMode mode)
    {
        PlacedFurniture selected = moveModeController != null ? moveModeController.SelectedFurniture : null;
        bool selectedFixed = selected != null && selected.IsFixed;

        SetToggleButtonOn(userToggleMoveModeButton, !selectedFixed && mode == FurnitureManipulationMode.Move, toggleOnMaterial);
        SetToggleButtonOn(userToggleRotateModeButton, !selectedFixed && mode == FurnitureManipulationMode.Rotate, toggleOnMaterial);
        SetToggleButtonOn(userToggleScaleModeButton, !selectedFixed && mode == FurnitureManipulationMode.Scale, toggleOnMaterial);
        SetToggleButtonOn(userToggleFurnitureFixedButton, selectedFixed, fixedToggleOnMaterial);
        SetToggleButtonOn(userToggleFloorContactlessButton, selected != null && selected.IsFloorContactless, floorContactlessToggleOnMaterial);
    }

    private void SetToggleButtonOn(PressableButton button, bool isOn, Material activeMaterial)
    {
        if (button == null)
        {
            return;
        }

        Renderer renderer = FindToggleButtonBackplateOuterGeometry(button);
        if (renderer == null)
        {
            return;
        }

        if (toggleButtonVisualStates.TryGetValue(button, out bool currentState) && currentState == isOn)
        {
            return;
        }

        toggleButtonVisualStates[button] = isOn;

        if (!originalToggleButtonMaterials.ContainsKey(renderer))
        {
            originalToggleButtonMaterials.Add(renderer, renderer.sharedMaterials);
        }

        if (isOn && activeMaterial != null)
        {
            Material[] originalMaterials = originalToggleButtonMaterials[renderer];
            Material[] activeMaterials = new Material[originalMaterials.Length];
            for (int i = 0; i < activeMaterials.Length; i++)
            {
                activeMaterials[i] = activeMaterial;
            }

            renderer.sharedMaterials = activeMaterials;
        }
        else if (originalToggleButtonMaterials.TryGetValue(renderer, out Material[] originalMaterials))
        {
            renderer.sharedMaterials = originalMaterials;
        }
    }

    private static Renderer FindToggleButtonBackplateOuterGeometry(PressableButton button)
    {
        Renderer[] renderers = button.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in renderers)
        {
            if (renderer != null && renderer.gameObject.name == ToggleButtonBackplateOuterGeometryName)
            {
                return renderer;
            }
        }

        return null;
    }

    private void RestoreToggleButtonMaterials()
    {
        foreach (KeyValuePair<Renderer, Material[]> entry in originalToggleButtonMaterials)
        {
            if (entry.Key != null)
            {
                entry.Key.sharedMaterials = entry.Value;
            }
        }

        toggleButtonVisualStates.Clear();
    }

    private string BuildCurrentPlacementStatus(int placed, int count)
    {
        if (IsSelectedFurnitureFixed())
        {
            return BuildSelectedFixedStatus();
        }

        if (moveModeController != null)
        {
            if (moveModeController.CurrentMode == FurnitureManipulationMode.Move) return moveModeStatusText;
            if (moveModeController.CurrentMode == FurnitureManipulationMode.Rotate) return rotateModeStatusText;
            if (moveModeController.CurrentMode == FurnitureManipulationMode.Scale) return scaleModeStatusText;
        }

        if (serverPlacementPipeline != null && serverPlacementPipeline.AreAllPendingObjectsPlaced())
            return "모든 가구가 배치되었습니다. 완료 버튼을 눌러 시뮬레이션을 시작하세요.";

        return $"가구를 선택하고 배치 버튼을 누르세요. ({placed}/{count})";
    }

    private bool IsSelectedFurnitureFixed()
    {
        PlacedFurniture selected = moveModeController != null ? moveModeController.SelectedFurniture : null;
        return selected != null && selected.IsFixed;
    }

    private string BuildSelectedFixedStatus()
    {
        PlacedFurniture selected = moveModeController != null ? moveModeController.SelectedFurniture : null;
        string name = selected != null ? selected.DisplayName : "선택된 가구";
        return $"{name}: 고정 상태입니다. 고정 해제 또는 삭제만 가능합니다.";
    }

    private void SetSelectedFixedStatus()
    {
        SetServerFurnitureStatus(BuildSelectedFixedStatus());
    }

    // --- Server Request & Preview Logic ---

    private bool IsServerFurnitureRequestLocked() => serverFurnitureRequestLocked && !HasServerFurnitureResponse() && !HasServerFurnitureRequestTimedOut();
    private bool HasServerFurnitureResponse() => serverPlacementPipeline != null && serverPlacementPipeline.LastResult != null;
    private bool HasServerFurnitureRequestTimedOut() => serverFurnitureRequestLocked && serverFurnitureRequestStartedRealtime >= 0 && (Time.realtimeSinceStartup - serverFurnitureRequestStartedRealtime >= serverFurnitureRequestTimeoutSeconds);
    private void BeginServerFurnitureRequestLock() { serverFurnitureRequestLocked = true; serverFurnitureRequestStartedRealtime = Time.realtimeSinceStartup; }
    private void ClearServerFurnitureRequestLock(string reason) { serverFurnitureRequestLocked = false; serverFurnitureRequestStartedRealtime = -1f; uiManager?.HideLoading(); }
    private string BuildServerFurnitureRequestLockStatus() => $"서버에서 가구를 생성 중입니다. ({Mathf.CeilToInt(serverFurnitureRequestTimeoutSeconds - (Time.realtimeSinceStartup - serverFurnitureRequestStartedRealtime))}초 남음)";

    private void UpdateSelectedServerFurniturePreview(FurnitureServerResultObject selectedObject)
    {
        ClearSelectedServerFurniturePreview();
        if (!loadSelectedServerFurniturePreview || selectedObject == null || userSelectedServerFurniturePreviewAnchor == null) return;

#if UNITY_WSA && !UNITY_EDITOR
        if (suppressSelectedServerFurniturePreviewOnDevice) return;
#endif
        StartCoroutine(LoadSelectedServerFurniturePreviewRoutine(selectedObject, ++selectedServerFurniturePreviewRequestId));
    }

    private IEnumerator LoadSelectedServerFurniturePreviewRoutine(FurnitureServerResultObject selectedObject, int requestId)
    {
        FurnitureServerMeshLoader meshLoader = FindFirst<FurnitureServerMeshLoader>();
        if (meshLoader == null) yield break;

        GameObject preview = null;
        yield return meshLoader.LoadFurnitureObject(selectedObject, (obj, err) => preview = obj);

        if (preview == null || requestId != selectedServerFurniturePreviewRequestId || userSelectedServerFurniturePreviewAnchor == null)
        {
            if (preview != null) Destroy(preview);
            yield break;
        }

        selectedServerFurniturePreviewObject = preview;
        preview.transform.SetParent(userSelectedServerFurniturePreviewAnchor, false);
        preview.transform.localScale = Vector3.one * selectedServerFurniturePreviewScale;
        foreach (var col in preview.GetComponentsInChildren<Collider>(true)) col.enabled = false;
        foreach (var rb in preview.GetComponentsInChildren<Rigidbody>(true)) { rb.isKinematic = true; rb.useGravity = false; }
    }

    private void ClearSelectedServerFurniturePreview()
    {
        selectedServerFurniturePreviewRequestId++;
        if (selectedServerFurniturePreviewObject != null) { Destroy(selectedServerFurniturePreviewObject); selectedServerFurniturePreviewObject = null; }
    }

    private void SetSelectedFurnitureText(string v) { if (userSelectedServerFurnitureText != null) userSelectedServerFurnitureText.text = v; }
    private void SetFurnitureListText(string v) { if (userServerFurnitureListText != null) userServerFurnitureListText.text = v; }
    private void SetServerFurnitureStatus(string v) { if (userServerFurnitureStatusText != null) userServerFurnitureStatusText.text = v; }
    private static string BuildFurnitureName(FurnitureServerResultObject obj, int index) => (obj == null || string.IsNullOrWhiteSpace(obj.label)) ? $"가구 {index + 1}" : obj.label;

    // --- Click Actions ---

    public void OnClickPlaceFurnitureFromServer()
    {
        if (serverPlacementPipeline == null || IsServerFurnitureRequestLocked()) return;

        // [수정됨] 중앙 통제 로딩 UI 호출
        uiManager?.ShowLoading("인식된 가구 받는중", "가구 모델링은 5분~10분정도 \n소요됩니다.");

        serverPlacementPipeline.StartPlacementFromCurrentScanSession();
        if (serverPlacementPipeline.IsRunning) BeginServerFurnitureRequestLock();
        RefreshServerFurnitureListView();
    }

    public void OnClickSelectNextServerFurniture() { serverPlacementPipeline?.SelectNextPendingObject(); RefreshServerFurnitureListView(); }
    public void OnClickSelectPreviousServerFurniture() { serverPlacementPipeline?.SelectPreviousPendingObject(); RefreshServerFurnitureListView(); }
    public void OnClickPlaceSelectedServerFurniture() { serverPlacementPipeline?.PlaceSelectedPendingObject(); RefreshServerFurnitureListView(); }

    public void OnClickToggleMoveMode()
    {
        if (moveModeController == null) return;
        if (IsSelectedFurnitureFixed())
        {
            SetSelectedFixedStatus();
            RefreshServerFurnitureListView();
            return;
        }

        moveModeController.ToggleMoveMode();
        UpdateManipulationModeVisuals();
        RefreshServerFurnitureListView();
    }

    public void OnClickToggleRotateMode()
    {
        if (moveModeController == null) return;
        if (IsSelectedFurnitureFixed())
        {
            SetSelectedFixedStatus();
            RefreshServerFurnitureListView();
            return;
        }

        moveModeController.ToggleRotateMode();
        UpdateManipulationModeVisuals();
        RefreshServerFurnitureListView();
    }

    public void OnClickToggleScaleMode()
    {
        if (moveModeController == null) return;
        if (IsSelectedFurnitureFixed())
        {
            SetSelectedFixedStatus();
            RefreshServerFurnitureListView();
            return;
        }

        moveModeController.ToggleScaleMode();
        UpdateManipulationModeVisuals();
        RefreshServerFurnitureListView();
    }

    public void OnClickToggleFurnitureFixed()
    {
        PlacedFurniture selected = moveModeController != null ? moveModeController.SelectedFurniture : null;
        if (selected == null) return;

        selected.ToggleFixed();
        if (moveModeController != null)
        {
            moveModeController.SetManipulationMode(moveModeController.CurrentMode);
        }

        string status;
        if (selected.IsFixed)
        {
            status = $"{selected.DisplayName}: 고정됨. 선택/고정 해제/삭제는 가능하며 이동/회전/크기 조정은 잠겼습니다.";
        }
        else
        {
            status = $"{selected.DisplayName}: 고정 해제됨.";
        }

        UpdateManipulationModeVisuals();
        RefreshServerFurnitureListView();
        SetServerFurnitureStatus(status);
    }

    public void OnClickToggleFloorContactless()
    {
        PlacedFurniture selected = moveModeController != null ? moveModeController.SelectedFurniture : null;
        if (selected == null) return;
        if (selected.IsFixed)
        {
            SetSelectedFixedStatus();
            RefreshServerFurnitureListView();
            return;
        }

        selected.ToggleFloorContactless();
        if (selected.IsFloorContactless && roomGeometryProvider != null &&
            roomGeometryProvider.TrySnapToNearestWall(selected.transform.position, selected.wallSnapDistance, out Vector3 snapPos, out Vector3 snapNormal))
        {
            selected.transform.position = snapPos + snapNormal * Mathf.Max(0.0f, selected.wallSnapOffsetMeters);
            selected.transform.rotation = Quaternion.LookRotation(snapNormal);
            roomGeometryProvider.ResolveWallPenetrationAfterSnap(selected);
            selected.SaveCurrentPoseAsValid();
        }

        string status = $"{selected.DisplayName}: floor contactless {(selected.IsFloorContactless ? "enabled" : "disabled")}.";
        UpdateManipulationModeVisuals();
        RefreshServerFurnitureListView();
        SetServerFurnitureStatus(status);
    }

    public void OnClickSnapSelectedFurniture()
    {
        PlacedFurniture selected = moveModeController != null ? moveModeController.SelectedFurniture : null;
        if (selected == null)
        {
            SetServerFurnitureStatus("스냅할 가구가 선택되지 않았습니다.");
            return;
        }

        if (selected.IsFixed)
        {
            SetServerFurnitureStatus($"{selected.DisplayName}: 고정된 가구는 스냅할 수 없습니다.");
            return;
        }

        if (roomGeometryProvider == null)
        {
            SetServerFurnitureStatus("Room geometry is not available.");
            return;
        }

        float snapDistance = Mathf.Max(selected.nearbySurfaceSnapDistance, selected.wallSnapDistance);
        bool snapped = roomGeometryProvider.ApplyNearbySurfaceSnap(
            selected,
            snapDistance,
            out bool snappedToWall,
            out Vector3 snappedWallNormal);

        if (snapped && snappedToWall && snappedWallNormal.sqrMagnitude > 0.0001f)
        {
            selected.transform.rotation = Quaternion.LookRotation(snappedWallNormal);
            roomGeometryProvider.ApplyNearbySurfaceSnap(selected, snapDistance, out _, out _);
        }
        else if (!snapped && selected.IsFloorContactless &&
                 roomGeometryProvider.TrySnapToNearestWall(selected.transform.position, selected.wallSnapDistance, out Vector3 snapPos, out Vector3 snapNormal))
        {
            selected.transform.position = snapPos + snapNormal * Mathf.Max(0.0f, selected.wallSnapOffsetMeters);
            selected.transform.rotation = Quaternion.LookRotation(snapNormal);
            snapped = true;
            snappedToWall = true;
        }

        string status;
        if (snapped)
        {
            roomGeometryProvider.ResolveWallPenetrationAfterSnap(selected);
            selected.SaveCurrentPoseAsValid();
            status = $"{selected.DisplayName}: 가장 가까운 {(snappedToWall ? "벽" : "가구")}에 스냅되었습니다.";
        }
        else
        {
            status = $"{selected.DisplayName}: 주변에 스냅할 벽이나 가구가 없습니다.";
        }

        UpdateManipulationModeVisuals();
        RefreshServerFurnitureListView();
        SetServerFurnitureStatus(status);
    }

    public void OnClickDeleteSelectedFurniture()
    {
        PlacedFurniture selected = moveModeController != null ? moveModeController.SelectedFurniture : null;
        if (selected == null)
        {
            SetServerFurnitureStatus("삭제할 가구가 선택되지 않았습니다.");
            return;
        }

        EnsureFurniturePlacementReferences();
        string deletedName = selected.DisplayName;
        bool deleted = furniturePlacementManager != null
            ? furniturePlacementManager.DeleteFurniture(selected)
            : false;

        if (!deleted)
        {
            SetServerFurnitureStatus($"{deletedName}: 가구 삭제에 실패했습니다.");
            return;
        }

        UpdateManipulationModeVisuals();
        RefreshServerFurnitureListView();
        SetServerFurnitureStatus($"{deletedName}: 삭제되었습니다.");
    }

    public void OnClickScaleUp() => ApplyScaleStepToSelected(Mathf.Max(1.0001f, scaleStepFactor));
    public void OnClickScaleDown() => ApplyScaleStepToSelected(1f / Mathf.Max(1.0001f, scaleStepFactor));

    private void ApplyScaleStepToSelected(float factor)
    {
        PlacedFurniture selected = moveModeController != null ? moveModeController.SelectedFurniture : null;
        if (selected == null)
        {
            SetServerFurnitureStatus("크기를 조절할 가구가 선택되지 않았습니다.");
            return;
        }
        if (selected.IsFixed)
        {
            SetSelectedFixedStatus();
            return;
        }

        FurnitureScaleStepResult result = selected.ApplyUniformScaleStep(factor);
        if (result == FurnitureScaleStepResult.BlockedByRoomHeight || result == FurnitureScaleStepResult.BlockedByOverlap)
        {
            uiManager?.ShowNotification("해당 가구는 그 위치에서 더 커질 수 없습니다");
        }
        else if (result == FurnitureScaleStepResult.BlockedByFixed)
        {
            uiManager?.ShowNotification("고정된 가구는 크기를 조정할 수 없습니다.");
        }
        RefreshServerFurnitureListView();
    }

    public void OnClickScaleMore()
    {
        if (IsSelectedFurnitureFixed())
        {
            SetSelectedFixedStatus();
            return;
        }

        bool scaleModeOn = moveModeController != null
            && moveModeController.CurrentMode == FurnitureManipulationMode.Scale;
        if (!scaleModeOn) return;

        scaleMoreExpanded = !scaleMoreExpanded;
        RefreshServerFurnitureListView();
    }

    public void OnClickScaleXUp() => ApplyAxisScaleStepToSelected(0, Mathf.Max(1.0001f, axisScaleStepFactor));
    public void OnClickScaleXDown() => ApplyAxisScaleStepToSelected(0, 1f / Mathf.Max(1.0001f, axisScaleStepFactor));
    public void OnClickScaleYUp() => ApplyAxisScaleStepToSelected(1, Mathf.Max(1.0001f, axisScaleStepFactor));
    public void OnClickScaleYDown() => ApplyAxisScaleStepToSelected(1, 1f / Mathf.Max(1.0001f, axisScaleStepFactor));
    public void OnClickScaleZUp() => ApplyAxisScaleStepToSelected(2, Mathf.Max(1.0001f, axisScaleStepFactor));
    public void OnClickScaleZDown() => ApplyAxisScaleStepToSelected(2, 1f / Mathf.Max(1.0001f, axisScaleStepFactor));

    private void ApplyAxisScaleStepToSelected(int axis, float factor)
    {
        PlacedFurniture selected = moveModeController != null ? moveModeController.SelectedFurniture : null;
        if (selected == null)
        {
            SetServerFurnitureStatus("크기를 조절할 가구가 선택되지 않았습니다.");
            return;
        }
        if (selected.IsFixed)
        {
            SetSelectedFixedStatus();
            return;
        }

        FurnitureScaleStepResult result = selected.ApplyAxisScaleStep(axis, factor);
        if (result == FurnitureScaleStepResult.BlockedByRoomHeight || result == FurnitureScaleStepResult.BlockedByOverlap)
        {
            uiManager?.ShowNotification("해당 가구는 그 위치에서 더 커질 수 없습니다");
        }
        else if (result == FurnitureScaleStepResult.BlockedByFixed)
        {
            uiManager?.ShowNotification("고정된 가구는 크기를 조정할 수 없습니다.");
        }
        RefreshServerFurnitureListView();
    }

    private void UpdateScaleButtonHoldRepeat()
    {
        HandleHoldRepeat(userScaleUpButton, ref scaleUpHeldActive, ref scaleUpNextRepeatTime, OnClickScaleUp);
        HandleHoldRepeat(userScaleDownButton, ref scaleDownHeldActive, ref scaleDownNextRepeatTime, OnClickScaleDown);
    }

    private void HandleHoldRepeat(PressableButton button, ref bool heldActive, ref float nextRepeatTime, System.Action fire)
    {
        bool pressed = button != null && button.gameObject.activeInHierarchy && button.enabled && button.isSelected;
        if (!pressed)
        {
            heldActive = false;
            return;
        }

        float now = Time.unscaledTime;
        float interval = Mathf.Max(0.05f, scaleRepeatIntervalSeconds);

        if (!heldActive)
        {
            heldActive = true;
            fire();
            nextRepeatTime = now + interval;
            return;
        }

        if (now >= nextRepeatTime)
        {
            fire();
            nextRepeatTime = now + interval;
        }
    }

    public void OnClickCompleteFurniturePlacement()
    {
        if (requireAllServerFurniturePlacedBeforeComplete && serverPlacementPipeline != null && !serverPlacementPipeline.AreAllPendingObjectsPlaced())
        {
            uiManager?.ShowNotification("모든 서버 가구를 배치해야 합니다.");
            return;
        }

        EnsureFurniturePlacementReferences();
        if (furniturePlacementManager != null)
        {
            List<PlacedFurniture> invalidFurnitures = furniturePlacementManager.EvaluatePlacementConditions();
            foreach (PlacedFurniture furniture in furniturePlacementManager.PlacedFurnitures)
            {
                if (furniture == null) continue;
                furniture.SetPlacementValidationFailed(invalidFurnitures.Contains(furniture));
            }
            moveModeController?.RefreshPlacementValidityVisuals();

            if (invalidFurnitures.Count > 0)
            {
                uiManager?.ShowNotification($"배치 조건에 맞지 않는 가구가 {invalidFurnitures.Count}개 있습니다. 표시된 가구를 다시 배치한 뒤 완료 버튼을 누르세요.");
                RefreshServerFurnitureListView();
                return;
            }
        }

        moveModeController?.SetManipulationMode(FurnitureManipulationMode.None);

        RequestWorkflowCommand(RoomBuildWorkflowManager.WorkflowCommand.CompleteFurniturePlacement);
    }

    // --- Debug Actions ---
    public void OnClickDebugBindRoomGeometry() => roomGeometryProvider?.BindFromConfirmRoomManager(workflowManager?.confirmRoomManager);
    public void OnClickDebugClearPlacedFurniture() => furniturePlacementManager?.ClearPlacedFurniture();
    public void OnClickDebugSelectNextFurniture() => moveModeController?.SelectNext();
    public void OnClickDebugSelectPreviousFurniture() => moveModeController?.SelectPrevious();

    private void TryBindRoomGeometryFromWorkflow()
    {
        if (roomGeometryProvider == null || roomGeometryProvider.IsInitialized) return;
        var workflow = workflowManager ?? uiManager?.GetWorkflowManager();
        if (workflow?.confirmRoomManager != null) roomGeometryProvider.BindFromConfirmRoomManager(workflow.confirmRoomManager);
    }

    private static void SetButtonEnabled(PressableButton b, bool e) { if (b != null) b.enabled = e; }
    private static void SetButtonVisible(PressableButton b, bool v) { if (b != null) { b.gameObject.SetActive(v); b.enabled = v; } }
    private static T FindFirst<T>() where T : UnityEngine.Object { T[] objs = FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None); return objs != null && objs.Length > 0 ? objs[0] : null; }
}

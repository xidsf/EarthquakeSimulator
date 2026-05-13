using System.Collections;
using MixedReality.Toolkit.UX;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 실제 사용자용 FurniturePlacement 패널 컨트롤러입니다.
///
/// 사용자 버튼:
/// - 서버 scan_session_id 기반 자동 배치
/// - Move Mode On/Off
/// - 선택 가구 확정
/// - 가구 배치 완료
///
/// Debug 버튼:
/// - 방 geometry 재바인딩
/// - 랜덤 배치/촬영 시뮬레이션
/// - 선택 fallback
/// - 서버 자동 배치 재요청
///
/// TMP 로그 패널은 사용하지 않고 Debug.Log만 출력합니다.
/// </summary>
public class FurniturePlacementPanelController : WorkflowPanelControllerBase
{
    [Header("Furniture Placement References")]
    public FurniturePlacementManager furniturePlacementManager;
    public FurnitureMoveModeController moveModeController;
    public FurnitureSimulationStartController simulationStartController;
    public ConfirmedRoomGeometryProvider roomGeometryProvider;
    public FurnitureServerPlacementPipeline serverPlacementPipeline;

    [Header("User Buttons")]
    [Tooltip("촬영 단계에서 만들어진 scan_session_id로 detect/process/result를 실행하고 서버 결과를 배치합니다.")]
    public PressableButton userPlaceFurnitureFromServerButton;

    [Tooltip("Selects the next furniture item from the cached server result.")]
    public PressableButton userSelectNextServerFurnitureButton;

    [Tooltip("Selects the previous furniture item from the cached server result.")]
    public PressableButton userSelectPreviousServerFurnitureButton;

    [Tooltip("Places only the currently selected furniture item from the cached server result.")]
    public PressableButton userPlaceSelectedServerFurnitureButton;

    [Tooltip("가구 이동 모드를 켜거나 끕니다. Move Mode가 켜져 있을 때만 터치 선택/이동이 가능합니다.")]
    public PressableButton userToggleMoveModeButton;

    [Tooltip("Toggles furniture rotate mode. Rotate mode only allows Y-axis rotation on the selected/active furniture.")]
    public PressableButton userToggleRotateModeButton;

    [Tooltip("가구 배치 단계를 완료하고 시뮬레이션 단계로 이동합니다.")]
    public PressableButton userCompleteFurniturePlacementButton;

    [Header("Server Furniture List UI")]
    public TMP_Text userSelectedServerFurnitureText;
    public TMP_Text userServerFurnitureListText;
    public TMP_Text userServerFurnitureStatusText;
    public TMP_Text text_MoveMode;
    public TMP_Text text_RotateMode;
    public Transform userSelectedServerFurniturePreviewAnchor;
    public bool loadSelectedServerFurniturePreview = true;
    public float selectedServerFurniturePreviewScale = 0.03f;
    public bool requireAllServerFurnitureConfirmedBeforeComplete = true;

    [Header("Mode Text")]
    [SerializeField] private string moveModeLabel = "Move";
    [SerializeField] private string rotateModeLabel = "Rotate";
    [SerializeField] private string modeOnText = "On";
    [SerializeField] private string modeOffText = "Off";
    [SerializeField] private string moveModeStatusText = "Move mode is on. Drag the selected furniture to adjust its position.";
    [SerializeField] private string rotateModeStatusText = "Rotate mode is on. Rotate the selected furniture around the Y axis.";
    [SerializeField] private string manipulationModeOffStatusText = "Move/Rotate mode is off.";

    [Header("Debug References - Optional")]
    [Tooltip("테스트용 랜덤 가구 배치 컨트롤러입니다. 실제 사용자 UI에서는 연결하지 않아도 됩니다.")]
    public DebugRandomFurniturePlacer debugRandomFurniturePlacer;

    [Tooltip("테스트용 촬영 시뮬레이션 컨트롤러입니다. 실제 사용자 UI에서는 연결하지 않아도 됩니다.")]
    public DebugCaptureSimulationController debugCaptureSimulationController;

    [Header("Debug Buttons - Optional")]
    [Tooltip("ConfirmRoomManager의 확정 방 정보를 가구 배치 geometry에 다시 바인딩합니다.")]
    public PressableButton debugBindRoomGeometryButton;

    [Tooltip("서버 scan_session_id 기반 자동 배치 흐름을 다시 실행합니다. 사용자 버튼과 같은 기능입니다.")]
    public PressableButton debugPlaceFurnitureFromServerButton;

    [Tooltip("Inspector에 등록된 테스트 prefab 중 하나를 랜덤 위치에 배치합니다.")]
    public PressableButton debugPlaceRandomFurnitureButton;

    [Tooltip("Inspector에 등록된 테스트 prefab을 여러 개 랜덤 위치에 배치합니다.")]
    public PressableButton debugPlaceMultipleRandomFurnitureButton;

    [Tooltip("Selects the next prefab from DebugFurniturePrefabPool without using the server.")]
    public PressableButton debugSelectNextPrefabButton;

    [Tooltip("Selects the previous prefab from DebugFurniturePrefabPool without using the server.")]
    public PressableButton debugSelectPreviousPrefabButton;

    [Tooltip("Places the selected prefab from DebugFurniturePrefabPool in front of the user without using the server.")]
    public PressableButton debugPlaceSelectedPrefabButton;

    [Tooltip("촬영 후 서버 응답을 흉내 내기 위해 일정 시간 뒤 랜덤 가구를 배치합니다.")]
    public PressableButton debugSimulatedCaptureButton;

    [Tooltip("등록된 가구를 모두 삭제합니다.")]
    public PressableButton debugClearPlacedFurnitureButton;

    [Tooltip("디버깅용 Move Mode 토글입니다. 사용자 버튼과 같은 기능입니다.")]
    public PressableButton debugToggleMoveModeButton;

    [Tooltip("Debug-only Rotate Mode toggle. Same behavior as the user rotate mode button.")]
    public PressableButton debugToggleRotateModeButton;

    [Tooltip("직접 터치 선택이 어려울 때 디버깅용으로 다음 가구를 선택합니다. 실제 사용자 UI에는 노출하지 않습니다.")]
    public PressableButton debugSelectNextFurnitureButton;

    [Tooltip("직접 터치 선택이 어려울 때 디버깅용으로 이전 가구를 선택합니다. 실제 사용자 UI에는 노출하지 않습니다.")]
    public PressableButton debugSelectPreviousFurnitureButton;

    [Tooltip("디버깅용 선택 가구 확정 버튼입니다. 사용자 버튼과 같은 기능입니다.")]
    public PressableButton debugConfirmSelectedFurnitureButton;

    [Tooltip("현재 터치로 선택된 가구의 위치를 확정합니다.")]
    public PressableButton userConfirmSelectedFurnitureButton;

    [Header("Debug Values")]
    [Min(1)] public int debugMultiplePlacementCount = 3;
    public TMP_Text debugSelectedPrefabText;

    private UnityAction userPlaceFurnitureFromServerAction;
    private UnityAction userSelectNextServerFurnitureAction;
    private UnityAction userSelectPreviousServerFurnitureAction;
    private UnityAction userPlaceSelectedServerFurnitureAction;
    private UnityAction userToggleMoveModeAction;
    private UnityAction userToggleRotateModeAction;
    private UnityAction userConfirmSelectedFurnitureAction;
    private UnityAction userCompleteFurniturePlacementAction;

    private UnityAction debugBindRoomGeometryAction;
    private UnityAction debugPlaceFurnitureFromServerAction;
    private UnityAction debugPlaceRandomFurnitureAction;
    private UnityAction debugPlaceMultipleRandomFurnitureAction;
    private UnityAction debugSelectNextPrefabAction;
    private UnityAction debugSelectPreviousPrefabAction;
    private UnityAction debugPlaceSelectedPrefabAction;
    private UnityAction debugSimulatedCaptureAction;
    private UnityAction debugClearPlacedFurnitureAction;
    private UnityAction debugToggleMoveModeAction;
    private UnityAction debugToggleRotateModeAction;
    private UnityAction debugSelectNextFurnitureAction;
    private UnityAction debugSelectPreviousFurnitureAction;
    private UnityAction debugConfirmSelectedFurnitureAction;
    private FurnitureServerPlacementPipeline subscribedServerPlacementPipeline;
    private FurnitureMoveModeController subscribedMoveModeController;
    private GameObject selectedServerFurniturePreviewObject;
    private int selectedServerFurniturePreviewRequestId;

    protected override void Awake()
    {
        base.Awake();
        EnsureFurniturePlacementReferences();
        EnsureDebugReferences();
    }

    private void OnEnable()
    {
        EnsureCommonReferences();
        EnsureFurniturePlacementReferences();
        EnsureDebugReferences();
        TryBindRoomGeometryFromWorkflow();
        CreateActions();
        RegisterButtons();
        SubscribeServerPlacementPipelineEvents();
        SubscribeMoveModeControllerEvents();
        RefreshServerFurnitureListView();
        RefreshDebugPrefabView();
        UpdateManipulationModeTexts();
    }

    private void OnDisable()
    {
        UnsubscribeServerPlacementPipelineEvents();
        UnsubscribeMoveModeControllerEvents();
        UnregisterButtons();
        ClearSelectedServerFurniturePreview();
    }

    private void EnsureFurniturePlacementReferences()
    {
        if (!autoFindReferences)
        {
            return;
        }

        if (furniturePlacementManager == null)
        {
            furniturePlacementManager = FindFirst<FurniturePlacementManager>();
        }

        if (moveModeController == null)
        {
            moveModeController = FindFirst<FurnitureMoveModeController>();
        }

        if (simulationStartController == null)
        {
            simulationStartController = FindFirst<FurnitureSimulationStartController>();
        }

        if (roomGeometryProvider == null)
        {
            roomGeometryProvider = FindFirst<ConfirmedRoomGeometryProvider>();
        }

        if (serverPlacementPipeline == null)
        {
            serverPlacementPipeline = FindFirst<FurnitureServerPlacementPipeline>();
        }
    }

    private void SubscribeServerPlacementPipelineEvents()
    {
        if (subscribedServerPlacementPipeline == serverPlacementPipeline)
        {
            return;
        }

        UnsubscribeServerPlacementPipelineEvents();

        if (serverPlacementPipeline == null)
        {
            return;
        }

        subscribedServerPlacementPipeline = serverPlacementPipeline;
        subscribedServerPlacementPipeline.ResultListChanged += OnServerResultListChanged;
        subscribedServerPlacementPipeline.PendingSelectionChanged += OnServerPendingSelectionChanged;
        subscribedServerPlacementPipeline.PendingObjectLoadedForPlacement += OnServerPendingObjectLoadedForPlacement;
        subscribedServerPlacementPipeline.PendingObjectConfirmed += OnServerPendingObjectConfirmed;
    }

    private void UnsubscribeServerPlacementPipelineEvents()
    {
        if (subscribedServerPlacementPipeline == null)
        {
            return;
        }

        subscribedServerPlacementPipeline.ResultListChanged -= OnServerResultListChanged;
        subscribedServerPlacementPipeline.PendingSelectionChanged -= OnServerPendingSelectionChanged;
        subscribedServerPlacementPipeline.PendingObjectLoadedForPlacement -= OnServerPendingObjectLoadedForPlacement;
        subscribedServerPlacementPipeline.PendingObjectConfirmed -= OnServerPendingObjectConfirmed;
        subscribedServerPlacementPipeline = null;
    }

    private void SubscribeMoveModeControllerEvents()
    {
        if (subscribedMoveModeController == moveModeController)
        {
            return;
        }

        UnsubscribeMoveModeControllerEvents();

        if (moveModeController == null)
        {
            return;
        }

        subscribedMoveModeController = moveModeController;
        subscribedMoveModeController.MoveModeChanged += OnMoveModeChanged;
        subscribedMoveModeController.SelectionChanged += OnMoveModeSelectionChanged;
    }

    private void UnsubscribeMoveModeControllerEvents()
    {
        if (subscribedMoveModeController == null)
        {
            return;
        }

        subscribedMoveModeController.MoveModeChanged -= OnMoveModeChanged;
        subscribedMoveModeController.SelectionChanged -= OnMoveModeSelectionChanged;
        subscribedMoveModeController = null;
    }

    private void EnsureDebugReferences()
    {
        if (!autoFindReferences)
        {
            return;
        }

        if (debugRandomFurniturePlacer == null)
        {
            debugRandomFurniturePlacer = FindFirst<DebugRandomFurniturePlacer>();
        }

        if (debugCaptureSimulationController == null)
        {
            debugCaptureSimulationController = FindFirst<DebugCaptureSimulationController>();
        }
    }

    private void TryBindRoomGeometryFromWorkflow()
    {
        if (roomGeometryProvider == null || roomGeometryProvider.IsInitialized)
        {
            return;
        }

        RoomBuildWorkflowManager workflow = workflowManager;
        if (workflow == null && uiManager != null)
        {
            workflow = uiManager.GetWorkflowManager();
        }

        if (workflow != null && workflow.confirmRoomManager != null)
        {
            bool result = roomGeometryProvider.BindFromConfirmRoomManager(workflow.confirmRoomManager);
            Debug.Log($"[FurniturePlacementPanelController] Auto bind room geometry result:{result}");
        }
    }

    private void CreateActions()
    {
        userPlaceFurnitureFromServerAction ??= OnClickPlaceFurnitureFromServer;
        userSelectNextServerFurnitureAction ??= OnClickSelectNextServerFurniture;
        userSelectPreviousServerFurnitureAction ??= OnClickSelectPreviousServerFurniture;
        userPlaceSelectedServerFurnitureAction ??= OnClickPlaceSelectedServerFurniture;
        userToggleMoveModeAction ??= OnClickToggleMoveMode;
        userToggleRotateModeAction ??= OnClickToggleRotateMode;
        userConfirmSelectedFurnitureAction ??= OnClickConfirmSelectedFurniture;
        userCompleteFurniturePlacementAction ??= OnClickCompleteFurniturePlacement;

        debugBindRoomGeometryAction ??= OnClickDebugBindRoomGeometry;
        debugPlaceFurnitureFromServerAction ??= OnClickPlaceFurnitureFromServer;
        debugPlaceRandomFurnitureAction ??= OnClickDebugPlaceRandomFurniture;
        debugPlaceMultipleRandomFurnitureAction ??= OnClickDebugPlaceMultipleRandomFurniture;
        debugSelectNextPrefabAction ??= OnClickDebugSelectNextPrefab;
        debugSelectPreviousPrefabAction ??= OnClickDebugSelectPreviousPrefab;
        debugPlaceSelectedPrefabAction ??= OnClickDebugPlaceSelectedPrefab;
        debugSimulatedCaptureAction ??= OnClickDebugSimulatedCapture;
        debugClearPlacedFurnitureAction ??= OnClickDebugClearPlacedFurniture;
        debugToggleMoveModeAction ??= OnClickToggleMoveMode;
        debugToggleRotateModeAction ??= OnClickToggleRotateMode;
        debugSelectNextFurnitureAction ??= OnClickDebugSelectNextFurniture;
        debugSelectPreviousFurnitureAction ??= OnClickDebugSelectPreviousFurniture;
        debugConfirmSelectedFurnitureAction ??= OnClickConfirmSelectedFurniture;
    }

    private void RegisterButtons()
    {
        AddClick(userPlaceFurnitureFromServerButton, userPlaceFurnitureFromServerAction);
        AddClick(userSelectNextServerFurnitureButton, userSelectNextServerFurnitureAction);
        AddClick(userSelectPreviousServerFurnitureButton, userSelectPreviousServerFurnitureAction);
        AddClick(userPlaceSelectedServerFurnitureButton, userPlaceSelectedServerFurnitureAction);
        AddClick(userToggleMoveModeButton, userToggleMoveModeAction);
        AddClick(userToggleRotateModeButton, userToggleRotateModeAction);
        AddClick(userConfirmSelectedFurnitureButton, userConfirmSelectedFurnitureAction);
        AddClick(userCompleteFurniturePlacementButton, userCompleteFurniturePlacementAction);

        // Debug / test listeners. 실제 사용자 UI에서는 이 버튼들을 연결하지 않아도 됩니다.
        AddClick(debugBindRoomGeometryButton, debugBindRoomGeometryAction);
        AddClick(debugPlaceFurnitureFromServerButton, debugPlaceFurnitureFromServerAction);
        AddClick(debugPlaceRandomFurnitureButton, debugPlaceRandomFurnitureAction);
        AddClick(debugPlaceMultipleRandomFurnitureButton, debugPlaceMultipleRandomFurnitureAction);
        AddClick(debugSelectNextPrefabButton, debugSelectNextPrefabAction);
        AddClick(debugSelectPreviousPrefabButton, debugSelectPreviousPrefabAction);
        AddClick(debugPlaceSelectedPrefabButton, debugPlaceSelectedPrefabAction);
        AddClick(debugSimulatedCaptureButton, debugSimulatedCaptureAction);
        AddClick(debugClearPlacedFurnitureButton, debugClearPlacedFurnitureAction);
        AddClick(debugToggleMoveModeButton, debugToggleMoveModeAction);
        AddClick(debugToggleRotateModeButton, debugToggleRotateModeAction);
        AddClick(debugSelectNextFurnitureButton, debugSelectNextFurnitureAction);
        AddClick(debugSelectPreviousFurnitureButton, debugSelectPreviousFurnitureAction);
        AddClick(debugConfirmSelectedFurnitureButton, debugConfirmSelectedFurnitureAction);
    }

    private void UnregisterButtons()
    {
        RemoveClick(userPlaceFurnitureFromServerButton, userPlaceFurnitureFromServerAction);
        RemoveClick(userSelectNextServerFurnitureButton, userSelectNextServerFurnitureAction);
        RemoveClick(userSelectPreviousServerFurnitureButton, userSelectPreviousServerFurnitureAction);
        RemoveClick(userPlaceSelectedServerFurnitureButton, userPlaceSelectedServerFurnitureAction);
        RemoveClick(userToggleMoveModeButton, userToggleMoveModeAction);
        RemoveClick(userToggleRotateModeButton, userToggleRotateModeAction);
        RemoveClick(userConfirmSelectedFurnitureButton, userConfirmSelectedFurnitureAction);
        RemoveClick(userCompleteFurniturePlacementButton, userCompleteFurniturePlacementAction);

        RemoveClick(debugBindRoomGeometryButton, debugBindRoomGeometryAction);
        RemoveClick(debugPlaceFurnitureFromServerButton, debugPlaceFurnitureFromServerAction);
        RemoveClick(debugPlaceRandomFurnitureButton, debugPlaceRandomFurnitureAction);
        RemoveClick(debugPlaceMultipleRandomFurnitureButton, debugPlaceMultipleRandomFurnitureAction);
        RemoveClick(debugSelectNextPrefabButton, debugSelectNextPrefabAction);
        RemoveClick(debugSelectPreviousPrefabButton, debugSelectPreviousPrefabAction);
        RemoveClick(debugPlaceSelectedPrefabButton, debugPlaceSelectedPrefabAction);
        RemoveClick(debugSimulatedCaptureButton, debugSimulatedCaptureAction);
        RemoveClick(debugClearPlacedFurnitureButton, debugClearPlacedFurnitureAction);
        RemoveClick(debugToggleMoveModeButton, debugToggleMoveModeAction);
        RemoveClick(debugToggleRotateModeButton, debugToggleRotateModeAction);
        RemoveClick(debugSelectNextFurnitureButton, debugSelectNextFurnitureAction);
        RemoveClick(debugSelectPreviousFurnitureButton, debugSelectPreviousFurnitureAction);
        RemoveClick(debugConfirmSelectedFurnitureButton, debugConfirmSelectedFurnitureAction);
    }

    private void OnServerResultListChanged(FurnitureServerPlacementPipeline pipeline)
    {
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
        SetServerFurnitureStatus($"Adjust and confirm {BuildFurnitureName(selectedObject, index)}.");
    }

    private void OnServerPendingObjectConfirmed(int index, FurnitureServerResultObject selectedObject)
    {
        RefreshServerFurnitureListView();
        SetServerFurnitureStatus($"Confirmed {BuildFurnitureName(selectedObject, index)}.");
    }

    private void OnMoveModeChanged(bool enabled)
    {
        UpdateManipulationModeTexts();
        RefreshServerFurnitureListView();
    }

    private void OnMoveModeSelectionChanged(PlacedFurniture selectedFurniture)
    {
        UpdateManipulationModeTexts();
        RefreshServerFurnitureListView();
    }

    private void RefreshServerFurnitureListView()
    {
        EnsureFurniturePlacementReferences();
        SubscribeServerPlacementPipelineEvents();
        SubscribeMoveModeControllerEvents();
        RefreshFurnitureActionButtonState();
        UpdateManipulationModeTexts();

        if (serverPlacementPipeline == null || serverPlacementPipeline.PendingObjectCount == 0)
        {
            SetSelectedFurnitureText("No server furniture loaded.");
            SetFurnitureListText(string.Empty);
            SetServerFurnitureStatus(serverPlacementPipeline != null ? serverPlacementPipeline.LastStatus : string.Empty);
            ClearSelectedServerFurniturePreview();
            return;
        }

        int selectedIndex = serverPlacementPipeline.SelectedPendingObjectIndex;
        FurnitureServerResultObject selectedObject = serverPlacementPipeline.SelectedPendingObject;
        int count = serverPlacementPipeline.PendingObjectCount;
        int placed = serverPlacementPipeline.GetPlacedPendingObjectCount();
        int confirmed = serverPlacementPipeline.GetConfirmedPendingObjectCount();

        if (selectedObject != null)
        {
            string selectedLine = $"{selectedIndex + 1}/{count}  {BuildFurnitureName(selectedObject, selectedIndex)}";
            if (selectedObject.confidence > 0f)
            {
                selectedLine += $"  confidence:{selectedObject.confidence:0.00}";
            }

            SetSelectedFurnitureText(selectedLine);
        }
        else
        {
            SetSelectedFurnitureText($"Furniture list ready. placed:{placed}/{count}");
        }

        string listText = string.Empty;
        for (int i = 0; i < count; i++)
        {
            FurnitureServerResultObject obj = serverPlacementPipeline.GetPendingObject(i);
            string marker = i == selectedIndex ? "> " : "  ";
            string state = serverPlacementPipeline.IsPendingObjectConfirmed(i)
                ? " [confirmed]"
                : serverPlacementPipeline.PendingConfirmationIndex == i
                    ? " [not confirmed]"
                    : serverPlacementPipeline.IsPendingObjectLoadedForPlacement(i)
                        ? " [placed]"
                        : string.Empty;

            listText += $"{marker}{i + 1}. {BuildFurnitureName(obj, i)}{state}\n";
        }

        SetFurnitureListText(listText.TrimEnd());

        string status = BuildCurrentPlacementStatus(placed, confirmed, count);

        SetServerFurnitureStatus(status);
    }

    private void RefreshFurnitureActionButtonState()
    {
        bool isRunning = serverPlacementPipeline != null && serverPlacementPipeline.IsRunning;
        int count = serverPlacementPipeline != null ? serverPlacementPipeline.PendingObjectCount : 0;
        int unplaced = count > 0 ? serverPlacementPipeline.GetUnplacedPendingObjectCount() : 0;
        bool hasServerFurniture = count > 0;
        bool hasSelectedFurniture = hasServerFurniture && serverPlacementPipeline.SelectedPendingObject != null;
        bool selectedFurnitureAlreadyPlaced = hasServerFurniture &&
                                             serverPlacementPipeline.IsPendingObjectLoadedForPlacement(serverPlacementPipeline.SelectedPendingObjectIndex);
        bool allPlaced = serverPlacementPipeline != null && serverPlacementPipeline.AreAllPendingObjectsPlaced();
        bool hasPlacedSelection = moveModeController != null && moveModeController.SelectedFurniture != null;
        bool canSelectOtherFurniture = hasServerFurniture && !isRunning && unplaced > 1;
        bool canPlaceSelectedFurniture = hasSelectedFurniture && !isRunning && !selectedFurnitureAlreadyPlaced;
        bool canAdjustSelectedFurniture = hasPlacedSelection && !isRunning;
        bool canCompletePlacement = hasServerFurniture &&
                                    !isRunning &&
                                    (!requireAllServerFurnitureConfirmedBeforeComplete || allPlaced);

        SetButtonEnabled(userPlaceFurnitureFromServerButton, !isRunning);
        SetButtonEnabled(userSelectNextServerFurnitureButton, canSelectOtherFurniture);
        SetButtonEnabled(userSelectPreviousServerFurnitureButton, canSelectOtherFurniture);
        SetButtonEnabled(userPlaceSelectedServerFurnitureButton, canPlaceSelectedFurniture);
        SetButtonVisible(userToggleMoveModeButton, canAdjustSelectedFurniture);
        SetButtonVisible(userToggleRotateModeButton, canAdjustSelectedFurniture);
        SetButtonVisible(userConfirmSelectedFurnitureButton, false);
        SetButtonEnabled(userCompleteFurniturePlacementButton, canCompletePlacement);
    }

    private static void SetButtonEnabled(PressableButton button, bool enabled)
    {
        if (button != null && button.enabled != enabled)
        {
            button.enabled = enabled;
        }
    }

    private static void SetButtonVisible(PressableButton button, bool visible)
    {
        if (button != null && button.gameObject.activeSelf != visible)
        {
            button.gameObject.SetActive(visible);
        }

        SetButtonEnabled(button, visible);
    }

    private void UpdateManipulationModeTexts()
    {
        FurnitureManipulationMode mode = moveModeController != null
            ? moveModeController.CurrentMode
            : FurnitureManipulationMode.None;

        if (text_MoveMode != null)
        {
            text_MoveMode.text = BuildModeText(moveModeLabel, mode == FurnitureManipulationMode.Move);
        }

        if (text_RotateMode != null)
        {
            text_RotateMode.text = BuildModeText(rotateModeLabel, mode == FurnitureManipulationMode.Rotate);
        }
    }

    private string BuildModeText(string label, bool isOn)
    {
        return label + "\n" + (isOn ? modeOnText : modeOffText);
    }

    private string BuildCurrentPlacementStatus(int placed, int confirmed, int count)
    {
        if (moveModeController != null)
        {
            switch (moveModeController.CurrentMode)
            {
                case FurnitureManipulationMode.Move:
                    return moveModeStatusText;
                case FurnitureManipulationMode.Rotate:
                    return rotateModeStatusText;
            }
        }

        if (serverPlacementPipeline != null && serverPlacementPipeline.HasPendingUnconfirmedPlacement)
        {
            return $"Adjust the selected furniture or choose the next furniture. placed:{placed}/{count}, confirmed:{confirmed}/{count}";
        }

        if (serverPlacementPipeline != null && serverPlacementPipeline.AreAllPendingObjectsPlaced())
        {
            return $"All furniture is placed. Complete placement to continue. confirmed:{confirmed}/{count}";
        }

        return $"Select with left/right, then place. placed:{placed}/{count}";
    }

    private void UpdateSelectedServerFurniturePreview(FurnitureServerResultObject selectedObject)
    {
        ClearSelectedServerFurniturePreview();

        if (!loadSelectedServerFurniturePreview || selectedObject == null || userSelectedServerFurniturePreviewAnchor == null)
        {
            return;
        }

        int requestId = ++selectedServerFurniturePreviewRequestId;
        StartCoroutine(LoadSelectedServerFurniturePreviewRoutine(selectedObject, requestId));
    }

    private IEnumerator LoadSelectedServerFurniturePreviewRoutine(FurnitureServerResultObject selectedObject, int requestId)
    {
        EnsureFurniturePlacementReferences();

        FurnitureServerMeshLoader meshLoader = serverPlacementPipeline != null &&
                                               serverPlacementPipeline.resultPlacer != null
            ? serverPlacementPipeline.resultPlacer.meshLoader
            : FindFirst<FurnitureServerMeshLoader>();

        if (meshLoader == null)
        {
            yield break;
        }

        GameObject preview = null;
        string error = string.Empty;

        yield return meshLoader.LoadFurnitureObject(selectedObject, (loadedObject, loadError) =>
        {
            preview = loadedObject;
            error = loadError;
        });

        if (preview == null)
        {
            Debug.LogWarning($"[FurniturePlacementPanelController] Selected furniture preview load failed. {error}");
            yield break;
        }

        if (requestId != selectedServerFurniturePreviewRequestId || userSelectedServerFurniturePreviewAnchor == null)
        {
            Destroy(preview);
            yield break;
        }

        selectedServerFurniturePreviewObject = preview;
        preview.transform.SetParent(userSelectedServerFurniturePreviewAnchor, false);
        preview.transform.localPosition = Vector3.zero;
        preview.transform.localRotation = Quaternion.identity;
        preview.transform.localScale = Vector3.one * selectedServerFurniturePreviewScale;

        Collider[] colliders = preview.GetComponentsInChildren<Collider>(true);
        foreach (Collider collider in colliders)
        {
            collider.enabled = false;
        }

        Rigidbody[] rigidbodies = preview.GetComponentsInChildren<Rigidbody>(true);
        foreach (Rigidbody rb in rigidbodies)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
        }
    }

    private void ClearSelectedServerFurniturePreview()
    {
        selectedServerFurniturePreviewRequestId++;

        if (selectedServerFurniturePreviewObject != null)
        {
            Destroy(selectedServerFurniturePreviewObject);
            selectedServerFurniturePreviewObject = null;
        }
    }

    private void SetSelectedFurnitureText(string value)
    {
        if (userSelectedServerFurnitureText != null)
        {
            userSelectedServerFurnitureText.text = value;
        }
    }

    private void SetFurnitureListText(string value)
    {
        if (userServerFurnitureListText != null)
        {
            userServerFurnitureListText.text = value;
        }
    }

    private void SetServerFurnitureStatus(string value)
    {
        if (userServerFurnitureStatusText != null)
        {
            userServerFurnitureStatusText.text = value;
        }
    }

    private static string BuildFurnitureName(FurnitureServerResultObject obj, int index)
    {
        if (obj == null)
        {
            return $"Furniture {index + 1}";
        }

        if (!string.IsNullOrWhiteSpace(obj.label))
        {
            return obj.label;
        }

        if (!string.IsNullOrWhiteSpace(obj.id))
        {
            return obj.id;
        }

        return $"Furniture {index + 1}";
    }

    public void OnClickPlaceFurnitureFromServer()
    {
        EnsureFurniturePlacementReferences();

        if (serverPlacementPipeline == null)
        {
            Debug.LogWarning("[FurniturePlacementPanelController] Server placement failed. FurnitureServerPlacementPipeline is missing.");
            return;
        }

        SubscribeServerPlacementPipelineEvents();
        serverPlacementPipeline.StartPlacementFromCurrentScanSession();
        RefreshServerFurnitureListView();
        Debug.Log("[FurniturePlacementPanelController] Server furniture list requested from current scan_session_id. Select and place an object after result is ready.");
    }

    public void OnClickSelectNextServerFurniture()
    {
        EnsureFurniturePlacementReferences();
        SubscribeServerPlacementPipelineEvents();
        serverPlacementPipeline?.SelectNextPendingObject();
        RefreshServerFurnitureListView();
    }

    public void OnClickSelectPreviousServerFurniture()
    {
        EnsureFurniturePlacementReferences();
        SubscribeServerPlacementPipelineEvents();
        serverPlacementPipeline?.SelectPreviousPendingObject();
        RefreshServerFurnitureListView();
    }

    public void OnClickPlaceSelectedServerFurniture()
    {
        EnsureFurniturePlacementReferences();
        SubscribeServerPlacementPipelineEvents();

        if (serverPlacementPipeline == null)
        {
            Debug.LogWarning("[FurniturePlacementPanelController] Place selected failed. FurnitureServerPlacementPipeline is missing.");
            return;
        }

        serverPlacementPipeline.PlaceSelectedPendingObject();
        RefreshServerFurnitureListView();
    }

    public void SelectServerFurnitureByIndex(int index)
    {
        EnsureFurniturePlacementReferences();
        SubscribeServerPlacementPipelineEvents();
        serverPlacementPipeline?.SelectPendingObject(index);
        RefreshServerFurnitureListView();
    }

    public void PlaceServerFurnitureByIndex(int index)
    {
        EnsureFurniturePlacementReferences();
        SubscribeServerPlacementPipelineEvents();
        serverPlacementPipeline?.PlacePendingObject(index);
        RefreshServerFurnitureListView();
    }

    public void OnClickToggleMoveMode()
    {
        EnsureFurniturePlacementReferences();

        if (moveModeController == null)
        {
            Debug.LogWarning("[FurniturePlacementPanelController] Toggle Move Mode failed. MoveModeController is missing.");
            return;
        }

        moveModeController.ToggleMoveMode();
        SetServerFurnitureStatus(moveModeController.CurrentMode == FurnitureManipulationMode.Move
            ? moveModeStatusText
            : manipulationModeOffStatusText);
        UpdateManipulationModeTexts();
        RefreshServerFurnitureListView();
        Debug.Log($"[FurniturePlacementPanelController] Move Mode toggled. mode:{moveModeController.CurrentMode}");
    }

    public void OnClickToggleRotateMode()
    {
        EnsureFurniturePlacementReferences();

        if (moveModeController == null)
        {
            Debug.LogWarning("[FurniturePlacementPanelController] Toggle Rotate Mode failed. MoveModeController is missing.");
            return;
        }

        moveModeController.ToggleRotateMode();
        SetServerFurnitureStatus(moveModeController.CurrentMode == FurnitureManipulationMode.Rotate
            ? rotateModeStatusText
            : manipulationModeOffStatusText);
        UpdateManipulationModeTexts();
        RefreshServerFurnitureListView();
        Debug.Log($"[FurniturePlacementPanelController] Rotate Mode toggled. mode:{moveModeController.CurrentMode}");
    }

    public void OnClickConfirmSelectedFurniture()
    {
        EnsureFurniturePlacementReferences();

        if (furniturePlacementManager == null)
        {
            Debug.LogWarning("[FurniturePlacementPanelController] Confirm selected failed. FurniturePlacementManager is missing.");
            return;
        }

        bool result = furniturePlacementManager.ConfirmSelectedFurniture();
        if (result && serverPlacementPipeline != null && serverPlacementPipeline.HasPendingUnconfirmedPlacement)
        {
            serverPlacementPipeline.ConfirmPlacedPendingObjectAndSelectNext();
            moveModeController?.SetMoveMode(false);
        }

        RefreshServerFurnitureListView();
        Debug.Log($"[FurniturePlacementPanelController] Confirm selected requested. result:{result}");
    }

    public void OnClickCompleteFurniturePlacement()
    {
        EnsureFurniturePlacementReferences();

        if (requireAllServerFurnitureConfirmedBeforeComplete &&
            serverPlacementPipeline != null &&
            serverPlacementPipeline.PendingObjectCount > 0)
        {
            if (!serverPlacementPipeline.AreAllPendingObjectsPlaced())
            {
                Debug.LogWarning($"[FurniturePlacementPanelController] Complete furniture placement blocked. Place all server furniture first. placed:{serverPlacementPipeline.GetPlacedPendingObjectCount()}/{serverPlacementPipeline.PendingObjectCount}");
                RefreshServerFurnitureListView();
                return;
            }
        }

        if (furniturePlacementManager != null && !furniturePlacementManager.ConfirmActiveNotConfirmedFurniture())
        {
            Debug.LogWarning("[FurniturePlacementPanelController] Complete furniture placement blocked. Active furniture could not be confirmed.");
            RefreshServerFurnitureListView();
            return;
        }

        moveModeController?.SetManipulationMode(FurnitureManipulationMode.None);
        RefreshServerFurnitureListView();

        if (simulationStartController != null)
        {
            bool result = simulationStartController.TryStartSimulation();
            Debug.Log($"[FurniturePlacementPanelController] Complete furniture placement requested through SimulationStartController. result:{result}");
            return;
        }

        bool workflowResult = RequestWorkflowCommand(RoomBuildWorkflowManager.WorkflowCommand.CompleteFurniturePlacement);
        Debug.Log($"[FurniturePlacementPanelController] Complete furniture placement requested directly to workflow. result:{workflowResult}");
    }

    public void OnClickDebugBindRoomGeometry()
    {
        EnsureFurniturePlacementReferences();

        RoomBuildWorkflowManager workflow = workflowManager;
        if (workflow == null && uiManager != null)
        {
            workflow = uiManager.GetWorkflowManager();
        }

        bool result = false;
        if (roomGeometryProvider != null && workflow != null && workflow.confirmRoomManager != null)
        {
            result = roomGeometryProvider.BindFromConfirmRoomManager(workflow.confirmRoomManager);
        }

        Debug.Log($"[FurniturePlacementPanelController] Debug bind room geometry result:{result}");
    }

    public void OnClickDebugPlaceRandomFurniture()
    {
        EnsureDebugReferences();

        bool result = debugRandomFurniturePlacer != null && debugRandomFurniturePlacer.PlaceOneRandomFurniture();
        Debug.Log($"[FurniturePlacementPanelController] Debug place random furniture result:{result}");
    }

    public void OnClickDebugPlaceMultipleRandomFurniture()
    {
        EnsureDebugReferences();

        int placedCount = debugRandomFurniturePlacer != null
            ? debugRandomFurniturePlacer.PlaceRandomFurniture(debugMultiplePlacementCount)
            : 0;

        Debug.Log($"[FurniturePlacementPanelController] Debug place multiple random furniture result:{placedCount}/{debugMultiplePlacementCount}");
    }

    public void OnClickDebugSelectNextPrefab()
    {
        EnsureDebugReferences();

        bool result = debugRandomFurniturePlacer != null && debugRandomFurniturePlacer.SelectNextPrefab();
        RefreshDebugPrefabView();
        Debug.Log($"[FurniturePlacementPanelController] Debug select next prefab result:{result}");
    }

    public void OnClickDebugSelectPreviousPrefab()
    {
        EnsureDebugReferences();

        bool result = debugRandomFurniturePlacer != null && debugRandomFurniturePlacer.SelectPreviousPrefab();
        RefreshDebugPrefabView();
        Debug.Log($"[FurniturePlacementPanelController] Debug select previous prefab result:{result}");
    }

    public void OnClickDebugPlaceSelectedPrefab()
    {
        EnsureDebugReferences();

        bool result = debugRandomFurniturePlacer != null && debugRandomFurniturePlacer.PlaceSelectedPrefabAtCameraForward();
        RefreshDebugPrefabView();
        RefreshServerFurnitureListView();
        Debug.Log($"[FurniturePlacementPanelController] Debug place selected prefab result:{result}");
    }

    public void OnClickDebugSimulatedCapture()
    {
        EnsureDebugReferences();

        if (debugCaptureSimulationController == null)
        {
            Debug.LogWarning("[FurniturePlacementPanelController] Debug simulated capture failed. DebugCaptureSimulationController is missing.");
            return;
        }

        debugCaptureSimulationController.StartSimulatedCapture();
        Debug.Log("[FurniturePlacementPanelController] Debug simulated capture requested.");
    }

    public void OnClickDebugClearPlacedFurniture()
    {
        EnsureFurniturePlacementReferences();

        if (furniturePlacementManager == null)
        {
            Debug.LogWarning("[FurniturePlacementPanelController] Debug clear failed. FurniturePlacementManager is missing.");
            return;
        }

        furniturePlacementManager.ClearPlacedFurniture();
        Debug.Log("[FurniturePlacementPanelController] Debug clear placed furniture requested.");
    }

    public void OnClickDebugSelectNextFurniture()
    {
        EnsureFurniturePlacementReferences();

        if (moveModeController == null)
        {
            Debug.LogWarning("[FurniturePlacementPanelController] Debug SelectNext failed. MoveModeController is missing.");
            return;
        }

        moveModeController.SelectNext();
        Debug.Log("[FurniturePlacementPanelController] Debug SelectNext requested.");
    }

    public void OnClickDebugSelectPreviousFurniture()
    {
        EnsureFurniturePlacementReferences();

        if (moveModeController == null)
        {
            Debug.LogWarning("[FurniturePlacementPanelController] Debug SelectPrevious failed. MoveModeController is missing.");
            return;
        }

        moveModeController.SelectPrevious();
        Debug.Log("[FurniturePlacementPanelController] Debug SelectPrevious requested.");
    }

    private void RefreshDebugPrefabView()
    {
        if (debugSelectedPrefabText == null)
        {
            return;
        }

        EnsureDebugReferences();

        if (debugRandomFurniturePlacer == null || debugRandomFurniturePlacer.PrefabCount == 0)
        {
            debugSelectedPrefabText.text = "Debug furniture: none";
            return;
        }

        int index = debugRandomFurniturePlacer.SelectedPrefabIndex;
        if (index < 0)
        {
            index = 0;
            debugRandomFurniturePlacer.SelectPrefab(index);
        }

        debugSelectedPrefabText.text =
            $"Debug furniture {index + 1}/{debugRandomFurniturePlacer.PrefabCount}: {debugRandomFurniturePlacer.SelectedPrefabName}";
    }

    private static T FindFirst<T>() where T : Object
    {
        T[] objects = FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        return objects != null && objects.Length > 0 ? objects[0] : null;
    }
}

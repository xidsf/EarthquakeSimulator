using MixedReality.Toolkit.UX;
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

    [Tooltip("현재 터치로 선택된 가구의 위치를 확정합니다.")]
    public PressableButton userConfirmSelectedFurnitureButton;

    [Tooltip("가구 배치 단계를 완료하고 시뮬레이션 단계로 이동합니다.")]
    public PressableButton userCompleteFurniturePlacementButton;

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

    [Header("Debug Values")]
    [Min(1)] public int debugMultiplePlacementCount = 3;

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
    private UnityAction debugSimulatedCaptureAction;
    private UnityAction debugClearPlacedFurnitureAction;
    private UnityAction debugToggleMoveModeAction;
    private UnityAction debugToggleRotateModeAction;
    private UnityAction debugSelectNextFurnitureAction;
    private UnityAction debugSelectPreviousFurnitureAction;
    private UnityAction debugConfirmSelectedFurnitureAction;

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
    }

    private void OnDisable()
    {
        UnregisterButtons();
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
        RemoveClick(debugSimulatedCaptureButton, debugSimulatedCaptureAction);
        RemoveClick(debugClearPlacedFurnitureButton, debugClearPlacedFurnitureAction);
        RemoveClick(debugToggleMoveModeButton, debugToggleMoveModeAction);
        RemoveClick(debugToggleRotateModeButton, debugToggleRotateModeAction);
        RemoveClick(debugSelectNextFurnitureButton, debugSelectNextFurnitureAction);
        RemoveClick(debugSelectPreviousFurnitureButton, debugSelectPreviousFurnitureAction);
        RemoveClick(debugConfirmSelectedFurnitureButton, debugConfirmSelectedFurnitureAction);
    }

    public void OnClickPlaceFurnitureFromServer()
    {
        EnsureFurniturePlacementReferences();

        if (serverPlacementPipeline == null)
        {
            Debug.LogWarning("[FurniturePlacementPanelController] Server placement failed. FurnitureServerPlacementPipeline is missing.");
            return;
        }

        serverPlacementPipeline.StartPlacementFromCurrentScanSession();
        Debug.Log("[FurniturePlacementPanelController] Server furniture list requested from current scan_session_id. Select and place an object after result is ready.");
    }

    public void OnClickSelectNextServerFurniture()
    {
        EnsureFurniturePlacementReferences();
        serverPlacementPipeline?.SelectNextPendingObject();
    }

    public void OnClickSelectPreviousServerFurniture()
    {
        EnsureFurniturePlacementReferences();
        serverPlacementPipeline?.SelectPreviousPendingObject();
    }

    public void OnClickPlaceSelectedServerFurniture()
    {
        EnsureFurniturePlacementReferences();
        serverPlacementPipeline?.PlaceSelectedPendingObject();
    }

    public void SelectServerFurnitureByIndex(int index)
    {
        EnsureFurniturePlacementReferences();
        serverPlacementPipeline?.SelectPendingObject(index);
    }

    public void PlaceServerFurnitureByIndex(int index)
    {
        EnsureFurniturePlacementReferences();
        serverPlacementPipeline?.PlacePendingObject(index);
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
        Debug.Log($"[FurniturePlacementPanelController] Confirm selected requested. result:{result}");
    }

    public void OnClickCompleteFurniturePlacement()
    {
        EnsureFurniturePlacementReferences();

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

    private static T FindFirst<T>() where T : Object
    {
        T[] objects = FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        return objects != null && objects.Length > 0 ? objects[0] : null;
    }
}

using MixedReality.Toolkit.UX;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Debug 전용 패널입니다.
/// 실제 사용자 패널에는 연결하지 않아도 됩니다.
/// ManualWallPanelController와 동일하게 PressableButton을 직접 등록합니다.
/// </summary>
public class FurniturePlacementDebugPanelController : WorkflowPanelControllerBase
{
    [Header("Debug References")]
    public DebugRandomFurniturePlacer randomFurniturePlacer;
    public DebugCaptureSimulationController captureSimulationController;
    public FurniturePlacementManager furniturePlacementManager;
    public FurnitureMoveModeController moveModeController;
    public ConfirmedRoomGeometryProvider roomGeometryProvider;

    [Header("Debug Buttons - Optional")]
    public PressableButton bindRoomGeometryButton;
    public PressableButton placeRandomFurnitureButton;
    public PressableButton placeMultipleRandomFurnitureButton;
    public PressableButton simulatedCaptureButton;
    public PressableButton clearPlacedFurnitureButton;
    public PressableButton toggleMoveModeButton;
    public PressableButton selectNextFurnitureButton;
    public PressableButton selectPreviousFurnitureButton;
    public PressableButton confirmSelectedFurnitureButton;

    [Header("Debug Values")]
    [Min(1)] public int multiplePlacementCount = 3;

    private UnityAction bindRoomGeometryAction;
    private UnityAction placeRandomFurnitureAction;
    private UnityAction placeMultipleRandomFurnitureAction;
    private UnityAction simulatedCaptureAction;
    private UnityAction clearPlacedFurnitureAction;
    private UnityAction toggleMoveModeAction;
    private UnityAction selectNextFurnitureAction;
    private UnityAction selectPreviousFurnitureAction;
    private UnityAction confirmSelectedFurnitureAction;

    protected override void Awake()
    {
        base.Awake();
        EnsureDebugReferences();
    }

    private void OnEnable()
    {
        EnsureCommonReferences();
        EnsureDebugReferences();
        CreateActions();
        RegisterButtons();
    }

    private void OnDisable()
    {
        UnregisterButtons();
    }

    private void EnsureDebugReferences()
    {
        if (!autoFindReferences)
        {
            return;
        }

        if (randomFurniturePlacer == null)
        {
            randomFurniturePlacer = FindFirst<DebugRandomFurniturePlacer>();
        }

        if (captureSimulationController == null)
        {
            captureSimulationController = FindFirst<DebugCaptureSimulationController>();
        }

        if (furniturePlacementManager == null)
        {
            furniturePlacementManager = FindFirst<FurniturePlacementManager>();
        }

        if (moveModeController == null)
        {
            moveModeController = FindFirst<FurnitureMoveModeController>();
        }

        if (roomGeometryProvider == null)
        {
            roomGeometryProvider = FindFirst<ConfirmedRoomGeometryProvider>();
        }
    }

    private void CreateActions()
    {
        bindRoomGeometryAction ??= OnClickBindRoomGeometry;
        placeRandomFurnitureAction ??= OnClickPlaceRandomFurniture;
        placeMultipleRandomFurnitureAction ??= OnClickPlaceMultipleRandomFurniture;
        simulatedCaptureAction ??= OnClickSimulatedCapture;
        clearPlacedFurnitureAction ??= OnClickClearPlacedFurniture;
        toggleMoveModeAction ??= OnClickToggleMoveMode;
        selectNextFurnitureAction ??= OnClickSelectNextFurniture;
        selectPreviousFurnitureAction ??= OnClickSelectPreviousFurniture;
        confirmSelectedFurnitureAction ??= OnClickConfirmSelectedFurniture;
    }

    private void RegisterButtons()
    {
        AddClick(bindRoomGeometryButton, bindRoomGeometryAction);
        AddClick(placeRandomFurnitureButton, placeRandomFurnitureAction);
        AddClick(placeMultipleRandomFurnitureButton, placeMultipleRandomFurnitureAction);
        AddClick(simulatedCaptureButton, simulatedCaptureAction);
        AddClick(clearPlacedFurnitureButton, clearPlacedFurnitureAction);
        AddClick(toggleMoveModeButton, toggleMoveModeAction);
        AddClick(selectNextFurnitureButton, selectNextFurnitureAction);
        AddClick(selectPreviousFurnitureButton, selectPreviousFurnitureAction);
        AddClick(confirmSelectedFurnitureButton, confirmSelectedFurnitureAction);
    }

    private void UnregisterButtons()
    {
        RemoveClick(bindRoomGeometryButton, bindRoomGeometryAction);
        RemoveClick(placeRandomFurnitureButton, placeRandomFurnitureAction);
        RemoveClick(placeMultipleRandomFurnitureButton, placeMultipleRandomFurnitureAction);
        RemoveClick(simulatedCaptureButton, simulatedCaptureAction);
        RemoveClick(clearPlacedFurnitureButton, clearPlacedFurnitureAction);
        RemoveClick(toggleMoveModeButton, toggleMoveModeAction);
        RemoveClick(selectNextFurnitureButton, selectNextFurnitureAction);
        RemoveClick(selectPreviousFurnitureButton, selectPreviousFurnitureAction);
        RemoveClick(confirmSelectedFurnitureButton, confirmSelectedFurnitureAction);
    }

    public void OnClickBindRoomGeometry()
    {
        EnsureDebugReferences();

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

        Debug.Log($"[FurniturePlacementDebugPanelController] Bind room geometry result:{result}");
    }

    public void OnClickPlaceRandomFurniture()
    {
        EnsureDebugReferences();
        bool result = randomFurniturePlacer != null && randomFurniturePlacer.PlaceOneRandomFurniture();
        Debug.Log($"[FurniturePlacementDebugPanelController] Place random furniture result:{result}");
    }

    public void OnClickPlaceMultipleRandomFurniture()
    {
        EnsureDebugReferences();
        int result = randomFurniturePlacer != null ? randomFurniturePlacer.PlaceRandomFurniture(multiplePlacementCount) : 0;
        Debug.Log($"[FurniturePlacementDebugPanelController] Place multiple random furniture result:{result}/{multiplePlacementCount}");
    }

    public void OnClickSimulatedCapture()
    {
        EnsureDebugReferences();
        captureSimulationController?.StartSimulatedCapture();
        Debug.Log("[FurniturePlacementDebugPanelController] Simulated capture requested.");
    }

    public void OnClickClearPlacedFurniture()
    {
        EnsureDebugReferences();
        furniturePlacementManager?.ClearPlacedFurniture();
        Debug.Log("[FurniturePlacementDebugPanelController] Clear placed furniture requested.");
    }

    public void OnClickToggleMoveMode()
    {
        EnsureDebugReferences();
        moveModeController?.ToggleMoveMode();
        Debug.Log("[FurniturePlacementDebugPanelController] Toggle Move Mode requested.");
    }

    public void OnClickSelectNextFurniture()
    {
        EnsureDebugReferences();
        moveModeController?.SelectNext();
        Debug.Log("[FurniturePlacementDebugPanelController] Select next requested.");
    }

    public void OnClickSelectPreviousFurniture()
    {
        EnsureDebugReferences();
        moveModeController?.SelectPrevious();
        Debug.Log("[FurniturePlacementDebugPanelController] Select previous requested.");
    }

    public void OnClickConfirmSelectedFurniture()
    {
        EnsureDebugReferences();
        Debug.Log("[FurniturePlacementDebugPanelController] Confirm selected requested.");
    }

    private static T FindFirst<T>() where T : Object
    {
        T[] objects = FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        return objects != null && objects.Length > 0 ? objects[0] : null;
    }
}

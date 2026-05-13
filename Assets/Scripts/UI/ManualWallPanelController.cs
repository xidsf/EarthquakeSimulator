using MixedReality.Toolkit.UX;
using UnityEngine;
using UnityEngine.Events;
using TMPro;

public class ManualWallPanelController : WorkflowPanelControllerBase
{
    [Header("Manual Wall References")]
    public ManualWallBuilder manualWallBuilder;

    [Header("User Buttons")]
    [Tooltip("Switches to manual wall create mode. Press again to turn all manual wall modes off.")]
    public PressableButton userSetManualCreateModeButton;

    [Tooltip("Switches to wall cut mode. Press again to turn all manual wall modes off.")]
    public PressableButton userSetWallCutModeButton;

    [Tooltip("Undoes the last operation or cancels the current selection.")]
    public PressableButton userUndoButton;

    [Tooltip("Rotates the most recently generated perpendicular wall.")]
    public PressableButton userRotateLastWallButton;

    [Tooltip("Clears all manually created walls.")]
    public PressableButton userClearManualWallsButton;

    [Tooltip("Cancels Manual Wall editing and returns to Room Build.")]
    public PressableButton userReturnToRoomBuildButton;

    [Tooltip("Completes Manual Wall editing and opens Confirm Room.")]
    public PressableButton userOpenConfirmRoomButton;

    [Header("Text References")]
    public TMP_Text text_ManualCreateMode;
    public TMP_Text text_WallCutMode;

    [Header("Mode Text")]
    [SerializeField] private string manualCreateModeLabel = "\uBCBD \uC0DD\uC131 \uBAA8\uB4DC";
    [SerializeField] private string wallCutModeLabel = "\uBCBD \uC790\uB974\uAE30 \uBAA8\uB4DC";
    [SerializeField] private string modeOnText = "On";
    [SerializeField] private string modeOffText = "Off";

    [Header("Debug Buttons - Optional")]
    public PressableButton rebuildSelectableSurfacesButton;
    public PressableButton toggleManualWallEditModeButton;
    public PressableButton setManualCreateModeButton;
    public PressableButton setWallCutModeButton;
    public PressableButton rotateLastWallButton;
    public PressableButton undoButton;
    public PressableButton clearManualWallsButton;
    public PressableButton toggleSelectableSurfaceVisibleButton;
    public PressableButton clearSelectableSurfacesButton;
    public PressableButton returnToRoomBuildButton;
    public PressableButton confirmManualWallsButton;

    private bool isCreateModeOn;
    private bool isCutModeOn;

    private UnityAction userSetManualCreateModeAction;
    private UnityAction userSetWallCutModeAction;
    private UnityAction userUndoAction;
    private UnityAction userRotateLastWallAction;
    private UnityAction userClearManualWallsAction;
    private UnityAction userReturnToRoomBuildAction;
    private UnityAction userOpenConfirmRoomAction;

    private UnityAction rebuildSelectableSurfacesAction;
    private UnityAction toggleManualWallEditModeAction;
    private UnityAction setManualCreateModeAction;
    private UnityAction setWallCutModeAction;
    private UnityAction rotateLastWallAction;
    private UnityAction undoAction;
    private UnityAction clearManualWallsAction;
    private UnityAction toggleSelectableSurfaceVisibleAction;
    private UnityAction clearSelectableSurfacesAction;
    private UnityAction returnToRoomBuildAction;
    private UnityAction confirmManualWallsAction;

    protected override void Awake()
    {
        base.Awake();
        EnsureManualWallBuilderReference();
    }

    private void OnEnable()
    {
        EnsureCommonReferences();
        EnsureManualWallBuilderReference();
        CreateActions();
        RegisterButtons();
        SyncModeStateFromBuilder();
        UpdateModeTexts();
    }

    private void OnDisable()
    {
        UnregisterButtons();
    }

    private void EnsureManualWallBuilderReference()
    {
        if (manualWallBuilder != null || !autoFindReferences)
        {
            return;
        }

        if (workflowManager != null)
        {
            manualWallBuilder = workflowManager.manualWallBuilder;
        }

        if (manualWallBuilder == null)
        {
            ManualWallBuilder[] builders = FindObjectsByType<ManualWallBuilder>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (builders != null && builders.Length > 0)
            {
                manualWallBuilder = builders[0];
            }
        }
    }

    private void CreateActions()
    {
        userSetManualCreateModeAction ??= OnClickSetManualCreateMode;
        userSetWallCutModeAction ??= OnClickSetWallCutMode;
        userUndoAction ??= OnClickUndo;
        userRotateLastWallAction ??= OnClickRotateLastWall;
        userClearManualWallsAction ??= OnClickClearManualWalls;
        userReturnToRoomBuildAction ??= OnClickReturnToRoomBuild;
        userOpenConfirmRoomAction ??= OnClickOpenConfirmRoom;

        rebuildSelectableSurfacesAction ??= OnClickRebuildSelectableSurfaces;
        toggleManualWallEditModeAction ??= OnClickToggleManualWallEditMode;
        setManualCreateModeAction ??= OnClickSetManualCreateMode;
        setWallCutModeAction ??= OnClickSetWallCutMode;
        rotateLastWallAction ??= OnClickRotateLastWall;
        undoAction ??= OnClickUndo;
        clearManualWallsAction ??= OnClickClearManualWalls;
        toggleSelectableSurfaceVisibleAction ??= OnClickToggleSelectableSurfaceVisible;
        clearSelectableSurfacesAction ??= OnClickClearSelectableSurfaces;
        returnToRoomBuildAction ??= OnClickReturnToRoomBuild;
        confirmManualWallsAction ??= OnClickOpenConfirmRoom;
    }

    private void RegisterButtons()
    {
        AddClick(userSetManualCreateModeButton, userSetManualCreateModeAction);
        AddClick(userSetWallCutModeButton, userSetWallCutModeAction);
        AddClick(userUndoButton, userUndoAction);
        AddClick(userRotateLastWallButton, userRotateLastWallAction);
        AddClick(userClearManualWallsButton, userClearManualWallsAction);
        AddClick(userReturnToRoomBuildButton, userReturnToRoomBuildAction);
        AddClick(userOpenConfirmRoomButton, userOpenConfirmRoomAction);

        AddClick(rebuildSelectableSurfacesButton, rebuildSelectableSurfacesAction);
        AddClick(toggleManualWallEditModeButton, toggleManualWallEditModeAction);
        AddClick(setManualCreateModeButton, setManualCreateModeAction);
        AddClick(setWallCutModeButton, setWallCutModeAction);
        AddClick(rotateLastWallButton, rotateLastWallAction);
        AddClick(undoButton, undoAction);
        AddClick(clearManualWallsButton, clearManualWallsAction);
        AddClick(toggleSelectableSurfaceVisibleButton, toggleSelectableSurfaceVisibleAction);
        AddClick(clearSelectableSurfacesButton, clearSelectableSurfacesAction);
        AddClick(returnToRoomBuildButton, returnToRoomBuildAction);
        AddClick(confirmManualWallsButton, confirmManualWallsAction);
    }

    private void UnregisterButtons()
    {
        RemoveClick(userSetManualCreateModeButton, userSetManualCreateModeAction);
        RemoveClick(userSetWallCutModeButton, userSetWallCutModeAction);
        RemoveClick(userUndoButton, userUndoAction);
        RemoveClick(userRotateLastWallButton, userRotateLastWallAction);
        RemoveClick(userClearManualWallsButton, userClearManualWallsAction);
        RemoveClick(userReturnToRoomBuildButton, userReturnToRoomBuildAction);
        RemoveClick(userOpenConfirmRoomButton, userOpenConfirmRoomAction);

        RemoveClick(rebuildSelectableSurfacesButton, rebuildSelectableSurfacesAction);
        RemoveClick(toggleManualWallEditModeButton, toggleManualWallEditModeAction);
        RemoveClick(setManualCreateModeButton, setManualCreateModeAction);
        RemoveClick(setWallCutModeButton, setWallCutModeAction);
        RemoveClick(rotateLastWallButton, rotateLastWallAction);
        RemoveClick(undoButton, undoAction);
        RemoveClick(clearManualWallsButton, clearManualWallsAction);
        RemoveClick(toggleSelectableSurfaceVisibleButton, toggleSelectableSurfaceVisibleAction);
        RemoveClick(clearSelectableSurfacesButton, clearSelectableSurfacesAction);
        RemoveClick(returnToRoomBuildButton, returnToRoomBuildAction);
        RemoveClick(confirmManualWallsButton, confirmManualWallsAction);
    }

    private void SyncModeStateFromBuilder()
    {
        if (manualWallBuilder == null)
        {
            isCreateModeOn = false;
            isCutModeOn = false;
            return;
        }

        isCreateModeOn = manualWallBuilder.currentMode == ManualWallBuilder.BuildMode.ManualWallCreate;
        isCutModeOn = manualWallBuilder.currentMode == ManualWallBuilder.BuildMode.WallCut || manualWallBuilder.wallCutModeActive;
    }

    private void UpdateModeTexts()
    {
        if (text_ManualCreateMode != null)
        {
            text_ManualCreateMode.text = BuildModeText(manualCreateModeLabel, isCreateModeOn);
        }

        if (text_WallCutMode != null)
        {
            text_WallCutMode.text = BuildModeText(wallCutModeLabel, isCutModeOn);
        }
    }

    private string BuildModeText(string label, bool isOn)
    {
        return label + "\n" + (isOn ? modeOnText : modeOffText);
    }

    public void OnClickSetManualCreateMode()
    {
        EnsureManualWallBuilderReference();

        if (isCreateModeOn)
        {
            SetNoManualWallMode();
            return;
        }

        isCreateModeOn = true;
        isCutModeOn = false;
        manualWallBuilder?.SetManualCreateMode();
        UpdateModeTexts();
    }

    public void OnClickSetWallCutMode()
    {
        EnsureManualWallBuilderReference();

        if (isCutModeOn)
        {
            SetNoManualWallMode();
            return;
        }

        isCreateModeOn = false;
        isCutModeOn = true;
        manualWallBuilder?.SetWallCutMode(true);
        UpdateModeTexts();
    }

    private void SetNoManualWallMode()
    {
        isCreateModeOn = false;
        isCutModeOn = false;
        manualWallBuilder?.SetNoneMode();
        UpdateModeTexts();
    }

    public void OnClickRebuildSelectableSurfaces()
    {
        EnsureManualWallBuilderReference();
        manualWallBuilder?.RebuildSelectableSurfaces();
    }

    public void OnClickToggleManualWallEditMode()
    {
        EnsureManualWallBuilderReference();
        manualWallBuilder?.ToggleManualWallEditMode();
        SyncModeStateFromBuilder();
        UpdateModeTexts();
    }

    public void OnClickRotateLastWall()
    {
        EnsureManualWallBuilderReference();
        manualWallBuilder?.RotateLastGeneratedPerpendicularWall();
    }

    public void OnClickUndo()
    {
        EnsureManualWallBuilderReference();
        manualWallBuilder?.UndoLastManualWall();
    }

    public void OnClickClearManualWalls()
    {
        EnsureManualWallBuilderReference();
        manualWallBuilder?.ClearManualWalls();
    }

    public void OnClickToggleSelectableSurfaceVisible()
    {
        EnsureManualWallBuilderReference();
        manualWallBuilder?.ToggleSelectableSurfaceVisible();
    }

    public void OnClickClearSelectableSurfaces()
    {
        EnsureManualWallBuilderReference();
        manualWallBuilder?.ClearSelectableSurfaces();
    }

    public void OnClickReturnToRoomBuild()
    {
        RequestWorkflowCommand(RoomBuildWorkflowManager.WorkflowCommand.ReturnToRoomBuild);
    }

    public void OnClickOpenConfirmRoom()
    {
        RequestWorkflowCommand(RoomBuildWorkflowManager.WorkflowCommand.ConfirmManualWalls);
    }
}

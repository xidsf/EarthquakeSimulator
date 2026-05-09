using MixedReality.Toolkit.UX;
using UnityEngine;
using UnityEngine.Events;

public class ManualWallPanelController : WorkflowPanelControllerBase
{
    [Header("Manual Wall References")]
    public ManualWallBuilder manualWallBuilder;

    [Header("User Buttons")]
    [Tooltip("벽 생성 모드로 전환합니다.")]
    public PressableButton userSetManualCreateModeButton;

    [Tooltip("벽 자르기 모드로 전환합니다.")]
    public PressableButton userSetWallCutModeButton;

    [Tooltip("마지막 작업을 되돌리거나 진행 중인 선택을 취소합니다.")]
    public PressableButton userUndoButton;

    [Tooltip("방금 만든 수직 벽을 회전합니다.")]
    public PressableButton userRotateLastWallButton;

    [Tooltip("모든 수동 벽을 초기화합니다.")]
    public PressableButton userClearManualWallsButton;

    [Tooltip("Manual Wall 작업을 취소하고 RoomBuild 단계로 돌아갑니다.")]
    public PressableButton userReturnToRoomBuildButton;

    [Tooltip("Manual Wall 편집을 마치고 Confirm Room 패널로 이동합니다.")]
    public PressableButton userOpenConfirmRoomButton;

    [Header("Debug Buttons - Optional")]
    [Tooltip("디버깅용 Selectable Surface 재생성 버튼입니다. 실제 사용자 UI에는 노출하지 않는 것을 권장합니다.")]
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

        // Debug / legacy listeners. 실제 사용자 UI에서는 이 버튼들을 연결하지 않아도 됩니다.
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

    public void OnClickRebuildSelectableSurfaces()
    {
        EnsureManualWallBuilderReference();
        manualWallBuilder?.RebuildSelectableSurfaces();
    }

    public void OnClickToggleManualWallEditMode()
    {
        EnsureManualWallBuilderReference();
        manualWallBuilder?.ToggleManualWallEditMode();
    }

    public void OnClickSetManualCreateMode()
    {
        EnsureManualWallBuilderReference();
        manualWallBuilder?.SetManualCreateMode();
    }

    public void OnClickSetWallCutMode()
    {
        EnsureManualWallBuilderReference();
        manualWallBuilder?.SetWallCutMode(true);
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

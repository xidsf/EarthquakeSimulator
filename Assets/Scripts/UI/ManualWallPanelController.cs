using MixedReality.Toolkit.UX;
using UnityEngine;
using UnityEngine.Events;
using TMPro;

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

    [Header("Text References")]
    public TMP_Text text_ManualCreateMode;
    public TMP_Text text_WallCutMode;

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

    // 현재 모드가 켜져 있는지 추적하는 상태 변수
    private bool isCreateModeOn = false;
    private bool isCutModeOn = false;

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

        // 패널이 켜질 때마다 두 모드를 모두 Off 상태로 초기화합니다.
        isCreateModeOn = false;
        isCutModeOn = false;
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

    private void UpdateModeTexts()
    {
        if (text_ManualCreateMode != null)
            text_ManualCreateMode.text = "벽 생성 모드\n" + (isCreateModeOn ? "On" : "Off");

        if (text_WallCutMode != null)
            text_WallCutMode.text = "벽 자르기 모드\n" + (isCutModeOn ? "On" : "Off");
    }

    public void OnClickSetManualCreateMode()
    {
        EnsureManualWallBuilderReference();

        // 현재 꺼져있는 상태에서 켜려고 할 때
        if (!isCreateModeOn)
        {
            // 상대방(자르기) 모드가 켜져 있다면 강제로 끕니다.
            if (isCutModeOn)
            {
                isCutModeOn = false;
                manualWallBuilder?.SetWallCutMode(false);
            }
        }

        // 상태 토글 및 텍스트 업데이트
        isCreateModeOn = !isCreateModeOn;
        UpdateModeTexts();

        // 실제 빌더 로직 실행
        if (isCreateModeOn)
        {
            manualWallBuilder?.SetManualCreateMode();
        }
        else
        {
            // 벽 생성 모드를 끄는 로직 (구현되어 있다면 호출)
            // manualWallBuilder?.CancelManualCreateMode();
        }
    }

    public void OnClickSetWallCutMode()
    {
        EnsureManualWallBuilderReference();

        // 현재 꺼져있는 상태에서 켜려고 할 때
        if (!isCutModeOn)
        {
            // 상대방(생성) 모드가 켜져 있다면 강제로 끕니다.
            if (isCreateModeOn)
            {
                isCreateModeOn = false;
                // 벽 생성 모드를 끄는 로직 (구현되어 있다면 호출)
                // manualWallBuilder?.CancelManualCreateMode();
            }
        }

        // 상태 토글 및 텍스트 업데이트
        isCutModeOn = !isCutModeOn;
        UpdateModeTexts();

        // 실제 빌더 로직 실행
        if (isCutModeOn)
        {
            manualWallBuilder?.SetWallCutMode(true);
        }
        else
        {
            manualWallBuilder?.SetWallCutMode(false);
        }
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
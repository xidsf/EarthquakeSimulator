using MixedReality.Toolkit.UX;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class ManualWallPanelController : WorkflowPanelControllerBase
{
    private const string ToggleButtonBackplateOuterGeometryName = "UX.Button.BackplateOuterGeometry";

    [Header("Manual Wall References")]
    public ManualWallBuilder manualWallBuilder;

    [Header("User Buttons")]
    [Tooltip("Opens the wall-add sub panel.")]
    public PressableButton userOpenWallAddRootButton;

    [Tooltip("Opens the wall-edit sub panel.")]
    public PressableButton userOpenWallEditRootButton;

    [Tooltip("Rotates the most recently generated perpendicular wall.")]
    public PressableButton userRotateLastWallButton;

    [Tooltip("Clears all manually created walls.")]
    public PressableButton userClearManualWallsButton;

    [Tooltip("Cancels Manual Wall editing and returns to Room Build.")]
    public PressableButton userReturnToRoomBuildButton;

    [Tooltip("Completes Manual Wall editing and opens Confirm Room.")]
    public PressableButton userOpenConfirmRoomButton;

    [Header("User Root Objects")]
    public GameObject userMainRoot;
    public GameObject userWallAddRoot;
    public GameObject userWallEditRoot;
    public GameObject userAlwaysRoot;

    [Header("Wall Add Root Buttons")]
    public PressableButton userWallAddBackButton;
    public PressableButton userWallAddUndoButton;
    public PressableButton userWallAddRotateLastWallButton;

    [Header("Wall Edit Root Buttons")]
    public PressableButton userWallEditCutButton;
    public PressableButton userWallEditDeleteButton;
    public PressableButton userWallEditExtendButton;
    public PressableButton userWallEditBackButton;
    public PressableButton userWallEditUndoButton;

    [Header("Always Visible Wall Display Toggles")]
    public PressableButton userToggleDetectedWallsButton;
    public PressableButton userToggleActiveCandidatesButton;
    public PressableButton userToggleIgnoredWallsButton;

    [Header("Toggle Button Visuals")]
    public Material toggleOnMaterial;

    [Header("Simplified User Buttons")]
    public bool hideAdvancedUserButtons = true;
    public bool hideUserRotateLastWallButton = true;
    public bool hideUserClearManualWallsButton = true;

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

    private UnityAction userRotateLastWallAction;
    private UnityAction userClearManualWallsAction;
    private UnityAction userReturnToRoomBuildAction;
    private UnityAction userOpenConfirmRoomAction;
    private UnityAction userOpenWallAddRootAction;
    private UnityAction userOpenWallEditRootAction;
    private UnityAction userWallAddBackAction;
    private UnityAction userWallAddUndoAction;
    private UnityAction userWallAddRotateLastWallAction;
    private UnityAction userWallEditCutAction;
    private UnityAction userWallEditDeleteAction;
    private UnityAction userWallEditExtendAction;
    private UnityAction userWallEditBackAction;
    private UnityAction userWallEditUndoAction;
    private UnityAction userToggleDetectedWallsAction;
    private UnityAction userToggleActiveCandidatesAction;
    private UnityAction userToggleIgnoredWallsAction;

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
    private readonly Dictionary<Renderer, Material[]> originalToggleButtonMaterials = new Dictionary<Renderer, Material[]>();
    private readonly Dictionary<PressableButton, bool> toggleButtonVisualStates = new Dictionary<PressableButton, bool>();

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
        UpdateUserButtonVisibility();
        ShowUserMainRoot();
        SetNoManualWallMode();
    }

    private void OnDisable()
    {
        UnregisterButtons();
        RestoreToggleButtonMaterials();
    }

    private void Update()
    {
        UpdateToggleButtonVisuals();
        UpdateRotateLastWallButtonVisibility();
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
        userRotateLastWallAction ??= OnClickRotateLastWall;
        userClearManualWallsAction ??= OnClickClearManualWalls;
        userReturnToRoomBuildAction ??= OnClickReturnToRoomBuild;
        userOpenConfirmRoomAction ??= OnClickOpenConfirmRoom;
        userOpenWallAddRootAction ??= OnClickOpenWallAddRoot;
        userOpenWallEditRootAction ??= OnClickOpenWallEditRoot;
        userWallAddBackAction ??= OnClickBackToManualWallMainRoot;
        userWallAddUndoAction ??= OnClickUndo;
        userWallAddRotateLastWallAction ??= OnClickRotateLastWall;
        userWallEditCutAction ??= OnClickSetWallCutMode;
        userWallEditDeleteAction ??= OnClickSetWallDeleteMode;
        userWallEditExtendAction ??= OnClickSetWallExtendMode;
        userWallEditBackAction ??= OnClickBackToManualWallMainRoot;
        userWallEditUndoAction ??= OnClickUndo;
        userToggleDetectedWallsAction ??= OnClickToggleDetectedWalls;
        userToggleActiveCandidatesAction ??= OnClickToggleActiveCandidates;
        userToggleIgnoredWallsAction ??= OnClickToggleIgnoredWalls;

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
        AddClick(userRotateLastWallButton, userRotateLastWallAction);
        AddClick(userClearManualWallsButton, userClearManualWallsAction);
        AddClick(userReturnToRoomBuildButton, userReturnToRoomBuildAction);
        AddClick(userOpenConfirmRoomButton, userOpenConfirmRoomAction);
        AddClick(userOpenWallAddRootButton, userOpenWallAddRootAction);
        AddClick(userOpenWallEditRootButton, userOpenWallEditRootAction);
        AddClick(userWallAddBackButton, userWallAddBackAction);
        AddClick(userWallAddUndoButton, userWallAddUndoAction);
        AddClick(userWallAddRotateLastWallButton, userWallAddRotateLastWallAction);
        AddClick(userWallEditCutButton, userWallEditCutAction);
        AddClick(userWallEditDeleteButton, userWallEditDeleteAction);
        AddClick(userWallEditExtendButton, userWallEditExtendAction);
        AddClick(userWallEditBackButton, userWallEditBackAction);
        AddClick(userWallEditUndoButton, userWallEditUndoAction);
        AddClick(userToggleDetectedWallsButton, userToggleDetectedWallsAction);
        AddClick(userToggleActiveCandidatesButton, userToggleActiveCandidatesAction);
        AddClick(userToggleIgnoredWallsButton, userToggleIgnoredWallsAction);

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
        RemoveClick(userRotateLastWallButton, userRotateLastWallAction);
        RemoveClick(userClearManualWallsButton, userClearManualWallsAction);
        RemoveClick(userReturnToRoomBuildButton, userReturnToRoomBuildAction);
        RemoveClick(userOpenConfirmRoomButton, userOpenConfirmRoomAction);
        RemoveClick(userOpenWallAddRootButton, userOpenWallAddRootAction);
        RemoveClick(userOpenWallEditRootButton, userOpenWallEditRootAction);
        RemoveClick(userWallAddBackButton, userWallAddBackAction);
        RemoveClick(userWallAddUndoButton, userWallAddUndoAction);
        RemoveClick(userWallAddRotateLastWallButton, userWallAddRotateLastWallAction);
        RemoveClick(userWallEditCutButton, userWallEditCutAction);
        RemoveClick(userWallEditDeleteButton, userWallEditDeleteAction);
        RemoveClick(userWallEditExtendButton, userWallEditExtendAction);
        RemoveClick(userWallEditBackButton, userWallEditBackAction);
        RemoveClick(userWallEditUndoButton, userWallEditUndoAction);
        RemoveClick(userToggleDetectedWallsButton, userToggleDetectedWallsAction);
        RemoveClick(userToggleActiveCandidatesButton, userToggleActiveCandidatesAction);
        RemoveClick(userToggleIgnoredWallsButton, userToggleIgnoredWallsAction);

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

    private void UpdateUserButtonVisibility()
    {
        if (!hideAdvancedUserButtons)
        {
            SetButtonVisible(userRotateLastWallButton, true);
            SetButtonVisible(userClearManualWallsButton, true);
            return;
        }

        SetButtonVisible(userRotateLastWallButton, !hideUserRotateLastWallButton);
        SetButtonVisible(userClearManualWallsButton, !hideUserClearManualWallsButton);
    }

    private static void SetButtonVisible(PressableButton button, bool visible)
    {
        if (button != null)
        {
            button.gameObject.SetActive(visible);
        }
    }

    private void ShowUserMainRoot()
    {
        SetRootVisible(userMainRoot, true);
        SetRootVisible(userWallAddRoot, false);
        SetRootVisible(userWallEditRoot, false);
        SetRootVisible(userAlwaysRoot, true);
        UpdateRotateLastWallButtonVisibility();
    }

    private void ShowUserWallAddRoot()
    {
        SetRootVisible(userMainRoot, false);
        SetRootVisible(userWallAddRoot, true);
        SetRootVisible(userWallEditRoot, false);
        SetRootVisible(userAlwaysRoot, true);
        UpdateRotateLastWallButtonVisibility();
    }

    private void ShowUserWallEditRoot()
    {
        SetRootVisible(userMainRoot, false);
        SetRootVisible(userWallAddRoot, false);
        SetRootVisible(userWallEditRoot, true);
        SetRootVisible(userAlwaysRoot, true);
        UpdateRotateLastWallButtonVisibility();
    }

    private static void SetRootVisible(GameObject root, bool visible)
    {
        if (root != null)
        {
            root.SetActive(visible);
        }
    }

    private void UpdateRotateLastWallButtonVisibility()
    {
        bool addRootVisible = userWallAddRoot != null && userWallAddRoot.activeInHierarchy;
        bool canRotate = manualWallBuilder != null &&
                         manualWallBuilder.CanRotateLastGeneratedPerpendicularWall();

        SetButtonVisible(userWallAddRotateLastWallButton, addRootVisible && canRotate);
    }

    private void UpdateToggleButtonVisuals()
    {
        bool cutOn = manualWallBuilder != null &&
                     (manualWallBuilder.currentMode == ManualWallBuilder.BuildMode.WallCut ||
                      manualWallBuilder.wallCutModeActive);
        bool deleteOn = manualWallBuilder != null &&
                        manualWallBuilder.currentMode == ManualWallBuilder.BuildMode.WallDelete;
        bool extendOn = manualWallBuilder != null &&
                        manualWallBuilder.currentMode == ManualWallBuilder.BuildMode.WallExtend;

        SetToggleButtonOn(userWallEditCutButton, cutOn);
        SetToggleButtonOn(userWallEditDeleteButton, deleteOn);
        SetToggleButtonOn(userWallEditExtendButton, extendOn);
        SetToggleButtonOn(userToggleDetectedWallsButton, manualWallBuilder != null && manualWallBuilder.showSelectableDetectedWalls);
        SetToggleButtonOn(userToggleActiveCandidatesButton, manualWallBuilder != null && manualWallBuilder.showSelectableActiveWallCandidates);
        SetToggleButtonOn(userToggleIgnoredWallsButton, manualWallBuilder != null && manualWallBuilder.showSelectableIgnoredWallCandidates);
    }

    private void SetToggleButtonOn(PressableButton button, bool isOn)
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

        if (isOn && toggleOnMaterial != null)
        {
            Material[] originalMaterials = originalToggleButtonMaterials[renderer];
            Material[] activeMaterials = new Material[originalMaterials.Length];
            for (int i = 0; i < activeMaterials.Length; i++)
            {
                activeMaterials[i] = toggleOnMaterial;
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

    public void OnClickOpenWallAddRoot()
    {
        EnsureManualWallBuilderReference();
        ShowUserWallAddRoot();
        manualWallBuilder?.SetManualCreateMode();
        UpdateToggleButtonVisuals();
    }

    public void OnClickOpenWallEditRoot()
    {
        EnsureManualWallBuilderReference();
        ShowUserWallEditRoot();
        manualWallBuilder?.SetNoneMode();
        UpdateToggleButtonVisuals();
    }

    public void OnClickBackToManualWallMainRoot()
    {
        EnsureManualWallBuilderReference();
        ShowUserMainRoot();
        SetNoManualWallMode();
        UpdateRotateLastWallButtonVisibility();
    }

    public void OnClickSetManualCreateMode()
    {
        EnsureManualWallBuilderReference();

        if (manualWallBuilder != null &&
            manualWallBuilder.currentMode == ManualWallBuilder.BuildMode.ManualWallCreate)
        {
            SetNoManualWallMode();
            return;
        }

        manualWallBuilder?.SetManualCreateMode();
        UpdateToggleButtonVisuals();
    }

    public void OnClickSetWallDeleteMode()
    {
        EnsureManualWallBuilderReference();

        if (manualWallBuilder != null && manualWallBuilder.currentMode == ManualWallBuilder.BuildMode.WallDelete)
        {
            SetNoManualWallMode();
            return;
        }

        manualWallBuilder?.SetWallDeleteMode(true);
        UpdateToggleButtonVisuals();
    }

    public void OnClickSetWallExtendMode()
    {
        EnsureManualWallBuilderReference();

        if (manualWallBuilder != null && manualWallBuilder.currentMode == ManualWallBuilder.BuildMode.WallExtend)
        {
            SetNoManualWallMode();
            return;
        }

        manualWallBuilder?.SetWallExtendMode(true);
        UpdateToggleButtonVisuals();
    }

    public void OnClickSetWallCutMode()
    {
        EnsureManualWallBuilderReference();

        if (manualWallBuilder != null &&
            (manualWallBuilder.currentMode == ManualWallBuilder.BuildMode.WallCut ||
             manualWallBuilder.wallCutModeActive))
        {
            SetNoManualWallMode();
            return;
        }

        manualWallBuilder?.SetWallCutMode(true);
        UpdateToggleButtonVisuals();
    }

    private void SetNoManualWallMode()
    {
        manualWallBuilder?.SetNoneMode();
        UpdateToggleButtonVisuals();
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
        UpdateToggleButtonVisuals();
    }

    public void OnClickRotateLastWall()
    {
        EnsureManualWallBuilderReference();
        manualWallBuilder?.RotateLastGeneratedPerpendicularWall();
        UpdateRotateLastWallButtonVisibility();
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

    public void OnClickToggleDetectedWalls()
    {
        EnsureManualWallBuilderReference();
        manualWallBuilder?.ToggleDetectedWallsVisible();
        UpdateToggleButtonVisuals();
    }

    public void OnClickToggleActiveCandidates()
    {
        EnsureManualWallBuilderReference();
        manualWallBuilder?.ToggleActiveCandidatesVisible();
        UpdateToggleButtonVisuals();
    }

    public void OnClickToggleIgnoredWalls()
    {
        EnsureManualWallBuilderReference();
        manualWallBuilder?.ToggleIgnoredWallsVisible();
        UpdateToggleButtonVisuals();
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

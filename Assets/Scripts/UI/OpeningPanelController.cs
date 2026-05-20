using MixedReality.Toolkit.UX;
using UnityEngine.Events;

public class OpeningPanelController : WorkflowPanelControllerBase
{
    public PressableButton btn_Start;

    private UnityAction startAction;

    protected override void Awake()
    {
        base.Awake();
    }

    private void OnEnable()
    {
        EnsureCommonReferences();
        CreateActions();
        RegisterButtons();
    }

    private void OnDisable()
    {
        UnregisterButtons();
    }

    private void CreateActions()
    {
        startAction ??= OnClickStart;
    }

    private void RegisterButtons()
    {
        AddClick(btn_Start, startAction);
    }

    private void UnregisterButtons()
    {
        RemoveClick(btn_Start, startAction);
    }

    public void OnClickStart()
    {
        EnsureCommonReferences();

        if (uiManager == null)
        {
            ShowWarning("UIManager is not connected.");
            return;
        }

        uiManager.OpenRoomInfoInputFromOpening();
    }
}

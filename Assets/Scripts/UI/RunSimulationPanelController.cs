using MixedReality.Toolkit.UX;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;

public class RunSimulationPanelController : WorkflowPanelControllerBase
{
    [Header("Simulation Control Buttons")]
    [Tooltip("Start playback, or restart it if playback has already ended.")]
    public PressableButton btn_Start;

    [Tooltip("Stop the currently running playback.")]
    public PressableButton btn_Stop;

    [Tooltip("Restart playback from the latest simulation result.")]
    public PressableButton btn_Reset;

    [Tooltip("Return to the simulation result screen.")]
    [FormerlySerializedAs("btn_GoToRelocation")]
    public PressableButton btn_BackToResult;

    [Header("Playback References")]
    public SimulationClientPlaybackController playbackController;
    public bool autoFindPlaybackController = true;

    private UnityAction startAction;
    private UnityAction stopAction;
    private UnityAction resetAction;
    private UnityAction backToResultAction;

    protected override void Awake()
    {
        base.Awake();
        EnsurePlaybackController();
    }

    private void OnEnable()
    {
        EnsureCommonReferences();
        EnsurePlaybackController();
        CreateActions();
        RegisterButtons();

        if (btn_Start != null) btn_Start.enabled = true;
        if (btn_Stop != null) btn_Stop.enabled = true;
        if (btn_Reset != null) btn_Reset.enabled = true;
        if (btn_BackToResult != null) btn_BackToResult.enabled = true;
    }

    private void OnDisable()
    {
        UnregisterButtons();
    }

    private void CreateActions()
    {
        startAction ??= OnClick_Start;
        stopAction ??= OnClick_Stop;
        resetAction ??= OnClick_Reset;
        backToResultAction ??= OnClick_BackToResult;
    }

    private void RegisterButtons()
    {
        AddClick(btn_Start, startAction);
        AddClick(btn_Stop, stopAction);
        AddClick(btn_Reset, resetAction);
        AddClick(btn_BackToResult, backToResultAction);
    }

    private void UnregisterButtons()
    {
        RemoveClick(btn_Start, startAction);
        RemoveClick(btn_Stop, stopAction);
        RemoveClick(btn_Reset, resetAction);
        RemoveClick(btn_BackToResult, backToResultAction);
    }

    public void OnClick_Start()
    {
        if (!PlayLatestSimulation())
        {
            ShowWarning("Simulation playback is not ready.");
        }
    }

    public void OnClick_Stop()
    {
        EnsurePlaybackController();
        playbackController?.PausePlayback();
    }

    public void OnClick_Reset()
    {
        EnsurePlaybackController();
        bool restarted = playbackController != null && playbackController.RestartLatestSimulation();
        if (!restarted)
        {
            ShowWarning("Simulation playback is not ready.");
        }
    }

    public void OnClick_BackToResult()
    {
        EnsurePlaybackController();
        playbackController?.StopPlayback();
        bool moved = RequestWorkflowCommand(RoomBuildWorkflowManager.WorkflowCommand.CompleteRunSimulation);
        EnsureResultPanelVisible();
        if (!moved)
        {
            ShowWarning("결과 화면으로 돌아가지 못했습니다.");
        }
    }

    private bool PlayLatestSimulation()
    {
        EnsurePlaybackController();
        return playbackController != null && playbackController.PlayLatestSimulation();
    }

    private void EnsurePlaybackController()
    {
        if (playbackController != null || !autoFindPlaybackController)
        {
            return;
        }

        SimulationClientPlaybackController[] controllers =
            FindObjectsByType<SimulationClientPlaybackController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        playbackController = controllers != null && controllers.Length > 0 ? controllers[0] : null;
    }

    private void EnsureResultPanelVisible()
    {
        EnsureCommonReferences();

        if (uiManager != null)
        {
            uiManager.ShowWorkflowState(RoomBuildWorkflowManager.WorkflowState.SimulationSuccess);
            if (uiManager.panel_SimulationSuccess != null && SimulationProcessManager.LastCompletedResult != null)
            {
                SimulationResultUI.ShowResultOnPanel(uiManager.panel_SimulationSuccess, SimulationProcessManager.LastCompletedResult);
            }
        }
    }
}

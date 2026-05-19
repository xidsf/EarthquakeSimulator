using System.Collections;
using MixedReality.Toolkit.UX;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

public class SimulationResultUI : WorkflowPanelControllerBase
{
    [Header("UI Component References")]
    [SerializeField] private TMP_Text aiAdviceText;
    [SerializeField] private TMP_Text riskResultText;
    [SerializeField] private TMP_Text roomLevelText;
    [SerializeField] private TMP_Text fallHazardFurnituresList;
    [SerializeField] private TMP_Text recognizedFurnitureText;
    [SerializeField] private TMP_Text destructiveDirectionText;

    [Header("Playback References")]
    public SimulationClientPlaybackController playbackController;
    public bool autoFindPlaybackController = true;

    [Header("User Buttons")]
    public PressableButton btn_ViewSimulation; 
    public PressableButton btn_EndSimulation; 

    [Header("System Message UI")]
    public GameObject panel_SystemMessage;
    public TMP_Text text_SystemMessage;

    private Coroutine loadingCoroutine;
    private UnityAction viewSimulationAction;
    private UnityAction endSimulationAction;

    protected override void Awake()
    {
        base.Awake();
    }

    private void OnEnable()
    {
        CreateActions();
        RegisterButtons();
        if (panel_SystemMessage != null) panel_SystemMessage.SetActive(false);

        if (SimulationProcessManager.LastCompletedResult != null)
        {
            UpdateUI(SimulationProcessManager.LastCompletedResult);
        }
    }

    private void OnDisable()
    {
        UnregisterButtons();
        StopLoadingAnimation();
    }

    private void CreateActions()
    {
        viewSimulationAction ??= OnClickViewSimulation;
        endSimulationAction ??= OnClickEndSimulation;
    }

    private void RegisterButtons()
    {
        AddClick(btn_ViewSimulation, viewSimulationAction);
        AddClick(btn_EndSimulation, endSimulationAction);
    }

    private void UnregisterButtons()
    {
        RemoveClick(btn_ViewSimulation, viewSimulationAction);
        RemoveClick(btn_EndSimulation, endSimulationAction);
    }

    public void UpdateUI(SimulationResultResponse response)
    {
        if (response == null) return;

        if (aiAdviceText != null) aiAdviceText.text = SimulationResultDisplayFormatter.BuildAdviceText(response);
        if (riskResultText != null) riskResultText.text = SimulationResultDisplayFormatter.BuildRiskScoreText(response);
        if (roomLevelText != null) roomLevelText.text = SimulationResultDisplayFormatter.BuildRiskLevelText(response);
        if (fallHazardFurnituresList != null) fallHazardFurnituresList.text = SimulationResultDisplayFormatter.BuildFallHazardText(response);
        if (recognizedFurnitureText != null) recognizedFurnitureText.text = SimulationResultDisplayFormatter.BuildRecognizedFurnitureText(response);
        if (destructiveDirectionText != null) destructiveDirectionText.text = SimulationResultDisplayFormatter.BuildDestructiveDirectionText(response);
    }

    public void OnClickViewSimulation()
    {
        EnsurePlaybackController();

        if (panel_SystemMessage != null) panel_SystemMessage.SetActive(true);
        StopLoadingAnimation();
        loadingCoroutine = StartCoroutine(AnimateLoadingText("Loading simulation"));

        bool started = playbackController != null && playbackController.PlayLatestSimulation();
        StopLoadingAnimation();
        if (text_SystemMessage != null)
        {
            text_SystemMessage.text = started ? "Simulation playback started." : "Simulation playback is not ready.";
        }
    }

    public void OnClickEndSimulation()
    {
        RequestWorkflowCommand(RoomBuildWorkflowManager.WorkflowCommand.ReturnToRoomBuild);
    }

    private void EnsurePlaybackController()
    {
        if (playbackController != null || !autoFindPlaybackController) return;

        SimulationClientPlaybackController[] controllers =
            FindObjectsByType<SimulationClientPlaybackController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        playbackController = controllers != null && controllers.Length > 0 ? controllers[0] : null;
    }

    private void StopLoadingAnimation()
    {
        if (loadingCoroutine != null)
        {
            StopCoroutine(loadingCoroutine);
            loadingCoroutine = null;
        }
    }

    private IEnumerator AnimateLoadingText(string baseText)
    {
        int dotCount = 1;
        while (true)
        {
            if (text_SystemMessage != null)
            {
                text_SystemMessage.text = baseText + new string('.', dotCount);
            }

            dotCount++;
            if (dotCount > 3) dotCount = 1;

            yield return new WaitForSeconds(1.0f); 
        }
    }
}

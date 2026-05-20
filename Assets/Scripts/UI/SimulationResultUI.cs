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
    public PressableButton btn_ReArrangeFurniture;

    [Header("System Message UI")]
    public GameObject panel_SystemMessage;
    public TMP_Text text_SystemMessage;

    private Coroutine loadingCoroutine;
    private UnityAction viewSimulationAction;
    private UnityAction endSimulationAction;
    private UnityAction reArrangeFurnitureAction;

    protected override void Awake()
    {
        base.Awake();
    }

    private void OnEnable()
    {
        EnsureCommonReferences();
        EnsurePlaybackController();
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
        reArrangeFurnitureAction ??= OnClickReArrangeFurniture;
    }

    private void RegisterButtons()
    {
        AddClick(btn_ViewSimulation, viewSimulationAction);
        AddClick(btn_EndSimulation, endSimulationAction);
        AddClick(btn_ReArrangeFurniture, reArrangeFurnitureAction);
    }

    private void UnregisterButtons()
    {
        RemoveClick(btn_ViewSimulation, viewSimulationAction);
        RemoveClick(btn_EndSimulation, endSimulationAction);
        RemoveClick(btn_ReArrangeFurniture, reArrangeFurnitureAction);
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

    public static void ShowResultOnPanel(GameObject panel, SimulationResultResponse response)
    {
        if (panel == null)
        {
            Debug.LogWarning("[SimulationResultUI] Simulation success panel is missing.");
            return;
        }

        SimulationResultUI resultUi = panel.GetComponent<SimulationResultUI>();
        if (resultUi == null)
        {
            Debug.LogWarning("[SimulationResultUI] SimulationResultUI is missing on the simulation success panel.");
            return;
        }

        resultUi.UpdateUI(response);
        panel.SetActive(true);
    }

    public void OnClickViewSimulation()
    {
        EnsurePlaybackController();

        StopLoadingAnimation();
        bool started = playbackController != null && playbackController.PlayLatestSimulation();
        if (!started)
        {
            SetSystemMessage("Simulation playback is not ready.");
            return;
        }

        bool moved = RequestWorkflowCommand(RoomBuildWorkflowManager.WorkflowCommand.StartRunSimulation);
        SetSystemMessage(moved ? "Simulation playback started." : "Simulation playback started, but RunSimulation panel transition failed.");
    }

    public void OnClickEndSimulation()
    {
        StopPlayback();

        EnsureCommonReferences();
        if (uiManager != null && uiManager.panel_Opening != null)
        {
            uiManager.ReturnToOpeningPanel();
            return;
        }

        SetSystemMessage("Simulation ended.");

#if UNITY_EDITOR
        Debug.Log("[SimulationResultUI] Exit simulation requested. Application.Quit is ignored in the Unity Editor.");
#else
        Application.Quit();
#endif
    }

    public void OnClickReArrangeFurniture()
    {
        StopPlayback();
        bool moved = RequestWorkflowCommand(RoomBuildWorkflowManager.WorkflowCommand.StartFurnitureRePlacement);
        if (!moved)
        {
            SetSystemMessage("Cannot enter furniture rearrangement.");
        }
    }

    private void EnsurePlaybackController()
    {
        if (playbackController != null || !autoFindPlaybackController) return;

        SimulationClientPlaybackController[] controllers =
            FindObjectsByType<SimulationClientPlaybackController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        playbackController = controllers != null && controllers.Length > 0 ? controllers[0] : null;
    }

    private void StopPlayback()
    {
        EnsurePlaybackController();
        playbackController?.StopPlayback();
        StopLoadingAnimation();
    }

    private void SetSystemMessage(string message)
    {
        if (panel_SystemMessage != null)
        {
            panel_SystemMessage.SetActive(true);
        }

        if (text_SystemMessage != null)
        {
            text_SystemMessage.text = message;
        }
        else
        {
            Debug.Log($"[SimulationResultUI] {message}");
        }
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

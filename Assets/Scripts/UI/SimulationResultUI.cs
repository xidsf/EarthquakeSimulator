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
    public bool createReArrangeButtonAtRuntime = true;

    [Header("System Message UI")]
    public GameObject panel_SystemMessage;
    public TMP_Text text_SystemMessage;

    private Coroutine loadingCoroutine;
    private UnityAction viewSimulationAction;
    private UnityAction endSimulationAction;
    private UnityAction reArrangeFurnitureAction;
    private bool runtimeReArrangeButtonCreated;

    protected override void Awake()
    {
        base.Awake();
    }

    private void OnEnable()
    {
        EnsureCommonReferences();
        EnsurePlaybackController();
        EnsureReArrangeButton();
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

    private void EnsureReArrangeButton()
    {
        if (btn_ReArrangeFurniture != null || !createReArrangeButtonAtRuntime || runtimeReArrangeButtonCreated)
        {
            return;
        }

        PressableButton source = btn_EndSimulation != null ? btn_EndSimulation : btn_ViewSimulation;
        if (source == null || source.transform.parent == null)
        {
            return;
        }

        PressableButton button = Instantiate(source, source.transform.parent);
        button.name = "ReArrangeFurnitureButton";
        btn_ReArrangeFurniture = button;
        runtimeReArrangeButtonCreated = true;

        RectTransform buttonRect = button.GetComponent<RectTransform>();
        RectTransform viewRect = btn_ViewSimulation != null ? btn_ViewSimulation.GetComponent<RectTransform>() : null;
        RectTransform endRect = btn_EndSimulation != null ? btn_EndSimulation.GetComponent<RectTransform>() : null;
        if (buttonRect != null)
        {
            if (viewRect != null && endRect != null)
            {
                buttonRect.localPosition = Vector3.Lerp(viewRect.localPosition, endRect.localPosition, 0.5f);
            }
            else
            {
                buttonRect.localPosition += new Vector3(0f, 240f, 0f);
            }
        }

        TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
        if (label != null)
        {
            label.text = "재배치 하기";
        }

        button.gameObject.SetActive(true);
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

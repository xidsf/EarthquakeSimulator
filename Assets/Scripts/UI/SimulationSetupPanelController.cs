using System.Collections;
using MixedReality.Toolkit.UX;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

public class SimulationSetupPanelController : WorkflowPanelControllerBase
{
    [Header("User Buttons")]
    public PressableButton btn_StartSimulation;
    public PressableButton btn_BackToFurniture;

    [Header("References")]
    public SimulationProcessManager simulationProcessManager;
    public bool autoFindSimulationProcessManager = true;
    public bool hideLegacyIntensityControls = true;

    [Header("System Message UI (로딩창)")]
    public GameObject panel_SystemMessage;
    public TMP_Text text_SystemMessage;

    private Coroutine simulationCoroutine;
    private readonly string simulationBaseText = "Simulation running";
    private readonly string resultLoadingText = "결과 창 로딩중";
    private string simulationStatusText = "Simulation running";

    private UnityAction startSimulationAction;
    private UnityAction backToFurnitureAction;

    protected override void Awake()
    {
        base.Awake();
        EnsureSimulationProcessManager();
    }

    private void OnEnable()
    {
        EnsureCommonReferences();
        EnsureSimulationProcessManager();
        CreateActions();
        RegisterButtons();
        SubscribeSimulationProcessManager();

        HideLegacyIntensityControls();
        SetButtonsActive(true);
        if (panel_SystemMessage != null) panel_SystemMessage.SetActive(false);
    }

    private void OnDisable()
    {
        UnsubscribeSimulationProcessManager();
        UnregisterButtons();
        StopSimulationAnimation();
    }

    private void CreateActions()
    {
        startSimulationAction ??= OnClickStartSimulation;
        backToFurnitureAction ??= OnClickBackToFurniture;
    }

    private void RegisterButtons()
    {
        AddClick(btn_StartSimulation, startSimulationAction);
        AddClick(btn_BackToFurniture, backToFurnitureAction);
    }

    private void UnregisterButtons()
    {
        RemoveClick(btn_StartSimulation, startSimulationAction);
        RemoveClick(btn_BackToFurniture, backToFurnitureAction);
    }

    private void SetButtonsActive(bool active)
    {
        if (btn_StartSimulation != null) btn_StartSimulation.gameObject.SetActive(active);
        if (btn_BackToFurniture != null) btn_BackToFurniture.gameObject.SetActive(active);
    }

    private void HideLegacyIntensityControls()
    {
        if (!hideLegacyIntensityControls)
        {
            return;
        }

        Transform[] children = GetComponentsInChildren<Transform>(true);
        foreach (Transform child in children)
        {
            if (child == null || child == transform)
            {
                continue;
            }

            string childName = child.name;
            if (string.Equals(childName, "intensitySettingButton", System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(childName, "SettingIntensity", System.StringComparison.OrdinalIgnoreCase))
            {
                child.gameObject.SetActive(false);
            }
        }
    }

    public void OnClickStartSimulation()
    {
        EnsureSimulationProcessManager();

        if (simulationProcessManager == null || !simulationProcessManager.TryStartSimulation())
        {
            if (panel_SystemMessage != null) panel_SystemMessage.SetActive(true);
            if (text_SystemMessage != null) text_SystemMessage.text = "Simulation start failed.";
        }
    }

    public void StartSimulationUI()
    {
        StopSimulationAnimation();
        simulationStatusText = string.IsNullOrWhiteSpace(simulationStatusText)
            ? simulationBaseText
            : simulationStatusText;
        if (panel_SystemMessage != null) panel_SystemMessage.SetActive(true);
        simulationCoroutine = StartCoroutine(AnimateSimulationText());
    }

    public void CompleteSimulationUI()
    {
        StopSimulationAnimation();
        simulationStatusText = resultLoadingText;
        if (panel_SystemMessage != null) panel_SystemMessage.SetActive(true);
        simulationCoroutine = StartCoroutine(HandleSimulationCompleteSequence());
    }

    private IEnumerator HandleSimulationCompleteSequence()
    {
        int dotCount = 1;
        float elapsed = 0f;
        float duration = 2.0f; // 결과 창 로딩중 문구를 보여줄 시간 (2초 후 닫힘)

        while (elapsed < duration)
        {
            if (text_SystemMessage != null)
            {
                text_SystemMessage.text = simulationStatusText + new string('.', dotCount);
            }

            dotCount++;
            if (dotCount > 3) dotCount = 1; // . -> .. -> ... -> . 사이클

            elapsed += 1.0f;
            yield return new WaitForSeconds(1.0f);
        }

        if (panel_SystemMessage != null) panel_SystemMessage.SetActive(false);
    }

    private void StopSimulationAnimation()
    {
        if (simulationCoroutine != null)
        {
            StopCoroutine(simulationCoroutine);
            simulationCoroutine = null;
        }
    }

    private IEnumerator AnimateSimulationText()
    {
        int dotCount = 1;
        while (true)
        {
            if (text_SystemMessage != null)
            {
                string baseText = string.IsNullOrWhiteSpace(simulationStatusText)
                    ? simulationBaseText
                    : simulationStatusText;
                text_SystemMessage.text = baseText + new string('.', dotCount);
            }

            dotCount++;
            if (dotCount > 3) dotCount = 1; // . -> .. -> ... -> . 사이클

            yield return new WaitForSeconds(1.0f); // 1초 단위 루프
        }
    }

    public void OnClickBackToFurniture()
    {
        RequestWorkflowCommand(RoomBuildWorkflowManager.WorkflowCommand.StartFurnitureRePlacement);
    }

    private void EnsureSimulationProcessManager()
    {
        if (simulationProcessManager != null || !autoFindSimulationProcessManager)
        {
            return;
        }

        SimulationProcessManager[] managers =
            FindObjectsByType<SimulationProcessManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        if (managers != null && managers.Length > 0)
        {
            simulationProcessManager = managers[0];
            return;
        }

        simulationProcessManager = gameObject.AddComponent<SimulationProcessManager>();
    }

    private void SubscribeSimulationProcessManager()
    {
        if (simulationProcessManager == null)
        {
            return;
        }

        simulationProcessManager.SimulationStarted += HandleSimulationStarted;
        simulationProcessManager.SimulationStatusChanged += HandleSimulationStatusChanged;
        simulationProcessManager.SimulationCompleted += HandleSimulationCompleted;
        simulationProcessManager.SimulationFailed += HandleSimulationFailed;
    }

    private void UnsubscribeSimulationProcessManager()
    {
        if (simulationProcessManager == null)
        {
            return;
        }

        simulationProcessManager.SimulationStarted -= HandleSimulationStarted;
        simulationProcessManager.SimulationStatusChanged -= HandleSimulationStatusChanged;
        simulationProcessManager.SimulationCompleted -= HandleSimulationCompleted;
        simulationProcessManager.SimulationFailed -= HandleSimulationFailed;
    }

    private void HandleSimulationStarted(string message)
    {
        simulationStatusText = string.IsNullOrWhiteSpace(message) ? simulationBaseText : message;
        SetButtonsActive(false);
        StartSimulationUI();
    }

    private void HandleSimulationStatusChanged(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        simulationStatusText = message;
        if (panel_SystemMessage != null) panel_SystemMessage.SetActive(true);
        if (text_SystemMessage != null) text_SystemMessage.text = simulationStatusText;
    }

    private void HandleSimulationCompleted(string message)
    {
        CompleteSimulationUI();
    }

    private void HandleSimulationFailed(string message)
    {
        StopSimulationAnimation();
        SetButtonsActive(true);
        if (panel_SystemMessage != null) panel_SystemMessage.SetActive(true);
        if (text_SystemMessage != null) text_SystemMessage.text = string.IsNullOrWhiteSpace(message) ? "Simulation failed." : message;
    }
}
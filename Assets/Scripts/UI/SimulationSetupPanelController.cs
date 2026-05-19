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

    // [수정됨] 내부 System Message UI 변수 및 텍스트 상태 변수 삭제 (UIManager 중앙 통제)

    private Coroutine delayCoroutine; // 결과 대기용 코루틴

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
    }

    private void OnDisable()
    {
        UnsubscribeSimulationProcessManager();
        UnregisterButtons();

        if (delayCoroutine != null)
        {
            StopCoroutine(delayCoroutine);
            delayCoroutine = null;
        }
        uiManager?.HideLoading(); // 패널 꺼질 때 안전하게 로딩창 끄기
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
            uiManager?.ShowWarningMessage("Simulation start failed."); // Warning 패널로 대체
        }
    }

    public void CompleteSimulationUI()
    {
        if (delayCoroutine != null) StopCoroutine(delayCoroutine);
        delayCoroutine = StartCoroutine(HandleSimulationCompleteSequence());
    }

    private IEnumerator HandleSimulationCompleteSequence()
    {
        // 결과 창 로딩중으로 텍스트 업데이트 (도움말 텍스트는 비움)
        uiManager?.UpdateLoadingText("결과 창 로딩중", "");

        yield return new WaitForSeconds(2.0f); // 2초 대기

        uiManager?.HideLoading(); // 로딩창 종료
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
        SetButtonsActive(false);
        string baseText = string.IsNullOrWhiteSpace(message) ? "시뮬레이션 실행중" : message;

        // [수정됨] 중앙 통제 로딩 UI 호출 (도움말은 비워둠)
        // 나중에 도움말 문구가 결정되면 두 번째 "" 안에 "여기에 도움말 입력" 형태로 작성하시면 됩니다.
        uiManager?.ShowLoading(baseText, "");
    }

    private void HandleSimulationStatusChanged(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        // [수정됨] 상태 텍스트 변경 이벤트 발생 시 로딩창 텍스트 업데이트
        uiManager?.UpdateLoadingText(message, "");
    }

    private void HandleSimulationCompleted(string message)
    {
        CompleteSimulationUI();
    }

    private void HandleSimulationFailed(string message)
    {
        SetButtonsActive(true);
        uiManager?.HideLoading(); // 에러 발생 시 로딩창 끄기
        uiManager?.ShowWarningMessage(string.IsNullOrWhiteSpace(message) ? "Simulation failed." : message); // Warning 패널로 에러 표시
    }
}
using System.Collections;
using MixedReality.Toolkit.UX;
using UnityEngine;
using UnityEngine.Events;
using TMPro;

public class SimulationSetupPanelController : WorkflowPanelControllerBase
{
    [Header("User Buttons")]
    public PressableButton btn_SetIntensity;
    public PressableButton btn_StartSimulation;
    public PressableButton btn_BackToFurniture;

    [Header("System Message UI")]
    public GameObject panel_SystemMessage;
    public TMP_Text text_SystemMessage;

    private Coroutine simulationCoroutine;
    private readonly string simulationBaseText = "시뮬레이션 실행 중";

    // UnityAction 캐싱
    private UnityAction setIntensityAction;
    private UnityAction startSimulationAction;
    private UnityAction backToFurnitureAction;

    protected override void Awake()
    {
        base.Awake();
    }

    private void OnEnable()
    {
        EnsureCommonReferences();
        CreateActions();
        RegisterButtons();

        // 단계 진입 시 버튼들을 다시 보이게 초기화
        SetButtonsActive(true);
        if (panel_SystemMessage != null) panel_SystemMessage.SetActive(false);
    }

    private void OnDisable()
    {
        UnregisterButtons();
        StopSimulationAnimation();
    }

    private void CreateActions()
    {
        setIntensityAction ??= OnClickSetIntensity;
        startSimulationAction ??= OnClickStartSimulation;
        backToFurnitureAction ??= OnClickBackToFurniture;
    }

    private void RegisterButtons()
    {
        AddClick(btn_SetIntensity, setIntensityAction);
        AddClick(btn_StartSimulation, startSimulationAction);
        AddClick(btn_BackToFurniture, backToFurnitureAction);
    }

    private void UnregisterButtons()
    {
        RemoveClick(btn_SetIntensity, setIntensityAction);
        RemoveClick(btn_StartSimulation, startSimulationAction);
        RemoveClick(btn_BackToFurniture, backToFurnitureAction);
    }

    // 버튼들의 활성화 상태를 한꺼번에 제어하는 함수
    private void SetButtonsActive(bool active)
    {
        if (btn_SetIntensity != null) btn_SetIntensity.gameObject.SetActive(active);
        if (btn_StartSimulation != null) btn_StartSimulation.gameObject.SetActive(active);
        if (btn_BackToFurniture != null) btn_BackToFurniture.gameObject.SetActive(active);
    }

    public void OnClickSetIntensity()
    {
        if (uiManager != null && uiManager.sub_IntensitySlider != null)
        {
            // [해결] 변수 대신 실제 오브젝트의 현재 활성화 상태를 반전시켜 전달합니다.
            // 이렇게 하면 슬라이더 안의 [완료] 버튼으로 껐더라도 상태가 꼬이지 않습니다.
            bool nextState = !uiManager.sub_IntensitySlider.activeSelf;
            uiManager.ToggleIntensitySlider(nextState);
        }
    }

    public void OnClickStartSimulation()
    {
        // 1. 우측 버튼 3개 숨기기
        SetButtonsActive(false);

        // 2. 시뮬레이션 중... 메시지 표시
        StartSimulationUI();

        // 3. 실제 시뮬레이션 명령 전달
        RequestWorkflowCommand(RoomBuildWorkflowManager.WorkflowCommand.StartRunSimulation);
    }

    public void StartSimulationUI()
    {
        StopSimulationAnimation();
        if (panel_SystemMessage != null) panel_SystemMessage.SetActive(true);
        simulationCoroutine = StartCoroutine(AnimateSimulationText());
    }

    public void CompleteSimulationUI()
    {
        StopSimulationAnimation();
        StartCoroutine(HandleSimulationCompleteSequence());
    }

    private IEnumerator HandleSimulationCompleteSequence()
    {
        if (text_SystemMessage != null) text_SystemMessage.text = "시뮬레이션 완료";
        yield return new WaitForSeconds(2.0f);

        if (panel_SystemMessage != null) panel_SystemMessage.SetActive(false);
        RequestWorkflowCommand(RoomBuildWorkflowManager.WorkflowCommand.CompleteRunSimulation);
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
                text_SystemMessage.text = simulationBaseText + new string('.', dotCount);
            dotCount = (dotCount % 3) + 1;
            yield return new WaitForSeconds(1.0f);
        }
    }

    public void OnClickBackToFurniture()
    {
        RequestWorkflowCommand(RoomBuildWorkflowManager.WorkflowCommand.StartFurnitureRePlacement);
    }
}
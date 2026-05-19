using System.Collections;
using MixedReality.Toolkit.UX;
using TMPro;
using UnityEngine;
using UnityEngine.Events; // UnityAction 사용을 위해 추가

// WorkflowPanelControllerBase를 상속받아 통일성을 맞춥니다.
public class SimulationResultUI : WorkflowPanelControllerBase
{
    [Header("UI Component References")]
    [SerializeField] private TMP_Text aiAdviceText;
    [SerializeField] private TMP_Text riskResultText;
    [SerializeField] private TMP_Text roomLevelText;
    [SerializeField] private TMP_Text fallHazardFurnituresList;

    [Header("User Buttons (추가됨)")]
    public PressableButton btn_ViewSimulation; // 시뮬레이션 보기 버튼
    public PressableButton btn_EndSimulation;  // 시뮬레이션 종료 버튼

    [Header("System Message UI (로딩창)")]
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
        // WorkflowPanelControllerBase의 AddClick 메서드 사용
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
        // ... (기존 UpdateUI 내용은 그대로 유지) ...
        if (response == null) return;
        if (aiAdviceText != null) aiAdviceText.text = string.IsNullOrEmpty(response.advice) ? "시뮬레이션 분석 결과가 없습니다." : response.advice;
        if (riskResultText != null) riskResultText.text = $"위험지수 (S): {Mathf.RoundToInt(response.risk_score)}";
        if (roomLevelText != null) roomLevelText.text = response.risk_level;
        if (fallHazardFurnituresList != null)
        {
            string listString = "낙하 위험 물건:\n";
            if (response.fallen != null && response.fallen.Length > 0)
            {
                foreach (string item in response.fallen) listString += $"- {item}\n";
            }
            else listString += "- 없음\n";
            fallHazardFurnituresList.text = listString;
        }
    }

    // --- 버튼 클릭 이벤트 및 로딩 코루틴 ---

    public void OnClickViewSimulation()
    {
        // 1. 버튼들 비활성화 (중복 클릭 방지)
        btn_ViewSimulation.enabled = false;
        btn_EndSimulation.enabled = false;

        // 2. 로딩창 켜고 애니메이션 시작
        if (panel_SystemMessage != null) panel_SystemMessage.SetActive(true);
        loadingCoroutine = StartCoroutine(AnimateLoadingText("시뮬레이션 로딩중"));

        // 3. 실제 시뮬레이션 재생 로직 호출 (예시)
        // workflowManager.ReplaySimulation(); 혹은 다른 매니저 호출
    }

    public void OnClickEndSimulation()
    {
        // 종료 로직 (필요시 WorkflowManager.RequestCommand 호출)
        RequestWorkflowCommand(RoomBuildWorkflowManager.WorkflowCommand.ReturnToRoomBuild); // 예시 커맨드
    }

    private void StopLoadingAnimation()
    {
        if (loadingCoroutine != null)
        {
            StopCoroutine(loadingCoroutine);
            loadingCoroutine = null;
        }
    }

    // . -> .. -> ... -> . 초 단위 루프 코루틴
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

            yield return new WaitForSeconds(1.0f); // 1초 단위
        }
    }
}
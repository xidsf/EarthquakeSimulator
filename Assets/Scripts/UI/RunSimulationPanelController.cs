using MixedReality.Toolkit.UX;
using UnityEngine;
using UnityEngine.Events;

public class RunSimulationPanelController : WorkflowPanelControllerBase
{
    [Header("Simulation Control Buttons")]
    [Tooltip("시뮬레이션을 시작하거나 일시정지 후 다시 재개합니다.")]
    public PressableButton btn_Start;

    [Tooltip("실행 중인 시뮬레이션을 일시정지합니다.")]
    public PressableButton btn_Stop;

    [Tooltip("시뮬레이션을 중단하고 처음 상태(가구 배치 직후)로 되돌립니다.")]
    public PressableButton btn_Reset;

    [Tooltip("시뮬레이션 결과를 바탕으로 가구 재배치 방안 단계로 이동합니다.")]
    public PressableButton btn_GoToRelocation;

    // UnityAction 캐싱
    private UnityAction startAction;
    private UnityAction stopAction;
    private UnityAction resetAction;
    private UnityAction goToRelocationAction;

    protected override void Awake()
    {
        base.Awake();
    }

    private void OnEnable()
    {
        EnsureCommonReferences();
        CreateActions();
        RegisterButtons();

        // 초기 버튼 상태 설정 (예: 시작 시에는 정지/재설정 버튼 활성화 등)
        if (btn_Start != null) btn_Start.enabled = true;
        if (btn_Stop != null) btn_Stop.enabled = true;
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
        goToRelocationAction ??= OnClick_GoToRelocation;
    }

    private void RegisterButtons()
    {
        AddClick(btn_Start, startAction);
        AddClick(btn_Stop, stopAction);
        AddClick(btn_Reset, resetAction);
        AddClick(btn_GoToRelocation, goToRelocationAction);
    }

    private void UnregisterButtons()
    {
        RemoveClick(btn_Start, startAction);
        RemoveClick(btn_Stop, stopAction);
        RemoveClick(btn_Reset, resetAction);
        RemoveClick(btn_GoToRelocation, goToRelocationAction);
    }

    // ==========================================
    // 버튼 이벤트 로직
    // ==========================================

    public void OnClick_Start()
    {
        // TODO: 지진 물리 연산(Rigidbody Shaking)을 시작하거나 재개하는 로직을 호출하세요.
        Debug.Log("[RunSimulation] 시뮬레이션 시작/재개");
    }

    public void OnClick_Stop()
    {
        // TODO: 현재 진행 중인 물리 연산을 일시정지 시키는 로직을 호출하세요.
        Debug.Log("[RunSimulation] 시뮬레이션 일시정지");
    }

    public void OnClick_Reset()
    {
        // 시뮬레이션을 초기화하기 위해 단계를 리셋하거나 처음 상태로 되돌립니다.
        // 필요 시 WorkflowCommand.ResetRoomBuild 등을 활용할 수 있습니다.
        Debug.Log("[RunSimulation] 시뮬레이션 처음부터 다시 시작");
    }

    public void OnClick_GoToRelocation()
    {
        // 시뮬레이션을 종료하고 재배치 방안(FurnitureRePlacement) 단계로 이동합니다.
        RequestWorkflowCommand(RoomBuildWorkflowManager.WorkflowCommand.StartFurnitureRePlacement);
    }
}
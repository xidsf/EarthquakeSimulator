using System.Collections;
using MixedReality.Toolkit.UX;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using TMPro;

public class ManualWallConfirmedPanelController : WorkflowPanelControllerBase
{
    public enum ValidationState
    {
        Checking,
        Success,
        Fail
    }

    [Header("UI References (MainPlate 하위)")]
    [Tooltip("SystemMessageToPlayer 패널 오브젝트를 연결해주세요.")]
    public GameObject panel_SystemMessage;

    [Tooltip("상태 문구를 띄워줄 TextMeshPro 컴포넌트를 연결해주세요.")]
    public TMP_Text text_SystemMessage;

    [Header("User Buttons (현재 오브젝트 하위)")]
    [Tooltip("수동 벽 생성 단계로 돌아가는 버튼입니다.")]
    public PressableButton btn_EditWallAgain;

    [Tooltip("방 캡처 단계로 넘어가는 확정 버튼입니다.")]
    public PressableButton btn_ProceedNext;

    // UnityAction 캐싱
    private UnityAction editWallAgainAction;
    private UnityAction proceedNextAction;

    // 코루틴 및 애니메이션 상태
    private Coroutine checkingCoroutine;
    private readonly string checkingBaseText = "방의 구조가\n닫혀있는지\n검사 중";

    // [추가됨] 현재 상태 추적용 변수 (디버깅용)
    private ValidationState currentValidationState;

    protected override void Awake()
    {
        base.Awake();
    }

    private void OnEnable()
    {
        EnsureCommonReferences();
        CreateActions();
        RegisterButtons();

        if (panel_SystemMessage != null) panel_SystemMessage.SetActive(true);

        // 패널 활성화 시 검사 상태로 진입
        SetValidationState(ValidationState.Checking);
    }

    private void OnDisable()
    {
        UnregisterButtons();
        StopCheckingAnimation();
    }

    // ==========================================
    // [추가됨] 디버깅용 키 입력 (G키)
    // ==========================================
    private void Update()
    {
        // MRTK3 (New Input System) 키보드 입력 체크 방식
        if (Keyboard.current != null && Keyboard.current.gKey.wasPressedThisFrame)
        {
            int nextStateIndex = ((int)currentValidationState + 1) % 3;
            SetValidationState((ValidationState)nextStateIndex);

            Debug.Log($"[Debug] 수동 벽 검증 상태 임시 변경: {(ValidationState)nextStateIndex}");
        }
    }

    private void CreateActions()
    {
        editWallAgainAction ??= OnClickEditWallAgain;
        proceedNextAction ??= OnClickProceedNext;
    }

    private void RegisterButtons()
    {
        AddClick(btn_EditWallAgain, editWallAgainAction);
        AddClick(btn_ProceedNext, proceedNextAction);
    }

    private void UnregisterButtons()
    {
        RemoveClick(btn_EditWallAgain, editWallAgainAction);
        RemoveClick(btn_ProceedNext, proceedNextAction);
    }

    // ==========================================
    // 검사 상태 변경 및 애니메이션 로직
    // ==========================================

    public void SetValidationState(ValidationState state)
    {
        StopCheckingAnimation();

        // [추가됨] 현재 상태 저장
        currentValidationState = state;

        switch (state)
        {
            case ValidationState.Checking:
                if (btn_ProceedNext != null) btn_ProceedNext.enabled = false;
                checkingCoroutine = StartCoroutine(AnimateCheckingText());
                break;

            case ValidationState.Success:
                if (btn_ProceedNext != null) btn_ProceedNext.enabled = true;
                if (text_SystemMessage != null) text_SystemMessage.text = "방 생성 성공";
                break;

            case ValidationState.Fail:
                if (btn_ProceedNext != null) btn_ProceedNext.enabled = false;
                if (text_SystemMessage != null) text_SystemMessage.text = "방 생성 실패";
                break;
        }
    }

    private void StopCheckingAnimation()
    {
        if (checkingCoroutine != null)
        {
            StopCoroutine(checkingCoroutine);
            checkingCoroutine = null;
        }
    }

    private IEnumerator AnimateCheckingText()
    {
        int dotCount = 1;

        while (true)
        {
            if (text_SystemMessage != null)
            {
                text_SystemMessage.text = checkingBaseText + new string('.', dotCount);
            }

            dotCount++;
            if (dotCount > 3) dotCount = 1;

            yield return new WaitForSeconds(1.0f);
        }
    }

    // ==========================================
    // Workflow 버튼 이벤트
    // ==========================================

    public void OnClickEditWallAgain()
    {
        RequestWorkflowCommand(RoomBuildWorkflowManager.WorkflowCommand.BackToManualWallGeneration);
    }

    public void OnClickProceedNext()
    {
        RequestWorkflowCommand(RoomBuildWorkflowManager.WorkflowCommand.ConfirmRoomAndStartRoomCapture);
    }
}
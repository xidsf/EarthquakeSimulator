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

    public PressableButton btn_SetEntrancePoint;
    public PressableButton btn_ClearEntrancePoint;
    public TMP_Text text_EntranceStatus;

    [Header("Confirm Room")]
    public ConfirmRoomManager confirmRoomManager;

    // UnityAction 캐싱
    private UnityAction editWallAgainAction;
    private UnityAction proceedNextAction;
    private UnityAction setEntrancePointAction;
    private UnityAction clearEntrancePointAction;

    // 코루틴 및 애니메이션 상태
    private Coroutine checkingCoroutine;
    private readonly string checkingBaseText = "방이 닫혀있는지 검사하는 중";

    // 현재 상태 추적용 변수 (디버깅용)
    private ValidationState currentValidationState;

    protected override void Awake()
    {
        base.Awake();
    }

    private void OnEnable()
    {
        EnsureCommonReferences();
        EnsureEntranceReferences();
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
    // 디버깅용 키 입력 (G키)
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

        RefreshEntranceStatus();
    }

    private void CreateActions()
    {
        editWallAgainAction ??= OnClickEditWallAgain;
        proceedNextAction ??= OnClickProceedNext;
        setEntrancePointAction ??= OnClickSetEntrancePoint;
        clearEntrancePointAction ??= OnClickClearEntrancePoint;
    }

    private void RegisterButtons()
    {
        AddClick(btn_EditWallAgain, editWallAgainAction);
        AddClick(btn_ProceedNext, proceedNextAction);
        AddClick(btn_SetEntrancePoint, setEntrancePointAction);
        AddClick(btn_ClearEntrancePoint, clearEntrancePointAction);
    }

    private void UnregisterButtons()
    {
        RemoveClick(btn_EditWallAgain, editWallAgainAction);
        RemoveClick(btn_ProceedNext, proceedNextAction);
        RemoveClick(btn_SetEntrancePoint, setEntrancePointAction);
        RemoveClick(btn_ClearEntrancePoint, clearEntrancePointAction);
    }

    private void EnsureEntranceReferences()
    {
        if (!autoFindReferences)
        {
            return;
        }

        if (confirmRoomManager == null && workflowManager != null)
        {
            confirmRoomManager = workflowManager.confirmRoomManager;
        }

        if (confirmRoomManager == null)
        {
            ConfirmRoomManager[] managers = FindObjectsByType<ConfirmRoomManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (managers != null && managers.Length > 0)
            {
                confirmRoomManager = managers[0];
            }
        }
    }

    // ==========================================
    // 검사 상태 변경 및 애니메이션 로직
    // ==========================================

    public void SetValidationState(ValidationState state)
    {
        StopCheckingAnimation();

        // 현재 상태 저장
        currentValidationState = state;

        switch (state)
        {
            case ValidationState.Checking:
                if (btn_ProceedNext != null) btn_ProceedNext.enabled = false;
                checkingCoroutine = StartCoroutine(AnimateCheckingText());
                break;

            case ValidationState.Success:
                if (text_SystemMessage != null) text_SystemMessage.text = "방 생성 성공";
                RefreshProceedButtonState();
                break;

            case ValidationState.Fail:
                if (btn_ProceedNext != null) btn_ProceedNext.enabled = false;
                if (text_SystemMessage != null) text_SystemMessage.text = "방 생성 실패";
                break;
        }

        RefreshEntranceStatus();
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
            if (dotCount > 3) dotCount = 1; // . -> .. -> ... -> . 사이클 구현

            yield return new WaitForSeconds(1.0f); // 초 단위 업데이트
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
        EnsureCommonReferences();
        EnsureEntranceReferences();

        if (confirmRoomManager != null && !confirmRoomManager.CanProceedWithEntrance(out string reason))
        {
            ShowWarning(reason);
            RefreshEntranceStatus();
            return;
        }

        RequestWorkflowCommand(RoomBuildWorkflowManager.WorkflowCommand.ConfirmRoomAndStartRoomCapture);
    }

    public void OnClickSetEntrancePoint()
    {
        EnsureCommonReferences();
        EnsureEntranceReferences();

        if (confirmRoomManager == null)
        {
            ShowWarning("ConfirmRoomManager가 연결되어 있지 않습니다.");
            return;
        }

        confirmRoomManager.BeginEntrancePlacementMode();
        RefreshEntranceStatus();
    }

    public void OnClickClearEntrancePoint()
    {
        EnsureCommonReferences();
        EnsureEntranceReferences();

        if (confirmRoomManager == null)
        {
            ShowWarning("ConfirmRoomManager가 연결되어 있지 않습니다.");
            return;
        }

        confirmRoomManager.ClearEntrance();
        RefreshEntranceStatus();
    }

    private void RefreshEntranceStatus()
    {
        EnsureEntranceReferences();

        string entrancePlacementBlockReason = string.Empty;
        bool canPlaceEntrance = confirmRoomManager != null &&
            confirmRoomManager.CanPlaceEntrance(out entrancePlacementBlockReason);

        if (btn_SetEntrancePoint != null)
        {
            btn_SetEntrancePoint.enabled = canPlaceEntrance;
        }

        if (btn_ClearEntrancePoint != null)
        {
            btn_ClearEntrancePoint.enabled = confirmRoomManager != null && confirmRoomManager.hasEntrance;
        }

        if (text_EntranceStatus != null)
        {
            if (confirmRoomManager == null)
            {
                text_EntranceStatus.text = "입구 위치 상태 확인 불가";
            }
            else if (!canPlaceEntrance)
            {
                text_EntranceStatus.text = entrancePlacementBlockReason;
            }
            else if (confirmRoomManager.entrancePlacementMode)
            {
                text_EntranceStatus.text = "바닥에서 입구 위치를 선택하세요";
            }
            else if (!confirmRoomManager.hasEntrance)
            {
                text_EntranceStatus.text = "입구 위치 미지정";
            }
            else
            {
                text_EntranceStatus.text = $"입구 위치 지정 완료 / 서버 판정 반경 {confirmRoomManager.entranceRadiusMeters:0.0}m";
            }
        }

        RefreshProceedButtonState();
    }

    private void RefreshProceedButtonState()
    {
        if (btn_ProceedNext == null)
        {
            return;
        }

        bool entranceOk = confirmRoomManager == null || confirmRoomManager.CanProceedWithEntrance(out _);
        bool roomOk = confirmRoomManager != null
            ? confirmRoomManager.roomReady && confirmRoomManager.finalRoomClosed
            : currentValidationState == ValidationState.Success;

        btn_ProceedNext.enabled = confirmRoomManager != null
            ? roomOk && entranceOk
            : currentValidationState == ValidationState.Success;
    }
}
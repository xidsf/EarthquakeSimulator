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

        // 패널 활성화 시 검사 상태로 진입
        SetValidationState(ValidationState.Checking);
    }

    private void OnDisable()
    {
        UnregisterButtons();
    }

    // ==========================================
    // 디버깅용 키 입력 (G키)
    // ==========================================
    private void Update()
    {
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
    // [수정됨] 검사 상태 변경 로직 (로딩플레이트 출력 삭제)
    // ==========================================

    public void SetValidationState(ValidationState state)
    {
        currentValidationState = state;

        switch (state)
        {
            case ValidationState.Checking:
                if (btn_ProceedNext != null) btn_ProceedNext.enabled = false;
                // 매우 빠르게 완료되므로 ShowLoading 코드를 호출하지 않고 즉시 검사 진행
                break;

            case ValidationState.Success:
                // HideLoading 코드 삭제
                uiManager?.ShowNotification("방 생성 성공");
                RefreshProceedButtonState();
                break;

            case ValidationState.Fail:
                // HideLoading 코드 삭제
                if (btn_ProceedNext != null) btn_ProceedNext.enabled = false;
                uiManager?.ShowWarningMessage("방 생성 실패");
                break;
        }

        RefreshEntranceStatus();
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
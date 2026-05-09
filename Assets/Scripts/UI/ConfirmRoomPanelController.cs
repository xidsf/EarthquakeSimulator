using MixedReality.Toolkit.UX;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

public class ConfirmRoomPanelController : WorkflowPanelControllerBase
{
    [Header("Confirm Room Buttons")]
    public PressableButton backToManualWallButton;
    public PressableButton confirmRoomButton;

    [Header("Debug Buttons - Optional")]
    [Tooltip("디버깅용 수동 재검사 버튼입니다. 실제 사용자 UI에는 노출하지 않는 것을 권장합니다.")]
    public PressableButton refreshValidationButton;

    [Header("Confirm Room Status UI")]
    public TMP_Text validationStatusText;
    public GameObject readyIndicatorObject;
    public GameObject blockedIndicatorObject;

    [Tooltip("true이면 방이 닫히지 않았을 때 Confirm 버튼 GameObject를 숨깁니다. false이면 버튼은 보이게 두고 PressableButton만 비활성화합니다.")]
    public bool hideConfirmButtonWhenInvalid = false;

    [Tooltip("디버깅용입니다. true이면 패널 OnEnable 때 재검사합니다. 사용자 플로우에서는 ConfirmRoom 상태 진입 시 WorkflowManager가 한 번만 검사하므로 false를 권장합니다.")]
    public bool refreshValidationOnEnable = false;

    [Tooltip("디버깅용입니다. 0보다 크면 패널이 켜져 있는 동안 주기적으로 재검사합니다. 사용자 플로우에서는 0을 권장합니다.")]
    public float autoRefreshIntervalSeconds = 0.0f;

    private UnityAction backToManualWallAction;
    private UnityAction confirmRoomAction;
    private UnityAction refreshValidationAction;
    private float nextAutoRefreshTime;

    private void OnEnable()
    {
        EnsureCommonReferences();
        CreateActions();
        RegisterButtons();

        if (refreshValidationOnEnable)
        {
            RefreshValidation();
        }
        else
        {
            ApplyCachedValidation();
        }
    }

    private void OnDisable()
    {
        UnregisterButtons();
    }

    private void Update()
    {
        if (autoRefreshIntervalSeconds <= 0.0f)
        {
            return;
        }

        if (Time.time < nextAutoRefreshTime)
        {
            return;
        }

        nextAutoRefreshTime = Time.time + autoRefreshIntervalSeconds;
        RefreshValidation();
    }

    private void CreateActions()
    {
        backToManualWallAction ??= OnClickBackToManualWall;
        confirmRoomAction ??= OnClickConfirmRoom;
        refreshValidationAction ??= RefreshValidation;
    }

    private void RegisterButtons()
    {
        AddClick(backToManualWallButton, backToManualWallAction);
        AddClick(confirmRoomButton, confirmRoomAction);
        AddClick(refreshValidationButton, refreshValidationAction);
    }

    private void UnregisterButtons()
    {
        RemoveClick(backToManualWallButton, backToManualWallAction);
        RemoveClick(confirmRoomButton, confirmRoomAction);
        RemoveClick(refreshValidationButton, refreshValidationAction);
    }

    public void RefreshValidation()
    {
        EnsureCommonReferences();

        if (workflowManager == null)
        {
            ShowWarning("WorkflowManager가 연결되어 있지 않습니다.");
            ApplyValidationUi(false, "WorkflowManager가 연결되어 있지 않습니다.");
            return;
        }

        bool ready = workflowManager.RefreshConfirmRoomValidation();
        ApplyValidationUi(ready, workflowManager.confirmRoomValidationStatus);
    }

    public void OnClickBackToManualWall()
    {
        RequestWorkflowCommand(RoomBuildWorkflowManager.WorkflowCommand.BackToManualWallGeneration);
    }

    public void OnClickConfirmRoom()
    {
        EnsureCommonReferences();

        if (workflowManager == null)
        {
            ShowWarning("WorkflowManager가 연결되어 있지 않습니다.");
            return;
        }

        if (!workflowManager.confirmRoomReady)
        {
            ApplyCachedValidation();
            ShowWarning(string.IsNullOrEmpty(workflowManager.confirmRoomValidationStatus)
                ? "방이 아직 확정 가능한 상태가 아닙니다."
                : workflowManager.confirmRoomValidationStatus);
            return;
        }

        RequestWorkflowCommand(RoomBuildWorkflowManager.WorkflowCommand.ConfirmRoomAndStartRoomCapture);
    }


    private void ApplyCachedValidation()
    {
        EnsureCommonReferences();

        if (workflowManager == null)
        {
            ApplyValidationUi(false, "WorkflowManager가 연결되어 있지 않습니다.");
            return;
        }

        ApplyValidationUi(
            workflowManager.confirmRoomReady,
            workflowManager.confirmRoomValidationStatus
        );
    }

    private void ApplyValidationUi(bool ready, string message)
    {
        if (validationStatusText != null)
        {
            validationStatusText.text = message;
        }

        if (readyIndicatorObject != null)
        {
            readyIndicatorObject.SetActive(ready);
        }

        if (blockedIndicatorObject != null)
        {
            blockedIndicatorObject.SetActive(!ready);
        }

        if (confirmRoomButton != null)
        {
            if (hideConfirmButtonWhenInvalid)
            {
                confirmRoomButton.gameObject.SetActive(ready);
            }
            else
            {
                if (!confirmRoomButton.gameObject.activeSelf)
                {
                    confirmRoomButton.gameObject.SetActive(true);
                }

                confirmRoomButton.enabled = ready;
            }
        }
    }
}

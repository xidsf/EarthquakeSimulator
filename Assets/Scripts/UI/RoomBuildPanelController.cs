using MixedReality.Toolkit.UX;
using UnityEngine;
using UnityEngine.Events;

public class RoomBuildPanelController : WorkflowPanelControllerBase
{
    [Header("RoomBuild References")]
    public SceneUnderstandingRoomScanner scanner;

    [Header("User Buttons")]
    [Tooltip("사용자용 방 고정 토글입니다. 방 고정 시 autoUpdate를 끄고 lock/stop scanning 처리합니다. 다시 누르면 unlock + autoUpdate + start scanning 처리합니다.")]
    public PressableButton toggleRoomFixedButton;

    [Tooltip("현재 RoomBuild 단계의 스캔 결과와 임시 데이터를 초기화하고 다시 스캔을 시작합니다.")]
    public PressableButton resetRoomBuildButton;

    [Tooltip("현재 스캔된 방을 기준으로 Manual Wall 생성 단계로 이동합니다.")]
    public PressableButton openManualWallButton;

    [Header("Debug Buttons - Optional")]
    [Tooltip("디버깅용 강제 Scene Understanding 갱신 버튼입니다. 실제 사용자 UI에는 노출하지 않는 것을 권장합니다.")]
    public PressableButton forceUpdateButton;
    public PressableButton startScanningButton;
    public PressableButton stopScanningButton;
    public PressableButton toggleAutoUpdateButton;
    public PressableButton lockRoomButton;
    public PressableButton unlockRoomButton;
    public PressableButton toggleLockRoomButton;

    [Tooltip("이전 버전 호환용입니다. 새 사용자 UI에서는 openManualWallButton을 사용하세요.")]
    public PressableButton completeRoomBuildButton;

    private UnityAction toggleRoomFixedAction;
    private UnityAction resetRoomBuildAction;
    private UnityAction openManualWallAction;

    private UnityAction forceUpdateAction;
    private UnityAction startScanningAction;
    private UnityAction stopScanningAction;
    private UnityAction toggleAutoUpdateAction;
    private UnityAction lockRoomAction;
    private UnityAction unlockRoomAction;
    private UnityAction toggleLockRoomAction;
    private UnityAction completeRoomBuildAction;

    protected override void Awake()
    {
        base.Awake();
        EnsureScannerReference();
    }

    private void OnEnable()
    {
        EnsureCommonReferences();
        EnsureScannerReference();
        CreateActions();
        RegisterButtons();
    }

    private void OnDisable()
    {
        UnregisterButtons();
    }

    private void EnsureScannerReference()
    {
        if (scanner != null || !autoFindReferences)
        {
            return;
        }

        if (workflowManager != null)
        {
            scanner = workflowManager.scanner;
        }

        if (scanner == null)
        {
            SceneUnderstandingRoomScanner[] scanners = FindObjectsByType<SceneUnderstandingRoomScanner>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (scanners != null && scanners.Length > 0)
            {
                scanner = scanners[0];
            }
        }
    }

    private void CreateActions()
    {
        toggleRoomFixedAction ??= OnClickToggleRoomFixed;
        resetRoomBuildAction ??= OnClickResetRoomBuild;
        openManualWallAction ??= OnClickOpenManualWall;

        forceUpdateAction ??= OnClickForceUpdate;
        startScanningAction ??= OnClickStartScanning;
        stopScanningAction ??= OnClickStopScanning;
        toggleAutoUpdateAction ??= OnClickToggleAutoUpdate;
        lockRoomAction ??= OnClickLockRoom;
        unlockRoomAction ??= OnClickUnlockRoom;
        toggleLockRoomAction ??= OnClickToggleLockRoom;
        completeRoomBuildAction ??= OnClickOpenManualWall;
    }

    private void RegisterButtons()
    {
        AddClick(toggleRoomFixedButton, toggleRoomFixedAction);
        AddClick(resetRoomBuildButton, resetRoomBuildAction);
        AddClick(openManualWallButton, openManualWallAction);

        // Debug / legacy listeners. 실제 사용자 UI에서는 이 버튼들을 연결하지 않아도 됩니다.
        AddClick(forceUpdateButton, forceUpdateAction);
        AddClick(startScanningButton, startScanningAction);
        AddClick(stopScanningButton, stopScanningAction);
        AddClick(toggleAutoUpdateButton, toggleAutoUpdateAction);
        AddClick(lockRoomButton, lockRoomAction);
        AddClick(unlockRoomButton, unlockRoomAction);
        AddClick(toggleLockRoomButton, toggleLockRoomAction);
        AddClick(completeRoomBuildButton, completeRoomBuildAction);
    }

    private void UnregisterButtons()
    {
        RemoveClick(toggleRoomFixedButton, toggleRoomFixedAction);
        RemoveClick(resetRoomBuildButton, resetRoomBuildAction);
        RemoveClick(openManualWallButton, openManualWallAction);

        RemoveClick(forceUpdateButton, forceUpdateAction);
        RemoveClick(startScanningButton, startScanningAction);
        RemoveClick(stopScanningButton, stopScanningAction);
        RemoveClick(toggleAutoUpdateButton, toggleAutoUpdateAction);
        RemoveClick(lockRoomButton, lockRoomAction);
        RemoveClick(unlockRoomButton, unlockRoomAction);
        RemoveClick(toggleLockRoomButton, toggleLockRoomAction);
        RemoveClick(completeRoomBuildButton, completeRoomBuildAction);
    }

    public void OnClickToggleRoomFixed()
    {
        EnsureScannerReference();
        if (scanner == null)
        {
            ShowWarning("SceneUnderstandingRoomScanner가 연결되어 있지 않습니다.");
            return;
        }

        if (scanner.lockRoom)
        {
            scanner.UnlockRoom();
            scanner.autoUpdate = true;
            scanner.StartScanning();
        }
        else
        {
            scanner.autoUpdate = false;
            scanner.StopScanning();
            scanner.LockRoom();
        }
    }

    public void OnClickResetRoomBuild()
    {
        RequestWorkflowCommand(RoomBuildWorkflowManager.WorkflowCommand.ResetRoomBuild);
    }

    public void OnClickOpenManualWall()
    {
        RequestWorkflowCommand(RoomBuildWorkflowManager.WorkflowCommand.SwitchToManualWallGeneration);
    }

    public void OnClickForceUpdate()
    {
        EnsureScannerReference();
        scanner?.ForceUpdate();
    }

    public void OnClickStartScanning()
    {
        EnsureScannerReference();
        if (scanner == null)
        {
            ShowWarning("SceneUnderstandingRoomScanner가 연결되어 있지 않습니다.");
            return;
        }

        scanner.autoUpdate = true;
        scanner.UnlockRoom();
        scanner.StartScanning();
    }

    public void OnClickStopScanning()
    {
        EnsureScannerReference();
        if (scanner == null)
        {
            return;
        }

        scanner.autoUpdate = false;
        scanner.StopScanning();
    }

    public void OnClickToggleAutoUpdate()
    {
        EnsureScannerReference();
        if (scanner == null)
        {
            ShowWarning("SceneUnderstandingRoomScanner가 연결되어 있지 않습니다.");
            return;
        }

        scanner.autoUpdate = !scanner.autoUpdate;

        if (scanner.autoUpdate)
        {
            scanner.StartScanning();
        }
        else
        {
            scanner.StopScanning();
        }
    }

    public void OnClickLockRoom()
    {
        EnsureScannerReference();
        if (scanner == null)
        {
            ShowWarning("SceneUnderstandingRoomScanner가 연결되어 있지 않습니다.");
            return;
        }

        scanner.autoUpdate = false;
        scanner.StopScanning();
        scanner.LockRoom();
    }

    public void OnClickUnlockRoom()
    {
        EnsureScannerReference();
        if (scanner == null)
        {
            ShowWarning("SceneUnderstandingRoomScanner가 연결되어 있지 않습니다.");
            return;
        }

        scanner.UnlockRoom();
        scanner.autoUpdate = true;
        scanner.StartScanning();
    }

    public void OnClickToggleLockRoom()
    {
        OnClickToggleRoomFixed();
    }
}

using MixedReality.Toolkit.UX;
using UnityEngine;
using UnityEngine.Events;
using TMPro; // [추가] TextMeshPro를 사용하기 위한 네임스페이스

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

    [Header("Text References")]
    [Tooltip("[추가] 토글 버튼 하위에 있는 텍스트 컴포넌트를 연결해주세요.")]
    public TMP_Text text_ToggleRoomFixed;

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

        // [추가] 패널이 켜질 때 현재 스캐너의 상태에 맞춰 텍스트를 초기화합니다.
        UpdateToggleRoomFixedText();
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

        // [추가] 토글 상태가 변경된 직후 텍스트를 갱신합니다.
        UpdateToggleRoomFixedText();
    }

    // [추가] 스캐너의 잠금 상태(lockRoom)를 확인하여 텍스트를 변경하는 로직
    private void UpdateToggleRoomFixedText()
    {
        if (text_ToggleRoomFixed != null && scanner != null)
        {
            // 방이 잠겨있다면(고정됨) 텍스트를 "On", 풀려있다면 "Off"로 표시합니다.
            // 만약 반대로 표시하고 싶으시다면 "On"과 "Off"의 위치를 바꿔주시면 됩니다.
            if (scanner.lockRoom)
            {
                text_ToggleRoomFixed.text = "현재 상태로 잠그기\nOn";
            }
            else
            {
                text_ToggleRoomFixed.text = "현재 상태로 잠그기\nOff";
            }
        }
    }

    // (이하 기존 함수들 동일)
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
        UpdateToggleRoomFixedText(); // 명시적 스캔 시작 시 텍스트 갱신
    }

    public void OnClickStopScanning()
    {
        EnsureScannerReference();
        if (scanner == null) return;

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

        if (scanner.autoUpdate) scanner.StartScanning();
        else scanner.StopScanning();
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
        UpdateToggleRoomFixedText(); // 명시적 잠금 시 텍스트 갱신
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
        UpdateToggleRoomFixedText(); // 명시적 잠금 해제 시 텍스트 갱신
    }

    public void OnClickToggleLockRoom()
    {
        OnClickToggleRoomFixed();
    }
}
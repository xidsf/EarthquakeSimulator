using MixedReality.Toolkit.UX;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// RoomCapture 단계에서 scan session 시작, 사진 촬영/업로드, 다음 단계 이동을 담당하는 패널 컨트롤러입니다.
///
/// 사용자 UI 권장:
/// - capturePhotoButton: 사진 촬영. 첫 CaptureAndUpload 촬영이면 자동으로 POST /scan-session/start 후 POST /scan-frame까지 수행
/// - nextToFurniturePlacementButton: 가구 추출 완료/도착 완료 검사를 건너뛰고 FurniturePlacement 단계로 이동
///
/// Debug UI 권장:
/// - startScanSessionButton: 수동으로 촬영 세션 시작, POST /scan-session/start
/// - captureAndUploadDebugButton: 이미 생성된 scan_session_id로 사진 촬영 + POST /scan-frame만 수행
///
/// 상태 표시는 별도 Debug.Log 수집 UI에서 처리하므로, 이 컨트롤러는 TMP_Text 기반 status UI를 관리하지 않습니다.
/// </summary>
public class FurnitureCapturePanelController : WorkflowPanelControllerBase
{
    public enum CaptureButtonMode
    {
        SaveOnly,
        CaptureAndUpload
    }

    [Header("Furniture Capture References")]
    public FurnitureCaptureManager furnitureCaptureManager;
    public bool autoFindFurnitureCaptureManager = true;

    [Header("Main Buttons")]
    [Tooltip("사용자용 사진 촬영 버튼입니다. CaptureAndUpload 모드에서 scan_session_id가 없으면 자동으로 session을 생성한 뒤 첫 frame을 촬영/업로드합니다.")]
    public PressableButton capturePhotoButton;

    [Tooltip("가구 인식 완료/도착 완료 검사를 건너뛰고 FurniturePlacement 단계로 이동하는 버튼입니다.")]
    public PressableButton nextToFurniturePlacementButton;

    [Header("Debug Buttons - Optional")]
    [Tooltip("디버그용 촬영 시작 버튼입니다. 서버에서 scan_session_id만 받아오고 사진은 촬영하지 않습니다.")]
    public PressableButton startScanSessionButton;

    public PressableButton toggleDebugPanelButton;
    public PressableButton bindRoomObjectsButton;
    public PressableButton setSaveOnlyModeButton;
    public PressableButton setCaptureAndUploadModeButton;
    public PressableButton captureSaveOnlyDebugButton;
    public PressableButton captureAndUploadDebugButton;
    public PressableButton uploadLatestSavedCaptureButton;
    public PressableButton uploadAllSavedCapturesButton;

    [Tooltip("Deprecated. Current server flow uses /scan-session/{scan_session_id}/finish.")]
    public PressableButton detectSessionButton;

    [Tooltip("Debug: GET /scan-job/{scan_session_id}")]
    public PressableButton getDetectionsButton;

    [Tooltip("Debug: POST /scan-session/{scan_session_id}/finish")]
    public PressableButton processScanButton;

    [Tooltip("디버그용: GET /result/{scan_session_id}")]
    public PressableButton getResultButton;

    [Tooltip("디버그용: 현재 scan session 상태를 초기화합니다.")]
    public PressableButton clearScanSessionButton;

    [Header("Debug UI Group")]
    public GameObject debugPanelObject;
    public bool showDebugPanelOnEnable = false;

    [Header("Capture Options")]
    [Tooltip("사용자 촬영 버튼의 기본 동작입니다. 서버 연동 테스트는 CaptureAndUpload를 사용하세요.")]
    public CaptureButtonMode captureButtonMode = CaptureButtonMode.CaptureAndUpload;

    [Tooltip("촬영 전에 ConfirmRoomManager에서 ConfirmedRoom_Floor/Ceiling/Wall 참조를 다시 등록합니다.")]
    public bool bindRoomObjectsBeforeCapture = true;

    [Tooltip("true이면 방 오브젝트 등록에 실패했을 때 촬영 요청을 막습니다.")]
    public bool requireRoomBindingBeforeCapture = true;

    [Tooltip("true이면 사용자용 사진 촬영 버튼에서 scan_session_id가 없을 때 자동으로 /scan-session/start를 먼저 호출한 뒤 촬영/업로드합니다.")]
    public bool autoStartScanSessionOnUserCapture = true;

    [Tooltip("autoStartScanSessionOnUserCapture가 false일 때만 의미가 있습니다. true이면 CaptureAndUpload 모드에서 scan_session_id가 없을 때 촬영/업로드를 막습니다.")]
    public bool requireScanSessionBeforeUploadCapture = true;

    [Header("Next Step Options")]
    [Tooltip("true이면 사용자가 최소 한 번 촬영 버튼을 눌러야 다음 단계로 넘어갑니다. 현재는 추출/도착 검사를 무시하기 위해 false를 권장합니다.")]
    public bool requireCaptureRequestBeforeNextStep = false;

    [Tooltip("true이면 패널 OnEnable 시 ConfirmedRoom_Floor/Ceiling/Wall 참조 등록을 시도합니다.")]
    public bool bindRoomObjectsOnEnable = true;

    private UnityAction startScanSessionAction;
    private UnityAction capturePhotoAction;
    private UnityAction nextToFurniturePlacementAction;
    private UnityAction toggleDebugPanelAction;
    private UnityAction bindRoomObjectsAction;
    private UnityAction setSaveOnlyModeAction;
    private UnityAction setCaptureAndUploadModeAction;
    private UnityAction captureSaveOnlyDebugAction;
    private UnityAction captureAndUploadDebugAction;
    private UnityAction uploadLatestSavedCaptureAction;
    private UnityAction uploadAllSavedCapturesAction;
    private UnityAction detectSessionAction;
    private UnityAction getDetectionsAction;
    private UnityAction processScanAction;
    private UnityAction getResultAction;
    private UnityAction clearScanSessionAction;

    private bool hasCaptureRequested;
    private bool lastRoomBindingSucceeded;

    private void OnEnable()
    {
        EnsurePanelReferences();
        CreateActions();
        RegisterButtons();

        SetDebugPanelVisible(showDebugPanelOnEnable);

        if (bindRoomObjectsOnEnable)
        {
            TryBindRoomObjects(showWarningOnFailure: false);
        }

        LogPanel("방 촬영 단계입니다. 사진 촬영 버튼을 누르면 필요한 경우 scan session을 자동 생성한 뒤 촬영/업로드합니다.");
    }

    private void OnDisable()
    {
        UnregisterButtons();
    }

    private void EnsurePanelReferences()
    {
        EnsureCommonReferences();

        if (!autoFindFurnitureCaptureManager)
            return;

        if (furnitureCaptureManager == null)
        {
            FurnitureCaptureManager[] managers = FindObjectsByType<FurnitureCaptureManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (managers != null && managers.Length > 0)
                furnitureCaptureManager = managers[0];
        }
    }

    private void CreateActions()
    {
        startScanSessionAction ??= OnClickStartScanSession;
        capturePhotoAction ??= OnClickCapturePhoto;
        nextToFurniturePlacementAction ??= OnClickNextToFurniturePlacement;
        toggleDebugPanelAction ??= OnClickToggleDebugPanel;
        bindRoomObjectsAction ??= OnClickBindRoomObjects;
        setSaveOnlyModeAction ??= OnClickSetSaveOnlyMode;
        setCaptureAndUploadModeAction ??= OnClickSetCaptureAndUploadMode;
        captureSaveOnlyDebugAction ??= OnClickCaptureSaveOnlyDebug;
        captureAndUploadDebugAction ??= OnClickCaptureAndUploadDebug;
        uploadLatestSavedCaptureAction ??= OnClickUploadLatestSavedCapture;
        uploadAllSavedCapturesAction ??= OnClickUploadAllSavedCaptures;
        detectSessionAction ??= OnClickDetectSession;
        getDetectionsAction ??= OnClickGetDetections;
        processScanAction ??= OnClickProcessScan;
        getResultAction ??= OnClickGetResult;
        clearScanSessionAction ??= OnClickClearScanSession;
    }

    private void RegisterButtons()
    {
        AddClick(startScanSessionButton, startScanSessionAction);
        AddClick(capturePhotoButton, capturePhotoAction);
        AddClick(nextToFurniturePlacementButton, nextToFurniturePlacementAction);

        AddClick(toggleDebugPanelButton, toggleDebugPanelAction);
        AddClick(bindRoomObjectsButton, bindRoomObjectsAction);
        AddClick(setSaveOnlyModeButton, setSaveOnlyModeAction);
        AddClick(setCaptureAndUploadModeButton, setCaptureAndUploadModeAction);
        AddClick(captureSaveOnlyDebugButton, captureSaveOnlyDebugAction);
        AddClick(captureAndUploadDebugButton, captureAndUploadDebugAction);
        AddClick(uploadLatestSavedCaptureButton, uploadLatestSavedCaptureAction);
        AddClick(uploadAllSavedCapturesButton, uploadAllSavedCapturesAction);
        AddClick(detectSessionButton, detectSessionAction);
        AddClick(getDetectionsButton, getDetectionsAction);
        AddClick(processScanButton, processScanAction);
        AddClick(getResultButton, getResultAction);
        AddClick(clearScanSessionButton, clearScanSessionAction);
    }

    private void UnregisterButtons()
    {
        RemoveClick(startScanSessionButton, startScanSessionAction);
        RemoveClick(capturePhotoButton, capturePhotoAction);
        RemoveClick(nextToFurniturePlacementButton, nextToFurniturePlacementAction);

        RemoveClick(toggleDebugPanelButton, toggleDebugPanelAction);
        RemoveClick(bindRoomObjectsButton, bindRoomObjectsAction);
        RemoveClick(setSaveOnlyModeButton, setSaveOnlyModeAction);
        RemoveClick(setCaptureAndUploadModeButton, setCaptureAndUploadModeAction);
        RemoveClick(captureSaveOnlyDebugButton, captureSaveOnlyDebugAction);
        RemoveClick(captureAndUploadDebugButton, captureAndUploadDebugAction);
        RemoveClick(uploadLatestSavedCaptureButton, uploadLatestSavedCaptureAction);
        RemoveClick(uploadAllSavedCapturesButton, uploadAllSavedCapturesAction);
        RemoveClick(detectSessionButton, detectSessionAction);
        RemoveClick(getDetectionsButton, getDetectionsAction);
        RemoveClick(processScanButton, processScanAction);
        RemoveClick(getResultButton, getResultAction);
        RemoveClick(clearScanSessionButton, clearScanSessionAction);
    }

    public void OnClickStartScanSession()
    {
        EnsurePanelReferences();

        if (!EnsureFurnitureCaptureManagerAvailable())
            return;

        furnitureCaptureManager.StartScanSession();
        hasCaptureRequested = false;
        LogPanel("Debug 촬영 시작: scan session 시작을 요청했습니다. 서버 응답 후 scan_session_id가 저장됩니다.");
    }

    public void OnClickCapturePhoto()
    {
        EnsurePanelReferences();

        if (!EnsureFurnitureCaptureManagerAvailable())
            return;

        if (bindRoomObjectsBeforeCapture)
        {
            bool bound = TryBindRoomObjects(showWarningOnFailure: requireRoomBindingBeforeCapture);
            if (!bound && requireRoomBindingBeforeCapture)
            {
                LogPanel("방 오브젝트 등록 실패로 촬영을 중단했습니다.");
                return;
            }
        }

        hasCaptureRequested = true;

        if (captureButtonMode == CaptureButtonMode.CaptureAndUpload)
        {
            if (!furnitureCaptureManager.HasActiveServerScanSession)
            {
                if (autoStartScanSessionOnUserCapture)
                {
                    furnitureCaptureManager.CapturePhotoAndUploadWithAutoSession();
                    LogPanel("scan_session_id가 없어 /scan-session/start를 먼저 요청합니다. 성공하면 자동으로 첫 사진을 촬영하고 /scan-frame으로 업로드합니다.");
                    return;
                }

                if (requireScanSessionBeforeUploadCapture)
                {
                    ShowWarning("서버 업로드 전에는 Debug 촬영 시작 버튼으로 scan_session_id를 받아야 합니다.");
                    LogPanel("scan_session_id가 없어 촬영 + 업로드를 중단했습니다.");
                    return;
                }
            }

            furnitureCaptureManager.CapturePhotoAndUpload();
            LogPanel("사진 촬영 + /scan-frame 업로드를 요청했습니다. 기존 scan session 내에서 frame_id만 자동 증가합니다.");
        }
        else
        {
            furnitureCaptureManager.CapturePhotoForDebug();
            LogPanel("사진 촬영 + 로컬 저장을 요청했습니다. 서버 업로드는 수행하지 않습니다.");
        }
    }

    public void OnClickNextToFurniturePlacement()
    {
        EnsurePanelReferences();

        if (requireCaptureRequestBeforeNextStep && !hasCaptureRequested)
        {
            ShowWarning("사진 촬영 요청이 아직 없습니다. 먼저 방 사진을 촬영하세요.");
            LogPanel("다음 단계 이동 차단: 사진 촬영 요청이 필요합니다.");
            return;
        }

        bool requested = RequestWorkflowCommand(RoomBuildWorkflowManager.WorkflowCommand.CompleteRoomCapture);

        if (requested)
            LogPanel("가구 인식 완료/도착 완료 검사를 건너뛰고 FurniturePlacement 단계로 이동했습니다.");
        else
            LogPanel("FurniturePlacement 단계 이동 요청이 실패했습니다. 현재 Workflow 상태가 RoomCapture인지 확인하세요.");
    }

    public void OnClickBindRoomObjects()
    {
        TryBindRoomObjects(showWarningOnFailure: true);
    }

    public void OnClickSetSaveOnlyMode()
    {
        captureButtonMode = CaptureButtonMode.SaveOnly;
        LogPanel("촬영 버튼 모드: 로컬 저장");
    }

    public void OnClickSetCaptureAndUploadMode()
    {
        captureButtonMode = CaptureButtonMode.CaptureAndUpload;
        LogPanel("촬영 버튼 모드: 저장 후 /scan-frame 서버 전송");
    }

    public void OnClickCaptureSaveOnlyDebug()
    {
        EnsurePanelReferences();

        if (!EnsureFurnitureCaptureManagerAvailable())
            return;

        if (bindRoomObjectsBeforeCapture)
        {
            bool bound = TryBindRoomObjects(showWarningOnFailure: requireRoomBindingBeforeCapture);
            if (!bound && requireRoomBindingBeforeCapture)
            {
                LogPanel("방 오브젝트 등록 실패로 디버그 로컬 저장 촬영을 중단했습니다.");
                return;
            }
        }

        hasCaptureRequested = true;
        furnitureCaptureManager.CapturePhotoForDebug();
        LogPanel("Debug 사진 촬영: 로컬 저장만 요청했습니다. 서버 session 자동 생성은 수행하지 않습니다.");
    }

    public void OnClickCaptureAndUploadDebug()
    {
        EnsurePanelReferences();

        if (!EnsureFurnitureCaptureManagerAvailable())
            return;

        if (bindRoomObjectsBeforeCapture)
        {
            bool bound = TryBindRoomObjects(showWarningOnFailure: requireRoomBindingBeforeCapture);
            if (!bound && requireRoomBindingBeforeCapture)
            {
                LogPanel("방 오브젝트 등록 실패로 디버그 촬영 + 업로드를 중단했습니다.");
                return;
            }
        }

        if (!furnitureCaptureManager.HasActiveServerScanSession)
        {
            ShowWarning("Debug 사진 촬영은 session을 자동 생성하지 않습니다. 먼저 Debug 촬영 시작 버튼을 눌러 scan_session_id를 생성하세요.");
            LogPanel("Debug 사진 촬영 중단: scan_session_id가 없습니다.");
            return;
        }

        hasCaptureRequested = true;
        furnitureCaptureManager.CapturePhotoAndUpload();
        LogPanel("Debug 사진 촬영: 기존 scan_session_id로 /scan-frame 업로드를 요청했습니다. 새 session은 만들지 않습니다.");
    }

    public void OnClickUploadLatestSavedCapture()
    {
        EnsurePanelReferences();

        if (!EnsureFurnitureCaptureManagerAvailable())
            return;

        furnitureCaptureManager.UploadLatestSavedCapture();
        LogPanel("최근 저장된 촬영 패키지의 /scan-frame 업로드를 요청했습니다.");
    }

    public void OnClickUploadAllSavedCaptures()
    {
        EnsurePanelReferences();

        if (!EnsureFurnitureCaptureManagerAvailable())
            return;

        furnitureCaptureManager.UploadAllSavedCaptures();
        LogPanel("저장된 모든 촬영 패키지의 /scan-frame 업로드를 요청했습니다.");
    }

    public void OnClickDetectSession()
    {
        EnsurePanelReferences();

        if (!EnsureFurnitureCaptureManagerAvailable())
            return;

        LogPanel("Deprecated button ignored. Current server flow uses POST /scan-session/{scan_session_id}/finish, not /detect-session.");
    }

    public void OnClickGetDetections()
    {
        EnsurePanelReferences();

        if (!EnsureFurnitureCaptureManagerAvailable())
            return;

        furnitureCaptureManager.GetScanJobForCurrentSession();
        LogPanel("GLB generation status requested: GET /scan-job/{scan_session_id}");
    }

    public void OnClickProcessScan()
    {
        EnsurePanelReferences();

        if (!EnsureFurnitureCaptureManagerAvailable())
            return;

        furnitureCaptureManager.FinishScanForCurrentSession();
        LogPanel("GLB generation requested: POST /scan-session/{scan_session_id}/finish");
    }

    public void OnClickGetResult()
    {
        EnsurePanelReferences();

        if (!EnsureFurnitureCaptureManagerAvailable())
            return;

        furnitureCaptureManager.GetResultForCurrentSession();
        LogPanel("Unity 배치 결과 조회 요청: GET /result/{scan_session_id}");
    }

    public void OnClickClearScanSession()
    {
        EnsurePanelReferences();

        if (!EnsureFurnitureCaptureManagerAvailable())
            return;

        furnitureCaptureManager.ClearScanSession();
        hasCaptureRequested = false;
        LogPanel("scan session 상태를 초기화했습니다.");
    }

    public void OnClickToggleDebugPanel()
    {
        if (debugPanelObject == null)
            return;

        SetDebugPanelVisible(!debugPanelObject.activeSelf);
    }

    public void SetDebugPanelVisible(bool visible)
    {
        if (debugPanelObject != null)
            debugPanelObject.SetActive(visible);
    }

    private bool TryBindRoomObjects(bool showWarningOnFailure)
    {
        EnsurePanelReferences();

        if (!EnsureFurnitureCaptureManagerAvailable())
        {
            lastRoomBindingSucceeded = false;
            LogPanel("방 오브젝트 등록 실패: FurnitureCaptureManager가 연결되어 있지 않습니다.");
            return false;
        }

        bool bound = furnitureCaptureManager.BindConfirmedRoomObjectsFromConfirmRoom();
        lastRoomBindingSucceeded = bound;

        string message = string.IsNullOrEmpty(furnitureCaptureManager.lastBindStatus)
            ? (bound ? "방 오브젝트 등록 성공" : "방 오브젝트 등록 실패")
            : furnitureCaptureManager.lastBindStatus;

        LogPanel(message);

        if (!bound && showWarningOnFailure)
            ShowWarning(message);

        return bound;
    }

    private bool EnsureFurnitureCaptureManagerAvailable()
    {
        if (furnitureCaptureManager != null)
            return true;

        ShowWarning("FurnitureCaptureManager가 연결되어 있지 않습니다.");
        return false;
    }

    private void LogPanel(string message)
    {
        Debug.Log($"[FurnitureCapturePanel] {message}");
    }
}

using System.Collections;
using MixedReality.Toolkit.UX;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 수정된 가구 배치 패널 컨트롤러입니다.
/// 주요 변경 사항:
/// 1. 수동 확정(Confirm) 버튼 제거 및 로직 삭제 (자동 확정 대응)
/// 2. 동일 가구의 다중 배치 허용 (배치 후 버튼 잠금 해제)
/// 3. 모든 가구를 배치해야 한다는 강제 조건 삭제 (사용자 자율성 부여)
/// 4. 컴파일 오류(T 형식, 삭제된 메서드 호출 등) 수정
/// </summary>
public class FurniturePlacementPanelController : WorkflowPanelControllerBase
{
    [Header("Furniture Placement References")]
    public FurniturePlacementManager furniturePlacementManager;
    public FurnitureMoveModeController moveModeController;
    public FurnitureSimulationStartController simulationStartController;
    public ConfirmedRoomGeometryProvider roomGeometryProvider;
    public FurnitureServerPlacementPipeline serverPlacementPipeline;
    [Tooltip("촬영 세션 정보를 읽어 가구 생성 버튼 활성화 조건을 판단합니다.")]
    public FurnitureCaptureManager furnitureCaptureManager;

    [Header("User Buttons")]
    public PressableButton userPlaceFurnitureFromServerButton;
    public PressableButton userSelectNextServerFurnitureButton;
    public PressableButton userSelectPreviousServerFurnitureButton;
    public PressableButton userPlaceSelectedServerFurnitureButton;
    public PressableButton userToggleMoveModeButton;
    public PressableButton userToggleRotateModeButton;
    public PressableButton userCompleteFurniturePlacementButton;

    [Header("Server Furniture List UI")]
    public TMP_Text userSelectedServerFurnitureText;
    public TMP_Text userServerFurnitureListText;
    public TMP_Text userServerFurnitureStatusText;
    public TMP_Text text_MoveMode;
    public TMP_Text text_RotateMode;
    public Transform userSelectedServerFurniturePreviewAnchor;
    public bool loadSelectedServerFurniturePreview = true;
    public bool suppressSelectedServerFurniturePreviewOnDevice = false;
    public float selectedServerFurniturePreviewScale = 0.03f;

    [Tooltip("true이면 리스트의 모든 가구를 최소 한 번씩 배치해야 완료 버튼이 활성화됩니다.")]
    public bool requireAllServerFurniturePlacedBeforeComplete = false;

    [Header("Server Furniture Request Guard")]
    [Min(1.0f)] public float serverFurnitureRequestTimeoutSeconds = 600.0f;

    [Header("Mode Text")]
    [SerializeField] private string moveModeLabel = "Move";
    [SerializeField] private string rotateModeLabel = "Rotate";
    [SerializeField] private string modeOnText = "On";
    [SerializeField] private string modeOffText = "Off";
    [SerializeField] private string moveModeStatusText = "이동 모드 활성화. 가구를 잡아 위치를 조정하세요.";
    [SerializeField] private string rotateModeStatusText = "회전 모드 활성화. 가구를 Y축 기준으로 회전시키세요.";

    [Header("Debug Buttons - Optional")]
    public PressableButton debugBindRoomGeometryButton;
    public PressableButton debugClearPlacedFurnitureButton;
    public PressableButton debugSelectNextFurnitureButton;
    public PressableButton debugSelectPreviousFurnitureButton;

    private UnityAction userPlaceFurnitureFromServerAction;
    private UnityAction userSelectNextServerFurnitureAction;
    private UnityAction userSelectPreviousServerFurnitureAction;
    private UnityAction userPlaceSelectedServerFurnitureAction;
    private UnityAction userToggleMoveModeAction;
    private UnityAction userToggleRotateModeAction;
    private UnityAction userCompleteFurniturePlacementAction;

    private UnityAction debugBindRoomGeometryAction;
    private UnityAction debugClearPlacedFurnitureAction;
    private UnityAction debugSelectNextFurnitureAction;
    private UnityAction debugSelectPreviousFurnitureAction;

    private FurnitureServerPlacementPipeline subscribedServerPlacementPipeline;
    private FurnitureMoveModeController subscribedMoveModeController;
    private GameObject selectedServerFurniturePreviewObject;
    private int selectedServerFurniturePreviewRequestId;
    private bool serverFurnitureRequestLocked;
    private float serverFurnitureRequestStartedRealtime = -1.0f;

    protected override void Awake()
    {
        base.Awake();
        EnsureFurniturePlacementReferences();
    }

    private void OnEnable()
    {
        EnsureCommonReferences();
        EnsureFurniturePlacementReferences();
        TryBindRoomGeometryFromWorkflow();
        CreateActions();
        RegisterButtons();
        SubscribeServerPlacementPipelineEvents();
        SubscribeMoveModeControllerEvents();
        RefreshServerFurnitureListView();
        UpdateManipulationModeTexts();
    }

    private void OnDisable()
    {
        UnsubscribeServerPlacementPipelineEvents();
        UnsubscribeMoveModeControllerEvents();
        UnregisterButtons();
        ClearSelectedServerFurniturePreview();
    }

    private void Update()
    {
        if (!serverFurnitureRequestLocked) return;

        if (HasServerFurnitureResponse())
        {
            ClearServerFurnitureRequestLock("server response received");
            RefreshServerFurnitureListView();
            return;
        }

        if (HasServerFurnitureRequestTimedOut())
        {
            if (serverPlacementPipeline != null && serverPlacementPipeline.IsRunning)
                serverPlacementPipeline.StopRunningFlow();

            ClearServerFurnitureRequestLock("request timed out");
            RefreshServerFurnitureListView();
        }
    }

    private void EnsureFurniturePlacementReferences()
    {
        if (!autoFindReferences) return;
        if (furniturePlacementManager == null) furniturePlacementManager = FindFirst<FurniturePlacementManager>();
        if (moveModeController == null) moveModeController = FindFirst<FurnitureMoveModeController>();
        if (simulationStartController == null) simulationStartController = FindFirst<FurnitureSimulationStartController>();
        if (roomGeometryProvider == null) roomGeometryProvider = FindFirst<ConfirmedRoomGeometryProvider>();
        if (serverPlacementPipeline == null) serverPlacementPipeline = FindFirst<FurnitureServerPlacementPipeline>();
        if (furnitureCaptureManager == null) furnitureCaptureManager = FindFirst<FurnitureCaptureManager>();
    }

    private void SubscribeServerPlacementPipelineEvents()
    {
        if (subscribedServerPlacementPipeline == serverPlacementPipeline) return;
        UnsubscribeServerPlacementPipelineEvents();
        if (serverPlacementPipeline == null) return;

        subscribedServerPlacementPipeline = serverPlacementPipeline;
        subscribedServerPlacementPipeline.ResultListChanged += OnServerResultListChanged;
        subscribedServerPlacementPipeline.PendingSelectionChanged += OnServerPendingSelectionChanged;
        subscribedServerPlacementPipeline.PendingObjectLoadedForPlacement += OnServerPendingObjectLoadedForPlacement;
        subscribedServerPlacementPipeline.PendingObjectConfirmed += OnServerPendingObjectConfirmed;
    }

    private void UnsubscribeServerPlacementPipelineEvents()
    {
        if (subscribedServerPlacementPipeline == null) return;
        subscribedServerPlacementPipeline.ResultListChanged -= OnServerResultListChanged;
        subscribedServerPlacementPipeline.PendingSelectionChanged -= OnServerPendingSelectionChanged;
        subscribedServerPlacementPipeline.PendingObjectLoadedForPlacement -= OnServerPendingObjectLoadedForPlacement;
        subscribedServerPlacementPipeline.PendingObjectConfirmed -= OnServerPendingObjectConfirmed;
        subscribedServerPlacementPipeline = null;
    }

    private void SubscribeMoveModeControllerEvents()
    {
        if (subscribedMoveModeController == moveModeController) return;
        UnsubscribeMoveModeControllerEvents();
        if (moveModeController == null) return;

        subscribedMoveModeController = moveModeController;
        subscribedMoveModeController.MoveModeChanged += OnMoveModeChanged;
        subscribedMoveModeController.SelectionChanged += OnMoveModeSelectionChanged;
    }

    private void UnsubscribeMoveModeControllerEvents()
    {
        if (subscribedMoveModeController == null) return;
        subscribedMoveModeController.MoveModeChanged -= OnMoveModeChanged;
        subscribedMoveModeController.SelectionChanged -= OnMoveModeSelectionChanged;
        subscribedMoveModeController = null;
    }

    private void CreateActions()
    {
        userPlaceFurnitureFromServerAction ??= OnClickPlaceFurnitureFromServer;
        userSelectNextServerFurnitureAction ??= OnClickSelectNextServerFurniture;
        userSelectPreviousServerFurnitureAction ??= OnClickSelectPreviousServerFurniture;
        userPlaceSelectedServerFurnitureAction ??= OnClickPlaceSelectedServerFurniture;
        userToggleMoveModeAction ??= OnClickToggleMoveMode;
        userToggleRotateModeAction ??= OnClickToggleRotateMode;
        userCompleteFurniturePlacementAction ??= OnClickCompleteFurniturePlacement;

        debugBindRoomGeometryAction ??= OnClickDebugBindRoomGeometry;
        debugClearPlacedFurnitureAction ??= OnClickDebugClearPlacedFurniture;
        debugSelectNextFurnitureAction ??= OnClickDebugSelectNextFurniture;
        debugSelectPreviousFurnitureAction ??= OnClickDebugSelectPreviousFurniture;
    }

    private void RegisterButtons()
    {
        AddClick(userPlaceFurnitureFromServerButton, userPlaceFurnitureFromServerAction);
        AddClick(userSelectNextServerFurnitureButton, userSelectNextServerFurnitureAction);
        AddClick(userSelectPreviousServerFurnitureButton, userSelectPreviousServerFurnitureAction);
        AddClick(userPlaceSelectedServerFurnitureButton, userPlaceSelectedServerFurnitureAction);
        AddClick(userToggleMoveModeButton, userToggleMoveModeAction);
        AddClick(userToggleRotateModeButton, userToggleRotateModeAction);
        AddClick(userCompleteFurniturePlacementButton, userCompleteFurniturePlacementAction);

        AddClick(debugBindRoomGeometryButton, debugBindRoomGeometryAction);
        AddClick(debugClearPlacedFurnitureButton, debugClearPlacedFurnitureAction);
        AddClick(debugSelectNextFurnitureButton, debugSelectNextFurnitureAction);
        AddClick(debugSelectPreviousFurnitureButton, debugSelectPreviousFurnitureAction);
    }

    private void UnregisterButtons()
    {
        RemoveClick(userPlaceFurnitureFromServerButton, userPlaceFurnitureFromServerAction);
        RemoveClick(userSelectNextServerFurnitureButton, userSelectNextServerFurnitureAction);
        RemoveClick(userSelectPreviousServerFurnitureButton, userSelectPreviousServerFurnitureAction);
        RemoveClick(userPlaceSelectedServerFurnitureButton, userPlaceSelectedServerFurnitureAction);
        RemoveClick(userToggleMoveModeButton, userToggleMoveModeAction);
        RemoveClick(userToggleRotateModeButton, userToggleRotateModeAction);
        RemoveClick(userCompleteFurniturePlacementButton, userCompleteFurniturePlacementAction);

        RemoveClick(debugBindRoomGeometryButton, debugBindRoomGeometryAction);
        RemoveClick(debugClearPlacedFurnitureButton, debugClearPlacedFurnitureAction);
        RemoveClick(debugSelectNextFurnitureButton, debugSelectNextFurnitureAction);
        RemoveClick(debugSelectPreviousFurnitureButton, debugSelectPreviousFurnitureAction);
    }

    private void OnServerResultListChanged(FurnitureServerPlacementPipeline pipeline)
    {
        if (HasServerFurnitureResponse()) ClearServerFurnitureRequestLock("server response received");
        RefreshServerFurnitureListView();
    }

    private void OnServerPendingSelectionChanged(int index, FurnitureServerResultObject selectedObject)
    {
        RefreshServerFurnitureListView();
        UpdateSelectedServerFurniturePreview(selectedObject);
    }

    private void OnServerPendingObjectLoadedForPlacement(int index, FurnitureServerResultObject selectedObject, GameObject placedObject)
    {
        RefreshServerFurnitureListView();
        SetServerFurnitureStatus($"{BuildFurnitureName(selectedObject, index)}의 위치를 조정하세요.");
    }

    private void OnServerPendingObjectConfirmed(int index, FurnitureServerResultObject selectedObject)
    {
        RefreshServerFurnitureListView();
    }

    private void OnMoveModeChanged(bool enabled)
    {
        UpdateManipulationModeTexts();
        RefreshServerFurnitureListView();
    }

    private void OnMoveModeSelectionChanged(PlacedFurniture selectedFurniture)
    {
        UpdateManipulationModeTexts();
        RefreshServerFurnitureListView();
    }

    private void RefreshServerFurnitureListView()
    {
        EnsureFurniturePlacementReferences();
        RefreshFurnitureActionButtonState();
        UpdateManipulationModeTexts();

        if (serverPlacementPipeline == null || serverPlacementPipeline.PendingObjectCount == 0)
        {
            SetSelectedFurnitureText("불러온 가구가 없습니다.");
            SetFurnitureListText(string.Empty);
            SetServerFurnitureStatus(IsServerFurnitureRequestLocked()
                ? BuildServerFurnitureRequestLockStatus()
                : serverPlacementPipeline != null ? serverPlacementPipeline.LastStatus : string.Empty);
            ClearSelectedServerFurniturePreview();
            return;
        }

        int selectedIndex = serverPlacementPipeline.SelectedPendingObjectIndex;
        FurnitureServerResultObject selectedObject = serverPlacementPipeline.SelectedPendingObject;
        int count = serverPlacementPipeline.PendingObjectCount;
        int placed = serverPlacementPipeline.GetPlacedPendingObjectCount();

        if (selectedObject != null)
        {
            string selectedLine = $"{selectedIndex + 1}/{count}  {BuildFurnitureName(selectedObject, selectedIndex)}";
            SetSelectedFurnitureText(selectedLine);
        }
        else
        {
            SetSelectedFurnitureText($"가구 리스트 준비됨. 배치됨:{placed}/{count}");
        }

        string listText = string.Empty;
        for (int i = 0; i < count; i++)
        {
            FurnitureServerResultObject obj = serverPlacementPipeline.GetPendingObject(i);
            string marker = i == selectedIndex ? "> " : "  ";
            // 자동 확정이므로 배치 여부만 표시 (다중 배치를 위해 [배치됨]으로 유지)
            string state = serverPlacementPipeline.IsPendingObjectLoadedForPlacement(i) ? " [배치됨]" : "";
            listText += $"{marker}{i + 1}. {BuildFurnitureName(obj, i)}{state}\n";
        }

        SetFurnitureListText(listText.TrimEnd());
        SetServerFurnitureStatus(BuildCurrentPlacementStatus(placed, count));
    }

    private void RefreshFurnitureActionButtonState()
    {
        bool isRunning = serverPlacementPipeline != null && serverPlacementPipeline.IsRunning;
        int count = serverPlacementPipeline != null ? serverPlacementPipeline.PendingObjectCount : 0;
        bool hasServerFurniture = count > 0;
        bool hasSelectedFurniture = hasServerFurniture && serverPlacementPipeline.SelectedPendingObject != null;

        // 💡 [수정] 다중 배치를 위해 이미 배치되었는지 체크하는 로직을 제거했습니다.
        bool allPlaced = serverPlacementPipeline != null && serverPlacementPipeline.AreAllPendingObjectsPlaced();
        bool hasPlacedSelection = moveModeController != null && moveModeController.SelectedFurniture != null;

        SetButtonVisible(userPlaceFurnitureFromServerButton, !hasServerFurniture);
        SetButtonEnabled(userPlaceFurnitureFromServerButton, !isRunning && !IsServerFurnitureRequestLocked());
        SetButtonEnabled(userSelectNextServerFurnitureButton, hasServerFurniture && !isRunning);
        SetButtonEnabled(userSelectPreviousServerFurnitureButton, hasServerFurniture && !isRunning);

        // 💡 [수정] 선택한 가구가 있다면 언제든지 다시 배치 버튼을 누를 수 있습니다.
        SetButtonEnabled(userPlaceSelectedServerFurnitureButton, hasSelectedFurniture && !isRunning);

        SetButtonVisible(userToggleMoveModeButton, hasPlacedSelection && !isRunning);
        SetButtonVisible(userToggleRotateModeButton, hasPlacedSelection && !isRunning);

        // 💡 [수정] 모든 가구 배치 강제 조건을 풀었습니다. 가구 리스트만 있으면 완료 가능합니다.
        bool canComplete = hasServerFurniture && !isRunning;
        if (requireAllServerFurniturePlacedBeforeComplete)
        {
            canComplete = canComplete && allPlaced;
        }

        SetButtonEnabled(userCompleteFurniturePlacementButton, canComplete);
    }

    private void UpdateManipulationModeTexts()
    {
        FurnitureManipulationMode mode = moveModeController != null ? moveModeController.CurrentMode : FurnitureManipulationMode.None;
        if (text_MoveMode != null) text_MoveMode.text = BuildModeText(moveModeLabel, mode == FurnitureManipulationMode.Move);
        if (text_RotateMode != null) text_RotateMode.text = BuildModeText(rotateModeLabel, mode == FurnitureManipulationMode.Rotate);
    }

    private string BuildModeText(string label, bool isOn) => label + "\n" + (isOn ? modeOnText : modeOffText);

    private string BuildCurrentPlacementStatus(int placed, int count)
    {
        if (moveModeController != null)
        {
            if (moveModeController.CurrentMode == FurnitureManipulationMode.Move) return moveModeStatusText;
            if (moveModeController.CurrentMode == FurnitureManipulationMode.Rotate) return rotateModeStatusText;
        }

        if (serverPlacementPipeline != null && serverPlacementPipeline.AreAllPendingObjectsPlaced())
            return "모든 가구가 배치되었습니다. 완료 버튼을 눌러 시뮬레이션을 시작하세요.";

        return $"가구를 선택하고 배치 버튼을 누르세요. ({placed}/{count})";
    }

    // --- Server Request & Preview Logic ---

    private bool IsServerFurnitureRequestLocked() => serverFurnitureRequestLocked && !HasServerFurnitureResponse() && !HasServerFurnitureRequestTimedOut();
    private bool HasServerFurnitureResponse() => serverPlacementPipeline != null && serverPlacementPipeline.LastResult != null;
    private bool HasServerFurnitureRequestTimedOut() => serverFurnitureRequestLocked && serverFurnitureRequestStartedRealtime >= 0 && (Time.realtimeSinceStartup - serverFurnitureRequestStartedRealtime >= serverFurnitureRequestTimeoutSeconds);
    private void BeginServerFurnitureRequestLock() { serverFurnitureRequestLocked = true; serverFurnitureRequestStartedRealtime = Time.realtimeSinceStartup; }
    private void ClearServerFurnitureRequestLock(string reason) { serverFurnitureRequestLocked = false; serverFurnitureRequestStartedRealtime = -1f; }
    private string BuildServerFurnitureRequestLockStatus() => $"서버에서 가구를 생성 중입니다. ({Mathf.CeilToInt(serverFurnitureRequestTimeoutSeconds - (Time.realtimeSinceStartup - serverFurnitureRequestStartedRealtime))}초 남음)";

    private void UpdateSelectedServerFurniturePreview(FurnitureServerResultObject selectedObject)
    {
        ClearSelectedServerFurniturePreview();
        if (!loadSelectedServerFurniturePreview || selectedObject == null || userSelectedServerFurniturePreviewAnchor == null) return;

#if UNITY_WSA && !UNITY_EDITOR
        if (suppressSelectedServerFurniturePreviewOnDevice) return;
#endif
        StartCoroutine(LoadSelectedServerFurniturePreviewRoutine(selectedObject, ++selectedServerFurniturePreviewRequestId));
    }

    private IEnumerator LoadSelectedServerFurniturePreviewRoutine(FurnitureServerResultObject selectedObject, int requestId)
    {
        FurnitureServerMeshLoader meshLoader = FindFirst<FurnitureServerMeshLoader>();
        if (meshLoader == null) yield break;

        GameObject preview = null;
        yield return meshLoader.LoadFurnitureObject(selectedObject, (obj, err) => preview = obj);

        if (preview == null || requestId != selectedServerFurniturePreviewRequestId || userSelectedServerFurniturePreviewAnchor == null)
        {
            if (preview != null) Destroy(preview);
            yield break;
        }

        selectedServerFurniturePreviewObject = preview;
        preview.transform.SetParent(userSelectedServerFurniturePreviewAnchor, false);
        preview.transform.localScale = Vector3.one * selectedServerFurniturePreviewScale;
        foreach (var col in preview.GetComponentsInChildren<Collider>(true)) col.enabled = false;
        foreach (var rb in preview.GetComponentsInChildren<Rigidbody>(true)) { rb.isKinematic = true; rb.useGravity = false; }
    }

    private void ClearSelectedServerFurniturePreview()
    {
        selectedServerFurniturePreviewRequestId++;
        if (selectedServerFurniturePreviewObject != null) { Destroy(selectedServerFurniturePreviewObject); selectedServerFurniturePreviewObject = null; }
    }

    private void SetSelectedFurnitureText(string v) { if (userSelectedServerFurnitureText != null) userSelectedServerFurnitureText.text = v; }
    private void SetFurnitureListText(string v) { if (userServerFurnitureListText != null) userServerFurnitureListText.text = v; }
    private void SetServerFurnitureStatus(string v) { if (userServerFurnitureStatusText != null) userServerFurnitureStatusText.text = v; }
    private static string BuildFurnitureName(FurnitureServerResultObject obj, int index) => (obj == null || string.IsNullOrWhiteSpace(obj.label)) ? $"가구 {index + 1}" : obj.label;

    // --- Click Actions ---

    public void OnClickPlaceFurnitureFromServer()
    {
        if (serverPlacementPipeline == null || IsServerFurnitureRequestLocked()) return;
        uiManager?.ShowNotification("서버에서 가구 데이터를 가져오는 중입니다...");
        serverPlacementPipeline.StartPlacementFromCurrentScanSession();
        if (serverPlacementPipeline.IsRunning) BeginServerFurnitureRequestLock();
        RefreshServerFurnitureListView();
    }

    public void OnClickSelectNextServerFurniture() { serverPlacementPipeline?.SelectNextPendingObject(); RefreshServerFurnitureListView(); }
    public void OnClickSelectPreviousServerFurniture() { serverPlacementPipeline?.SelectPreviousPendingObject(); RefreshServerFurnitureListView(); }
    public void OnClickPlaceSelectedServerFurniture() { serverPlacementPipeline?.PlaceSelectedPendingObject(); RefreshServerFurnitureListView(); }

    public void OnClickToggleMoveMode()
    {
        if (moveModeController == null) return;
        moveModeController.ToggleMoveMode();
        UpdateManipulationModeTexts();
        RefreshServerFurnitureListView();
    }

    public void OnClickToggleRotateMode()
    {
        if (moveModeController == null) return;
        moveModeController.ToggleRotateMode();
        UpdateManipulationModeTexts();
        RefreshServerFurnitureListView();
    }

    public void OnClickCompleteFurniturePlacement()
    {
        // 💡 [수정] 강제 배치 조건 로직을 제거하거나 설정값에 따르도록 변경했습니다.
        if (requireAllServerFurniturePlacedBeforeComplete && serverPlacementPipeline != null && !serverPlacementPipeline.AreAllPendingObjectsPlaced())
        {
            uiManager?.ShowNotification("모든 서버 가구를 배치해야 합니다.");
            return;
        }

        // 현재 모드 해제
        moveModeController?.SetManipulationMode(FurnitureManipulationMode.None);

        // 시뮬레이션 전환 시도 (자동 확정 로직이므로 별도의 확정 호출 없이 진행)
        if (simulationStartController != null && simulationStartController.TryStartSimulation())
        {
            Debug.Log("[FurniturePlacementPanelController] Simulation sequence started.");
            return;
        }

        RequestWorkflowCommand(RoomBuildWorkflowManager.WorkflowCommand.CompleteFurniturePlacement);
    }

    // --- Debug Actions ---
    public void OnClickDebugBindRoomGeometry() => roomGeometryProvider?.BindFromConfirmRoomManager(workflowManager?.confirmRoomManager);
    public void OnClickDebugClearPlacedFurniture() => furniturePlacementManager?.ClearPlacedFurniture();
    public void OnClickDebugSelectNextFurniture() => moveModeController?.SelectNext();
    public void OnClickDebugSelectPreviousFurniture() => moveModeController?.SelectPrevious();

    private void TryBindRoomGeometryFromWorkflow()
    {
        if (roomGeometryProvider == null || roomGeometryProvider.IsInitialized) return;
        var workflow = workflowManager ?? uiManager?.GetWorkflowManager();
        if (workflow?.confirmRoomManager != null) roomGeometryProvider.BindFromConfirmRoomManager(workflow.confirmRoomManager);
    }

    // 💡 [수정] T 형식 오류 해결을 위해 void와 명확한 UnityEngine.Object 사용
    private static void SetButtonEnabled(PressableButton b, bool e) { if (b != null) b.enabled = e; }
    private static void SetButtonVisible(PressableButton b, bool v) { if (b != null) { b.gameObject.SetActive(v); b.enabled = v; } }
    private static T FindFirst<T>() where T : UnityEngine.Object { T[] objs = FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None); return objs != null && objs.Length > 0 ? objs[0] : null; }
}
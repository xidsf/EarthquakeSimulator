using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class RoomBuildWorkflowManager : MonoBehaviour
{
    public enum WorkflowState
    {
        RoomInfoInput,
        RoomBuild,
        ManualWallGenerate,
        ManualWallConfirmed,
        RoomCapture,
        FurniturePlacement,
        SimulationProcess,
        SimulationSuccess,
        RunSimulation,
        FurnitureRePlacement
    }

    public enum WorkflowCommand
    {
        CompleteRoomInfoInput,
        SwitchToManualWallGeneration,
        ResetRoomBuild,
        ReturnToRoomBuild,
        ConfirmManualWalls,
        BackToManualWallGeneration,
        ConfirmRoomAndStartRoomCapture,
        CompleteRoomCapture,
        CompleteFurniturePlacement,
        CompleteSimulationProcess,
        SimulationSucceeded,
        SimulationFailed,
        StartRunSimulation,
        CompleteRunSimulation,
        StartFurnitureRePlacement,
        CompleteFurnitureRePlacement,
        ReturnToRoomCapture
    }


    [Header("References")]
    public SceneUnderstandingRoomScanner scanner;
    public ManualWallBuilder manualWallBuilder;

    [Tooltip("Confirm Room 단계의 방 닫힘 검증, 열린 gap marker 표시, 최종 ConfirmedRoomRoot 생성을 담당합니다.")]
    public ConfirmRoomManager confirmRoomManager;

    [Tooltip("confirmRoomManager가 비어 있으면 런타임에 같은 GameObject에 자동 추가합니다.")]
    public bool autoCreateConfirmRoomManager = true;

    [Tooltip("RoomCapture 단계에서 ConfirmedRoomRoot를 촬영/업로드 metadata에 등록하는 관리자입니다.")]
    public FurnitureCaptureManager furnitureCaptureManager;

    [Tooltip("furnitureCaptureManager가 비어 있으면 런타임에 같은 GameObject에 자동 추가합니다.")]
    public bool autoCreateFurnitureCaptureManager = true;

    [Header("Startup / Build Safety")]
    [Tooltip("빌드 실행 시 Scene에 serialize된 currentState 값과 무관하게 항상 방 정보 입력 UI로 시작합니다.")]
    public bool forceRoomInfoInputStateOnStartup = true;

    [Tooltip("이전 버전 호환용입니다. forceRoomInfoInputStateOnStartup이 켜져 있으면 이 값보다 방 정보 입력 단계가 우선됩니다.")]
    public bool forceRoomBuildStateOnStartup = false;

    [Tooltip("RoomBuild 진입 시 scanner lock을 풀고 scanning을 시작합니다.")]
    public bool startScannerWhenInitializingRoomBuild = true;

    [Tooltip("빌드 환경에서 다른 Start 초기화가 UI를 다시 바꾸는 경우를 막기 위해 다음 프레임에 RoomBuild UI를 한 번 더 적용합니다.")]
    public bool reapplyInitialRoomBuildStateNextFrame = true;

    [Tooltip("초기화 생명주기 로그를 Unity 로그로 출력합니다. 빌드에서 WorkflowManager가 실행되는지 확인할 때 사용합니다.")]
    public bool logWorkflowLifecycle = true;

    [Header("UI")]
    [Tooltip("Workflow의 상태 전환 결과를 화면에 표시하고 버튼 입력을 Workflow에 전달하는 UIManager입니다.")]
    public UIManager uiManager;

    [Tooltip("uiManager가 비어 있으면 런타임에 Scene에서 자동 탐색합니다.")]
    public bool autoFindUiManager = true;

    [Header("Switch Conditions")]
    public bool autoLockRoomWhenSwitchingToManual = true;
    public bool stopScanningWhenSwitchingToManual = true;
    public bool rebuildSelectableSurfacesWhenSwitching = true;

    [Tooltip("ManualWallGenerate 상태에서 SceneUnderstanding raw/detected mesh가 비동기 갱신으로 다시 나타나도 계속 숨깁니다.")]
    public bool keepAutoRoomVisualsHiddenWhileManual = true;

    public bool clearManualWallsWhenReturningToRoomBuild = true;

    [Tooltip("Confirm Room 상태에서 기존 raw mesh / wall segmentation 표시를 숨깁니다.")]
    public bool hideAutoRoomVisualsInConfirmRoom = true;

    [Tooltip("Confirm Room 상태에서 사용자가 직접 만든 Manual Wall 원본 표시와 collider를 숨깁니다. 최종 방은 ConfirmedRoomRoot로만 표시됩니다.")]
    public bool hideManualWallVisualsInConfirmRoom = true;

    [Tooltip("RoomCapture 단계부터 사진에 ConfirmedRoomRoot 시각화가 찍히지 않도록 Renderer를 숨깁니다. metadata 참조는 유지됩니다.")]
    public bool hideConfirmedRoomRenderersInRoomCapture = true;

    [Tooltip("Confirm Room에서 Manual Wall로 돌아갈 때 기존 raw mesh / wall segmentation 표시를 다시 켭니다.")]
    public bool restoreAutoRoomVisualsWhenBackToManual = true;

    [Header("Debug")]
    public WorkflowState currentState = WorkflowState.RoomInfoInput;
    public bool finalRoomClosed;
    public bool floodReachedOutside;
    public int selectedCellCount;
    public int confirmedBoundarySegmentCount;
    public int manualWallCount;
    public int openEndpointCount = -1;

    [Header("Confirm Room Review")]
    [Tooltip("Confirm Room 패널에서 현재 자동 벽 + 수동 벽이 닫힌 방인지 표시하는 값입니다.")]
    public bool confirmRoomReady;

    [Tooltip("Confirm Room 패널에서 표시할 마지막 검증 메시지입니다.")]
    public string confirmRoomValidationStatus;

    public string status;

    private GameObject confirmedRoomFloorObject;
    private GameObject confirmedRoomCeilingObject;
    private readonly List<GameObject> confirmedRoomWallObjects = new List<GameObject>();

    private readonly Dictionary<WorkflowState, WorkflowStateHandler> workflowStateHandlers =
        new Dictionary<WorkflowState, WorkflowStateHandler>();

    private WorkflowStateHandler activeWorkflowStateHandler;

    public GameObject ConfirmedRoomFloorObject => confirmRoomManager != null ? confirmRoomManager.ConfirmedRoomFloorObject : confirmedRoomFloorObject;
    public GameObject ConfirmedRoomCeilingObject => confirmRoomManager != null ? confirmRoomManager.ConfirmedRoomCeilingObject : confirmedRoomCeilingObject;
    public IReadOnlyList<GameObject> ConfirmedRoomWallObjects => confirmRoomManager != null ? confirmRoomManager.ConfirmedRoomWallObjects : confirmedRoomWallObjects;


    private void Awake()
    {
        if (logWorkflowLifecycle)
        {
            Debug.Log($"[Workflow] Awake. serialized currentState:{currentState}");
        }

        EnsureConfirmRoomManagerReference();
        EnsureFurnitureCaptureManagerReference();
        EnsureUiManagerReference();
        EnsureWorkflowStateHandlers();
    }

    private void OnEnable()
    {
        if (logWorkflowLifecycle)
        {
            Debug.Log($"[Workflow] OnEnable. serialized currentState:{currentState}");
        }

        EnsureUiManagerReference();
        EnsureWorkflowStateHandlers();

        if (forceRoomInfoInputStateOnStartup)
        {
            InitializeRoomInfoInputStartupState("OnEnable");
        }
        else if (forceRoomBuildStateOnStartup)
        {
            InitializeRoomBuildStartupState("OnEnable");
        }
        else
        {
            ApplyState(currentState);
        }
    }

    private void Start()
    {
        if (!forceRoomInfoInputStateOnStartup && !forceRoomBuildStateOnStartup)
        {
            return;
        }

        if (forceRoomInfoInputStateOnStartup)
        {
            InitializeRoomInfoInputStartupState("Start");
        }
        else
        {
            InitializeRoomBuildStartupState("Start");
        }

        if (reapplyInitialRoomBuildStateNextFrame)
        {
            StartCoroutine(ReapplyInitialWorkflowStateAfterOneFrame());
        }
    }

    private IEnumerator ReapplyInitialWorkflowStateAfterOneFrame()
    {
        yield return null;

        if (forceRoomInfoInputStateOnStartup && currentState == WorkflowState.RoomInfoInput)
        {
            InitializeRoomInfoInputStartupState("Start+1Frame");
            yield break;
        }

        if (forceRoomBuildStateOnStartup && currentState == WorkflowState.RoomBuild)
        {
            InitializeRoomBuildStartupState("Start+1Frame");
        }
    }

    private void InitializeRoomInfoInputStartupState(string source)
    {
        if (logWorkflowLifecycle)
        {
            Debug.Log($"[Workflow] InitializeRoomInfoInputStartupState from {source}.");
        }

        currentState = WorkflowState.RoomInfoInput;
        finalRoomClosed = false;
        floodReachedOutside = false;
        selectedCellCount = 0;
        confirmedBoundarySegmentCount = 0;
        openEndpointCount = -1;
        manualWallCount = CountManualWalls();

        if (manualWallBuilder != null)
        {
            manualWallBuilder.SetNoneMode();
            manualWallBuilder.ClearSelectableSurfaces();
        }

        ClearConfirmedRoomVisualization();
        SetAutoRoomVisualsActive(false);
        SetManualWallVisualsActive(false);

        if (scanner != null)
        {
            scanner.workflowManager = this;
            scanner.UnlockRoom();
            scanner.StopScanning();
        }

        ResetConfirmRoomReviewState();

        status = $"Workflow initialized to RoomInfoInput by {source}.";
        ApplyState(WorkflowState.RoomInfoInput);
    }

    private void InitializeRoomBuildStartupState(string source)
    {
        if (logWorkflowLifecycle)
        {
            Debug.Log($"[Workflow] InitializeRoomBuildStartupState from {source}.");
        }

        currentState = WorkflowState.RoomBuild;
        finalRoomClosed = false;
        floodReachedOutside = false;
        selectedCellCount = 0;
        confirmedBoundarySegmentCount = 0;
        openEndpointCount = -1;
        manualWallCount = CountManualWalls();

        if (manualWallBuilder != null)
        {
            manualWallBuilder.SetNoneMode();
            manualWallBuilder.ClearSelectableSurfaces();
        }

        ClearConfirmedRoomVisualization();
        SetAutoRoomVisualsActive(true);
        SetManualWallVisualsActive(true);

        if (scanner != null)
        {
            scanner.workflowManager = this;
            scanner.UnlockRoom();

            if (startScannerWhenInitializingRoomBuild)
            {
                scanner.StartScanning();
            }
        }

        ResetConfirmRoomReviewState();

        status = $"Workflow initialized to RoomBuild by {source}.";
        ApplyState(WorkflowState.RoomBuild);
    }

    private void LateUpdate()
    {
        // Scene Understanding ComputeAsync가 ManualWallGenerate 진입 직후 늦게 끝나면
        // Raw Scene Understanding Objects / Reconstructed Room이 다시 생성될 수 있습니다.
        // Manual 단계에서는 상호작용 가능한 selectable surface만 보여야 하므로 매 프레임 방어적으로 숨깁니다.
        if (currentState == WorkflowState.ManualWallGenerate && keepAutoRoomVisualsHiddenWhileManual)
        {
            SetAutoRoomVisualsActive(false);
        }

        // Confirm Room 검토 단계부터는 ConfirmRoomManager가 ConfirmedRoomRoot 아래에 만든
        // 검토/확정용 벽만 보여줍니다. 기존 자동 벽 시각화가 보이면 ManualWall 단계의 자르기 결과가
        // 반영되지 않은 것처럼 보일 수 있으므로 숨깁니다.
        if (IsConfirmedRoomOrLaterState(currentState) && hideAutoRoomVisualsInConfirmRoom)
        {
            SetAutoRoomVisualsActive(false);
        }
    }

    public void CompleteRoomInfoInput()
    {
        RequestWorkflowState(WorkflowState.RoomBuild, "Room information completed. Room build ready.");
    }


    public void ResetRoomBuild()
    {
        if (manualWallBuilder != null)
        {
            manualWallBuilder.SetNoneMode();
            manualWallBuilder.ClearSelectableSurfaces();
            manualWallBuilder.ClearManualWalls();
        }

        ClearConfirmedRoomVisualization();
        ResetConfirmRoomReviewState();
        EnsureFurnitureCaptureManagerReference();
        furnitureCaptureManager?.ClearRoomObjectBindings();

        finalRoomClosed = false;
        floodReachedOutside = false;
        selectedCellCount = 0;
        confirmedBoundarySegmentCount = 0;
        openEndpointCount = -1;
        manualWallCount = CountManualWalls();

        SetManualWallVisualsActive(true);
        SetAutoRoomVisualsActive(true);

        if (scanner != null)
        {
            scanner.ResetRoomScanState(restartScanning: startScannerWhenInitializingRoomBuild);
        }

        SetStatus("RoomBuild reset. Scan state and temporary room data were cleared.");
        ApplyState(WorkflowState.RoomBuild);
    }

    public void SwitchToManualWallGeneration()
    {
        if (scanner == null)
        {
            SetStatus("Cannot switch to Manual Wall. Scanner is null.");
            return;
        }

        if (!scanner.CanUseCurrentRoomForManualWall(out string scannerReason))
        {
            SetStatus(scannerReason);
            return;
        }

        if (manualWallBuilder != null &&
            !manualWallBuilder.CanEnterManualWallWorkflow(out string builderReason))
        {
            SetStatus(builderReason);
            return;
        }

        if (autoLockRoomWhenSwitchingToManual)
        {
            scanner.LockRoom();
        }

        if (!scanner.lockRoom)
        {
            SetStatus("Room must be locked before Manual Wall generation.");
            return;
        }

        if (stopScanningWhenSwitchingToManual)
        {
            scanner.StopScanning();
        }

        ClearConfirmedRoomVisualization();
        // ManualWallGenerate 단계에서는 raw Scene Understanding mesh / 자동 벽 cube를 숨기고,
        // 상호작용 가능한 selectable surface와 사용자가 만든 Manual Wall만 표시합니다.
        SetAutoRoomVisualsActive(false);
        SetManualWallVisualsActive(true);

        if (manualWallBuilder != null)
        {
            manualWallBuilder.SetNoneMode();

            if (rebuildSelectableSurfacesWhenSwitching)
            {
                manualWallBuilder.RebuildSelectableSurfaces();
            }
        }

        // RebuildSelectableSurfaces 직후 또는 아직 진행 중이던 SU 갱신 직후 자동 mesh가 다시 보이는 것을 방지합니다.
        SetAutoRoomVisualsActive(false);

        finalRoomClosed = false;
        floodReachedOutside = false;
        selectedCellCount = 0;
        confirmedBoundarySegmentCount = 0;
        openEndpointCount = -1;
        manualWallCount = CountManualWalls();

        SetStatus("Switched to Manual Wall generation.");
        ApplyState(WorkflowState.ManualWallGenerate);
    }

    public void ReturnToRoomBuild()
    {
        if (manualWallBuilder != null)
        {
            manualWallBuilder.SetNoneMode();
            manualWallBuilder.ClearSelectableSurfaces();

            if (clearManualWallsWhenReturningToRoomBuild)
            {
                manualWallBuilder.ClearManualWalls();
            }
        }

        ClearConfirmedRoomVisualization();
        SetAutoRoomVisualsActive(true);
        SetManualWallVisualsActive(true);

        if (scanner != null)
        {
            scanner.UnlockRoom();
        }

        finalRoomClosed = false;
        floodReachedOutside = false;
        selectedCellCount = 0;
        confirmedBoundarySegmentCount = 0;
        openEndpointCount = -1;
        manualWallCount = CountManualWalls();

        SetStatus("Returned to RoomBuild. Manual edit state cleared.");
        ApplyState(WorkflowState.RoomBuild);
    }

    public void ConfirmManualWalls()
    {
        OpenConfirmRoomReview();
    }

    public void OpenConfirmRoomReview()
    {
        if (scanner == null)
        {
            SetStatus("Cannot open Confirm Room. Scanner is null.");
            return;
        }

        if (!scanner.CanUseCurrentRoomForConfirm(out string scannerReason))
        {
            SetStatus(scannerReason);
            return;
        }

        if (manualWallBuilder != null &&
            !manualWallBuilder.CanConfirmManualWallWorkflow(out string builderReason))
        {
            SetStatus(builderReason);
            return;
        }

        ClearConfirmedRoomVisualization();

        if (manualWallBuilder != null)
        {
            manualWallBuilder.SetNoneMode();
            manualWallBuilder.ClearSelectableSurfaces();
        }

        // Confirm Room에서는 자동 생성 시각화와 ManualWall 원본을 직접 보여주지 않고,
        // ConfirmRoomManager가 현재 segment 데이터로 다시 만든 ConfirmedRoomRoot 미리보기를 보여줍니다.
        SetAutoRoomVisualsActive(false);
        SetManualWallVisualsActive(false);

        manualWallCount = CountManualWalls();

        SetStatus("Confirm Room review opened.");
        ApplyState(WorkflowState.ManualWallConfirmed);
    }

    public bool RefreshConfirmRoomValidation()
    {
        EnsureConfirmRoomManagerReference();

        bool ready = confirmRoomManager != null && confirmRoomManager.ValidateRoom();

        // 검증 성공/실패와 별개로 Confirm Room 단계에서는 현재 segment 기반 방 미리보기를 생성합니다.
        // 이 미리보기가 실제 사용자에게 보이는 벽이며, 자동 생성된 원본 시각화가 아닙니다.
        if (confirmRoomManager != null)
        {
            confirmRoomManager.BuildReviewRoomVisualization();
        }

        SyncConfirmRoomManagerState();

        SetStatus(confirmRoomValidationStatus);
        UpdateStatusText();
        return ready;
    }

    public void BackToManualWallGeneration()
    {
        if (currentState != WorkflowState.ManualWallConfirmed)
        {
            SetStatus("Back to Manual Wall is only available from Confirm Room state.");
            return;
        }

        ClearConfirmedRoomVisualization();
        ResetConfirmRoomReviewState();

        if (restoreAutoRoomVisualsWhenBackToManual)
        {
            // ManualWallGenerate로 돌아올 때도 사용자에게는 selectable surface만 보여줍니다.
            SetAutoRoomVisualsActive(false);
        }

        SetManualWallVisualsActive(true);

        if (manualWallBuilder != null)
        {
            manualWallBuilder.SetNoneMode();
            manualWallBuilder.RebuildSelectableSurfaces();
        }

        finalRoomClosed = false;
        floodReachedOutside = false;
        selectedCellCount = 0;
        confirmedBoundarySegmentCount = 0;
        openEndpointCount = -1;
        manualWallCount = CountManualWalls();

        SetStatus("Returned to Manual Wall generation. Existing manual walls are kept.");
        ApplyState(WorkflowState.ManualWallGenerate);
    }

    // 기존 UI 연결 호환을 위해 메서드 이름은 유지합니다.
    // 새 Workflow에서는 Confirm Room 이후 FurnitureRecognition이 아니라 RoomCapture 단계로 이동합니다.
    public void ConfirmRoomAndStartFurnitureRecognitionPlaceholder()
    {
        ConfirmRoomAndStartRoomCapture();
    }

    public void ConfirmRoomAndStartRoomCapture()
    {
        if (currentState != WorkflowState.ManualWallConfirmed)
        {
            SetStatus("Confirm Room blocked. Open Confirm Room panel first.");
            return;
        }

        if (!confirmRoomReady)
        {
            SetStatus(string.IsNullOrEmpty(confirmRoomValidationStatus)
                ? "Confirm Room blocked. Room validation is not ready."
                : confirmRoomValidationStatus);
            return;
        }

        ClearConfirmedRoomObjectsOnly();

        if (!BuildConfirmedRoomVisualization())
        {
            confirmRoomReady = false;
            finalRoomClosed = false;
            SetStatus("Confirm Room blocked. Final room visualization construction failed. Check open endpoints or disconnected manual walls.");
            return;
        }

        ClearConfirmRoomGapMarkers();

        if (hideManualWallVisualsInConfirmRoom)
        {
            SetManualWallVisualsActive(false);
        }

        if (hideAutoRoomVisualsInConfirmRoom)
        {
            SetAutoRoomVisualsActive(false);
        }

        DisableNonConfirmedRoomBuildGeometry();

        EnsureFurnitureCaptureManagerReference();

        bool boundRoomObjects = true;
        if (furnitureCaptureManager != null && furnitureCaptureManager.bindConfirmedRoomObjectsOnRoomConfirmed)
        {
            boundRoomObjects = furnitureCaptureManager.BindConfirmedRoomObjectsFromConfirmRoom();
        }

        ApplyState(WorkflowState.RoomCapture);

        if (furnitureCaptureManager != null && furnitureCaptureManager.bindConfirmedRoomObjectsOnRoomConfirmed)
        {
            SetStatus(
                boundRoomObjects
                    ? $"Room capture ready. Room objects bound. walls:{furnitureCaptureManager.lastBoundWallCount}"
                    : $"Room capture ready, but room objects were not bound. {furnitureCaptureManager.lastBindStatus}"
            );
        }
        else
        {
            SetStatus("Room capture ready. FurnitureCaptureManager binding is disabled or missing.");
        }
    }

    public void CompleteRoomCapture()
    {
        RequestWorkflowState(WorkflowState.FurniturePlacement, "Room capture completed. Furniture placement ready.");
    }

    public void CompleteFurniturePlacement()
    {
        RequestWorkflowState(WorkflowState.SimulationProcess, "Furniture placement completed. Simulation process ready.");
    }

    // 시뮬레이션 결과 수신 이벤트가 아직 연결되지 않은 테스트/호환용 메서드입니다.
    // 실제 서비스 흐름에서는 사용자 버튼이 아니라 OnSimulationResultReceived(...)에서 자동으로 호출됩니다.
    public void CompleteSimulationProcess()
    {
        OnSimulationResultReceived(true);
    }

    public void OnSimulationResultReceived(bool success)
    {
        if (currentState != WorkflowState.SimulationProcess)
        {
            SetStatus("Simulation result ignored. Current state is not SimulationProcess.");
            return;
        }

        if (success)
        {
            RequestWorkflowState(WorkflowState.SimulationSuccess, "Simulation result received. Simulation success state ready.");
        }
        else
        {
            SetStatus("Simulation result received, but simulation failed. Failure UI is not implemented yet.");
        }
    }

    public void StartRunSimulation()
    {
        RequestWorkflowState(WorkflowState.RunSimulation, "Run simulation started.");
    }

    public void CompleteRunSimulation()
    {
        RequestWorkflowState(WorkflowState.SimulationSuccess, "Simulation run completed successfully.");
    }

    public void StartFurnitureRePlacement()
    {
        RequestWorkflowState(WorkflowState.FurnitureRePlacement, "Furniture replacement mode ready.");
    }

    public void CompleteFurnitureRePlacement()
    {
        RequestWorkflowState(WorkflowState.FurniturePlacement, "Furniture replacement completed. Furniture placement ready.");
    }

    public void ReturnToRoomCapture()
    {
        SetStatus("Return to RoomCapture is blocked. After FurniturePlacement starts, the workflow cannot go back to RoomConfirm/RoomCapture.");
    }

    private bool ApplyState(WorkflowState nextState)
    {
        EnsureWorkflowStateHandlers();

        WorkflowStateHandler nextHandler = GetWorkflowStateHandler(nextState);

        if (nextHandler != null && !nextHandler.CanEnter())
        {
            SetStatus($"Cannot enter workflow state: {nextState}.");
            return false;
        }

        if (activeWorkflowStateHandler != null && activeWorkflowStateHandler.State != nextState)
        {
            activeWorkflowStateHandler.Exit();
        }

        currentState = nextState;
        activeWorkflowStateHandler = nextHandler;
        activeWorkflowStateHandler?.Enter();

        EnsureUiManagerReference();

        if (uiManager != null)
        {
            uiManager.ShowWorkflowState(nextState);
        }

        UpdateStatusText();
        return true;
    }

    public bool RequestCommand(WorkflowCommand command)
    {
        EnsureWorkflowStateHandlers();

        if (activeWorkflowStateHandler == null || activeWorkflowStateHandler.State != currentState)
        {
            activeWorkflowStateHandler = GetWorkflowStateHandler(currentState);
        }

        if (activeWorkflowStateHandler == null)
        {
            SetStatus($"Cannot handle command. No handler for current state: {currentState}.");
            return false;
        }

        bool handled = activeWorkflowStateHandler.HandleCommand(command);
        UpdateStatusText();
        return handled;
    }

    public bool RequestWorkflowState(WorkflowState nextState)
    {
        return RequestWorkflowState(nextState, null);
    }

    public bool RequestWorkflowState(WorkflowState nextState, string successStatus)
    {
        if (!ApplyState(nextState))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(successStatus))
        {
            SetStatus(successStatus);
        }

        return true;
    }

    public void RefreshWorkflowUi()
    {
        EnsureUiManagerReference();

        if (uiManager != null)
        {
            uiManager.ShowWorkflowState(currentState);
        }

        UpdateStatusText();
    }

    private bool IsConfirmedRoomOrLaterState(WorkflowState state)
    {
        return state == WorkflowState.ManualWallConfirmed ||
               state == WorkflowState.RoomCapture ||
               state == WorkflowState.FurniturePlacement ||
               state == WorkflowState.SimulationProcess ||
               state == WorkflowState.SimulationSuccess ||
               state == WorkflowState.RunSimulation ||
               state == WorkflowState.FurnitureRePlacement;
    }

    private bool BuildConfirmedRoomVisualization()
    {
        EnsureConfirmRoomManagerReference();

        bool built = confirmRoomManager != null && confirmRoomManager.BuildConfirmedRoomVisualization();
        SyncConfirmRoomManagerState();
        return built;
    }

    private void ClearConfirmedRoomObjectsOnly()
    {
        EnsureConfirmRoomManagerReference();

        if (confirmRoomManager != null)
        {
            confirmRoomManager.ClearConfirmedRoomObjectsOnly();
        }

        confirmedRoomFloorObject = null;
        confirmedRoomCeilingObject = null;
        confirmedRoomWallObjects.Clear();
    }

    private void ClearConfirmedRoomVisualization()
    {
        EnsureConfirmRoomManagerReference();

        if (confirmRoomManager != null)
        {
            confirmRoomManager.ClearConfirmedRoomVisualization();
        }

        confirmedRoomFloorObject = null;
        confirmedRoomCeilingObject = null;
        confirmedRoomWallObjects.Clear();
        ResetConfirmRoomReviewState();
    }

    private void ResetConfirmRoomReviewState()
    {
        EnsureConfirmRoomManagerReference();

        if (confirmRoomManager != null)
        {
            confirmRoomManager.ResetReviewState();
        }

        confirmRoomReady = false;
        confirmRoomValidationStatus = string.Empty;
        finalRoomClosed = false;
        floodReachedOutside = false;
        selectedCellCount = 0;
        confirmedBoundarySegmentCount = 0;
        openEndpointCount = -1;
    }

    private void ClearConfirmRoomGapMarkers()
    {
        EnsureConfirmRoomManagerReference();
        confirmRoomManager?.ClearGapMarkers();
    }

    private void SetAutoRoomVisualsActive(bool active)
    {
        if (scanner == null || scanner.sceneRoot == null)
        {
            return;
        }

        Transform sceneRoot = scanner.sceneRoot;
        for (int i = 0; i < sceneRoot.childCount; i++)
        {
            Transform child = sceneRoot.GetChild(i);
            if (child == null)
            {
                continue;
            }

            child.gameObject.SetActive(active);
        }
    }

    private void SetManualWallVisualsActive(bool active)
    {
        if (manualWallBuilder == null || manualWallBuilder.manualWallRoot == null)
        {
            return;
        }

        manualWallBuilder.manualWallRoot.gameObject.SetActive(active);
    }

    private void DisableNonConfirmedRoomBuildGeometry()
    {
        SetAutoRoomVisualsActive(false);
        SetManualWallVisualsActive(false);

        if (manualWallBuilder != null)
        {
            manualWallBuilder.SetNoneMode();
            manualWallBuilder.ClearSelectableSurfaces();
        }

        if (scanner != null)
        {
            scanner.StopScanning();
        }
    }

    private void SetConfirmedRoomRenderersVisible(bool visible)
    {
        EnsureConfirmRoomManagerReference();
        confirmRoomManager?.SetConfirmedRoomRenderersVisible(visible);
    }

    private int CountManualWalls()
    {
        if (manualWallBuilder == null)
        {
            return 0;
        }

        return manualWallBuilder.GetManualWallSegmentsSnapshot(includeInactiveObjects: true).Count;
    }

    private void SetStatus(string message)
    {
        status = message;
        Debug.Log($"[Workflow] {status}");
        UpdateStatusText();
    }

    public string BuildWorkflowStatusText()
    {
        bool roomExists = scanner != null && scanner.currentRoom != null;
        bool locked = scanner != null && scanner.lockRoom;

        return
            "=== Workflow ===\n" +
            $"state: {currentState}\n" +
            $"room exists: {roomExists}\n" +
            $"room locked: {locked}\n" +
            $"manual walls: {manualWallCount}\n" +
            $"confirm room ready: {confirmRoomReady}\n" +
            $"open endpoints: {openEndpointCount}\n" +
            $"final room closed: {finalRoomClosed}\n" +
            $"flood reached outside: {floodReachedOutside}\n" +
            $"selected cells: {selectedCellCount}\n" +
            $"confirmed boundary: {confirmedBoundarySegmentCount}\n" +
            $"confirm status: {confirmRoomValidationStatus}\n" +
            $"status: {status}";
    }

    private void UpdateStatusText()
    {
        EnsureUiManagerReference();

        if (uiManager != null)
        {
            uiManager.UpdateWorkflowStatus(this);
        }
    }


    private void EnsureConfirmRoomManagerReference()
    {
        if (confirmRoomManager == null)
        {
            confirmRoomManager = GetComponent<ConfirmRoomManager>();
        }

        if (confirmRoomManager == null && autoCreateConfirmRoomManager)
        {
            confirmRoomManager = gameObject.AddComponent<ConfirmRoomManager>();
        }

        if (confirmRoomManager != null)
        {
            confirmRoomManager.BindFromWorkflow(this);
            SyncConfirmRoomManagerState();
        }
    }

    private void SyncConfirmRoomManagerState()
    {
        if (confirmRoomManager == null)
        {
            return;
        }

        confirmRoomReady = confirmRoomManager.roomReady;
        confirmRoomValidationStatus = confirmRoomManager.validationStatus;
        finalRoomClosed = confirmRoomManager.finalRoomClosed;
        floodReachedOutside = confirmRoomManager.floodReachedOutside;
        selectedCellCount = confirmRoomManager.selectedCellCount;
        confirmedBoundarySegmentCount = confirmRoomManager.confirmedBoundarySegmentCount;
        manualWallCount = confirmRoomManager.manualWallCount;
        openEndpointCount = confirmRoomManager.openEndpointCount;

        confirmedRoomFloorObject = confirmRoomManager.ConfirmedRoomFloorObject;
        confirmedRoomCeilingObject = confirmRoomManager.ConfirmedRoomCeilingObject;
        confirmedRoomWallObjects.Clear();

        foreach (GameObject wallObject in confirmRoomManager.ConfirmedRoomWallObjects)
        {
            if (wallObject != null)
            {
                confirmedRoomWallObjects.Add(wallObject);
            }
        }
    }

    private void EnsureFurnitureCaptureManagerReference()
    {
        if (furnitureCaptureManager == null)
        {
            furnitureCaptureManager = GetComponent<FurnitureCaptureManager>();
        }

        if (furnitureCaptureManager == null && autoCreateFurnitureCaptureManager)
        {
            furnitureCaptureManager = gameObject.AddComponent<FurnitureCaptureManager>();
        }

        if (furnitureCaptureManager != null)
        {
            furnitureCaptureManager.BindFromWorkflow(this);
        }
    }

    private void EnsureUiManagerReference()
    {
        if (uiManager != null || !autoFindUiManager)
        {
            return;
        }

        UIManager[] managers = FindObjectsByType<UIManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        if (managers != null && managers.Length > 0)
        {
            uiManager = managers[0];
        }
    }

    private void EnsureWorkflowStateHandlers()
    {
        if (workflowStateHandlers.Count > 0)
        {
            return;
        }

        RegisterWorkflowStateHandler(new RoomInfoInputStateHandler(this));
        RegisterWorkflowStateHandler(new RoomBuildStateHandler(this));
        RegisterWorkflowStateHandler(new ManualWallGenerateStateHandler(this));
        RegisterWorkflowStateHandler(new ManualWallConfirmedStateHandler(this));
        RegisterWorkflowStateHandler(new RoomCaptureStateHandler(this));
        RegisterWorkflowStateHandler(new FurniturePlacementStateHandler(this));
        RegisterWorkflowStateHandler(new SimulationProcessStateHandler(this));
        RegisterWorkflowStateHandler(new SimulationSuccessStateHandler(this));
        RegisterWorkflowStateHandler(new RunSimulationStateHandler(this));
        RegisterWorkflowStateHandler(new FurnitureRePlacementStateHandler(this));
    }

    private void RegisterWorkflowStateHandler(WorkflowStateHandler handler)
    {
        if (handler == null)
        {
            return;
        }

        workflowStateHandlers[handler.State] = handler;
    }

    private WorkflowStateHandler GetWorkflowStateHandler(WorkflowState state)
    {
        EnsureWorkflowStateHandlers();
        workflowStateHandlers.TryGetValue(state, out WorkflowStateHandler handler);
        return handler;
    }

    // 상태 전환 제한 조건입니다. 세부 조건은 이후 각 기능 연결 시 이곳에 추가합니다.
    // 현재는 사용자가 확정한 역방향 제한만 먼저 반영합니다.
    public bool CanEnterRoomInfoInput()
    {
        return currentState == WorkflowState.RoomInfoInput;
    }

    public bool CanEnterRoomBuild()
    {
        return currentState == WorkflowState.RoomInfoInput ||
               currentState == WorkflowState.RoomBuild ||
               currentState == WorkflowState.ManualWallGenerate;
    }

    public bool CanEnterManualWallGenerate()
    {
        return currentState == WorkflowState.RoomBuild ||
               currentState == WorkflowState.ManualWallGenerate ||
               currentState == WorkflowState.ManualWallConfirmed;
    }

    public bool CanEnterManualWallConfirmed()
    {
        return currentState == WorkflowState.ManualWallGenerate ||
               currentState == WorkflowState.ManualWallConfirmed;
    }

    public bool CanEnterRoomCapture()
    {
        return currentState == WorkflowState.ManualWallConfirmed ||
               currentState == WorkflowState.RoomCapture;
    }

    public bool CanEnterFurniturePlacement()
    {
        return currentState == WorkflowState.RoomCapture ||
               currentState == WorkflowState.FurniturePlacement ||
               currentState == WorkflowState.SimulationProcess ||
               currentState == WorkflowState.FurnitureRePlacement;
    }

    public bool CanEnterSimulationProcess()
    {
        return currentState == WorkflowState.FurniturePlacement ||
               currentState == WorkflowState.SimulationProcess;
    }

    public bool CanEnterSimulationSuccess()
    {
        return currentState == WorkflowState.SimulationProcess ||
               currentState == WorkflowState.RunSimulation ||
               currentState == WorkflowState.SimulationSuccess;
    }

    public bool CanEnterRunSimulation()
    {
        return currentState == WorkflowState.SimulationSuccess ||
               currentState == WorkflowState.RunSimulation;
    }

    public bool CanEnterFurnitureRePlacement()
    {
        return currentState == WorkflowState.SimulationSuccess ||
               currentState == WorkflowState.FurnitureRePlacement;
    }

    private void OnEnterRoomInfoInputState()
    {
        if (scanner != null)
        {
            scanner.workflowManager = this;
            scanner.StopScanning();
        }
    }
    private void OnExitRoomInfoInputState() { }

    private void OnEnterRoomBuildState()
    {
        if (manualWallBuilder != null)
        {
            manualWallBuilder.SetNoneMode();
            manualWallBuilder.ClearSelectableSurfaces();
        }

        ClearConfirmedRoomVisualization();
        SetAutoRoomVisualsActive(true);
        SetManualWallVisualsActive(true);

        if (scanner != null)
        {
            scanner.workflowManager = this;
            scanner.UnlockRoom();

            if (startScannerWhenInitializingRoomBuild)
            {
                scanner.StartScanning();
            }
        }
    }
    private void OnExitRoomBuildState()
    {
        if (scanner != null)
        {
            scanner.StopScanning();
        }
    }
    private void OnEnterManualWallGenerateState() { }
    private void OnExitManualWallGenerateState() { }
    private void OnEnterManualWallConfirmedState()
    {
        confirmRoomManager?.SetEntranceMarkerVisible(true);
        RefreshConfirmRoomValidation();
    }
    private void OnExitManualWallConfirmedState() { }
    private void OnEnterRoomCaptureState()
    {
        DisableNonConfirmedRoomBuildGeometry();
        SetConfirmedRoomRenderersVisible(!hideConfirmedRoomRenderersInRoomCapture);
        confirmRoomManager?.SetEntranceMarkerVisible(false);
    }
    private void OnExitRoomCaptureState() { }
    private void OnEnterFurniturePlacementState()
    {
        DisableNonConfirmedRoomBuildGeometry();
        confirmRoomManager?.SetEntranceMarkerVisible(false);
    }
    private void OnExitFurniturePlacementState() { }
    private void OnEnterSimulationProcessState()
    {
        SetStatus("Simulation process ready. Review furniture placement before starting simulation.");
    }
    private void OnExitSimulationProcessState() { }
    private void OnEnterSimulationSuccessState() { }
    private void OnExitSimulationSuccessState() { }
    private void OnEnterRunSimulationState() { }
    private void OnExitRunSimulationState() { }
    private void OnEnterFurnitureRePlacementState() { }
    private void OnExitFurnitureRePlacementState() { }


}

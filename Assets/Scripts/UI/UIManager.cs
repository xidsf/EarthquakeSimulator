using System;
using MixedReality.Toolkit.UX;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

public class UIManager : MonoBehaviour
{
    [Serializable]
    public class WorkflowStateUiConfig
    {
        public RoomBuildWorkflowManager.WorkflowState state;
        public GameObject mainPanel;

        [Tooltip("해당 상태에서 MainPlate(메인 UI 판넬)를 활성화할지 여부입니다.")]
        public bool showMainPlate = true; // 기본값은 true

        [Tooltip("해당 상태에서 추가로 켤 버튼입니다.")]
        public PressableButton[] visibleButtons;

        [Tooltip("해당 상태에서 추가로 켤 오브젝트입니다.")]
        public GameObject[] visibleObjects;

        [Tooltip("상태 진입 시 Numpad/Slider 같은 서브패널을 닫습니다.")]
        public bool hideSubPanelsOnEnter = true;
    }

    [Header("Workflow 연결")]
    [Tooltip("방 생성/수동 벽/캡처/배치/시뮬레이션 단계의 실제 작업을 담당하는 WorkflowManager입니다.")]
    public RoomBuildWorkflowManager workflowManager;

    [Tooltip("workflowManager가 비어 있으면 런타임에 Scene에서 자동 탐색합니다.")]
    public bool autoFindWorkflowManager = true;

    [Tooltip("Workflow 상태가 바뀔 때 UIManager가 메인 패널을 전환합니다.")]
    public bool useWorkflowDrivenPanels = true;

    [Tooltip("상태별 UI 설정 배열을 우선 사용합니다. 각 상태마다 mainPanel과 필요한 버튼/오브젝트를 직접 지정할 수 있습니다.")]
    public bool useWorkflowStateUiConfigs = true;

    [Tooltip("이전 버전 호환용입니다. 새 구조에서는 각 PanelController가 버튼을 연결하므로 기본값은 false입니다.")]
    public bool registerWorkflowButtons = false;

    [Tooltip("현재 Workflow 상태에 맞는 버튼만 보이도록 버튼 GameObject active를 제어합니다. 버튼이 이미 패널 하위에서 관리되면 꺼도 됩니다.")]
    public bool manageWorkflowButtonVisibility = false;

    [Tooltip("WorkflowManager가 없을 때만 기존처럼 BuildingInfo 패널로 시작합니다.")]
    public bool useLegacyStartPanelWhenWorkflowMissing = true;

    [Header("Main UI Plate")]
    [Tooltip("단계별로 제어할 메인 UI의 부모판넬(MainPlate)입니다.")]
    public GameObject panel_MainPlate;

    [Header("1. Opening / RoomInfoInput 패널")]
    [Tooltip("앱 시작 시 가장 먼저 보여줄 Opening 패널입니다. 비워두면 Opening 패널 단계는 사용하지 않습니다.")]
    public GameObject panel_Opening;

    [Tooltip("panel_Opening이 연결되어 있으면 Start에서 Opening 패널을 먼저 보여줍니다.")]
    public bool showOpeningPanelOnStart = true;

    [Tooltip("RoomInfoInput 단계 패널입니다. 기존 panel_BuildingInfo 역할을 이 필드로 통합합니다.")]
    public GameObject panel_RoomInfoInput;

    [Header("1-0. Legacy Fallback 패널")]
    [Tooltip("이전 버전 호환용입니다. panel_RoomInfoInput이 비어 있을 때만 fallback으로 사용합니다.")]
    public GameObject panel_BuildingInfo;

    public GameObject panel_AutoScanCheck;
    public GameObject panel_ManualWall;
    public GameObject panel_MeshAndSimSetup;
    public GameObject panel_Result;

    [Header("1-1. Workflow 단계별 메인 패널")]
    [Tooltip("RoomBuild 단계 패널입니다. 비워두면 panel_AutoScanCheck를 사용합니다.")]
    public GameObject panel_RoomBuild;

    [Tooltip("ManualWallGenerate 단계 패널입니다. 비워두면 panel_ManualWall을 사용합니다.")]
    public GameObject panel_ManualWallGenerate;

    [Tooltip("ManualWallConfirmed 단계 패널입니다. 비워두면 panel_MeshAndSimSetup을 사용합니다.")]
    public GameObject panel_ManualWallConfirmed;

    [Tooltip("RoomCapture 단계 패널입니다.")]
    public GameObject panel_RoomCapture;

    [Tooltip("FurniturePlacement 단계 패널입니다.")]
    public GameObject panel_FurniturePlacement;

    [Tooltip("SimulationProcess 단계 패널입니다.")]
    public GameObject panel_SimulationProcess;

    [Tooltip("SimulationSuccess 단계 패널입니다. 비워두면 panel_Result를 사용합니다.")]
    public GameObject panel_SimulationSuccess;

    [Tooltip("RunSimulation 단계 패널입니다. 비워두면 panel_Result를 사용합니다.")]
    public GameObject panel_RunSimulation;

    [Tooltip("FurnitureRePlacement 단계 패널입니다. 비워두면 panel_FurniturePlacement를 사용합니다.")]
    public GameObject panel_FurnitureRePlacement;

    [Header("1-2. Workflow 상태별 UI 설정")]
    [Tooltip("상태별로 표시할 메인 패널과 추가 버튼/오브젝트를 지정합니다. 비워두면 위의 패널 필드 fallback을 사용합니다.")]
    public WorkflowStateUiConfig[] workflowStateUiConfigs;

    [Header("2. 서브 패널")]
    public GameObject sub_BuildingTypeOptions;
    public GameObject sub_Numpad;
    public GameObject sub_IntensitySlider;

    [Header("3. 시스템 메시지 UI")]
    public GameObject panel_SystemMessage;
    public TMP_Text text_SystemMessage;

    [Header("4. Workflow 버튼 - Legacy Fallback")]
    [Tooltip("새 구조에서는 각 PanelController에 버튼을 연결하는 것을 권장합니다. 이 필드는 이전 버전 호환용입니다.")]
    public PressableButton completeRoomInfoInputButton;
    public PressableButton switchToManualWallButton;
    public PressableButton returnToRoomBuildButton;
    public PressableButton confirmManualWallsButton;
    public PressableButton backToManualWallButton;
    public PressableButton confirmRoomButton;

    [Header("4-1. Workflow 버튼 - 확장 단계 Legacy Fallback")]
    public PressableButton completeRoomCaptureButton;
    public PressableButton completeFurniturePlacementButton;
    public PressableButton completeSimulationProcessButton;
    public PressableButton startRunSimulationButton;
    public PressableButton completeRunSimulationButton;
    public PressableButton startFurnitureRePlacementButton;
    public PressableButton completeFurnitureRePlacementButton;
    public PressableButton returnToRoomCaptureButton;

    [Header("5. Workflow 상태 표시")]
    public TMP_Text text_WorkflowStatus;

    private UnityAction completeRoomInfoInputAction;
    private UnityAction switchToManualWallAction;
    private UnityAction returnToRoomBuildAction;
    private UnityAction confirmManualWallsAction;
    private UnityAction backToManualWallAction;
    private UnityAction confirmRoomAction;
    private UnityAction completeRoomCaptureAction;
    private UnityAction completeFurniturePlacementAction;
    private UnityAction completeSimulationProcessAction;
    private UnityAction startRunSimulationAction;
    private UnityAction completeRunSimulationAction;
    private UnityAction startFurnitureRePlacementAction;
    private UnityAction completeFurnitureRePlacementAction;
    private UnityAction returnToRoomCaptureAction;

    private void Awake()
    {
        EnsureWorkflowManagerReference();

        if (workflowManager != null && workflowManager.uiManager == null)
        {
            workflowManager.uiManager = this;
        }
    }

    private void OnEnable()
    {
        if (registerWorkflowButtons)
        {
            CreateWorkflowButtonActions();
            RegisterWorkflowButtons();
        }
    }

    private void Start()
    {
        HideAllSubPanels();
        HideWarningMessage();

        EnsureWorkflowManagerReference();

        if (workflowManager != null)
        {
            if (workflowManager.uiManager == null)
            {
                workflowManager.uiManager = this;
            }

            if (showOpeningPanelOnStart && panel_Opening != null)
            {
                ShowMainPanel(panel_Opening);
                UpdateWorkflowStatus(workflowManager);
                return;
            }

            ShowWorkflowState(workflowManager.currentState);
            UpdateWorkflowStatus(workflowManager);
            return;
        }

        if (useLegacyStartPanelWhenWorkflowMissing)
        {
            ShowMainPanel(panel_Opening != null ? panel_Opening : (panel_RoomInfoInput != null ? panel_RoomInfoInput : panel_BuildingInfo));
        }
    }

    private void OnDisable()
    {
        if (registerWorkflowButtons)
        {
            UnregisterWorkflowButtons();
        }
    }

    public RoomBuildWorkflowManager GetWorkflowManager()
    {
        EnsureWorkflowManagerReference();
        return workflowManager;
    }

    private void EnsureWorkflowManagerReference()
    {
        if (workflowManager != null || !autoFindWorkflowManager)
        {
            return;
        }

        RoomBuildWorkflowManager[] managers =
            FindObjectsByType<RoomBuildWorkflowManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        if (managers != null && managers.Length > 0)
        {
            workflowManager = managers[0];
        }
    }

    private void CreateWorkflowButtonActions()
    {
        completeRoomInfoInputAction ??= () => RequestWorkflowCommand(RoomBuildWorkflowManager.WorkflowCommand.CompleteRoomInfoInput);
        switchToManualWallAction ??= () => RequestWorkflowCommand(RoomBuildWorkflowManager.WorkflowCommand.SwitchToManualWallGeneration);
        returnToRoomBuildAction ??= () => RequestWorkflowCommand(RoomBuildWorkflowManager.WorkflowCommand.ReturnToRoomBuild);
        confirmManualWallsAction ??= () => RequestWorkflowCommand(RoomBuildWorkflowManager.WorkflowCommand.ConfirmManualWalls);
        backToManualWallAction ??= () => RequestWorkflowCommand(RoomBuildWorkflowManager.WorkflowCommand.BackToManualWallGeneration);
        confirmRoomAction ??= () => RequestWorkflowCommand(RoomBuildWorkflowManager.WorkflowCommand.ConfirmRoomAndStartRoomCapture);
        completeRoomCaptureAction ??= () => RequestWorkflowCommand(RoomBuildWorkflowManager.WorkflowCommand.CompleteRoomCapture);
        completeFurniturePlacementAction ??= () => RequestWorkflowCommand(RoomBuildWorkflowManager.WorkflowCommand.CompleteFurniturePlacement);
        completeSimulationProcessAction ??= () => RequestWorkflowCommand(RoomBuildWorkflowManager.WorkflowCommand.CompleteSimulationProcess);
        startRunSimulationAction ??= () => RequestWorkflowCommand(RoomBuildWorkflowManager.WorkflowCommand.StartRunSimulation);
        completeRunSimulationAction ??= () => RequestWorkflowCommand(RoomBuildWorkflowManager.WorkflowCommand.CompleteRunSimulation);
        startFurnitureRePlacementAction ??= () => RequestWorkflowCommand(RoomBuildWorkflowManager.WorkflowCommand.StartFurnitureRePlacement);
        completeFurnitureRePlacementAction ??= () => RequestWorkflowCommand(RoomBuildWorkflowManager.WorkflowCommand.CompleteFurnitureRePlacement);
        returnToRoomCaptureAction ??= () => RequestWorkflowCommand(RoomBuildWorkflowManager.WorkflowCommand.ReturnToRoomCapture);
    }

    private void RegisterWorkflowButtons()
    {
        AddClick(completeRoomInfoInputButton, completeRoomInfoInputAction);
        AddClick(switchToManualWallButton, switchToManualWallAction);
        AddClick(returnToRoomBuildButton, returnToRoomBuildAction);
        AddClick(confirmManualWallsButton, confirmManualWallsAction);
        AddClick(backToManualWallButton, backToManualWallAction);
        AddClick(confirmRoomButton, confirmRoomAction);
        AddClick(completeRoomCaptureButton, completeRoomCaptureAction);
        AddClick(completeFurniturePlacementButton, completeFurniturePlacementAction);
        AddClick(completeSimulationProcessButton, completeSimulationProcessAction);
        AddClick(startRunSimulationButton, startRunSimulationAction);
        AddClick(completeRunSimulationButton, completeRunSimulationAction);
        AddClick(startFurnitureRePlacementButton, startFurnitureRePlacementAction);
        AddClick(completeFurnitureRePlacementButton, completeFurnitureRePlacementAction);
        AddClick(returnToRoomCaptureButton, returnToRoomCaptureAction);
    }

    private void UnregisterWorkflowButtons()
    {
        RemoveClick(completeRoomInfoInputButton, completeRoomInfoInputAction);
        RemoveClick(switchToManualWallButton, switchToManualWallAction);
        RemoveClick(returnToRoomBuildButton, returnToRoomBuildAction);
        RemoveClick(confirmManualWallsButton, confirmManualWallsAction);
        RemoveClick(backToManualWallButton, backToManualWallAction);
        RemoveClick(confirmRoomButton, confirmRoomAction);
        RemoveClick(completeRoomCaptureButton, completeRoomCaptureAction);
        RemoveClick(completeFurniturePlacementButton, completeFurniturePlacementAction);
        RemoveClick(completeSimulationProcessButton, completeSimulationProcessAction);
        RemoveClick(startRunSimulationButton, startRunSimulationAction);
        RemoveClick(completeRunSimulationButton, completeRunSimulationAction);
        RemoveClick(startFurnitureRePlacementButton, startFurnitureRePlacementAction);
        RemoveClick(completeFurnitureRePlacementButton, completeFurnitureRePlacementAction);
        RemoveClick(returnToRoomCaptureButton, returnToRoomCaptureAction);
    }

    private static void AddClick(PressableButton button, UnityAction action)
    {
        if (button == null || action == null)
        {
            return;
        }

        button.OnClicked.RemoveListener(action);
        button.OnClicked.AddListener(action);
    }

    private static void RemoveClick(PressableButton button, UnityAction action)
    {
        if (button == null || action == null)
        {
            return;
        }

        button.OnClicked.RemoveListener(action);
    }

    [ContextMenu("Reset Workflow State UI Configs From Panel Fields")]
    public void ResetWorkflowStateUiConfigsFromPanelFields()
    {
        workflowStateUiConfigs = new[]
        {
            CreateConfig(RoomBuildWorkflowManager.WorkflowState.RoomInfoInput, panel_RoomInfoInput != null ? panel_RoomInfoInput : panel_BuildingInfo, completeRoomInfoInputButton),
            CreateConfig(RoomBuildWorkflowManager.WorkflowState.RoomBuild, panel_RoomBuild != null ? panel_RoomBuild : panel_AutoScanCheck, switchToManualWallButton),
            CreateConfig(RoomBuildWorkflowManager.WorkflowState.ManualWallGenerate, panel_ManualWallGenerate != null ? panel_ManualWallGenerate : panel_ManualWall, returnToRoomBuildButton, confirmManualWallsButton),
            CreateConfig(RoomBuildWorkflowManager.WorkflowState.ManualWallConfirmed, panel_ManualWallConfirmed != null ? panel_ManualWallConfirmed : panel_MeshAndSimSetup, backToManualWallButton, confirmRoomButton),
            CreateConfig(RoomBuildWorkflowManager.WorkflowState.RoomCapture, panel_RoomCapture, completeRoomCaptureButton),
            CreateConfig(RoomBuildWorkflowManager.WorkflowState.FurniturePlacement, panel_FurniturePlacement, completeFurniturePlacementButton, startFurnitureRePlacementButton),
            CreateConfig(RoomBuildWorkflowManager.WorkflowState.SimulationProcess, panel_SimulationProcess),
            CreateConfig(RoomBuildWorkflowManager.WorkflowState.SimulationSuccess, panel_SimulationSuccess != null ? panel_SimulationSuccess : panel_Result, startRunSimulationButton, startFurnitureRePlacementButton),
            CreateConfig(RoomBuildWorkflowManager.WorkflowState.RunSimulation, panel_RunSimulation != null ? panel_RunSimulation : panel_Result, completeRunSimulationButton),
            CreateConfig(RoomBuildWorkflowManager.WorkflowState.FurnitureRePlacement, panel_FurnitureRePlacement != null ? panel_FurnitureRePlacement : panel_FurniturePlacement, completeFurnitureRePlacementButton)
        };
    }

    private static WorkflowStateUiConfig CreateConfig(
        RoomBuildWorkflowManager.WorkflowState state,
        GameObject panel,
        params PressableButton[] visibleButtons)
    {
        return new WorkflowStateUiConfig
        {
            state = state,
            mainPanel = panel,
            visibleButtons = visibleButtons,
            visibleObjects = null,
            hideSubPanelsOnEnter = true
        };
    }

    public void ShowWorkflowState(RoomBuildWorkflowManager.WorkflowState state)
    {
        if (!useWorkflowDrivenPanels) return;

        WorkflowStateUiConfig config = GetWorkflowStateUiConfig(state);

        // 1. 메인 패널 전환
        ShowMainPanel(GetPanelForWorkflowState(state, config));

        // 2. [추가] MainPlate 활성화 제어
        if (panel_MainPlate != null)
        {
            // 설정이 있으면 설정값에 따르고, 설정이 없으면 기본적으로 켭니다.
            bool shouldShowMainPlate = (config != null) ? config.showMainPlate : true;
            panel_MainPlate.SetActive(shouldShowMainPlate);
        }

        // 3. 버튼 및 오브젝트 가시성 적용
        ApplyWorkflowStateVisibleObjects(config);
        UpdateWorkflowButtonVisibility(state);

        if (config != null && config.hideSubPanelsOnEnter)
        {
            HideAllSubPanels();
        }
    }

    private WorkflowStateUiConfig GetWorkflowStateUiConfig(RoomBuildWorkflowManager.WorkflowState state)
    {
        if (!useWorkflowStateUiConfigs || workflowStateUiConfigs == null)
        {
            return null;
        }

        foreach (WorkflowStateUiConfig config in workflowStateUiConfigs)
        {
            if (config != null && config.state == state)
            {
                return config;
            }
        }

        return null;
    }

    private GameObject GetPanelForWorkflowState(RoomBuildWorkflowManager.WorkflowState state, WorkflowStateUiConfig config)
    {
        if (config != null && config.mainPanel != null)
        {
            return config.mainPanel;
        }

        switch (state)
        {
            case RoomBuildWorkflowManager.WorkflowState.RoomInfoInput:
                return panel_RoomInfoInput != null ? panel_RoomInfoInput : panel_BuildingInfo;
            case RoomBuildWorkflowManager.WorkflowState.RoomBuild:
                return panel_RoomBuild != null ? panel_RoomBuild : panel_AutoScanCheck;
            case RoomBuildWorkflowManager.WorkflowState.ManualWallGenerate:
                return panel_ManualWallGenerate != null ? panel_ManualWallGenerate : panel_ManualWall;
            case RoomBuildWorkflowManager.WorkflowState.ManualWallConfirmed:
                return panel_ManualWallConfirmed != null ? panel_ManualWallConfirmed : panel_MeshAndSimSetup;
            case RoomBuildWorkflowManager.WorkflowState.RoomCapture:
                return panel_RoomCapture != null ? panel_RoomCapture : panel_MeshAndSimSetup;
            case RoomBuildWorkflowManager.WorkflowState.FurniturePlacement:
                return panel_FurniturePlacement != null ? panel_FurniturePlacement : panel_Result;
            case RoomBuildWorkflowManager.WorkflowState.SimulationProcess:
                return panel_SimulationProcess != null ? panel_SimulationProcess : panel_MeshAndSimSetup;
            case RoomBuildWorkflowManager.WorkflowState.SimulationSuccess:
                return panel_SimulationSuccess != null ? panel_SimulationSuccess : panel_Result;
            case RoomBuildWorkflowManager.WorkflowState.RunSimulation:
                return panel_RunSimulation != null ? panel_RunSimulation : panel_Result;
            case RoomBuildWorkflowManager.WorkflowState.FurnitureRePlacement:
                return panel_FurnitureRePlacement != null ? panel_FurnitureRePlacement : (panel_FurniturePlacement != null ? panel_FurniturePlacement : panel_Result);
            default:
                return panel_RoomInfoInput != null ? panel_RoomInfoInput : panel_BuildingInfo;
        }
    }

    private void ApplyWorkflowStateVisibleObjects(WorkflowStateUiConfig activeConfig)
    {
        if (!useWorkflowStateUiConfigs || workflowStateUiConfigs == null)
        {
            return;
        }

        foreach (WorkflowStateUiConfig config in workflowStateUiConfigs)
        {
            if (config == null)
            {
                continue;
            }

            bool active = config == activeConfig;

            if (config.visibleButtons != null)
            {
                foreach (PressableButton button in config.visibleButtons)
                {
                    if (button != null)
                    {
                        button.gameObject.SetActive(active);
                    }
                }
            }

            if (config.visibleObjects != null)
            {
                foreach (GameObject visibleObject in config.visibleObjects)
                {
                    if (visibleObject != null)
                    {
                        visibleObject.SetActive(active);
                    }
                }
            }
        }
    }

    private void UpdateWorkflowButtonVisibility(RoomBuildWorkflowManager.WorkflowState state)
    {
        if (!manageWorkflowButtonVisibility)
        {
            return;
        }

        SetButtonActive(completeRoomInfoInputButton, state == RoomBuildWorkflowManager.WorkflowState.RoomInfoInput);
        SetButtonActive(switchToManualWallButton, state == RoomBuildWorkflowManager.WorkflowState.RoomBuild);
        SetButtonActive(returnToRoomBuildButton, state == RoomBuildWorkflowManager.WorkflowState.ManualWallGenerate);
        SetButtonActive(confirmManualWallsButton, state == RoomBuildWorkflowManager.WorkflowState.ManualWallGenerate);
        SetButtonActive(backToManualWallButton, state == RoomBuildWorkflowManager.WorkflowState.ManualWallConfirmed);
        SetButtonActive(confirmRoomButton, state == RoomBuildWorkflowManager.WorkflowState.ManualWallConfirmed);
        SetButtonActive(completeRoomCaptureButton, state == RoomBuildWorkflowManager.WorkflowState.RoomCapture);
        SetButtonActive(completeFurniturePlacementButton, state == RoomBuildWorkflowManager.WorkflowState.FurniturePlacement);
        SetButtonActive(completeSimulationProcessButton, false);
        SetButtonActive(startRunSimulationButton, state == RoomBuildWorkflowManager.WorkflowState.SimulationSuccess);
        SetButtonActive(completeRunSimulationButton, state == RoomBuildWorkflowManager.WorkflowState.RunSimulation);
        SetButtonActive(startFurnitureRePlacementButton, state == RoomBuildWorkflowManager.WorkflowState.FurniturePlacement || state == RoomBuildWorkflowManager.WorkflowState.SimulationSuccess);
        SetButtonActive(completeFurnitureRePlacementButton, state == RoomBuildWorkflowManager.WorkflowState.FurnitureRePlacement);
        SetButtonActive(returnToRoomCaptureButton, false);
    }

    private static void SetButtonActive(PressableButton button, bool active)
    {
        if (button != null)
        {
            button.gameObject.SetActive(active);
        }
    }

    private bool RequestWorkflowCommand(RoomBuildWorkflowManager.WorkflowCommand command)
    {
        RoomBuildWorkflowManager workflow = GetWorkflowManager();

        if (workflow == null)
        {
            ShowWarningMessage("WorkflowManager가 연결되어 있지 않습니다.");
            return false;
        }

        return workflow.RequestCommand(command);
    }

    public void CompleteRoomInfoInput()
    {
        RequestWorkflowCommand(RoomBuildWorkflowManager.WorkflowCommand.CompleteRoomInfoInput);
    }

    public void OpenRoomInfoInputFromOpening()
    {
        EnsureWorkflowManagerReference();

        if (workflowManager != null && workflowManager.currentState != RoomBuildWorkflowManager.WorkflowState.RoomInfoInput)
        {
            ShowWarningMessage("현재 상태에서는 RoomInfoInput 패널로 돌아갈 수 없습니다.");
            return;
        }

        ShowWorkflowState(RoomBuildWorkflowManager.WorkflowState.RoomInfoInput);
    }

    public void UpdateWorkflowStatus(RoomBuildWorkflowManager workflow)
    {
        if (workflow == null || text_WorkflowStatus == null)
        {
            return;
        }

        text_WorkflowStatus.text = workflow.BuildWorkflowStatusText();
    }

    public void ShowMainPanel(GameObject targetPanel)
    {
        SetMainPanelActive(panel_Opening, false);
        SetMainPanelActive(panel_RoomInfoInput, false);
        SetMainPanelActive(panel_BuildingInfo, false);
        SetMainPanelActive(panel_AutoScanCheck, false);
        SetMainPanelActive(panel_ManualWall, false);
        SetMainPanelActive(panel_MeshAndSimSetup, false);
        SetMainPanelActive(panel_Result, false);
        SetMainPanelActive(panel_RoomBuild, false);
        SetMainPanelActive(panel_ManualWallGenerate, false);
        SetMainPanelActive(panel_ManualWallConfirmed, false);
        SetMainPanelActive(panel_RoomCapture, false);
        SetMainPanelActive(panel_FurniturePlacement, false);
        SetMainPanelActive(panel_SimulationProcess, false);
        SetMainPanelActive(panel_SimulationSuccess, false);
        SetMainPanelActive(panel_RunSimulation, false);
        SetMainPanelActive(panel_FurnitureRePlacement, false);

        HideAllSubPanels();

        if (targetPanel != null)
        {
            targetPanel.SetActive(true);
        }
    }

    private static void SetMainPanelActive(GameObject panel, bool active)
    {
        if (panel != null)
        {
            panel.SetActive(active);
        }
    }

    public void HideAllSubPanels()
    {
        if (sub_BuildingTypeOptions != null) sub_BuildingTypeOptions.SetActive(false);
        if (sub_Numpad != null) sub_Numpad.SetActive(false);
        if (sub_IntensitySlider != null) sub_IntensitySlider.SetActive(false);
    }

    public void ToggleBuildingTypeOptions(bool isActive)
    {
        if (sub_BuildingTypeOptions != null) sub_BuildingTypeOptions.SetActive(isActive);
    }

    public void ToggleNumpad(bool isActive)
    {
        if (sub_Numpad != null) sub_Numpad.SetActive(isActive);
    }

    public void ToggleIntensitySlider(bool isActive)
    {
        if (sub_IntensitySlider != null) sub_IntensitySlider.SetActive(isActive);
    }

    public void ShowWarningMessage(string message, float duration = 2.0f)
    {
        if (panel_SystemMessage != null && text_SystemMessage != null)
        {
            text_SystemMessage.text = message;
            panel_SystemMessage.SetActive(true);
            CancelInvoke(nameof(HideWarningMessage));
            Invoke(nameof(HideWarningMessage), duration);
        }
    }

    public void HideWarningMessage()
    {
        if (panel_SystemMessage != null)
        {
            panel_SystemMessage.SetActive(false);
        }
    }
}

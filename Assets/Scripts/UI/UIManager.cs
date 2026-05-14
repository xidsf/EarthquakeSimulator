using System;
using System.Collections;
using MixedReality.Toolkit.UX;
using TMPro;
using UnityEngine;

public class UIManager : MonoBehaviour
{
    [Serializable]
    public class WorkflowStateUiConfig
    {
        public RoomBuildWorkflowManager.WorkflowState state;
        public GameObject mainPanel;

        [Tooltip("해당 상태에서 MainPlate(메인 UI 패널)를 활성화할지 여부입니다.")]
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

    [Header("Main UI Plate")]
    [Tooltip("단계별로 제어할 메인 UI의 부모판넬(MainPlate)입니다.")]
    public GameObject panel_MainPlate;

    [Header("1. Opening / RoomInfoInput 패널")]
    [Tooltip("앱 시작 시 가장 먼저 보여줄 Opening 패널입니다. 비워두면 Opening 패널 단계는 사용하지 않습니다.")]
    public GameObject panel_Opening;

    [Tooltip("panel_Opening이 연결되어 있으면 Start에서 Opening 패널을 먼저 보여줍니다.")]
    public bool showOpeningPanelOnStart = true;

    [Tooltip("RoomInfoInput 단계 패널입니다.")]
    public GameObject panel_RoomInfoInput;

    [Header("1-1. Workflow 단계별 메인 패널")]
    [Tooltip("RoomBuild 단계 패널입니다.")]
    public GameObject panel_RoomBuild;

    [Tooltip("ManualWallGenerate 단계 패널입니다.")]
    public GameObject panel_ManualWallGenerate;

    [Tooltip("ManualWallConfirmed 단계 패널입니다.")]
    public GameObject panel_ManualWallConfirmed;

    [Tooltip("RoomCapture 단계 패널입니다.")]
    public GameObject panel_RoomCapture;

    [Tooltip("FurniturePlacement 단계 패널입니다.")]
    public GameObject panel_FurniturePlacement;

    [Tooltip("SimulationProcess 단계 패널입니다.")]
    public GameObject panel_SimulationProcess;

    [Tooltip("SimulationSuccess 단계 패널입니다.")]
    public GameObject panel_SimulationSuccess;

    [Tooltip("RunSimulation 단계 패널입니다.")]
    public GameObject panel_RunSimulation;

    [Tooltip("FurnitureRePlacement 단계 패널입니다.")]
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

    [Header("4. Notification UI")]
    [Tooltip("알림 UI 루트 GameObject입니다. RectTransform을 가진 UI 오브젝트를 연결합니다.")]
    public GameObject notificationUI;
    [Tooltip("알림 문구를 표시할 TextMeshPro 컴포넌트입니다. 비워두면 notificationUI 하위에서 자동 탐색합니다.")]
    public TMP_Text text_Notification;
    [Tooltip("알림이 숨겨져 있을 때 RectTransform Pos Y 값입니다.")]
    public float notificationHiddenY = -180.0f;
    [Tooltip("알림이 표시될 때 RectTransform Pos Y 값입니다.")]
    public float notificationVisibleY = -75.0f;
    [Tooltip("알림이 표시된 뒤 유지되는 시간입니다.")]
    public float notificationVisibleSeconds = 3.0f;
    [Tooltip("알림이 이동하는 데 걸리는 시간입니다.")]
    public float notificationSlideSeconds = 0.25f;

    [Header("5. Workflow 상태 표시")]
    public TMP_Text text_WorkflowStatus;

    [Header("6. Workflow Help")]
    [Tooltip("Help 패널 전체를 관리하는 컨트롤러입니다. State 변경 시 ShowForState를 호출합니다.")]
    public WorkflowHelpPanelController workflowHelpPanel;
    public bool autoFindWorkflowHelpPanel = true;

    // 현재 활성화된 메인 패널을 추적합니다. Help 열림 시 비활성화에 사용합니다.
    private GameObject currentMainPanel;
    private RectTransform notificationRectTransform;
    private Coroutine notificationCoroutine;

    public GameObject CurrentMainPanel => currentMainPanel;

    private void Awake()
    {
        EnsureWorkflowManagerReference();

        if (workflowManager != null && workflowManager.uiManager == null)
        {
            workflowManager.uiManager = this;
        }
    }

    private void Start()
    {
        HideAllSubPanels();
        HideWarningMessage();
        InitializeNotificationUI();
        SetNotificationY(notificationHiddenY);
        if (notificationUI != null)
        {
            notificationUI.SetActive(false);
        }

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

        // WorkflowManager 미연결 시 Opening 또는 RoomInfoInput 패널 표시
        ShowMainPanel(panel_Opening != null ? panel_Opening : panel_RoomInfoInput);
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

    private void EnsureWorkflowHelpPanelReference()
    {
        if (workflowHelpPanel != null || !autoFindWorkflowHelpPanel)
        {
            return;
        }

        WorkflowHelpPanelController[] helpPanels =
            FindObjectsByType<WorkflowHelpPanelController>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        if (helpPanels != null && helpPanels.Length > 0)
        {
            workflowHelpPanel = helpPanels[0];
        }
    }

    [ContextMenu("Reset Workflow State UI Configs From Panel Fields")]
    public void ResetWorkflowStateUiConfigsFromPanelFields()
    {
        workflowStateUiConfigs = new[]
        {
            CreateConfig(RoomBuildWorkflowManager.WorkflowState.RoomInfoInput, panel_RoomInfoInput),
            CreateConfig(RoomBuildWorkflowManager.WorkflowState.RoomBuild, panel_RoomBuild),
            CreateConfig(RoomBuildWorkflowManager.WorkflowState.ManualWallGenerate, panel_ManualWallGenerate),
            CreateConfig(RoomBuildWorkflowManager.WorkflowState.ManualWallConfirmed, panel_ManualWallConfirmed),
            CreateConfig(RoomBuildWorkflowManager.WorkflowState.RoomCapture, panel_RoomCapture),
            CreateConfig(RoomBuildWorkflowManager.WorkflowState.FurniturePlacement, panel_FurniturePlacement),
            CreateConfig(RoomBuildWorkflowManager.WorkflowState.SimulationProcess, panel_SimulationProcess),
            CreateConfig(RoomBuildWorkflowManager.WorkflowState.SimulationSuccess, panel_SimulationSuccess),
            CreateConfig(RoomBuildWorkflowManager.WorkflowState.RunSimulation, panel_RunSimulation),
            CreateConfig(RoomBuildWorkflowManager.WorkflowState.FurnitureRePlacement, panel_FurnitureRePlacement)
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
        GameObject targetPanel = GetPanelForWorkflowState(state, config);
        ShowMainPanel(targetPanel);

        // 2. MainPlate 활성화 제어
        if (panel_MainPlate != null)
        {
            // 설정이 있으면 설정값에 따르고, 설정이 없으면 기본적으로 켭니다.
            bool shouldShowMainPlate = (config != null) ? config.showMainPlate : true;
            panel_MainPlate.SetActive(shouldShowMainPlate);
        }

        // 3. 버튼 및 오브젝트 가시성 적용
        ApplyWorkflowStateVisibleObjects(config);

        if (config != null && config.hideSubPanelsOnEnter)
        {
            HideAllSubPanels();
        }

        EnsureWorkflowHelpPanelReference();
        if (workflowHelpPanel != null)
        {
            workflowHelpPanel.ShowForState(state);
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
                return panel_RoomInfoInput;
            case RoomBuildWorkflowManager.WorkflowState.RoomBuild:
                return panel_RoomBuild;
            case RoomBuildWorkflowManager.WorkflowState.ManualWallGenerate:
                return panel_ManualWallGenerate;
            case RoomBuildWorkflowManager.WorkflowState.ManualWallConfirmed:
                return panel_ManualWallConfirmed;
            case RoomBuildWorkflowManager.WorkflowState.RoomCapture:
                return panel_RoomCapture;
            case RoomBuildWorkflowManager.WorkflowState.FurniturePlacement:
                return panel_FurniturePlacement;
            case RoomBuildWorkflowManager.WorkflowState.SimulationProcess:
                return panel_SimulationProcess;
            case RoomBuildWorkflowManager.WorkflowState.SimulationSuccess:
                return panel_SimulationSuccess;
            case RoomBuildWorkflowManager.WorkflowState.RunSimulation:
                return panel_RunSimulation;
            case RoomBuildWorkflowManager.WorkflowState.FurnitureRePlacement:
                return panel_FurnitureRePlacement != null ? panel_FurnitureRePlacement : panel_FurniturePlacement;
            default:
                return panel_RoomInfoInput;
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

        currentMainPanel = targetPanel;

        if (targetPanel != null)
        {
            targetPanel.SetActive(true);
        }
    }

    // Help 컨트롤러에서 호출합니다. 현재 메인 패널을 켜거나 끄는 인터페이스입니다.
    public void SetCurrentMainPanelInteractable(bool interactable)
    {
        if (currentMainPanel != null)
        {
            currentMainPanel.SetActive(interactable);
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

    public void ShowNotification(string message)
    {
        InitializeNotificationUI();

        if (notificationUI == null || notificationRectTransform == null)
        {
            return;
        }

        if (text_Notification != null)
        {
            text_Notification.text = message;
        }

        if (notificationCoroutine != null)
        {
            StopCoroutine(notificationCoroutine);
        }

        SetNotificationY(notificationHiddenY);
        notificationUI.SetActive(true);
        notificationCoroutine = StartCoroutine(ShowNotificationRoutine());
    }

    public void HideNotification()
    {
        InitializeNotificationUI();

        if (notificationCoroutine != null)
        {
            StopCoroutine(notificationCoroutine);
            notificationCoroutine = null;
        }

        SetNotificationY(notificationHiddenY);

        if (notificationUI != null)
        {
            notificationUI.SetActive(false);
        }
    }

    private void InitializeNotificationUI()
    {
        if (notificationUI == null)
        {
            return;
        }

        if (notificationRectTransform == null)
        {
            notificationRectTransform = notificationUI.GetComponent<RectTransform>();
        }

        if (text_Notification == null)
        {
            text_Notification = notificationUI.GetComponentInChildren<TMP_Text>(true);
        }
    }

    private IEnumerator ShowNotificationRoutine()
    {
        yield return SlideNotificationToY(notificationVisibleY);
        yield return new WaitForSecondsRealtime(Mathf.Max(0.0f, notificationVisibleSeconds));
        yield return SlideNotificationToY(notificationHiddenY);

        if (notificationUI != null)
        {
            notificationUI.SetActive(false);
        }

        notificationCoroutine = null;
    }

    private IEnumerator SlideNotificationToY(float targetY)
    {
        if (notificationRectTransform == null)
        {
            yield break;
        }

        Vector2 start = notificationRectTransform.anchoredPosition;
        Vector2 target = new Vector2(start.x, targetY);
        float duration = Mathf.Max(0.01f, notificationSlideSeconds);
        float elapsed = 0.0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            t = t * t * (3.0f - 2.0f * t);
            notificationRectTransform.anchoredPosition = Vector2.LerpUnclamped(start, target, t);
            yield return null;
        }

        notificationRectTransform.anchoredPosition = target;
    }

    private void SetNotificationY(float y)
    {
        if (notificationRectTransform == null)
        {
            return;
        }

        Vector2 position = notificationRectTransform.anchoredPosition;
        position.y = y;
        notificationRectTransform.anchoredPosition = position;
    }
}

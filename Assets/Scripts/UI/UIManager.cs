using System;
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

        [Tooltip("?대떦 ?곹깭?먯꽌 MainPlate(硫붿씤 UI ?먮꽟)瑜??쒖꽦?뷀븷吏 ?щ??낅땲??")]
        public bool showMainPlate = true; // 湲곕낯媛믪? true

        [Tooltip("?대떦 ?곹깭?먯꽌 異붽?濡?耳?踰꾪듉?낅땲??")]
        public PressableButton[] visibleButtons;

        [Tooltip("?대떦 ?곹깭?먯꽌 異붽?濡?耳??ㅻ툕?앺듃?낅땲??")]
        public GameObject[] visibleObjects;

        [Tooltip("?곹깭 吏꾩엯 ??Numpad/Slider 媛숈? ?쒕툕?⑤꼸???レ뒿?덈떎.")]
        public bool hideSubPanelsOnEnter = true;
    }

    [Header("Workflow ?곌껐")]
    [Tooltip("諛??앹꽦/?섎룞 踰?罹≪쿂/諛곗튂/?쒕??덉씠???④퀎???ㅼ젣 ?묒뾽???대떦?섎뒗 WorkflowManager?낅땲??")]
    public RoomBuildWorkflowManager workflowManager;

    [Tooltip("workflowManager媛 鍮꾩뼱 ?덉쑝硫??고??꾩뿉 Scene?먯꽌 ?먮룞 ?먯깋?⑸땲??")]
    public bool autoFindWorkflowManager = true;

    [Tooltip("Workflow ?곹깭媛 諛붾???UIManager媛 硫붿씤 ?⑤꼸???꾪솚?⑸땲??")]
    public bool useWorkflowDrivenPanels = true;

    [Tooltip("?곹깭蹂?UI ?ㅼ젙 諛곗뿴???곗꽑 ?ъ슜?⑸땲?? 媛??곹깭留덈떎 mainPanel怨??꾩슂??踰꾪듉/?ㅻ툕?앺듃瑜?吏곸젒 吏?뺥븷 ???덉뒿?덈떎.")]
    public bool useWorkflowStateUiConfigs = true;

    [Header("Main UI Plate")]
    [Tooltip("?④퀎蹂꾨줈 ?쒖뼱??硫붿씤 UI??遺紐⑦뙋??MainPlate)?낅땲??")]
    public GameObject panel_MainPlate;

    [Header("1. Opening / RoomInfoInput ?⑤꼸")]
    [Tooltip("???쒖옉 ??媛??癒쇱? 蹂댁뿬以?Opening ?⑤꼸?낅땲?? 鍮꾩썙?먮㈃ Opening ?⑤꼸 ?④퀎???ъ슜?섏? ?딆뒿?덈떎.")]
    public GameObject panel_Opening;

    [Tooltip("panel_Opening???곌껐?섏뼱 ?덉쑝硫?Start?먯꽌 Opening ?⑤꼸??癒쇱? 蹂댁뿬以띾땲??")]
    public bool showOpeningPanelOnStart = true;

    [Tooltip("RoomInfoInput ?④퀎 ?⑤꼸?낅땲??")]
    public GameObject panel_RoomInfoInput;

    [Header("1-1. Workflow ?④퀎蹂?硫붿씤 ?⑤꼸")]
    [Tooltip("RoomBuild ?④퀎 ?⑤꼸?낅땲??")]
    public GameObject panel_RoomBuild;

    [Tooltip("ManualWallGenerate ?④퀎 ?⑤꼸?낅땲??")]
    public GameObject panel_ManualWallGenerate;

    [Tooltip("ManualWallConfirmed ?④퀎 ?⑤꼸?낅땲??")]
    public GameObject panel_ManualWallConfirmed;

    [Tooltip("RoomCapture ?④퀎 ?⑤꼸?낅땲??")]
    public GameObject panel_RoomCapture;

    [Tooltip("FurniturePlacement ?④퀎 ?⑤꼸?낅땲??")]
    public GameObject panel_FurniturePlacement;

    [Tooltip("SimulationProcess ?④퀎 ?⑤꼸?낅땲??")]
    public GameObject panel_SimulationProcess;

    [Tooltip("SimulationSuccess ?④퀎 ?⑤꼸?낅땲??")]
    public GameObject panel_SimulationSuccess;

    [Tooltip("RunSimulation ?④퀎 ?⑤꼸?낅땲??")]
    public GameObject panel_RunSimulation;

    [Tooltip("FurnitureRePlacement ?④퀎 ?⑤꼸?낅땲??")]
    public GameObject panel_FurnitureRePlacement;

    [Header("1-2. Workflow ?곹깭蹂?UI ?ㅼ젙")]
    [Tooltip("?곹깭蹂꾨줈 ?쒖떆??硫붿씤 ?⑤꼸怨?異붽? 踰꾪듉/?ㅻ툕?앺듃瑜?吏?뺥빀?덈떎. 鍮꾩썙?먮㈃ ?꾩쓽 ?⑤꼸 ?꾨뱶 fallback???ъ슜?⑸땲??")]
    public WorkflowStateUiConfig[] workflowStateUiConfigs;

    [Header("2. ?쒕툕 ?⑤꼸")]
    public GameObject sub_BuildingTypeOptions;
    public GameObject sub_Numpad;
    public GameObject sub_IntensitySlider;

    [Header("3. ?쒖뒪??硫붿떆吏 UI")]
    public GameObject panel_SystemMessage;
    public TMP_Text text_SystemMessage;

    [Header("5. Workflow ?곹깭 ?쒖떆")]
    public TMP_Text text_WorkflowStatus;

    [Header("6. Workflow Help")]
    [Tooltip("Help ?⑤꼸 ?꾩껜瑜?愿由ы븯??而⑦듃濡ㅻ윭?낅땲?? State 蹂寃???ShowForState瑜??몄텧?⑸땲??")]
    public WorkflowHelpPanelController workflowHelpPanel;
    public bool autoFindWorkflowHelpPanel = true;

    // ?꾩옱 ?쒖꽦?붾맂 硫붿씤 ?⑤꼸 (Help ?대┝ ??鍮꾪솢?깊솕?섍린 ?꾪빐 異붿쟻)
    private GameObject currentMainPanel;

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

        // WorkflowManager 誘몄뿰寃???Opening ?먮뒗 RoomInfoInput ?⑤꼸 ?쒖떆
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

        // 1. 硫붿씤 ?⑤꼸 ?꾪솚
        GameObject targetPanel = GetPanelForWorkflowState(state, config);
        ShowMainPanel(targetPanel);

        // 2. MainPlate ?쒖꽦???쒖뼱
        if (panel_MainPlate != null)
        {
            // ?ㅼ젙???덉쑝硫??ㅼ젙媛믪뿉 ?곕Ⅴ怨? ?ㅼ젙???놁쑝硫?湲곕낯?곸쑝濡?耳?땲??
            bool shouldShowMainPlate = (config != null) ? config.showMainPlate : true;
            panel_MainPlate.SetActive(shouldShowMainPlate);
        }

        // 3. 踰꾪듉 諛??ㅻ툕?앺듃 媛?쒖꽦 ?곸슜
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
            ShowWarningMessage("WorkflowManager媛 ?곌껐?섏뼱 ?덉? ?딆뒿?덈떎.");
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
            ShowWarningMessage("?꾩옱 ?곹깭?먯꽌??RoomInfoInput ?⑤꼸濡??뚯븘媛????놁뒿?덈떎.");
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

    // Help 而⑦듃濡ㅻ윭?먯꽌 ?몄텧. ?꾩옱 硫붿씤 ?⑤꼸??耳쒓굅?????덊띁?곗뒪???좎?).
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
}

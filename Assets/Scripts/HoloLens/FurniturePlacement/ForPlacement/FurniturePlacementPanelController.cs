using System.Collections;
using System.Collections.Generic;
using MixedReality.Toolkit.UX;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;

/// <summary>
/// ?섏젙??媛援?諛곗튂 ?⑤꼸 而⑦듃濡ㅻ윭?낅땲??
/// 二쇱슂 蹂寃??ы빆:
/// 1. ?섎룞 ?뺤젙(Confirm) 踰꾪듉 ?쒓굅 諛?濡쒖쭅 ??젣 (?먮룞 ?뺤젙 ???
/// 2. ?숈씪 媛援ъ쓽 ?ㅼ쨷 諛곗튂 ?덉슜 (諛곗튂 ??踰꾪듉 ?좉툑 ?댁젣)
/// 3. 紐⑤뱺 媛援щ? 諛곗튂?댁빞 ?쒕떎??媛뺤젣 議곌굔 ??젣 (?ъ슜???먯쑉??遺??
/// 4. 而댄뙆???ㅻ쪟(T ?뺤떇, ??젣??硫붿꽌???몄텧 ?? ?섏젙
/// </summary>
public class FurniturePlacementPanelController : WorkflowPanelControllerBase
{
    private const string ToggleButtonBackplateOuterGeometryName = "UX.Button.BackplateOuterGeometry";

    [Header("Furniture Placement References")]
    public FurniturePlacementManager furniturePlacementManager;
    public FurnitureMoveModeController moveModeController;
    public ConfirmedRoomGeometryProvider roomGeometryProvider;
    public FurnitureServerPlacementPipeline serverPlacementPipeline;
    [Tooltip("珥ъ쁺 ?몄뀡 ?뺣낫瑜??쎌뼱 媛援??앹꽦 踰꾪듉 ?쒖꽦??議곌굔???먮떒?⑸땲??")]
    public FurnitureCaptureManager furnitureCaptureManager;

    [Header("User Buttons")]
    public PressableButton userPlaceFurnitureFromServerButton;
    public PressableButton userSelectNextServerFurnitureButton;
    public PressableButton userSelectPreviousServerFurnitureButton;
    public PressableButton userPlaceSelectedServerFurnitureButton;
    public PressableButton userToggleMoveModeButton;
    public PressableButton userToggleRotateModeButton;
    public PressableButton userToggleScaleModeButton;
    public PressableButton userToggleFurnitureFixedButton;
    [FormerlySerializedAs("userToggleWallMountedButton")]
    public PressableButton userToggleFloorContactlessButton;
    public PressableButton userSnapSelectedFurnitureButton;
    public PressableButton userDeleteSelectedFurnitureButton;
    public PressableButton userScaleUpButton;
    public PressableButton userScaleDownButton;
    public PressableButton userScaleMoreButton;
    public PressableButton userScaleXUpButton;
    public PressableButton userScaleXDownButton;
    public PressableButton userScaleYUpButton;
    public PressableButton userScaleYDownButton;
    public PressableButton userScaleZUpButton;
    public PressableButton userScaleZDownButton;
    [Tooltip("x/y/z 異뺣퀎 ?ㅼ???踰꾪듉?ㅼ쓽 怨듯넻 ?곸쐞 GameObject. ?붾낫湲??좉? ?????ㅻ툕?앺듃瑜??듭㎏濡??쒖꽦/鍮꾪솢?깊빀?덈떎.")]
    public GameObject userScaleAxisButtonsRoot;
    public PressableButton userCompleteFurniturePlacementButton;

    [Header("Server Furniture List UI")]
    public TMP_Text userSelectedServerFurnitureText;
    public TMP_Text userServerFurnitureListText;
    public TMP_Text userServerFurnitureStatusText;
    public Transform userSelectedServerFurniturePreviewAnchor;
    public bool loadSelectedServerFurniturePreview = true;
    public bool suppressSelectedServerFurniturePreviewOnDevice = false;
    public float selectedServerFurniturePreviewScale = 0.03f;

    [Tooltip("true?대㈃ 由ъ뒪?몄쓽 紐⑤뱺 媛援щ? 理쒖냼 ??踰덉뵫 諛곗튂?댁빞 ?꾨즺 踰꾪듉???쒖꽦?붾맗?덈떎.")]
    public bool requireAllServerFurniturePlacedBeforeComplete = false;

    [Header("Server Furniture Request Guard")]
    [Min(1.0f)] public float serverFurnitureRequestTimeoutSeconds = 600.0f;

    [Header("Mode Text")]
    [SerializeField] private string moveModeStatusText = "?대룞 紐⑤뱶 ?쒖꽦?? 媛援щ? ?≪븘 ?꾩튂瑜?議곗젙?섏꽭??";
    [SerializeField] private string rotateModeStatusText = "?뚯쟾 紐⑤뱶 ?쒖꽦?? 媛援щ? Y異?湲곗??쇰줈 ?뚯쟾?쒗궎?몄슂.";
    [SerializeField] private string scaleModeStatusText = "?ш린 議곗젅 紐⑤뱶 ?쒖꽦?? +/- 踰꾪듉?쇰줈 ?ш린瑜?議곗젅?섏꽭??";

    [Tooltip("踰꾪듉 ??踰??꾨? ???곸슜?섎뒗 洹좎씪 ?ㅼ???諛곗닔?낅땲?? ?묎쾶??1/??媛믪씠 ?곸슜?⑸땲??")]
    [SerializeField] private float scaleStepFactor = 1.1f;

    [Tooltip("?ㅼ???+/- 踰꾪듉??湲멸쾶 ?꾨Ⅴ怨??덉쓣 ??諛섎났 ?곸슜?섎뒗 媛꾧꺽(珥??낅땲??")]
    [SerializeField] private float scaleRepeatIntervalSeconds = 0.3f;

    [Tooltip("x/y/z 異뺣퀎 誘몄꽭 議곗젙 踰꾪듉 ??踰덉뿉 ?곸슜?섎뒗 諛곗닔?낅땲??誘몄꽭?섎?濡?洹좎씪 諛곗닔蹂대떎 ?묎쾶 沅뚯옣).")]
    [SerializeField] private float axisScaleStepFactor = 1.05f;

    [Header("Toggle Button Visuals")]
    [Tooltip("Move/Rotate ?좉? 踰꾪듉??On?????곸슜??癒명떚由ъ뼹?낅땲?? 鍮꾩썙?먮㈃ ?쒓컖 蹂寃??놁씠 ?숈옉?⑸땲??")]
    public Material toggleOnMaterial;
    [Tooltip("媛援?怨좎젙 踰꾪듉??On?????곸슜??蹂꾨룄 癒명떚由ъ뼹?낅땲??")]
    public Material fixedToggleOnMaterial;
    [Tooltip("踰쎄구??踰?遺李?踰꾪듉??On?????곸슜??蹂꾨룄 癒명떚由ъ뼹?낅땲??")]
    [FormerlySerializedAs("wallMountedToggleOnMaterial")]
    public Material floorContactlessToggleOnMaterial;

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
    private UnityAction userToggleScaleModeAction;
    private UnityAction userToggleFurnitureFixedAction;
    private UnityAction userToggleFloorContactlessAction;
    private UnityAction userSnapSelectedFurnitureAction;
    private UnityAction userDeleteSelectedFurnitureAction;
    private bool scaleUpHeldActive;
    private float scaleUpNextRepeatTime;
    private bool scaleDownHeldActive;
    private float scaleDownNextRepeatTime;
    private bool scaleMoreExpanded;
    private UnityAction userScaleMoreAction;
    private UnityAction userScaleXUpAction;
    private UnityAction userScaleXDownAction;
    private UnityAction userScaleYUpAction;
    private UnityAction userScaleYDownAction;
    private UnityAction userScaleZUpAction;
    private UnityAction userScaleZDownAction;
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
    private readonly Dictionary<Renderer, Material[]> originalToggleButtonMaterials = new Dictionary<Renderer, Material[]>();
    private readonly Dictionary<PressableButton, bool> toggleButtonVisualStates = new Dictionary<PressableButton, bool>();

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
        UpdateManipulationModeVisuals();
    }

    private void OnDisable()
    {
        UnsubscribeServerPlacementPipelineEvents();
        UnsubscribeMoveModeControllerEvents();
        UnregisterButtons();
        RestoreToggleButtonMaterials();
        ClearSelectedServerFurniturePreview();
    }

    private void Update()
    {
        UpdateScaleButtonHoldRepeat();

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
        userToggleScaleModeAction ??= OnClickToggleScaleMode;
        userToggleFurnitureFixedAction ??= OnClickToggleFurnitureFixed;
        userToggleFloorContactlessAction ??= OnClickToggleFloorContactless;
        userSnapSelectedFurnitureAction ??= OnClickSnapSelectedFurniture;
        userDeleteSelectedFurnitureAction ??= OnClickDeleteSelectedFurniture;
        userScaleMoreAction ??= OnClickScaleMore;
        userScaleXUpAction ??= OnClickScaleXUp;
        userScaleXDownAction ??= OnClickScaleXDown;
        userScaleYUpAction ??= OnClickScaleYUp;
        userScaleYDownAction ??= OnClickScaleYDown;
        userScaleZUpAction ??= OnClickScaleZUp;
        userScaleZDownAction ??= OnClickScaleZDown;
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
        AddClick(userToggleScaleModeButton, userToggleScaleModeAction);
        AddClick(userToggleFurnitureFixedButton, userToggleFurnitureFixedAction);
        AddClick(userToggleFloorContactlessButton, userToggleFloorContactlessAction);
        AddClick(userSnapSelectedFurnitureButton, userSnapSelectedFurnitureAction);
        AddClick(userDeleteSelectedFurnitureButton, userDeleteSelectedFurnitureAction);
        AddClick(userScaleMoreButton, userScaleMoreAction);
        AddClick(userScaleXUpButton, userScaleXUpAction);
        AddClick(userScaleXDownButton, userScaleXDownAction);
        AddClick(userScaleYUpButton, userScaleYUpAction);
        AddClick(userScaleYDownButton, userScaleYDownAction);
        AddClick(userScaleZUpButton, userScaleZUpAction);
        AddClick(userScaleZDownButton, userScaleZDownAction);
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
        RemoveClick(userToggleScaleModeButton, userToggleScaleModeAction);
        RemoveClick(userToggleFurnitureFixedButton, userToggleFurnitureFixedAction);
        RemoveClick(userToggleFloorContactlessButton, userToggleFloorContactlessAction);
        RemoveClick(userSnapSelectedFurnitureButton, userSnapSelectedFurnitureAction);
        RemoveClick(userDeleteSelectedFurnitureButton, userDeleteSelectedFurnitureAction);
        RemoveClick(userScaleMoreButton, userScaleMoreAction);
        RemoveClick(userScaleXUpButton, userScaleXUpAction);
        RemoveClick(userScaleXDownButton, userScaleXDownAction);
        RemoveClick(userScaleYUpButton, userScaleYUpAction);
        RemoveClick(userScaleYDownButton, userScaleYDownAction);
        RemoveClick(userScaleZUpButton, userScaleZUpAction);
        RemoveClick(userScaleZDownButton, userScaleZDownAction);
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
        SetServerFurnitureStatus($"{BuildFurnitureName(selectedObject, index)}???꾩튂瑜?議곗젙?섏꽭??");
    }

    private void OnServerPendingObjectConfirmed(int index, FurnitureServerResultObject selectedObject)
    {
        RefreshServerFurnitureListView();
    }

    private void OnMoveModeChanged(bool enabled)
    {
        UpdateManipulationModeVisuals();
        RefreshServerFurnitureListView();
    }

    private void OnMoveModeSelectionChanged(PlacedFurniture selectedFurniture)
    {
        UpdateManipulationModeVisuals();
        RefreshServerFurnitureListView();
    }

    private void RefreshServerFurnitureListView()
    {
        EnsureFurniturePlacementReferences();
        RefreshFurnitureActionButtonState();
        UpdateManipulationModeVisuals();

        if (serverPlacementPipeline == null || serverPlacementPipeline.PendingObjectCount == 0)
        {
            SetSelectedFurnitureText("遺덈윭??媛援ш? ?놁뒿?덈떎.");
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
            SetSelectedFurnitureText($"媛援?由ъ뒪??以鍮꾨맖. 諛곗튂??{placed}/{count}");
        }

        string listText = string.Empty;
        for (int i = 0; i < count; i++)
        {
            FurnitureServerResultObject obj = serverPlacementPipeline.GetPendingObject(i);
            string marker = i == selectedIndex ? "> " : "  ";
            // ?먮룞 ?뺤젙?대?濡?諛곗튂 ?щ?留??쒖떆 (?ㅼ쨷 諛곗튂瑜??꾪빐 [諛곗튂???쇰줈 ?좎?)
            string state = serverPlacementPipeline.IsPendingObjectLoadedForPlacement(i) ? " [諛곗튂??" : "";
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

        // ?뮕 [?섏젙] ?ㅼ쨷 諛곗튂瑜??꾪빐 ?대? 諛곗튂?섏뿀?붿? 泥댄겕?섎뒗 濡쒖쭅???쒓굅?덉뒿?덈떎.
        bool allPlaced = serverPlacementPipeline != null && serverPlacementPipeline.AreAllPendingObjectsPlaced();
        bool hasPlacedSelection = moveModeController != null && moveModeController.SelectedFurniture != null;
        PlacedFurniture selectedFurniture = moveModeController != null ? moveModeController.SelectedFurniture : null;
        bool selectedFurnitureFixed = selectedFurniture != null && selectedFurniture.IsFixed;
        bool moveModeOn = moveModeController != null
            && moveModeController.CurrentMode == FurnitureManipulationMode.Move;
        bool canUseManualSnap = hasPlacedSelection && !isRunning && !selectedFurnitureFixed && moveModeOn;
        bool canDeleteSelectedFurniture = hasPlacedSelection && !isRunning;

        SetButtonVisible(userPlaceFurnitureFromServerButton, !hasServerFurniture);
        SetButtonEnabled(userPlaceFurnitureFromServerButton, !isRunning && !IsServerFurnitureRequestLocked());
        SetButtonEnabled(userSelectNextServerFurnitureButton, hasServerFurniture && !isRunning);
        SetButtonEnabled(userSelectPreviousServerFurnitureButton, hasServerFurniture && !isRunning);

        // ?뮕 [?섏젙] ?좏깮??媛援ш? ?덈떎硫??몄젣?좎? ?ㅼ떆 諛곗튂 踰꾪듉???꾨? ???덉뒿?덈떎.
        SetButtonEnabled(userPlaceSelectedServerFurnitureButton, hasSelectedFurniture && !isRunning);

        SetButtonVisible(userToggleMoveModeButton, hasPlacedSelection && !isRunning);
        SetButtonVisible(userToggleRotateModeButton, hasPlacedSelection && !isRunning);
        SetButtonVisible(userToggleScaleModeButton, hasPlacedSelection && !isRunning);
        SetButtonVisible(userToggleFurnitureFixedButton, hasPlacedSelection && !isRunning);
        SetButtonVisible(userToggleFloorContactlessButton, hasPlacedSelection && !isRunning);
        SetButtonVisible(userSnapSelectedFurnitureButton, canUseManualSnap);
        SetButtonVisible(userDeleteSelectedFurnitureButton, canDeleteSelectedFurniture);
        SetButtonEnabled(userToggleMoveModeButton, hasPlacedSelection && !isRunning && !selectedFurnitureFixed);
        SetButtonEnabled(userToggleRotateModeButton, hasPlacedSelection && !isRunning && !selectedFurnitureFixed);
        SetButtonEnabled(userToggleScaleModeButton, hasPlacedSelection && !isRunning && !selectedFurnitureFixed);
        SetButtonEnabled(userToggleFurnitureFixedButton, hasPlacedSelection && !isRunning);
        SetButtonEnabled(userToggleFloorContactlessButton, hasPlacedSelection && !isRunning);
        SetButtonEnabled(userSnapSelectedFurnitureButton, canUseManualSnap);
        SetButtonEnabled(userDeleteSelectedFurnitureButton, canDeleteSelectedFurniture);

        bool scaleModeOn = moveModeController != null
            && moveModeController.CurrentMode == FurnitureManipulationMode.Scale;

        // Scale 紐⑤뱶媛 爰쇱?硫??붾낫湲??곹깭瑜??ル뒗?? ?ㅼ떆 耳쒕룄 異뺣퀎 踰꾪듉? ?묓엺 ?곹깭濡??쒖옉.
        if (!scaleModeOn)
        {
            scaleMoreExpanded = false;
        }

        bool baseScaleVisible = hasPlacedSelection && !isRunning && scaleModeOn;
        bool axisScaleVisible = baseScaleVisible && scaleMoreExpanded;

        SetButtonVisible(userScaleUpButton, baseScaleVisible);
        SetButtonVisible(userScaleDownButton, baseScaleVisible);
        SetButtonVisible(userScaleMoreButton, baseScaleVisible);
        if (userScaleAxisButtonsRoot != null) userScaleAxisButtonsRoot.SetActive(axisScaleVisible);

        // ?뮕 [?섏젙] 紐⑤뱺 媛援?諛곗튂 媛뺤젣 議곌굔????덉뒿?덈떎. 媛援?由ъ뒪?몃쭔 ?덉쑝硫??꾨즺 媛?ν빀?덈떎.
        bool canComplete = hasServerFurniture && !isRunning;
        if (requireAllServerFurniturePlacedBeforeComplete)
        {
            canComplete = canComplete && allPlaced;
        }

        SetButtonEnabled(userCompleteFurniturePlacementButton, canComplete);
    }

    private void UpdateManipulationModeVisuals()
    {
        FurnitureManipulationMode mode = moveModeController != null ? moveModeController.CurrentMode : FurnitureManipulationMode.None;
        UpdateToggleButtonVisuals(mode);
    }

    private void UpdateToggleButtonVisuals(FurnitureManipulationMode mode)
    {
        PlacedFurniture selected = moveModeController != null ? moveModeController.SelectedFurniture : null;
        bool selectedFixed = selected != null && selected.IsFixed;

        SetToggleButtonOn(userToggleMoveModeButton, !selectedFixed && mode == FurnitureManipulationMode.Move, toggleOnMaterial);
        SetToggleButtonOn(userToggleRotateModeButton, !selectedFixed && mode == FurnitureManipulationMode.Rotate, toggleOnMaterial);
        SetToggleButtonOn(userToggleScaleModeButton, !selectedFixed && mode == FurnitureManipulationMode.Scale, toggleOnMaterial);
        SetToggleButtonOn(userToggleFurnitureFixedButton, selectedFixed, fixedToggleOnMaterial);
        SetToggleButtonOn(userToggleFloorContactlessButton, selected != null && selected.IsFloorContactless, floorContactlessToggleOnMaterial);
    }

    private void SetToggleButtonOn(PressableButton button, bool isOn, Material activeMaterial)
    {
        if (button == null)
        {
            return;
        }

        Renderer renderer = FindToggleButtonBackplateOuterGeometry(button);
        if (renderer == null)
        {
            return;
        }

        if (toggleButtonVisualStates.TryGetValue(button, out bool currentState) && currentState == isOn)
        {
            return;
        }

        toggleButtonVisualStates[button] = isOn;

        if (!originalToggleButtonMaterials.ContainsKey(renderer))
        {
            originalToggleButtonMaterials.Add(renderer, renderer.sharedMaterials);
        }

        if (isOn && activeMaterial != null)
        {
            Material[] originalMaterials = originalToggleButtonMaterials[renderer];
            Material[] activeMaterials = new Material[originalMaterials.Length];
            for (int i = 0; i < activeMaterials.Length; i++)
            {
                activeMaterials[i] = activeMaterial;
            }

            renderer.sharedMaterials = activeMaterials;
        }
        else if (originalToggleButtonMaterials.TryGetValue(renderer, out Material[] originalMaterials))
        {
            renderer.sharedMaterials = originalMaterials;
        }
    }

    private static Renderer FindToggleButtonBackplateOuterGeometry(PressableButton button)
    {
        Renderer[] renderers = button.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in renderers)
        {
            if (renderer != null && renderer.gameObject.name == ToggleButtonBackplateOuterGeometryName)
            {
                return renderer;
            }
        }

        return null;
    }

    private void RestoreToggleButtonMaterials()
    {
        foreach (KeyValuePair<Renderer, Material[]> entry in originalToggleButtonMaterials)
        {
            if (entry.Key != null)
            {
                entry.Key.sharedMaterials = entry.Value;
            }
        }

        toggleButtonVisualStates.Clear();
    }

    private string BuildCurrentPlacementStatus(int placed, int count)
    {
        if (moveModeController != null)
        {
            if (moveModeController.CurrentMode == FurnitureManipulationMode.Move) return moveModeStatusText;
            if (moveModeController.CurrentMode == FurnitureManipulationMode.Rotate) return rotateModeStatusText;
            if (moveModeController.CurrentMode == FurnitureManipulationMode.Scale) return scaleModeStatusText;
        }

        if (serverPlacementPipeline != null && serverPlacementPipeline.AreAllPendingObjectsPlaced())
            return "紐⑤뱺 媛援ш? 諛곗튂?섏뿀?듬땲?? ?꾨즺 踰꾪듉???뚮윭 ?쒕??덉씠?섏쓣 ?쒖옉?섏꽭??";

        return $"媛援щ? ?좏깮?섍퀬 諛곗튂 踰꾪듉???꾨Ⅴ?몄슂. ({placed}/{count})";
    }

    // --- Server Request & Preview Logic ---

    private bool IsServerFurnitureRequestLocked() => serverFurnitureRequestLocked && !HasServerFurnitureResponse() && !HasServerFurnitureRequestTimedOut();
    private bool HasServerFurnitureResponse() => serverPlacementPipeline != null && serverPlacementPipeline.LastResult != null;
    private bool HasServerFurnitureRequestTimedOut() => serverFurnitureRequestLocked && serverFurnitureRequestStartedRealtime >= 0 && (Time.realtimeSinceStartup - serverFurnitureRequestStartedRealtime >= serverFurnitureRequestTimeoutSeconds);
    private void BeginServerFurnitureRequestLock() { serverFurnitureRequestLocked = true; serverFurnitureRequestStartedRealtime = Time.realtimeSinceStartup; }
    private void ClearServerFurnitureRequestLock(string reason) { serverFurnitureRequestLocked = false; serverFurnitureRequestStartedRealtime = -1f; }
    private string BuildServerFurnitureRequestLockStatus() => $"?쒕쾭?먯꽌 媛援щ? ?앹꽦 以묒엯?덈떎. ({Mathf.CeilToInt(serverFurnitureRequestTimeoutSeconds - (Time.realtimeSinceStartup - serverFurnitureRequestStartedRealtime))}珥??⑥쓬)";

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
    private static string BuildFurnitureName(FurnitureServerResultObject obj, int index) => (obj == null || string.IsNullOrWhiteSpace(obj.label)) ? $"媛援?{index + 1}" : obj.label;

    // --- Click Actions ---

    public void OnClickPlaceFurnitureFromServer()
    {
        if (serverPlacementPipeline == null || IsServerFurnitureRequestLocked()) return;
        uiManager?.ShowNotification("?쒕쾭?먯꽌 媛援??곗씠?곕? 媛?몄삤??以묒엯?덈떎...");
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
        UpdateManipulationModeVisuals();
        RefreshServerFurnitureListView();
    }

    public void OnClickToggleRotateMode()
    {
        if (moveModeController == null) return;
        moveModeController.ToggleRotateMode();
        UpdateManipulationModeVisuals();
        RefreshServerFurnitureListView();
    }

    public void OnClickToggleScaleMode()
    {
        if (moveModeController == null) return;
        moveModeController.ToggleScaleMode();
        UpdateManipulationModeVisuals();
        RefreshServerFurnitureListView();
    }

    public void OnClickToggleFurnitureFixed()
    {
        PlacedFurniture selected = moveModeController != null ? moveModeController.SelectedFurniture : null;
        if (selected == null) return;

        selected.ToggleFixed();
        string status;
        if (selected.IsFixed)
        {
            // ?꾩껜 ?대룞 紐⑤뱶瑜??꾨㈃ ?대뼡 媛援щ룄 ?곗튂/?좏깮?????놁뼱 紐⑤뱺 踰꾪듉??臾대젰?붾맂??
            // ???留덉?留됱쑝濡??꾨Ⅸ 鍮꾧퀬??媛援щ줈 ?좏깮????꺼 ?⑤꼸 踰꾪듉??怨꾩냽 ?숈옉?섍쾶 ?쒕떎.
            bool movedSelection = moveModeController != null && moveModeController.SelectLastPressedNonFixedFurniture();
            status = movedSelection
                ? $"{selected.DisplayName}: 怨좎젙?? ?좏깮??留덉?留됱쑝濡??꾨Ⅸ 媛援щ줈 ?대룞?덉뒿?덈떎."
                : $"{selected.DisplayName}: 怨좎젙?? ?대룞/?뚯쟾/?ш린 議곗젙???좉꼈?듬땲??";
        }
        else
        {
            status = $"{selected.DisplayName}: 怨좎젙 ?댁젣??";
        }

        UpdateManipulationModeVisuals();
        RefreshServerFurnitureListView();
        SetServerFurnitureStatus(status);
    }

    public void OnClickToggleFloorContactless()
    {
        PlacedFurniture selected = moveModeController != null ? moveModeController.SelectedFurniture : null;
        if (selected == null) return;

        selected.ToggleFloorContactless();
        if (selected.IsFloorContactless && roomGeometryProvider != null &&
            roomGeometryProvider.TrySnapToNearestWall(selected.transform.position, selected.wallSnapDistance, out Vector3 snapPos, out Vector3 snapNormal))
        {
            selected.transform.position = snapPos + snapNormal * Mathf.Max(0.0f, selected.wallSnapOffsetMeters);
            selected.transform.rotation = Quaternion.LookRotation(snapNormal);
            roomGeometryProvider.ResolveWallPenetrationAfterSnap(selected);
            selected.SaveCurrentPoseAsValid();
        }

        string status = $"{selected.DisplayName}: floor contactless {(selected.IsFloorContactless ? "enabled" : "disabled")}.";
        UpdateManipulationModeVisuals();
        RefreshServerFurnitureListView();
        SetServerFurnitureStatus(status);
    }

    public void OnClickSnapSelectedFurniture()
    {
        PlacedFurniture selected = moveModeController != null ? moveModeController.SelectedFurniture : null;
        if (selected == null)
        {
            SetServerFurnitureStatus("No selected furniture to snap.");
            return;
        }

        if (selected.IsFixed)
        {
            SetServerFurnitureStatus($"{selected.DisplayName}: fixed furniture cannot be snapped.");
            return;
        }

        if (roomGeometryProvider == null)
        {
            SetServerFurnitureStatus("Room geometry is not available.");
            return;
        }

        float snapDistance = Mathf.Max(selected.nearbySurfaceSnapDistance, selected.wallSnapDistance);
        bool snapped = roomGeometryProvider.ApplyNearbySurfaceSnap(
            selected,
            snapDistance,
            out bool snappedToWall,
            out Vector3 snappedWallNormal);

        if (snapped && snappedToWall && snappedWallNormal.sqrMagnitude > 0.0001f)
        {
            selected.transform.rotation = Quaternion.LookRotation(snappedWallNormal);
            roomGeometryProvider.ApplyNearbySurfaceSnap(selected, snapDistance, out _, out _);
        }
        else if (!snapped && selected.IsFloorContactless &&
                 roomGeometryProvider.TrySnapToNearestWall(selected.transform.position, selected.wallSnapDistance, out Vector3 snapPos, out Vector3 snapNormal))
        {
            selected.transform.position = snapPos + snapNormal * Mathf.Max(0.0f, selected.wallSnapOffsetMeters);
            selected.transform.rotation = Quaternion.LookRotation(snapNormal);
            snapped = true;
            snappedToWall = true;
        }

        string status;
        if (snapped)
        {
            roomGeometryProvider.ResolveWallPenetrationAfterSnap(selected);
            selected.SaveCurrentPoseAsValid();
            status = $"{selected.DisplayName}: snapped to nearest {(snappedToWall ? "wall" : "furniture")}.";
        }
        else
        {
            status = $"{selected.DisplayName}: no wall or furniture close enough to snap.";
        }

        UpdateManipulationModeVisuals();
        RefreshServerFurnitureListView();
        SetServerFurnitureStatus(status);
    }

    public void OnClickDeleteSelectedFurniture()
    {
        PlacedFurniture selected = moveModeController != null ? moveModeController.SelectedFurniture : null;
        if (selected == null)
        {
            SetServerFurnitureStatus("??젣??媛援ш? ?좏깮?섏? ?딆븯?듬땲??");
            return;
        }

        EnsureFurniturePlacementReferences();
        string deletedName = selected.DisplayName;
        bool deleted = furniturePlacementManager != null
            ? furniturePlacementManager.DeleteFurniture(selected)
            : false;

        if (!deleted)
        {
            SetServerFurnitureStatus($"{deletedName}: 媛援???젣???ㅽ뙣?덉뒿?덈떎.");
            return;
        }

        UpdateManipulationModeVisuals();
        RefreshServerFurnitureListView();
        SetServerFurnitureStatus($"{deletedName}: ??젣?섏뿀?듬땲??");
    }

    public void OnClickScaleUp() => ApplyScaleStepToSelected(Mathf.Max(1.0001f, scaleStepFactor));
    public void OnClickScaleDown() => ApplyScaleStepToSelected(1f / Mathf.Max(1.0001f, scaleStepFactor));

    private void ApplyScaleStepToSelected(float factor)
    {
        PlacedFurniture selected = moveModeController != null ? moveModeController.SelectedFurniture : null;
        if (selected == null)
        {
            SetServerFurnitureStatus("?ш린瑜?議곗젅??媛援ш? ?좏깮?섏? ?딆븯?듬땲??");
            return;
        }

        FurnitureScaleStepResult result = selected.ApplyUniformScaleStep(factor);
        if (result == FurnitureScaleStepResult.BlockedByRoomHeight)
        {
            uiManager?.ShowNotification("해당 가구는 그 위치에서 더 커질 수 없습니다");
        }
        else if (result == FurnitureScaleStepResult.BlockedByOverlap)
        {
            uiManager?.ShowNotification("해당 가구는 그 위치에서 더 커질 수 없습니다");
        }
        else if (result == FurnitureScaleStepResult.BlockedByFixed)
        {
            uiManager?.ShowNotification("怨좎젙??媛援щ뒗 ?ш린瑜?議곗젙?????놁뒿?덈떎.");
        }
        RefreshServerFurnitureListView();
    }

    public void OnClickScaleMore()
    {
        bool scaleModeOn = moveModeController != null
            && moveModeController.CurrentMode == FurnitureManipulationMode.Scale;
        if (!scaleModeOn) return;

        scaleMoreExpanded = !scaleMoreExpanded;
        RefreshServerFurnitureListView();
    }

    public void OnClickScaleXUp() => ApplyAxisScaleStepToSelected(0, Mathf.Max(1.0001f, axisScaleStepFactor));
    public void OnClickScaleXDown() => ApplyAxisScaleStepToSelected(0, 1f / Mathf.Max(1.0001f, axisScaleStepFactor));
    public void OnClickScaleYUp() => ApplyAxisScaleStepToSelected(1, Mathf.Max(1.0001f, axisScaleStepFactor));
    public void OnClickScaleYDown() => ApplyAxisScaleStepToSelected(1, 1f / Mathf.Max(1.0001f, axisScaleStepFactor));
    public void OnClickScaleZUp() => ApplyAxisScaleStepToSelected(2, Mathf.Max(1.0001f, axisScaleStepFactor));
    public void OnClickScaleZDown() => ApplyAxisScaleStepToSelected(2, 1f / Mathf.Max(1.0001f, axisScaleStepFactor));

    private void ApplyAxisScaleStepToSelected(int axis, float factor)
    {
        PlacedFurniture selected = moveModeController != null ? moveModeController.SelectedFurniture : null;
        if (selected == null)
        {
            SetServerFurnitureStatus("?ш린瑜?議곗젅??媛援ш? ?좏깮?섏? ?딆븯?듬땲??");
            return;
        }

        FurnitureScaleStepResult result = selected.ApplyAxisScaleStep(axis, factor);
        if (result == FurnitureScaleStepResult.BlockedByRoomHeight)
        {
            uiManager?.ShowNotification("해당 가구는 그 위치에서 더 커질 수 없습니다");
        }
        else if (result == FurnitureScaleStepResult.BlockedByOverlap)
        {
            uiManager?.ShowNotification("해당 가구는 그 위치에서 더 커질 수 없습니다");
        }
        else if (result == FurnitureScaleStepResult.BlockedByFixed)
        {
            uiManager?.ShowNotification("怨좎젙??媛援щ뒗 ?ш린瑜?議곗젙?????놁뒿?덈떎.");
        }
        RefreshServerFurnitureListView();
    }

    // ?ㅼ???+/- 踰꾪듉??袁??꾨Ⅴ怨??덉쑝硫??꾨Ⅸ ?쒓컙 1?? ?댄썑 scaleRepeatIntervalSeconds留덈떎 諛섎났 ?곸슜?쒕떎.
    private void UpdateScaleButtonHoldRepeat()
    {
        HandleHoldRepeat(userScaleUpButton, ref scaleUpHeldActive, ref scaleUpNextRepeatTime, OnClickScaleUp);
        HandleHoldRepeat(userScaleDownButton, ref scaleDownHeldActive, ref scaleDownNextRepeatTime, OnClickScaleDown);
    }

    private void HandleHoldRepeat(PressableButton button, ref bool heldActive, ref float nextRepeatTime, System.Action fire)
    {
        bool pressed = button != null && button.gameObject.activeInHierarchy && button.enabled && button.isSelected;
        if (!pressed)
        {
            heldActive = false;
            return;
        }

        float now = Time.unscaledTime;
        float interval = Mathf.Max(0.05f, scaleRepeatIntervalSeconds);

        if (!heldActive)
        {
            heldActive = true;
            fire();
            nextRepeatTime = now + interval;
            return;
        }

        if (now >= nextRepeatTime)
        {
            fire();
            nextRepeatTime = now + interval;
        }
    }

    public void OnClickCompleteFurniturePlacement()
    {
        // ?뮕 [?섏젙] 媛뺤젣 諛곗튂 議곌굔 濡쒖쭅???쒓굅?섍굅???ㅼ젙媛믪뿉 ?곕Ⅴ?꾨줉 蹂寃쏀뻽?듬땲??
        if (requireAllServerFurniturePlacedBeforeComplete && serverPlacementPipeline != null && !serverPlacementPipeline.AreAllPendingObjectsPlaced())
        {
            uiManager?.ShowNotification("紐⑤뱺 ?쒕쾭 媛援щ? 諛곗튂?댁빞 ?⑸땲??");
            return;
        }

        // 諛곗튂 議곌굔 寃利? 留ㅻ쾲 ?꾩껜 ?ы솗?명븯???꾨컲 媛援щ쭔 誘몄땐議?material濡??꾪솚 ?쒖떆?쒕떎.
        // ?꾨컲 媛援ш? ?섎굹?쇰룄 ?덉쑝硫??쒕??덉씠??吏꾪뻾??李⑤떒?쒕떎.
        EnsureFurniturePlacementReferences();
        if (furniturePlacementManager != null)
        {
            List<PlacedFurniture> invalidFurnitures = furniturePlacementManager.EvaluatePlacementConditions();
            foreach (PlacedFurniture furniture in furniturePlacementManager.PlacedFurnitures)
            {
                if (furniture == null) continue;
                furniture.SetPlacementValidationFailed(invalidFurnitures.Contains(furniture));
            }
            moveModeController?.RefreshPlacementValidityVisuals();

            if (invalidFurnitures.Count > 0)
            {
                uiManager?.ShowNotification($"諛곗튂 議곌굔??留뚯”?섏? ?딅뒗 媛援ш? {invalidFurnitures.Count}媛??덉뒿?덈떎. ?쒖떆??媛援щ? ?ㅼ떆 諛곗튂?????꾨즺 踰꾪듉???꾨Ⅴ?몄슂.");
                RefreshServerFurnitureListView();
                return;
            }
        }

        moveModeController?.SetManipulationMode(FurnitureManipulationMode.None);

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

    // ?뮕 [?섏젙] T ?뺤떇 ?ㅻ쪟 ?닿껐???꾪빐 void? 紐낇솗??UnityEngine.Object ?ъ슜
    private static void SetButtonEnabled(PressableButton b, bool e) { if (b != null) b.enabled = e; }
    private static void SetButtonVisible(PressableButton b, bool v) { if (b != null) { b.gameObject.SetActive(v); b.enabled = v; } }
    private static T FindFirst<T>() where T : UnityEngine.Object { T[] objs = FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None); return objs != null && objs.Length > 0 ? objs[0] : null; }
}

using UnityEngine;
using TMPro;
using System.Collections;
using UnityEngine.XR.Interaction.Toolkit.UI;

public enum RoomUIState { RoomSizeState, RoomSimulationState }

public class RoomBuildUIManager : MonoBehaviour
{
    [Header("Floor Setting")]
    [SerializeField] private GameObject floorSettingBasePanel;
    [SerializeField] private TMP_InputField inputFieldRoomFloor;

    [Header("Panels")]
    [SerializeField] private GameObject checkPanel;
    [SerializeField] private GameObject bottomPanel;
    [SerializeField] private GameObject simulationPanel;
    [SerializeField] private GameObject settingBasePanel;

    [Header("Alert UI")]
    [SerializeField] private RectTransform alertUI;
    [SerializeField] private TextMeshProUGUI alertText;
    [SerializeField, Range(0f, 1f)] private float alertSlideRatio = 0.1f;

    [SerializeField] EarthquakeManager EarthquakeManager;

    // ─────────────────────────────────────────────
    //  HoloLens 2 World Space UI 설정
    // ─────────────────────────────────────────────
    [Header("HoloLens 2 World Space UI")]
    [Tooltip("Canvas를 사용자 앞 몇 미터에 배치할지")]
    [SerializeField] private float uiDistance = 1.2f;

    [Tooltip("Canvas 아래 오프셋 (눈 높이 기준 내림)")]
    [SerializeField] private float uiVerticalOffset = -0.1f;

    [Tooltip("태그어롱 이동 속도")]
    [SerializeField] private float followSpeed = 2f;

    [Tooltip("이 각도를 벗어나면 Canvas가 따라옴 (수평)")]
    [SerializeField] private float followAngleThreshold = 35f;

    private Canvas mainCanvas;
    private Transform cameraTransform;
    private bool canvasInitialized = false;

    // ─────────────────────────────────────────────

    private int roomFloor;
    private Coroutine alertCoroutine;
    private RoomUIState currentState;
    private Vector2 alertHiddenPos;
    private Vector2 alertVisiblePos;

    public int RoomFloor => roomFloor;

    void Start()
    {
        floorSettingBasePanel.SetActive(true);
        checkPanel.SetActive(false);
        settingBasePanel.SetActive(false);

        if (EarthquakeManager == null)
            EarthquakeManager = FindAnyObjectByType<EarthquakeManager>();

        if (alertUI != null)
        {
            float canvasHeight = ((RectTransform)alertUI.root).rect.height;
            float slideDistance = canvasHeight * alertSlideRatio;
            alertVisiblePos = alertUI.anchoredPosition;
            alertHiddenPos  = alertVisiblePos + new Vector2(0, slideDistance);
            alertUI.anchoredPosition = alertHiddenPos;
        }

        SetUIState(RoomUIState.RoomSizeState);

        // HoloLens 2: World Space Canvas 설정
        SetupHoloLensCanvas();
    }

    void Update()
    {
        // HoloLens 2: Canvas 태그어롱
        if (canvasInitialized)
            UpdateCanvasFollowBehavior();
    }

    // ─────────────────────────────────────────────
    //  HoloLens 2: Canvas를 World Space로 전환
    // ─────────────────────────────────────────────
    private void SetupHoloLensCanvas()
    {
        // floorSettingBasePanel → 부모 Canvas 탐색
        mainCanvas = floorSettingBasePanel?.GetComponentInParent<Canvas>();
        if (mainCanvas == null)
        {
            Debug.LogWarning("[RoomBuildUIManager] Canvas를 찾을 수 없습니다.");
            return;
        }

        // Screen Space → World Space 전환
        mainCanvas.renderMode  = RenderMode.WorldSpace;
        mainCanvas.worldCamera = Camera.main;

        // 1픽셀 = 0.001미터 (1920px Canvas → 약 1.9m 폭)
        mainCanvas.transform.localScale = Vector3.one * 0.001f;

        // MRTK3 손 추적 호환: GraphicRaycaster → TrackedDeviceGraphicRaycaster
        ReplaceGraphicRaycaster(mainCanvas);

        if (Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
            PlaceCanvasInFrontOfUser();
            canvasInitialized = true;
        }
    }

    // GraphicRaycaster → TrackedDeviceGraphicRaycaster 교체
    // MRTK3 손 추적 Ray가 Canvas UI를 인식하려면 TrackedDeviceGraphicRaycaster 필요
    private void ReplaceGraphicRaycaster(Canvas canvas)
    {
        var legacy = canvas.GetComponent<UnityEngine.UI.GraphicRaycaster>();
        if (legacy != null)
            Destroy(legacy);

        if (canvas.GetComponent<TrackedDeviceGraphicRaycaster>() == null)
            canvas.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
    }

    // Canvas를 사용자 정면에 배치
    private void PlaceCanvasInFrontOfUser()
    {
        Vector3 fwd = GetFlatForward();
        Vector3 targetPos = cameraTransform.position
                            + fwd * uiDistance
                            + Vector3.up * uiVerticalOffset;

        mainCanvas.transform.position = targetPos;
        mainCanvas.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
    }

    // 태그어롱: 사용자가 일정 각도 이상 벗어나면 Canvas가 따라옴
    private void UpdateCanvasFollowBehavior()
    {
        if (mainCanvas == null || cameraTransform == null) return;

        Vector3 fwd    = GetFlatForward();
        Vector3 toUI   = mainCanvas.transform.position - cameraTransform.position;
        toUI.y = 0;

        if (toUI.sqrMagnitude < 0.001f) return;

        float angle = Vector3.Angle(fwd, toUI.normalized);
        if (angle > followAngleThreshold)
        {
            Vector3 targetPos = cameraTransform.position
                                + fwd * uiDistance
                                + Vector3.up * uiVerticalOffset;

            mainCanvas.transform.position = Vector3.Lerp(
                mainCanvas.transform.position, targetPos,
                Time.deltaTime * followSpeed);

            Quaternion targetRot = Quaternion.LookRotation(fwd, Vector3.up);
            mainCanvas.transform.rotation = Quaternion.Slerp(
                mainCanvas.transform.rotation, targetRot,
                Time.deltaTime * followSpeed);
        }
    }

    // 수평 방향 Forward (Y 제거)
    private Vector3 GetFlatForward()
    {
        if (cameraTransform == null) return Vector3.forward;
        Vector3 fwd = cameraTransform.forward;
        fwd.y = 0;
        return fwd.sqrMagnitude > 0.001f ? fwd.normalized : Vector3.forward;
    }

    // ─────────────────────────────────────────────
    //  Floor Setting
    // ─────────────────────────────────────────────
    public void OnConfirmFloorSetting()
    {
        string input = inputFieldRoomFloor.text;

        if (!int.TryParse(input, out int floor) || floor < 1)
        {
            ShowAlert("1 이상의 정수를 입력해주세요.");
            return;
        }

        roomFloor = floor;
        floorSettingBasePanel.SetActive(false);
        if (EarthquakeManager != null)
            EarthquakeManager.SetFloorValue(floor);
    }

    // ─────────────────────────────────────────────
    //  Check Panel
    // ─────────────────────────────────────────────
    public void ShowCheckPanel() => checkPanel.SetActive(true);
    public void HideCheckPanel() => checkPanel.SetActive(false);

    // ─────────────────────────────────────────────
    //  UI State
    // ─────────────────────────────────────────────
    public void SetUIState(RoomUIState state)
    {
        currentState = state;
        switch (state)
        {
            case RoomUIState.RoomSizeState:
                bottomPanel.SetActive(true);
                simulationPanel.SetActive(false);
                break;
            case RoomUIState.RoomSimulationState:
                bottomPanel.SetActive(false);
                simulationPanel.SetActive(true);
                break;
        }
    }

    public void EnterSimulationState() => SetUIState(RoomUIState.RoomSimulationState);

    // ─────────────────────────────────────────────
    //  Setting Panel
    // ─────────────────────────────────────────────
    public void ShowSettingBasePanel() => settingBasePanel.SetActive(true);
    public void HideSettingBasePanel() => settingBasePanel.SetActive(false);

    // ─────────────────────────────────────────────
    //  Alert UI
    // ─────────────────────────────────────────────
    public void ShowAlert(string message)
    {
        if (alertUI == null || alertText == null) return;
        if (alertCoroutine != null) StopCoroutine(alertCoroutine);
        alertText.text = message;
        alertCoroutine = StartCoroutine(AlertSlideCoroutine());
    }

    private IEnumerator AlertSlideCoroutine()
    {
        alertUI.anchoredPosition = alertHiddenPos;

        while (Vector2.Distance(alertUI.anchoredPosition, alertVisiblePos) > 0.5f)
        {
            alertUI.anchoredPosition = Vector2.Lerp(
                alertUI.anchoredPosition, alertVisiblePos, Time.deltaTime * 8f);
            yield return null;
        }
        alertUI.anchoredPosition = alertVisiblePos;
        yield return new WaitForSeconds(1f);

        while (Vector2.Distance(alertUI.anchoredPosition, alertHiddenPos) > 0.5f)
        {
            alertUI.anchoredPosition = Vector2.Lerp(
                alertUI.anchoredPosition, alertHiddenPos, Time.deltaTime * 8f);
            yield return null;
        }
        alertUI.anchoredPosition = alertHiddenPos;
        alertCoroutine = null;
    }
}

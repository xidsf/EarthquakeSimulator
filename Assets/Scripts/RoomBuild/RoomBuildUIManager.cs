using UnityEngine;
using TMPro;
using System.Collections;
using UnityEngine.XR.Interaction.Toolkit.UI;

public enum RoomUIState { RoomSizeState, RoomSimulationState }

public class RoomBuildUIManager : MonoBehaviour
{
    [Header("폰트")]
    [Tooltip("모든 TMP 텍스트에 공통으로 적용할 폰트 에셋")]
    [SerializeField] private TMP_FontAsset uiFont;

    /// <summary>외부(RoomBuilder 등)에서 동일 폰트를 사용할 때 참조</summary>
    public TMP_FontAsset UIFont => uiFont;

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

    [Header("HoloLens 2 World Space UI")]
    [SerializeField] private Canvas mainCanvas;
    
    [Tooltip("Canvas를 사용자 앞 몇 미터에 배치할지")]
    [SerializeField] private float uiDistance = 1.2f;

    [Tooltip("Canvas 아래 오프셋 (눈 높이 기준 내림)")]
    [SerializeField] private float uiVerticalOffset = -0.1f;

    [Tooltip("Canvas 추종 속도")]
    [SerializeField] private float followSpeed = 5f;

    private CanvasGroup mainCanvasGroup;
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
        if (floorSettingBasePanel != null) floorSettingBasePanel.SetActive(true);
        if (checkPanel           != null) checkPanel.SetActive(false);
        if (settingBasePanel     != null) settingBasePanel.SetActive(false);

        if (EarthquakeManager == null)
            EarthquakeManager = FindAnyObjectByType<EarthquakeManager>();

        if (mainCanvas != null)
        {
            mainCanvasGroup = mainCanvas.GetComponent<CanvasGroup>();
            // 캔버스 초기화 (카메라 할당 및 추종 활성화)
            if (Camera.main != null)
            {
                mainCanvas.worldCamera = Camera.main;
                cameraTransform = Camera.main.transform;
                canvasInitialized = true;
            }
        }

        if (alertUI != null)
        {
            float canvasHeight = ((RectTransform)alertUI.root).rect.height;
            float slideDistance = canvasHeight * alertSlideRatio;
            alertVisiblePos = alertUI.anchoredPosition;
            alertHiddenPos  = alertVisiblePos + new Vector2(0, slideDistance);
            alertUI.anchoredPosition = alertHiddenPos;
        }

        SetUIState(RoomUIState.RoomSizeState);

        // 설정된 폰트를 Canvas 하위 모든 TMP에 적용
        ApplyFontToCanvas();
    }

    void Update()
    {
        // HoloLens 2: Canvas 태그어롱
        if (canvasInitialized)
            UpdateCanvasFollowBehavior();
    }

    // Canvas 시야 추종 (매 프레임 Lerp)
    private void UpdateCanvasFollowBehavior()
    {
        if (mainCanvas == null || cameraTransform == null) return;

        Vector3 fwd = GetFlatForward();

        Vector3 targetPos = cameraTransform.position
                          + fwd * uiDistance
                          + Vector3.up * uiVerticalOffset;
        Quaternion targetRot = Quaternion.LookRotation(fwd, Vector3.up);

        // 매 프레임 Lerp → 부드러운 추종
        float t = Time.deltaTime * followSpeed;
        mainCanvas.transform.position = Vector3.Lerp(
            mainCanvas.transform.position, targetPos, t);
        mainCanvas.transform.rotation = Quaternion.Slerp(
            mainCanvas.transform.rotation, targetRot, t);
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
        if (inputFieldRoomFloor == null) return;

        string input = inputFieldRoomFloor.text;

        if (!int.TryParse(input, out int floor) || floor < 1)
        {
            ShowAlert("1 이상의 정수를 입력해주세요.");
            return;
        }

        roomFloor = floor;
        if (floorSettingBasePanel != null) floorSettingBasePanel.SetActive(false);
        if (EarthquakeManager != null)
            EarthquakeManager.SetFloorValue(floor);
    }

    // ─────────────────────────────────────────────
    //  Check Panel
    // ─────────────────────────────────────────────
    public void ShowCheckPanel() { if (checkPanel != null) checkPanel.SetActive(true); }
    public void HideCheckPanel() { if (checkPanel != null) checkPanel.SetActive(false); }

    // ─────────────────────────────────────────────
    //  UI State
    // ─────────────────────────────────────────────
    public void SetUIState(RoomUIState state)
    {
        currentState = state;
        switch (state)
        {
            case RoomUIState.RoomSizeState:
                if (bottomPanel      != null) bottomPanel.SetActive(true);
                if (simulationPanel  != null) simulationPanel.SetActive(false);
                break;
            case RoomUIState.RoomSimulationState:
                if (bottomPanel      != null) bottomPanel.SetActive(false);
                if (simulationPanel  != null) simulationPanel.SetActive(true);
                break;
        }
    }

    public void EnterSimulationState() => SetUIState(RoomUIState.RoomSimulationState);

    // ─────────────────────────────────────────────
    //  Setting Panel
    // ─────────────────────────────────────────────
    public void ShowSettingBasePanel() { if (settingBasePanel != null) settingBasePanel.SetActive(true); }
    public void HideSettingBasePanel() { if (settingBasePanel != null) settingBasePanel.SetActive(false); }

    // ─────────────────────────────────────────────
    //  폰트 일괄 적용
    // ─────────────────────────────────────────────
    private void ApplyFontToCanvas()
    {
        if (uiFont == null || mainCanvas == null) return;

        // 일반 TMP 텍스트
        foreach (var tmp in mainCanvas.GetComponentsInChildren<TextMeshProUGUI>(true))
            tmp.font = uiFont;

        // InputField 내부 텍스트 / 플레이스홀더
        foreach (var input in mainCanvas.GetComponentsInChildren<TMP_InputField>(true))
        {
            if (input.textComponent != null)
                input.textComponent.font = uiFont;
            if (input.placeholder is TextMeshProUGUI ph)
                ph.font = uiFont;
        }
    }

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

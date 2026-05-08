using UnityEngine;
using TMPro;
using System.Collections;

public enum RoomUIStateMobile { RoomSizeState, RoomSimulationState }

public class RoomBuildUIManagerMobile : MonoBehaviour
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

    private int roomFloor;
    private Coroutine alertCoroutine;
    private RoomUIStateMobile currentState;
    private Vector2 alertHiddenPos;
    private Vector2 alertVisiblePos;

    public int RoomFloor => roomFloor;

    void Start()
    {
        floorSettingBasePanel.SetActive(true);
        checkPanel.SetActive(false);
        settingBasePanel.SetActive(false);
        if(EarthquakeManager == null)
        {
            EarthquakeManager = FindAnyObjectByType<EarthquakeManager>();
        }

        if (alertUI != null)
        {
            float canvasHeight = ((RectTransform)alertUI.root).rect.height;
            float slideDistance = canvasHeight * alertSlideRatio;
            alertVisiblePos = alertUI.anchoredPosition;
            alertHiddenPos = alertVisiblePos + new Vector2(0, slideDistance);
            alertUI.anchoredPosition = alertHiddenPos;
        }

        SetUIState(RoomUIStateMobile.RoomSizeState);
    }

    // --- Floor Setting ---

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
        if(EarthquakeManager != null)
            EarthquakeManager.SetFloorValue(floor);
    }

    // --- Check Panel ---

    public void ShowCheckPanel()
    {
        checkPanel.SetActive(true);
    }

    public void HideCheckPanel()
    {
        checkPanel.SetActive(false);
    }

    // --- UI State ---

    public void SetUIState(RoomUIStateMobile state)
    {
        currentState = state;

        switch (state)
        {
            case RoomUIStateMobile.RoomSizeState:
                bottomPanel.SetActive(true);
                simulationPanel.SetActive(false);
                break;
            case RoomUIStateMobile.RoomSimulationState:
                bottomPanel.SetActive(false);
                simulationPanel.SetActive(true);
                break;
        }
    }

    public void EnterSimulationState()
    {
        SetUIState(RoomUIStateMobile.RoomSimulationState);
    }

    // --- Setting Panel ---

    public void ShowSettingBasePanel()
    {
        settingBasePanel.SetActive(true);
    }

    public void HideSettingBasePanel()
    {
        settingBasePanel.SetActive(false);
    }

    // --- Alert UI ---

    public void ShowAlert(string message)
    {
        if (alertUI == null || alertText == null) return;

        if (alertCoroutine != null)
            StopCoroutine(alertCoroutine);

        alertText.text = message;
        alertCoroutine = StartCoroutine(AlertSlideCoroutine());
    }

    private IEnumerator AlertSlideCoroutine()
    {
        // 위(화면 밖)에서 아래(화면 안)로 슬라이드 인
        alertUI.anchoredPosition = alertHiddenPos;

        while (Vector2.Distance(alertUI.anchoredPosition, alertVisiblePos) > 0.5f)
        {
            alertUI.anchoredPosition = Vector2.Lerp(alertUI.anchoredPosition, alertVisiblePos, Time.deltaTime * 8f);
            yield return null;
        }
        alertUI.anchoredPosition = alertVisiblePos;

        // 잠시 유지
        yield return new WaitForSeconds(1f);

        // 다시 위(화면 밖)로 슬라이드 아웃
        while (Vector2.Distance(alertUI.anchoredPosition, alertHiddenPos) > 0.5f)
        {
            alertUI.anchoredPosition = Vector2.Lerp(alertUI.anchoredPosition, alertHiddenPos, Time.deltaTime * 8f);
            yield return null;
        }
        alertUI.anchoredPosition = alertHiddenPos;

        alertCoroutine = null;
    }
}

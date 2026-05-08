using UnityEngine;
using TMPro;

public class BuildingInfoController : MonoBehaviour
{
    [Header("Manager References")]
    public UIManager uiManager;
    public NumpadController numpadController;

    [Header("UI Objects (본인 패널 내부)")]
    public GameObject btn_SelectBuildingType; // 건물 종류 선택 버튼
    public GameObject btn_InputFloor;         // 층수 입력 버튼

    [Header("Text References")]
    public TMP_Text text_BuildingType;        // 건물 종류 버튼 안의 텍스트
    public TMP_Text text_Floor;               // 층수 입력 버튼 안의 텍스트

    [Header("Next Panel Reference")]
    public GameObject panel_AutoScanCheck;    // 완료 시 넘어갈 다음 메인 패널

    // 안내 문구 변수
    private string placeholderText = "방이 몇층에 위치해 있나요?";

    private void OnEnable()
    {
        // 이 패널이 켜질 때마다 초기 상태를 보장합니다.
        btn_SelectBuildingType.SetActive(true);
        btn_InputFloor.SetActive(true);
        uiManager.ToggleBuildingTypeOptions(false);

        // 패널이 켜질 때 텍스트를 안내 문구로 초기화
        if (text_Floor != null)
        {
            text_Floor.text = placeholderText;
        }
    }

    // ==========================================
    // 1. 건물 종류 선택 기능
    // ==========================================

    public void OnClick_OpenBuildingTypeMenu()
    {
        btn_SelectBuildingType.SetActive(false);
        btn_InputFloor.SetActive(false);
        uiManager.ToggleBuildingTypeOptions(true);
    }

    public void OnSelect_BuildingType(string selectedType)
    {
        text_BuildingType.text = selectedType;
        uiManager.ToggleBuildingTypeOptions(false);
        btn_SelectBuildingType.SetActive(true);
        btn_InputFloor.SetActive(true);
    }

    // ==========================================
    // 2. 층수 입력 기능
    // ==========================================

    public void OnClick_OpenFloorNumpad()
    {
        numpadController.OpenNumpad(text_Floor);
    }

    // ==========================================
    // 3. 완료 및 다음 화면 전환 
    // ==========================================

    public void OnClick_CompleteBuildingInfo()
    {
        // [유효성 검사] 텍스트가 비어있거나 안내 문구 그대로라면 입력을 안 한 것으로 간주
        if (string.IsNullOrEmpty(text_Floor.text) || text_Floor.text == placeholderText)
        {
            // 시스템 메시지를 띄우고 함수 강제 종료
            uiManager.ShowWarningMessage("방이 위치한 층수를 입력해주세요.");
            return;
        }

        // 유효성 검사를 무사히 통과했다면 다음 화면으로 전환
        uiManager.ShowMainPanel(panel_AutoScanCheck);
    }
}
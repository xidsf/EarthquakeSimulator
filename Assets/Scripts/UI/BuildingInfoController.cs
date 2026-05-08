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

    private void OnEnable()
    {
        // 이 패널이 켜질 때마다 초기 상태를 보장합니다.
        btn_SelectBuildingType.SetActive(true);
        btn_InputFloor.SetActive(true);
        uiManager.ToggleBuildingTypeOptions(false);
    }

    // ==========================================
    // 1. 건물 종류 선택 기능
    // ==========================================

    // [건물 종류 선택 버튼]을 눌렀을 때
    public void OnClick_OpenBuildingTypeMenu()
    {
        // 본인 버튼들을 끄고
        btn_SelectBuildingType.SetActive(false);
        btn_InputFloor.SetActive(false);

        // 서브 패널(3개 늘어진 버튼)을 켬
        uiManager.ToggleBuildingTypeOptions(true);
    }

    // [아파트 / 빌라 / 단독주택 버튼] 중 하나를 눌렀을 때
    // (인스펙터에서 이 함수를 연결하고 매개변수로 "아파트" 등을 직접 타이핑해서 넣어줍니다)
    public void OnSelect_BuildingType(string selectedType)
    {
        // 텍스트를 선택한 종류로 변경
        text_BuildingType.text = selectedType;

        // 옵션 패널을 끄고
        uiManager.ToggleBuildingTypeOptions(false);

        // 다시 버튼들을 켬
        btn_SelectBuildingType.SetActive(true);
        btn_InputFloor.SetActive(true);
    }

    // ==========================================
    // 2. 층수 입력 기능
    // ==========================================

    // [층수 입력 버튼]을 눌렀을 때
    public void OnClick_OpenFloorNumpad()
    {
        // 넘버패드 컨트롤러에게 "이 텍스트(text_Floor)를 바꿔줘"라고 명령하며 넘버패드를 켭니다.
        numpadController.OpenNumpad(text_Floor);
    }

    // ==========================================
    // 3. 완료 및 다음 화면 전환
    // ==========================================

    // [완료 버튼]을 눌렀을 때
    public void OnClick_CompleteBuildingInfo()
    {
        // UIManager를 통해 메인 패널을 통째로 교체합니다.
        // 현재 패널(BuildingInfo)은 UIManager 내부 로직에 의해 자동으로 꺼집니다.
        uiManager.ShowMainPanel(panel_AutoScanCheck);
    }
}
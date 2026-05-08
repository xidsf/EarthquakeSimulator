using UnityEngine;

public class UIManager : MonoBehaviour
{
    [Header("1. 메인 패널 (화면 전환용)")]
    public GameObject panel_BuildingInfo;       // 1. 건물 정보 입력 판넬
    public GameObject panel_AutoScanCheck;      // 4. 둘러보며 Mesh 확인하는 버튼 모음
    public GameObject panel_ManualWall;         // 5. 수동 벽 생성 버튼 모음
    public GameObject panel_MeshAndSimSetup;    // 6. 전체 Mesh 추출 + 방 완성 + 진도 설정 UI
    public GameObject panel_Result;             // 8. 결과 UI

    [Header("2. 서브 패널 (팝업/오버레이용)")]
    public GameObject sub_BuildingTypeOptions;  // 2. 건물 종류들이 늘어져 있는 버튼 3개
    public GameObject sub_Numpad;               // 3. 넘버패드
    public GameObject sub_IntensitySlider;      // 7. 진도 설정 슬라이드

    private void Start()
    {
        // 시작할 때 모든 팝업을 끄고, 첫 번째 메인 패널만 켭니다.
        HideAllSubPanels();
        ShowMainPanel(panel_BuildingInfo);
    }

    // ==========================================
    // [핵심 기능 1] 메인 패널 전환
    // ==========================================
    public void ShowMainPanel(GameObject targetPanel)
    {
        // 1. 기존 메인 패널들을 모두 끕니다.
        panel_BuildingInfo.SetActive(false);
        panel_AutoScanCheck.SetActive(false);
        panel_ManualWall.SetActive(false);
        panel_MeshAndSimSetup.SetActive(false);
        panel_Result.SetActive(false);

        // 2. 화면이 전환될 때 열려있던 팝업(서브 패널)들도 깔끔하게 다 닫아줍니다.
        HideAllSubPanels();

        // 3. 목표 패널만 켭니다.
        if (targetPanel != null)
        {
            targetPanel.SetActive(true);
        }
    }

    // ==========================================
    // [핵심 기능 2] 서브 패널(팝업) 독립 제어
    // ==========================================

    // 서브 패널 전체 끄기 (초기화용)
    public void HideAllSubPanels()
    {
        if (sub_BuildingTypeOptions) sub_BuildingTypeOptions.SetActive(false);
        if (sub_Numpad) sub_Numpad.SetActive(false);
        if (sub_IntensitySlider) sub_IntensitySlider.SetActive(false);
    }

    // 건물 종류 선택 버튼(3개) On/Off
    public void ToggleBuildingTypeOptions(bool isActive)
    {
        if (sub_BuildingTypeOptions) sub_BuildingTypeOptions.SetActive(isActive);
    }

    // 넘버패드 On/Off
    public void ToggleNumpad(bool isActive)
    {
        if (sub_Numpad) sub_Numpad.SetActive(isActive);
    }

    // 진도 설정 슬라이드 On/Off
    public void ToggleIntensitySlider(bool isActive)
    {
        if (sub_IntensitySlider) sub_IntensitySlider.SetActive(isActive);
    }
}
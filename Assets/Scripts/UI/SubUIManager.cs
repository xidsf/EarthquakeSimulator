using UnityEngine;
using TMPro;

public class SubUIManager : MonoBehaviour
{
    [Header("서브 패널 (팝업/오버레이용)")]
    public GameObject sub_BuildingTypeOptions;  // 2. 건물 종류 선택 버튼 모음
    public GameObject sub_Numpad;               // 3. 넘버패드
    public GameObject sub_IntensitySlider;      // 7. 진도 설정 슬라이드

    [Header("시스템 메시지 UI")]
    public GameObject panel_SystemMessage;      // 경고/안내 메시지를 띄울 패널
    public TMP_Text text_SystemMessage;         // 메시지 내용

    private void Start()
    {
        HideAllSubPanels();
        HideWarningMessage();
    }

    // ==========================================
    // [서브 패널(팝업) 독립 제어]
    // ==========================================

    public void HideAllSubPanels()
    {
        if (sub_BuildingTypeOptions) sub_BuildingTypeOptions.SetActive(false);
        if (sub_Numpad) sub_Numpad.SetActive(false);
        if (sub_IntensitySlider) sub_IntensitySlider.SetActive(false);
    }

    public void ToggleBuildingTypeOptions(bool isActive)
    {
        if (sub_BuildingTypeOptions) sub_BuildingTypeOptions.SetActive(isActive);
    }

    public void ToggleNumpad(bool isActive)
    {
        if (sub_Numpad) sub_Numpad.SetActive(isActive);
    }

    public void ToggleIntensitySlider(bool isActive)
    {
        if (sub_IntensitySlider) sub_IntensitySlider.SetActive(isActive);
    }

    // ==========================================
    // [시스템 경고/안내 메시지 팝업]
    // ==========================================

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

    private void HideWarningMessage()
    {
        if (panel_SystemMessage != null)
        {
            panel_SystemMessage.SetActive(false);
        }
    }
}
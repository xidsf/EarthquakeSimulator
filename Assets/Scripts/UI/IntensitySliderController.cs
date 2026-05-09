using UnityEngine;
using TMPro;
using MixedReality.Toolkit.UX; // MRTK 슬라이더 데이터를 받기 위해 추가된 네임스페이스

public class IntensitySliderController : MonoBehaviour
{
    [Header("Manager Reference")]
    public UIManager uiManager;

    [Header("UI References")]
    public TMP_Text text_CurrentIntensity;

    // float 대신 MRTK 전용 SliderEventData를 매개변수로 받습니다.
    public void OnSliderValueUpdated(SliderEventData eventData)
    {
        // 이벤트 데이터 안에 들어있는 새로운 값을 꺼내옵니다.
        float newValue = eventData.NewValue;

        // 1. 값을 0.1 단위로 반올림 보정 (예: 8.0321 -> 8.0)
        float snappedValue = Mathf.Round(newValue * 10.0f) / 10.0f;

        // 2. 텍스트 업데이트 
        if (text_CurrentIntensity != null)
        {
            text_CurrentIntensity.text = $"현재 진도: {snappedValue:F1}";
        }
    }

    // [완료] 버튼
    public void OnClick_Done()
    {
        uiManager.ToggleIntensitySlider(false);
    }
}
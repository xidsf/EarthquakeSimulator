using UnityEngine;
using TMPro;
using MixedReality.Toolkit.UX;

public class IntensitySliderController : WorkflowPanelControllerBase
{
    [Header("UI References")]
    public TMP_Text text_CurrentIntensity;
    public PressableButton btn_Done; // 완료 버튼 연결

    protected override void Awake()
    {
        base.Awake();
    }

    private void OnEnable()
    {
        EnsureCommonReferences();
        // 완료 버튼 이벤트 연결
        if (btn_Done != null)
        {
            btn_Done.OnClicked.RemoveAllListeners();
            btn_Done.OnClicked.AddListener(OnClick_Done);
        }
    }

    public void OnSliderValueUpdated(SliderEventData eventData)
    {
        float newValue = eventData.NewValue;
        float snappedValue = Mathf.Round(newValue * 10.0f) / 10.0f;

        if (text_CurrentIntensity != null)
        {
            text_CurrentIntensity.text = $"현재 진도: {snappedValue:F1}";
        }
    }

    public void OnClick_Done()
    {
        if (uiManager != null)
        {
            uiManager.ToggleIntensitySlider(false);
        }
    }
}
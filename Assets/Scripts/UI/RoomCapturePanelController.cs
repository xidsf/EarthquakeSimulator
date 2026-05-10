using MixedReality.Toolkit.UX;
using UnityEngine;
using UnityEngine.Events;
using TMPro;

public class RoomCapturePanelController : WorkflowPanelControllerBase
{
    [Header("Main UI Control")]
    [Tooltip("비활성화할 MainPlate 오브젝트를 연결해주세요.")]
    public GameObject obj_MainPlate;

    [Header("User Buttons")]
    public PressableButton btn_Capture;
    public PressableButton btn_PopUpTip;
    public PressableButton btn_Gallery;
    public PressableButton btn_Complete;

    [Header("UI Elements")]
    [Tooltip("PopUpTip 버튼 내부의 텍스트입니다.")]
    public TMP_Text text_PopUpTipBtn;
    [Tooltip("띄우거나 숨길 CaptureTip 오브젝트입니다.")]
    public GameObject obj_CaptureTip;

    // 상태 추적
    private bool isTipVisible = false;

    // UnityAction 캐싱
    private UnityAction captureAction;
    private UnityAction popUpTipAction;
    private UnityAction galleryAction;
    private UnityAction completeAction;

    protected override void Awake()
    {
        base.Awake();
    }

    private void OnEnable()
    {
        EnsureCommonReferences();
        CreateActions();
        RegisterButtons();

        // [추가] RoomCapture 단계 진입 시 MainPlate를 비활성화합니다.
        if (obj_MainPlate != null)
        {
            obj_MainPlate.SetActive(false);
        }

        // 초기 상태 설정
        isTipVisible = false;
        UpdateTipUI();
    }

    private void OnDisable()
    {
        UnregisterButtons();
        //다음 단계에서도 MainPanel은 꺼져있어야 함
        //다다음 단계에서 킬 예정
    }

    private void CreateActions()
    {
        captureAction ??= OnClickCapture;
        popUpTipAction ??= OnClickPopUpTip;
        galleryAction ??= OnClickGallery;
        completeAction ??= OnClickComplete;
    }

    private void RegisterButtons()
    {
        AddClick(btn_Capture, captureAction);
        AddClick(btn_PopUpTip, popUpTipAction);
        AddClick(btn_Gallery, galleryAction);
        AddClick(btn_Complete, completeAction);
    }

    private void UnregisterButtons()
    {
        RemoveClick(btn_Capture, captureAction);
        RemoveClick(btn_PopUpTip, popUpTipAction);
        RemoveClick(btn_Gallery, galleryAction);
        RemoveClick(btn_Complete, completeAction);
    }

    // ==========================================
    // 버튼 이벤트 로직
    // ==========================================

    public void OnClickCapture()
    {
        // TODO: 실제 사진 촬영 로직 연동
        Debug.Log("[RoomCapture] 사진 촬영");
    }

    public void OnClickPopUpTip()
    {
        isTipVisible = !isTipVisible;
        UpdateTipUI();
    }

    private void UpdateTipUI()
    {
        if (obj_CaptureTip != null) obj_CaptureTip.SetActive(isTipVisible);

        if (text_PopUpTipBtn != null)
        {
            text_PopUpTipBtn.text = isTipVisible ? "툴팁 끄기" : "툴팁 띄우기";
        }
    }

    public void OnClickGallery()
    {
        Debug.Log("[RoomCapture] 갤러리 (기능 미구현)");
    }

    public void OnClickComplete()
    {
        // FurniturePlacement 단계로 이동 요청
        RequestWorkflowCommand(RoomBuildWorkflowManager.WorkflowCommand.CompleteRoomCapture);
    }
}
using System.Collections;
using MixedReality.Toolkit.UX;
using UnityEngine;
using UnityEngine.Events;
using TMPro;

public class RoomCapturePanelController : WorkflowPanelControllerBase
{
    [Header("User Buttons")]
    public PressableButton btn_Capture;
    public PressableButton btn_PopUpTip;
    public PressableButton btn_Gallery;
    public PressableButton btn_Complete;

    [Header("UI Elements")]
    [Tooltip("일시적으로 띄웠다 페이드아웃되는 툴팁(CaptureTip) 오브젝트입니다.")]
    public GameObject obj_CaptureTip;

    [Tooltip("MainPlate 위에서 계속 표시되어야 하는 안내 문구(ForCapture) 오브젝트입니다.")]
    public GameObject obj_ForCaptureText;

    // [추가] 뷰파인더 UI 제어를 위한 변수
    [Header("Camera Viewport")]
    [Tooltip("사용자 시선을 따라다니는 CameraViewPortUI 오브젝트를 연결해주세요.")]
    public GameObject obj_CameraViewPortUI;

    [Header("Loading UI Elements")]
    [Tooltip("가구 생성 로딩 화면의 부모 오브젝트(배경 패널 등)를 연결해주세요.")]
    public GameObject loadingUIParent;
    [Tooltip("로딩 상태를 표시할 TextMeshPro 컴포넌트를 연결해주세요.")]
    public TMP_Text text_LoadingStatus;

    [Header("Tip Animation Settings")]
    [Tooltip("도움말이 화면에 유지되는 시간(초)입니다.")]
    public float tipShowDuration = 5.0f;
    [Tooltip("도움말이 서서히 투명해지며 사라지는 시간(초)입니다.")]
    public float tipFadeDuration = 1.0f;

    private Coroutine tipSequenceCoroutine;
    private CanvasGroup tipCanvasGroup;

    // UnityAction 캐싱
    private UnityAction captureAction;
    private UnityAction popUpTipAction;
    private UnityAction galleryAction;
    private UnityAction completeAction;

    protected override void Awake()
    {
        base.Awake();
        SetupCanvasGroup();
    }

    private void SetupCanvasGroup()
    {
        if (obj_CaptureTip != null)
        {
            tipCanvasGroup = obj_CaptureTip.GetComponent<CanvasGroup>();
            if (tipCanvasGroup == null)
            {
                tipCanvasGroup = obj_CaptureTip.AddComponent<CanvasGroup>();
            }
        }
    }

    private void OnEnable()
    {
        EnsureCommonReferences();
        CreateActions();
        RegisterButtons();

        // 패널이 다시 켜질 때 모든 버튼의 상태를 활성화(초기화)합니다.
        SetAllButtonsEnabled(true);

        // 단계 진입 시 ForCapture 텍스트 활성화
        if (obj_ForCaptureText != null)
        {
            obj_ForCaptureText.SetActive(true);
        }

        // [추가] 단계 진입 시 CameraViewPortUI 활성화
        if (obj_CameraViewPortUI != null)
        {
            obj_CameraViewPortUI.SetActive(true);
        }

        // 단계 진입 시 로딩 UI 초기 숨김
        if (loadingUIParent != null) loadingUIParent.SetActive(false);

        // 단계 진입 시 툴팁만 띄웠다 사라지게 함
        ShowTipSequence();
    }

    private void OnDisable()
    {
        UnregisterButtons();
        StopTipSequence();

        // 단계가 종료될 때 안내 문구도 함께 꺼줌
        if (obj_ForCaptureText != null)
        {
            obj_ForCaptureText.SetActive(false);
        }

        // [추가] 단계가 강제로 종료되거나 다른 상태로 넘어갈 때 뷰파인더도 안전하게 꺼줌
        if (obj_CameraViewPortUI != null)
        {
            obj_CameraViewPortUI.SetActive(false);
        }
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

    private void SetAllButtonsEnabled(bool isEnabled)
    {
        if (btn_Capture != null) btn_Capture.enabled = isEnabled;
        if (btn_PopUpTip != null) btn_PopUpTip.enabled = isEnabled;
        if (btn_Gallery != null) btn_Gallery.enabled = isEnabled;
        if (btn_Complete != null) btn_Complete.enabled = isEnabled;
    }

    // ==========================================
    // 툴팁(CaptureTip) 애니메이션 로직
    // ==========================================

    public void OnClickPopUpTip()
    {
        ShowTipSequence();
    }

    private void ShowTipSequence()
    {
        StopTipSequence();

        if (obj_CaptureTip != null)
        {
            obj_CaptureTip.SetActive(true);
            if (tipCanvasGroup != null) tipCanvasGroup.alpha = 1.0f;
        }

        tipSequenceCoroutine = StartCoroutine(TipFadeOutRoutine());
    }

    private void HideTipInstantly()
    {
        StopTipSequence();

        if (obj_CaptureTip != null)
        {
            obj_CaptureTip.SetActive(false);
            if (tipCanvasGroup != null) tipCanvasGroup.alpha = 1.0f;
        }
    }

    private void StopTipSequence()
    {
        if (tipSequenceCoroutine != null)
        {
            StopCoroutine(tipSequenceCoroutine);
            tipSequenceCoroutine = null;
        }
    }

    private IEnumerator TipFadeOutRoutine()
    {
        yield return new WaitForSeconds(tipShowDuration);

        float elapsedTime = 0f;
        while (elapsedTime < tipFadeDuration)
        {
            elapsedTime += Time.deltaTime;
            if (tipCanvasGroup != null)
            {
                tipCanvasGroup.alpha = Mathf.Lerp(1.0f, 0.0f, elapsedTime / tipFadeDuration);
            }
            yield return null;
        }

        HideTipInstantly();
    }

    // ==========================================
    // Workflow 버튼 이벤트
    // ==========================================

    public void OnClickCapture() { Debug.Log("[RoomCapture] 사진 촬영 로직 실행"); }
    public void OnClickGallery() { Debug.Log("[RoomCapture] 갤러리 (기능 미구현)"); }

    public void OnClickComplete()
    {
        // 완료 버튼을 누르는 즉시 모든 버튼 비활성화 처리
        SetAllButtonsEnabled(false);

        // 가구 배치 단계로 넘어가는 즉시 안내 텍스트 비활성화
        if (obj_ForCaptureText != null)
        {
            obj_ForCaptureText.SetActive(false);
        }

        // [추가] 가구 생성 코루틴이 시작되기 직전에 뷰파인더(CameraViewPortUI)를 즉시 숨김
        if (obj_CameraViewPortUI != null)
        {
            obj_CameraViewPortUI.SetActive(false);
        }

        StartCoroutine(FurnitureGenerationRoutine());
    }

    // 가구 생성 및 UI 전환 코루틴
    private IEnumerator FurnitureGenerationRoutine()
    {
        // 1. 로딩 UI 표시 시작
        if (loadingUIParent != null) loadingUIParent.SetActive(true);

        float generationTimer = 0f;
        float dotTimer = 0f;
        int dotCount = 1;

        // 임시 가구 생성 대기 시간 (5초)
        float mockGenerationTime = 5.0f;

        while (generationTimer < mockGenerationTime)
        {
            generationTimer += Time.deltaTime;
            dotTimer += Time.deltaTime;

            // 1초마다 점 개수 순환 (. -> .. -> ... -> .)
            if (dotTimer >= 1.0f)
            {
                dotTimer = 0f;
                dotCount++;
                if (dotCount > 3) dotCount = 1;
            }

            if (text_LoadingStatus != null)
            {
                string dots = new string('.', dotCount);
                text_LoadingStatus.text = $"가구 생성중{dots}";
            }

            yield return null;
        }

        // 2. 가구 생성 완료 메시지
        if (text_LoadingStatus != null)
        {
            text_LoadingStatus.text = "가구 생성 완료!";
        }

        // 3. 2초 대기
        yield return new WaitForSeconds(2.0f);

        // 4. 상태 초기화 및 실제 다음 워크플로우로 이동
        if (loadingUIParent != null) loadingUIParent.SetActive(false);

        RequestWorkflowCommand(RoomBuildWorkflowManager.WorkflowCommand.CompleteRoomCapture);
    }
}
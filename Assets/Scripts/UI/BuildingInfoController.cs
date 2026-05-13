using UnityEngine;
using UnityEngine.Events;
using MixedReality.Toolkit.UX; // [추가] PressableButton 사용을 위함
using TMPro;

public class BuildingInfoController : WorkflowPanelControllerBase
{
    [Header("Manager References")]
    public NumpadController numpadController;

    // [수정] GameObject에서 PressableButton으로 타입 변경
    [Header("User Buttons (본인 패널 내부)")]
    public PressableButton btn_SelectBuildingType; // 건물 종류 선택 버튼
    public PressableButton btn_InputFloor;         // 층수 입력 버튼
    public PressableButton btn_CancelBuildingType; // 건물 종류 선택 취소 버튼
    public PressableButton btn_Complete;           // 완료(다음 단계) 버튼

    [Header("Text References")]
    public TMP_Text text_BuildingType;        // 건물 종류 버튼 안의 텍스트
    public TMP_Text text_Floor;               // 층수 입력 버튼 안의 텍스트

    // 안내 문구 변수
    private const string placeholderFloorText = "방이 몇층에 위치해 있나요?";
    private const string placeholderBuildingTypeText = "건물 종류 선택";

    // [추가] UnityAction 캐싱 변수
    private UnityAction openBuildingTypeMenuAction;
    private UnityAction cancelBuildingTypeMenuAction;
    private UnityAction openFloorNumpadAction;
    private UnityAction completeBuildingInfoAction;

    protected override void Awake()
    {
        base.Awake();
    }

    private void OnEnable()
    {
        EnsureCommonReferences();
        CreateActions();
        RegisterButtons();

        // 패널이 활성화될 때마다 버튼 상태를 초기화합니다.
        // PressableButton으로 바뀌었으므로 .gameObject를 통해 SetActive를 호출합니다.
        if (btn_SelectBuildingType != null) btn_SelectBuildingType.gameObject.SetActive(true);
        if (btn_InputFloor != null) btn_InputFloor.gameObject.SetActive(true);
        if (btn_Complete != null) btn_Complete.gameObject.SetActive(true);
        if (btn_CancelBuildingType != null) btn_CancelBuildingType.gameObject.SetActive(false);

        if (uiManager != null)
        {
            uiManager.ToggleBuildingTypeOptions(false);
        }

        // 텍스트를 안내 문구로 명확하게 초기화합니다.
        if (text_Floor != null) text_Floor.text = placeholderFloorText;
        if (text_BuildingType != null) text_BuildingType.text = placeholderBuildingTypeText;
    }

    private void OnDisable()
    {
        UnregisterButtons();
    }

    // ==========================================
    // [추가] 버튼 액션 생성 및 등록
    // ==========================================

    private void CreateActions()
    {
        openBuildingTypeMenuAction ??= OnClick_OpenBuildingTypeMenu;
        cancelBuildingTypeMenuAction ??= OnClick_CancelBuildingTypeMenu;
        openFloorNumpadAction ??= OnClick_OpenFloorNumpad;
        completeBuildingInfoAction ??= OnClick_CompleteBuildingInfo;
    }

    private void RegisterButtons()
    {
        AddClick(btn_SelectBuildingType, openBuildingTypeMenuAction);
        AddClick(btn_CancelBuildingType, cancelBuildingTypeMenuAction);
        AddClick(btn_InputFloor, openFloorNumpadAction);
        AddClick(btn_Complete, completeBuildingInfoAction);
    }

    private void UnregisterButtons()
    {
        RemoveClick(btn_SelectBuildingType, openBuildingTypeMenuAction);
        RemoveClick(btn_CancelBuildingType, cancelBuildingTypeMenuAction);
        RemoveClick(btn_InputFloor, openFloorNumpadAction);
        RemoveClick(btn_Complete, completeBuildingInfoAction);
    }

    // ==========================================
    // 1. 건물 종류 선택 기능
    // ==========================================

    public void OnClick_OpenBuildingTypeMenu()
    {
        if (btn_SelectBuildingType != null) btn_SelectBuildingType.gameObject.SetActive(false);
        if (btn_InputFloor != null) btn_InputFloor.gameObject.SetActive(false);
        if (btn_Complete != null) btn_Complete.gameObject.SetActive(false);
        if (btn_CancelBuildingType != null) btn_CancelBuildingType.gameObject.SetActive(true);

        if (uiManager != null) uiManager.ToggleBuildingTypeOptions(true);
    }

    public void OnClick_CancelBuildingTypeMenu()
    {
        if (uiManager != null) uiManager.ToggleBuildingTypeOptions(false);

        if (btn_SelectBuildingType != null) btn_SelectBuildingType.gameObject.SetActive(true);
        if (btn_InputFloor != null) btn_InputFloor.gameObject.SetActive(true);
        if (btn_Complete != null) btn_Complete.gameObject.SetActive(true);
        if (btn_CancelBuildingType != null) btn_CancelBuildingType.gameObject.SetActive(false);
    }

    // (참고: 이 메서드는 서브 패널의 UI 구조에서 개별적으로 호출되는 로직이므로, 
    // AddClick을 통한 자동 등록 대상에서는 제외하고 기존대로 둡니다.)
    public void OnSelect_BuildingType(string selectedType)
    {
        if (text_BuildingType != null) text_BuildingType.text = selectedType;

        if (uiManager != null) uiManager.ToggleBuildingTypeOptions(false);

        if (btn_SelectBuildingType != null) btn_SelectBuildingType.gameObject.SetActive(true);
        if (btn_InputFloor != null) btn_InputFloor.gameObject.SetActive(true);
        if (btn_Complete != null) btn_Complete.gameObject.SetActive(true);
        if (btn_CancelBuildingType != null) btn_CancelBuildingType.gameObject.SetActive(false);
    }

    // ==========================================
    // 2. 층수 입력 기능
    // ==========================================

    public void OnClick_OpenFloorNumpad()
    {
        if (numpadController != null && text_Floor != null)
        {
            numpadController.OpenNumpad(text_Floor);
        }
    }

    // ==========================================
    // 3. 완료 및 다음 화면 전환 
    // ==========================================

    public void OnClick_CompleteBuildingInfo()
    {
        // [유효성 검사 1] 건물 종류 선택 여부 확인
        if (text_BuildingType == null || string.IsNullOrWhiteSpace(text_BuildingType.text) || text_BuildingType.text == placeholderBuildingTypeText)
        {
            ShowWarning("건물 종류를 선택해주세요.");
            return;
        }

        // [유효성 검사 2] 층수 입력 여부 확인
        if (text_Floor == null || string.IsNullOrWhiteSpace(text_Floor.text) || text_Floor.text == placeholderFloorText)
        {
            ShowWarning("방이 위치한 층수를 입력해주세요.");
            return;
        }

        // [유효성 검사 3] 층수 범위 및 형식 확인 (0층 제외, 최대 100층)
        string floorInput = text_Floor.text.Replace("층", "").Trim();

        if (int.TryParse(floorInput, out int floorNumber))
        {
            if (floorNumber == 0)
            {
                ShowWarning("올바른 층수를 입력해주세요 (0층 제외).");
                return;
            }

            if (floorNumber > 100)
            {
                ShowWarning("층수는 최대 100층까지만 입력 가능합니다.");
                return;
            }

            // 불필요한 0을 제거하고 정돈된 텍스트로 갱신
            text_Floor.text = $"{floorNumber}층";
        }
        else
        {
            ShowWarning("층수는 숫자로 입력해주세요.");
            return;
        }

        // 모든 검사를 통과하면 다음 워크플로우 단계로 진행
        RequestWorkflowCommand(RoomBuildWorkflowManager.WorkflowCommand.CompleteRoomInfoInput);
    }
}

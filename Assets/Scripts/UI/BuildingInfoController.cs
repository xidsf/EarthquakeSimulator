using UnityEngine;
using UnityEngine.Events;
using MixedReality.Toolkit.UX; // [추가] PressableButton 사용을 위함
using TMPro;

public class BuildingInfoController : WorkflowPanelControllerBase
{
    [Header("Manager References")]
    public NumpadController numpadController;

    [Header("Owned Sub Panels")]
    [Tooltip("BuildingInfoController가 직접 여닫는 건물 종류 선택 패널입니다.")]
    public GameObject panel_BuildingTypeOptions;
    [Tooltip("BuildingInfoController가 직접 여닫는 필로티 여부 선택 패널입니다.")]
    public GameObject panel_PilotiOptions;
    [Tooltip("BuildingInfoController가 직접 여닫는 넘버패드 패널입니다.")]
    public GameObject panel_Numpad;
    [Tooltip("패널 참조가 비어 있으면 하위 오브젝트 이름으로 자동 탐색합니다.")]
    public bool autoFindOwnedSubPanels = true;

    // [수정] GameObject에서 PressableButton으로 타입 변경
    [Header("User Buttons (본인 패널 내부)")]
    public PressableButton btn_SelectBuildingType; // 건물 종류 선택 버튼
    public PressableButton btn_InputFloor;         // 층수 입력 버튼
    public PressableButton btn_CancelBuildingType; // 건물 종류 선택 취소 버튼
    public PressableButton btn_Complete;           // 완료(다음 단계) 버튼

    [Header("Base / Additional Info Roots")]
    [Tooltip("기본 입력(건물 종류 + 층수)을 담는 부모 오브젝트. 기본 활성화됩니다.")]
    public GameObject baseInfoObject;
    [Tooltip("추가 입력(전체 건물 층수 + 필로티)을 담는 부모 오브젝트. 기본 비활성화됩니다.")]
    public GameObject additionalInfoObject;

    [Header("Additional Info Buttons")]
    public PressableButton btn_OpenAdditionalInfo; // 추가 입력 진입 버튼 (BaseInfo 내)
    public PressableButton btn_BackToBaseInfo;     // 기본 입력으로 돌아가기 (AdditionalInfo 내)
    public PressableButton btn_InputTotalFloors;   // 전체 건물 층수 입력 버튼
    public PressableButton btn_SelectPiloti;       // 필로티 여부 선택 메뉴 열기 버튼
    public PressableButton btn_CancelPiloti;       // 필로티 여부 선택 취소 버튼

    [Header("Text References")]
    public TMP_Text text_BuildingType;        // 건물 종류 버튼 안의 텍스트
    public TMP_Text text_Floor;               // 층수 입력 버튼 안의 텍스트
    public TMP_Text text_TotalFloors;         // 전체 건물 층수 입력 버튼 안의 텍스트
    public TMP_Text text_Piloti;              // 필로티 여부 버튼 안의 텍스트

    // 안내 문구 변수
    private const string placeholderFloorText = "방이 몇층에 위치해 있나요?";
    private const string placeholderBuildingTypeText = "건물 종류 선택";
    private const string placeholderTotalFloorsText = "전체 건물 층수 (기본: 현재 층)";

    // 필로티 여부 선택 상태. 기본값은 Unknown.
    private enum PilotiSelection { Unknown, Yes, No }
    private PilotiSelection pilotiSelection = PilotiSelection.Unknown;

    // [추가] UnityAction 캐싱 변수
    private UnityAction openBuildingTypeMenuAction;
    private UnityAction cancelBuildingTypeMenuAction;
    private UnityAction openFloorNumpadAction;
    private UnityAction completeBuildingInfoAction;
    private UnityAction openAdditionalInfoAction;
    private UnityAction backToBaseInfoAction;
    private UnityAction openTotalFloorsNumpadAction;
    private UnityAction selectPilotiMenuAction;
    private UnityAction cancelPilotiMenuAction;

    protected override void Awake()
    {
        base.Awake();
    }

    private void OnEnable()
    {
        EnsureCommonReferences();
        EnsureOwnedSubPanelReferences();
        CreateActions();
        RegisterButtons();

        // 패널이 활성화될 때마다 버튼 상태를 초기화합니다.
        // PressableButton으로 바뀌었으므로 .gameObject를 통해 SetActive를 호출합니다.
        if (btn_SelectBuildingType != null) btn_SelectBuildingType.gameObject.SetActive(true);
        if (btn_InputFloor != null) btn_InputFloor.gameObject.SetActive(true);
        if (btn_Complete != null) btn_Complete.gameObject.SetActive(true);
        if (btn_OpenAdditionalInfo != null) btn_OpenAdditionalInfo.gameObject.SetActive(true);
        if (btn_CancelBuildingType != null) btn_CancelBuildingType.gameObject.SetActive(false);

        ToggleBuildingTypeOptions(false);
        TogglePilotiOptions(false);
        ToggleNumpad(false);

        if (btn_CancelPiloti != null) btn_CancelPiloti.gameObject.SetActive(false);

        // 기본은 BaseInfo 활성화, AdditionalInfo 비활성화.
        if (baseInfoObject != null) baseInfoObject.SetActive(true);
        if (additionalInfoObject != null) additionalInfoObject.SetActive(false);

        // 텍스트를 안내 문구로 명확하게 초기화합니다.
        if (text_Floor != null) text_Floor.text = placeholderFloorText;
        if (text_BuildingType != null) text_BuildingType.text = placeholderBuildingTypeText;
        if (text_TotalFloors != null) text_TotalFloors.text = placeholderTotalFloorsText;

        // 필로티 기본값은 Unknown.
        pilotiSelection = PilotiSelection.Unknown;
        UpdatePilotiText();
    }

    private void OnDisable()
    {
        UnregisterButtons();
        ToggleBuildingTypeOptions(false);
        TogglePilotiOptions(false);
        ToggleNumpad(false);
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
        openAdditionalInfoAction ??= OnClick_OpenAdditionalInfo;
        backToBaseInfoAction ??= OnClick_BackToBaseInfo;
        openTotalFloorsNumpadAction ??= OnClick_OpenTotalFloorsNumpad;
        selectPilotiMenuAction ??= OnClick_OpenPilotiMenu;
        cancelPilotiMenuAction ??= OnClick_CancelPilotiMenu;
    }

    private void RegisterButtons()
    {
        AddClick(btn_SelectBuildingType, openBuildingTypeMenuAction);
        AddClick(btn_CancelBuildingType, cancelBuildingTypeMenuAction);
        AddClick(btn_InputFloor, openFloorNumpadAction);
        AddClick(btn_Complete, completeBuildingInfoAction);
        AddClick(btn_OpenAdditionalInfo, openAdditionalInfoAction);
        AddClick(btn_BackToBaseInfo, backToBaseInfoAction);
        AddClick(btn_InputTotalFloors, openTotalFloorsNumpadAction);
        AddClick(btn_SelectPiloti, selectPilotiMenuAction);
        AddClick(btn_CancelPiloti, cancelPilotiMenuAction);
    }

    private void UnregisterButtons()
    {
        RemoveClick(btn_SelectBuildingType, openBuildingTypeMenuAction);
        RemoveClick(btn_CancelBuildingType, cancelBuildingTypeMenuAction);
        RemoveClick(btn_InputFloor, openFloorNumpadAction);
        RemoveClick(btn_Complete, completeBuildingInfoAction);
        RemoveClick(btn_OpenAdditionalInfo, openAdditionalInfoAction);
        RemoveClick(btn_BackToBaseInfo, backToBaseInfoAction);
        RemoveClick(btn_InputTotalFloors, openTotalFloorsNumpadAction);
        RemoveClick(btn_SelectPiloti, selectPilotiMenuAction);
        RemoveClick(btn_CancelPiloti, cancelPilotiMenuAction);
    }

    private void EnsureOwnedSubPanelReferences()
    {
        if (!autoFindOwnedSubPanels)
        {
            return;
        }

        if (panel_BuildingTypeOptions == null)
        {
            panel_BuildingTypeOptions = FindChildGameObjectByName(transform, "SelectBuildingButtons");
        }

        if (panel_PilotiOptions == null)
        {
            panel_PilotiOptions =
                FindChildGameObjectByName(transform, "SelectPilotiButtons") ??
                FindChildGameObjectByName(transform, "SelectPilotiOptions") ??
                FindChildGameObjectByName(transform, "PilotiOptions");
        }

        if (panel_Numpad == null && numpadController != null)
        {
            panel_Numpad = numpadController.gameObject;
        }

        if (panel_Numpad == null)
        {
            panel_Numpad = FindChildGameObjectByName(transform, "NumberPad");
        }
    }

    private static GameObject FindChildGameObjectByName(Transform root, string objectName)
    {
        if (root == null || string.IsNullOrEmpty(objectName))
        {
            return null;
        }

        Transform[] children = root.GetComponentsInChildren<Transform>(true);
        foreach (Transform child in children)
        {
            if (child != null && child.name == objectName)
            {
                return child.gameObject;
            }
        }

        return null;
    }

    private void ToggleBuildingTypeOptions(bool isActive)
    {
        EnsureOwnedSubPanelReferences();
        if (panel_BuildingTypeOptions != null) panel_BuildingTypeOptions.SetActive(isActive);
    }

    private void TogglePilotiOptions(bool isActive)
    {
        EnsureOwnedSubPanelReferences();
        if (panel_PilotiOptions != null) panel_PilotiOptions.SetActive(isActive);
    }

    private void ToggleNumpad(bool isActive)
    {
        EnsureOwnedSubPanelReferences();
        if (panel_Numpad != null) panel_Numpad.SetActive(isActive);
    }

    private void HideOwnedSubPanels()
    {
        ToggleBuildingTypeOptions(false);
        TogglePilotiOptions(false);
        ToggleNumpad(false);
    }

    private void OpenNumpad(TMP_Text targetText)
    {
        if (numpadController == null || targetText == null)
        {
            return;
        }

        string placeholder = targetText == text_TotalFloors ? placeholderTotalFloorsText : placeholderFloorText;
        numpadController.OpenNumpad(targetText, CloseNumpad, placeholder);
        ToggleNumpad(true);
    }

    private void CloseNumpad()
    {
        ToggleNumpad(false);
    }

    // ==========================================
    // 1. 건물 종류 선택 기능
    // ==========================================

    public void OnClick_OpenBuildingTypeMenu()
    {
        ToggleNumpad(false);
        TogglePilotiOptions(false);

        if (btn_SelectBuildingType != null) btn_SelectBuildingType.gameObject.SetActive(false);
        if (btn_InputFloor != null) btn_InputFloor.gameObject.SetActive(false);
        if (btn_Complete != null) btn_Complete.gameObject.SetActive(false);
        if (btn_OpenAdditionalInfo != null) btn_OpenAdditionalInfo.gameObject.SetActive(false);
        if (btn_CancelBuildingType != null) btn_CancelBuildingType.gameObject.SetActive(true);

        ToggleBuildingTypeOptions(true);
    }

    public void OnClick_CancelBuildingTypeMenu()
    {
        ToggleBuildingTypeOptions(false);

        if (btn_SelectBuildingType != null) btn_SelectBuildingType.gameObject.SetActive(true);
        if (btn_InputFloor != null) btn_InputFloor.gameObject.SetActive(true);
        if (btn_Complete != null) btn_Complete.gameObject.SetActive(true);
        if (btn_OpenAdditionalInfo != null) btn_OpenAdditionalInfo.gameObject.SetActive(true);
        if (btn_CancelBuildingType != null) btn_CancelBuildingType.gameObject.SetActive(false);
    }

    // (참고: 이 메서드는 서브 패널의 UI 구조에서 개별적으로 호출되는 로직이므로, 
    // AddClick을 통한 자동 등록 대상에서는 제외하고 기존대로 둡니다.)
    public void OnSelect_BuildingType(string selectedType)
    {
        if (text_BuildingType != null) text_BuildingType.text = selectedType;

        ToggleBuildingTypeOptions(false);

        if (btn_SelectBuildingType != null) btn_SelectBuildingType.gameObject.SetActive(true);
        if (btn_InputFloor != null) btn_InputFloor.gameObject.SetActive(true);
        if (btn_Complete != null) btn_Complete.gameObject.SetActive(true);
        if (btn_OpenAdditionalInfo != null) btn_OpenAdditionalInfo.gameObject.SetActive(true);
        if (btn_CancelBuildingType != null) btn_CancelBuildingType.gameObject.SetActive(false);
    }

    // ==========================================
    // 2. 층수 입력 기능
    // ==========================================

    public void OnClick_OpenFloorNumpad()
    {
        if (numpadController != null && text_Floor != null)
        {
            ToggleBuildingTypeOptions(false);
            TogglePilotiOptions(false);
            OpenNumpad(text_Floor);
        }
    }

    // ==========================================
    // 2-1. 추가 정보 입력 (전체 건물 층수 / 필로티)
    // ==========================================

    public void OnClick_OpenAdditionalInfo()
    {
        HideOwnedSubPanels();

        // AdditionalInfo가 BaseInfo를 대신해 표시된다.
        if (baseInfoObject != null) baseInfoObject.SetActive(false);
        if (additionalInfoObject != null) additionalInfoObject.SetActive(true);
    }

    public void OnClick_BackToBaseInfo()
    {
        HideOwnedSubPanels();

        if (additionalInfoObject != null) additionalInfoObject.SetActive(false);
        if (baseInfoObject != null) baseInfoObject.SetActive(true);
    }

    public void OnClick_OpenTotalFloorsNumpad()
    {
        // 기존 층수 입력과 동일한 NumPad를 사용하되 대상 텍스트만 전체 층수로 변경.
        if (numpadController != null && text_TotalFloors != null)
        {
            ToggleBuildingTypeOptions(false);
            TogglePilotiOptions(false);
            OpenNumpad(text_TotalFloors);
        }
    }

    // 건물 종류와 동일한 방식: BuildingInfoController 소유 서브패널을 열고,
    // 옵션 버튼이 OnSelect_Piloti(string)을 호출한다.
    public void OnClick_OpenPilotiMenu()
    {
        ToggleNumpad(false);
        ToggleBuildingTypeOptions(false);

        if (btn_InputTotalFloors != null) btn_InputTotalFloors.gameObject.SetActive(false);
        if (btn_SelectPiloti != null) btn_SelectPiloti.gameObject.SetActive(false);
        if (btn_BackToBaseInfo != null) btn_BackToBaseInfo.gameObject.SetActive(false);
        if (btn_CancelPiloti != null) btn_CancelPiloti.gameObject.SetActive(true);

        TogglePilotiOptions(true);
    }

    public void OnClick_CancelPilotiMenu()
    {
        TogglePilotiOptions(false);
        RestoreAdditionalInfoButtonsAfterPiloti();
    }

    // 필로티 옵션 서브패널의 버튼들이 호출한다(씬에서 UnityEvent로 "Unknown"/"Yes"/"No" 전달).
    // 건물 종류의 OnSelect_BuildingType과 동일한 역할이므로 자동 등록 대상이 아니다.
    public void OnSelect_Piloti(string selected)
    {
        string normalized = string.IsNullOrWhiteSpace(selected) ? "unknown" : selected.Trim().ToLowerInvariant();
        pilotiSelection = normalized switch
        {
            "yes" => PilotiSelection.Yes,
            "no" => PilotiSelection.No,
            _ => PilotiSelection.Unknown
        };
        UpdatePilotiText();

        TogglePilotiOptions(false);
        RestoreAdditionalInfoButtonsAfterPiloti();
    }

    private void RestoreAdditionalInfoButtonsAfterPiloti()
    {
        if (btn_InputTotalFloors != null) btn_InputTotalFloors.gameObject.SetActive(true);
        if (btn_SelectPiloti != null) btn_SelectPiloti.gameObject.SetActive(true);
        if (btn_BackToBaseInfo != null) btn_BackToBaseInfo.gameObject.SetActive(true);
        if (btn_CancelPiloti != null) btn_CancelPiloti.gameObject.SetActive(false);
    }

    private void UpdatePilotiText()
    {
        if (text_Piloti == null) return;
        text_Piloti.text = pilotiSelection switch
        {
            PilotiSelection.Yes => "필로티 여부: 있음",
            PilotiSelection.No => "필로티 여부: 없음",
            _ => "필로티 여부: 모름"
        };
    }

    // 현재 층수 텍스트에서 정수 층수를 추출한다.
    private bool TryGetFloorNumber(out int floorNumber)
    {
        floorNumber = 0;
        if (text_Floor == null) return false;
        if (string.IsNullOrWhiteSpace(text_Floor.text) || text_Floor.text == placeholderFloorText) return false;
        string input = text_Floor.text.Replace("층", "").Trim();
        return int.TryParse(input, out floorNumber);
    }

    // 전체 건물 층수 텍스트에서 정수를 추출한다. 미입력/기본 상태면 false.
    private bool TryGetTotalFloors(out int totalFloors)
    {
        totalFloors = 0;
        if (text_TotalFloors == null) return false;
        if (string.IsNullOrWhiteSpace(text_TotalFloors.text) || text_TotalFloors.text == placeholderTotalFloorsText) return false;
        string input = text_TotalFloors.text.Replace("층", "").Trim();
        return int.TryParse(input, out totalFloors);
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

        // [유효성 검사 4] 전체 건물 층수 확인.
        // 입력 단계에서는 미입력을 기본값 0으로 유지하고, 단계 완료 시 0이면 현재 층수로 확정한다.
        int totalFloors = 0;
        bool hasExplicitTotalFloors = TryGetTotalFloors(out int parsedTotal);
        if (hasExplicitTotalFloors)
        {
            totalFloors = parsedTotal;
        }

        if (!hasExplicitTotalFloors && totalFloors == 0)
        {
            totalFloors = floorNumber;
        }

        // 전체 건물 층수가 현재 층수보다 낮으면 진행 불가.
        if (totalFloors < floorNumber)
        {
            if (uiManager != null)
                uiManager.ShowNotification("전체 건물 층수는 현재 층수보다 낮을 수 없습니다.");
            return;
        }

        // 전체 층수 표기를 정돈된 값으로 갱신.
        if (text_TotalFloors != null) text_TotalFloors.text = $"{totalFloors}층";

        // 입력한 건물 정보를 클라이언트 전역 상태에 저장. 이후 가구 배치 완료 시
        // SimulationInputBuilder가 읽어 서버로 전송한다.
        //ClientBuildingInfo.Set(
        //    MapBuildingTypeToContract(text_BuildingType != null ? text_BuildingType.text : null),
        //    floorNumber,
        //    totalFloors,
        //    PilotiToContractString());

        // 모든 검사를 통과하면 다음 워크플로우 단계로 진행
        RequestWorkflowCommand(RoomBuildWorkflowManager.WorkflowCommand.CompleteRoomInfoInput);
    }

    // 건물 종류 선택 표시 문자열을 시뮬레이션 계약 enum 문자열로 매핑한다.
    // (옵션 버튼 문자열은 씬에서 설정되므로 한/영 키워드로 견고하게 판별한다.)
    private static string MapBuildingTypeToContract(string displayText)
    {
        string s = string.IsNullOrWhiteSpace(displayText) ? string.Empty : displayText.ToLowerInvariant();

        if (s.Contains("단독") || s.Contains("주택") || s.Contains("detached") || s.Contains("house"))
            return "DetachedHouse";

        if (s.Contains("빌라") || s.Contains("연립") || s.Contains("다세대") || s.Contains("villa") || s.Contains("low"))
            return "Villa";

        // 아파트/apartment 및 미상은 Apartment 기본.
        return "Apartment";
    }

    private string PilotiToContractString()
    {
        return pilotiSelection switch
        {
            PilotiSelection.Yes => "Yes",
            PilotiSelection.No => "No",
            _ => "Unknown"
        };
    }
}

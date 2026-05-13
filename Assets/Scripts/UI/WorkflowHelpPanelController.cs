using System.Collections.Generic;
using MixedReality.Toolkit.UX;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

// State별 WorkflowHelpContent를 관리하는 책임자.
// - State 진입 시 해당 Content를 활성화하여 표시
// - show/close 버튼으로 토글
// - next/prev 버튼으로 Content 내부 설명을 순차적으로 표시
// Content와 State의 매핑은 각 WorkflowHelpContent.state 필드에서 가져온다(단일 정보 출처).
public class WorkflowHelpPanelController : MonoBehaviour
{
    [Header("Root Plate")]
    [Tooltip("모든 Helper의 부모 Plate(루트 판). Helper를 열 때 항상 함께 켜지고, 닫힐 때 함께 꺼집니다.")]
    public GameObject rootHelpPlate;

    [Tooltip("rootHelpPlate가 비어 있을 때 자식에서 자동 탐색할지 여부. 비추천이므로 기본은 false.")]
    public bool autoFindRootHelpPlate = false;

    [Header("Main Panel 차단")]
    [Tooltip("Help가 열려 있는 동안 조작을 막기 위해 현재 활성 메인 패널을 비활성화시킬 UIManager 참조.")]
    public UIManager uiManager;

    [Tooltip("uiManager가 비어 있으면 Scene에서 자동 탐색합니다.")]
    public bool autoFindUIManager = true;

    [Tooltip("Help가 열릴 때 현재 Main Panel을 비활성화하고, 닫힐 때 다시 활성화합니다.")]
    public bool blockMainPanelWhileOpen = true;

    [Header("Content Mapping")]
    [Tooltip("Inspector에서 단계별 WorkflowHelpContent를 직접 등록합니다. Content는 이 컨트롤러의 자식일 필요가 없습니다.")]
    public List<WorkflowHelpContent> contents = new List<WorkflowHelpContent>();

    [Tooltip("기존 씬 호환용입니다. 새 구성에서는 끄고 contents에 직접 넣는 방식을 권장합니다.")]
    public bool autoCollectFromChildren = false;

    [Tooltip("기존 extraContents 필드 호환용입니다. 새 구성에서는 contents를 사용하세요.")]
    public List<WorkflowHelpContent> extraContents = new List<WorkflowHelpContent>();

    [Header("Buttons")]
    [Tooltip("Help 패널을 다시 여는 버튼 (X로 닫은 후 재오픈용). rootHelpPlate 외부에 두어야 닫혀 있어도 누를 수 있습니다.")]
    public PressableButton showHelpButton;

    [Tooltip("Help 패널을 닫는 X 버튼.")]
    public PressableButton closeButton;

    [Tooltip("이전 설명으로 이동.")]
    public PressableButton previousButton;

    [Tooltip("다음 설명으로 이동.")]
    public PressableButton nextButton;

    [Header("UI Indicator")]
    [Tooltip("현재 페이지 인덱스 표시(선택).")]
    public TMP_Text pageIndicatorText;

    [Header("동작 옵션")]
    [Tooltip("Help가 열릴 때 항상 첫 번째 설명부터 시작합니다.")]
    public bool reopenStartsFromFirstPage = true;

    [Tooltip("State에 처음 진입할 때 자동으로 Help를 표시합니다.")]
    public bool showOnFirstStateEntry = true;
    public bool suppressAutoShowForRoomInfoInput = true;

    private readonly Dictionary<RoomBuildWorkflowManager.WorkflowState, WorkflowHelpContent> contentByState
        = new Dictionary<RoomBuildWorkflowManager.WorkflowState, WorkflowHelpContent>();

    private readonly HashSet<RoomBuildWorkflowManager.WorkflowState> visitedStates
        = new HashSet<RoomBuildWorkflowManager.WorkflowState>();

    private WorkflowHelpContent currentContent;
    private int currentIndex;
    private bool isOpen;

    private UnityAction showAction;
    private UnityAction closeAction;
    private UnityAction prevAction;
    private UnityAction nextAction;

    private void Awake()
    {
        EnsureRootHelpPlate();
        EnsureUIManager();
        BuildContentMap();
        CreateActions();
    }

    private void OnEnable()
    {
        EnsureRootHelpPlate();
        EnsureUIManager();
        BuildContentMap();
        CreateActions();
        RegisterButtons();
        // 시작 시 모든 Content와 Plate는 꺼둠
        HideAllContents();
        SetRootHelpPlateActive(false);
        UpdateButtonsVisibility();
    }

    private void EnsureUIManager()
    {
        if (uiManager != null || !autoFindUIManager) return;

        UIManager[] found = FindObjectsByType<UIManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (found != null && found.Length > 0)
        {
            uiManager = found[0];
        }
    }

    // Help 오픈/클로즈에 맞춰 현재 Main Panel 조작을 차단/해제.
    private void SetMainPanelBlocked(bool blocked)
    {
        if (!blockMainPanelWhileOpen || uiManager == null) return;
        uiManager.SetCurrentMainPanelInteractable(!blocked);
    }

    // rootHelpPlate가 비어 있고 옵션이 켜져 있으면 자식 첫 번째 GameObject를 사용.
    private void EnsureRootHelpPlate()
    {
        if (rootHelpPlate != null || !autoFindRootHelpPlate) return;

        if (transform.childCount > 0)
        {
            rootHelpPlate = transform.GetChild(0).gameObject;
        }
    }

    private void SetRootHelpPlateActive(bool active)
    {
        if (active && !gameObject.activeSelf)
        {
            gameObject.SetActive(true);
        }

        if (rootHelpPlate != null)
        {
            rootHelpPlate.SetActive(active);
            return;
        }

        if (active)
        {
            gameObject.SetActive(true);
        }
    }

    private void OnDisable()
    {
        UnregisterButtons();
    }

    // 자식 계층 + extraContents에서 WorkflowHelpContent를 수집해 state -> content 맵을 만든다.
    // 매핑 정보는 각 Content의 state 필드에서만 가져온다(단일 정보 출처).
    private void BuildContentMap()
    {
        contentByState.Clear();

        if (contents != null)
        {
            foreach (WorkflowHelpContent c in contents)
            {
                RegisterContent(c);
            }
        }

        if (autoCollectFromChildren)
        {
            WorkflowHelpContent[] found = GetComponentsInChildren<WorkflowHelpContent>(true);
            foreach (WorkflowHelpContent c in found)
            {
                RegisterContent(c);
            }
        }

        if (extraContents != null)
        {
            foreach (WorkflowHelpContent c in extraContents)
            {
                RegisterContent(c);
            }
        }
    }

    private void RegisterContent(WorkflowHelpContent content)
    {
        if (content == null) return;

        if (contentByState.TryGetValue(content.state, out WorkflowHelpContent existing) && existing != content)
        {
            Debug.LogWarning($"[WorkflowHelpPanelController] State {content.state}에 이미 등록된 Content({existing.name})가 있습니다. {content.name}는 무시됩니다.", this);
            return;
        }

        contentByState[content.state] = content;
    }

    // 외부에서(UIManager 등) State 변경 시 호출.
    public void ShowForState(RoomBuildWorkflowManager.WorkflowState state)
    {
        EnsureRootHelpPlate();
        EnsureUIManager();
        BuildContentMap();

        // 새 State로 진입할 때 이전 Content는 닫음
        CloseCurrentContent();

        WorkflowHelpContent next;
        if (!contentByState.TryGetValue(state, out next) || next == null || !next.HasDescriptions)
        {
            // 해당 State에 Helper가 없으면 Plate도 함께 끔
            currentContent = null;
            isOpen = false;
            SetRootHelpPlateActive(false);
            UpdateButtonsVisibility();
            UpdateIndicator();
            return;
        }

        currentContent = next;
        currentIndex = 0;

        bool firstEntry = visitedStates.Add(state);
        bool autoShow = showOnFirstStateEntry
            && firstEntry
            && currentContent.showAutomaticallyOnFirstEnter
            && !(suppressAutoShowForRoomInfoInput && state == RoomBuildWorkflowManager.WorkflowState.RoomInfoInput);

        if (autoShow)
        {
            Open();
        }
        else
        {
            // 자동 오픈이 아니면 닫혀 있는 상태 유지. Plate, Helper 모두 닫고 showHelpButton만 노출.
            isOpen = false;
            currentContent.SetAllActive(false);
            currentContent.gameObject.SetActive(false);
            SetRootHelpPlateActive(false);
            UpdateButtonsVisibility();
            UpdateIndicator();
        }
    }

    // Help 패널 오픈 (X로 닫았다가 다시 열기, 혹은 자동 진입 시).
    // Helper가 열리는 순간 RootHelpPlate도 반드시 함께 켜진다.
    public void Open()
    {
        if (currentContent == null || !currentContent.HasDescriptions)
        {
            return;
        }

        if (reopenStartsFromFirstPage)
        {
            currentIndex = 0;
        }
        else
        {
            currentIndex = Mathf.Clamp(currentIndex, 0, currentContent.Count - 1);
        }

        isOpen = true;
        SetRootHelpPlateActive(true);
        currentContent.gameObject.SetActive(true);
        currentContent.ShowOnly(currentIndex);
        // Help가 열린 동안 현재 Main Panel 조작 차단
        SetMainPanelBlocked(true);
        UpdateButtonsVisibility();
        UpdateIndicator();
    }

    // X 버튼으로 닫기. currentContent 참조는 유지되어 showHelpButton으로 다시 열 수 있음.
    // Plate와 Helper GameObject도 함께 꺼진다.
    public void Close()
    {
        isOpen = false;
        if (currentContent != null)
        {
            currentContent.SetAllActive(false);
            currentContent.gameObject.SetActive(false);
        }
        SetRootHelpPlateActive(false);
        // Help가 닫혔으므로 Main Panel 조작 다시 허용
        SetMainPanelBlocked(false);
        UpdateButtonsVisibility();
        UpdateIndicator();
    }

    public void OpenFromBeginning() => Open();

    public void ShowNextPage()
    {
        if (!isOpen || currentContent == null || !currentContent.HasDescriptions) return;

        // 마지막에서 더 이동하지 않음
        if (currentIndex >= currentContent.Count - 1) return;

        currentIndex++;
        currentContent.ShowOnly(currentIndex);
        UpdateButtonsVisibility();
        UpdateIndicator();
    }

    public void ShowPreviousPage()
    {
        if (!isOpen || currentContent == null || !currentContent.HasDescriptions) return;

        // 처음에서 더 이동하지 않음
        if (currentIndex <= 0) return;

        currentIndex--;
        currentContent.ShowOnly(currentIndex);
        UpdateButtonsVisibility();
        UpdateIndicator();
    }

    // currentContent를 완전히 닫고 참조 해제(상태 전환 시 사용).
    // Plate 끄기는 ShowForState에서 다음 Content 유무에 따라 결정한다.
    private void CloseCurrentContent()
    {
        bool wasOpen = isOpen;
        if (currentContent != null)
        {
            currentContent.SetAllActive(false);
            currentContent.gameObject.SetActive(false);
        }
        isOpen = false;

        // 이전 State에서 Help가 열려 있었다면 Main Panel 차단도 해제
        // (새 State 진입 시 자동 Open되면 Open()에서 다시 차단됨)
        if (wasOpen)
        {
            SetMainPanelBlocked(false);
        }
    }

    // 모든 Helper GameObject와 그 안의 설명들을 끔.
    private void HideAllContents()
    {
        foreach (KeyValuePair<RoomBuildWorkflowManager.WorkflowState, WorkflowHelpContent> kv in contentByState)
        {
            if (kv.Value != null)
            {
                kv.Value.SetAllActive(false);
                kv.Value.gameObject.SetActive(false);
            }
        }
    }

    private void UpdateButtonsVisibility()
    {
        bool hasContent = currentContent != null && currentContent.HasDescriptions;
        bool hasPrev = isOpen && hasContent && currentIndex > 0;
        bool hasNext = isOpen && hasContent && currentIndex < currentContent.Count - 1;

        // showHelpButton: 닫혀 있고 Content가 있을 때만 노출
        SetButtonActive(showHelpButton, hasContent && !isOpen);
        // closeButton, next, prev: 열려 있을 때만 노출/활성
        SetButtonActive(closeButton, isOpen && hasContent);
        SetButtonActive(previousButton, isOpen && hasContent);
        SetButtonActive(nextButton, isOpen && hasContent);

        SetButtonEnabled(previousButton, hasPrev);
        SetButtonEnabled(nextButton, hasNext);
    }

    private void UpdateIndicator()
    {
        if (pageIndicatorText == null) return;

        if (currentContent != null && currentContent.HasDescriptions && isOpen)
        {
            pageIndicatorText.text = $"{currentIndex + 1}/{currentContent.Count}";
        }
        else
        {
            pageIndicatorText.text = string.Empty;
        }
    }

    private void CreateActions()
    {
        showAction ??= Open;
        closeAction ??= Close;
        prevAction ??= ShowPreviousPage;
        nextAction ??= ShowNextPage;
    }

    private void RegisterButtons()
    {
        AddClick(showHelpButton, showAction);
        AddClick(closeButton, closeAction);
        AddClick(previousButton, prevAction);
        AddClick(nextButton, nextAction);
    }

    private void UnregisterButtons()
    {
        RemoveClick(showHelpButton, showAction);
        RemoveClick(closeButton, closeAction);
        RemoveClick(previousButton, prevAction);
        RemoveClick(nextButton, nextAction);
    }

    private static void SetButtonActive(PressableButton button, bool active)
    {
        if (button != null)
        {
            button.gameObject.SetActive(active);
        }
    }

    private static void SetButtonEnabled(PressableButton button, bool enabled)
    {
        if (button != null)
        {
            button.enabled = enabled;
        }
    }

    private static void AddClick(PressableButton button, UnityAction action)
    {
        if (button == null || action == null) return;
        button.OnClicked.RemoveListener(action);
        button.OnClicked.AddListener(action);
    }

    private static void RemoveClick(PressableButton button, UnityAction action)
    {
        if (button == null || action == null) return;
        button.OnClicked.RemoveListener(action);
    }
}

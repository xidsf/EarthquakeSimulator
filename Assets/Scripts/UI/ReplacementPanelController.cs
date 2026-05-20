using MixedReality.Toolkit.UX;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

public class ReplacementPanelController : WorkflowPanelControllerBase
{
    [Header("User Buttons")]
    public PressableButton btn_SelectPreviousFurniture;
    public PressableButton btn_SelectNextFurniture;
    public PressableButton btn_BackToResult;

    [Header("Text")]
    public TMP_Text selectedFurnitureText;
    public TMP_Text evaluationText;

    [Header("References")]
    public ReplacementManager replacementManager;
    public bool autoFindReplacementManager = true;
    public bool hideCycleSelectionButtons = true;

    private UnityAction selectPreviousAction;
    private UnityAction selectNextAction;
    private UnityAction backToResultAction;

    protected override void Awake()
    {
        base.Awake();
        EnsureReplacementManager();
    }

    private void OnEnable()
    {
        EnsureCommonReferences();
        EnsureReplacementManager();
        CreateActions();
        ApplySelectionButtonVisibility();
        RegisterButtons();
        SubscribeReplacementManager();
        replacementManager?.BeginReplacementMode();
    }

    private void OnDisable()
    {
        UnsubscribeReplacementManager();
        UnregisterButtons();
        replacementManager?.EndReplacementMode();
    }

    private void CreateActions()
    {
        selectPreviousAction ??= OnClickSelectPreviousFurniture;
        selectNextAction ??= OnClickSelectNextFurniture;
        backToResultAction ??= OnClickBackToResult;
    }

    private void RegisterButtons()
    {
        if (!hideCycleSelectionButtons)
        {
            AddClick(btn_SelectPreviousFurniture, selectPreviousAction);
            AddClick(btn_SelectNextFurniture, selectNextAction);
        }

        AddClick(btn_BackToResult, backToResultAction);
    }

    private void UnregisterButtons()
    {
        RemoveClick(btn_SelectPreviousFurniture, selectPreviousAction);
        RemoveClick(btn_SelectNextFurniture, selectNextAction);
        RemoveClick(btn_BackToResult, backToResultAction);
    }

    private void ApplySelectionButtonVisibility()
    {
        SetButtonActive(btn_SelectPreviousFurniture, !hideCycleSelectionButtons);
        SetButtonActive(btn_SelectNextFurniture, !hideCycleSelectionButtons);
    }

    private static void SetButtonActive(PressableButton button, bool active)
    {
        if (button != null)
        {
            button.gameObject.SetActive(active);
        }
    }

    public void OnClickSelectPreviousFurniture()
    {
        replacementManager?.SelectPreviousFurniture();
    }

    public void OnClickSelectNextFurniture()
    {
        replacementManager?.SelectNextFurniture();
    }

    public void OnClickBackToResult()
    {
        RequestWorkflowCommand(RoomBuildWorkflowManager.WorkflowCommand.CompleteFurnitureRePlacement);
    }

    private void SubscribeReplacementManager()
    {
        if (replacementManager == null)
        {
            SetText(null, "Replacement manager is not connected.");
            return;
        }

        replacementManager.SelectionEvaluationChanged += HandleSelectionEvaluationChanged;
    }

    private void UnsubscribeReplacementManager()
    {
        if (replacementManager == null)
        {
            return;
        }

        replacementManager.SelectionEvaluationChanged -= HandleSelectionEvaluationChanged;
    }

    private void HandleSelectionEvaluationChanged(PlacedFurniture furniture, string evaluation)
    {
        SetText(furniture, evaluation);
    }

    private void SetText(PlacedFurniture furniture, string evaluation)
    {
        if (selectedFurnitureText != null)
        {
            selectedFurnitureText.text = furniture != null ? furniture.DisplayName : "선택된 가구 없음";
        }

        if (evaluationText != null)
        {
            evaluationText.text = string.IsNullOrWhiteSpace(evaluation) ? "가구를 선택하세요." : evaluation;
        }
    }

    private void EnsureReplacementManager()
    {
        if (replacementManager != null || !autoFindReplacementManager)
        {
            return;
        }

        ReplacementManager[] managers = FindObjectsByType<ReplacementManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (managers != null && managers.Length > 0)
        {
            replacementManager = managers[0];
            return;
        }

        replacementManager = gameObject.AddComponent<ReplacementManager>();
    }
}

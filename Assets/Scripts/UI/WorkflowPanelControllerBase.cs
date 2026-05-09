using MixedReality.Toolkit.UX;
using UnityEngine;
using UnityEngine.Events;

public abstract class WorkflowPanelControllerBase : MonoBehaviour
{
    [Header("Common References")]
    public UIManager uiManager;
    public RoomBuildWorkflowManager workflowManager;

    [Tooltip("참조가 비어 있으면 Scene에서 자동 탐색합니다.")]
    public bool autoFindReferences = true;

    protected virtual void Awake()
    {
        EnsureCommonReferences();
    }

    protected void EnsureCommonReferences()
    {
        if (!autoFindReferences)
        {
            return;
        }

        if (uiManager == null)
        {
            UIManager[] uiManagers = FindObjectsByType<UIManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (uiManagers != null && uiManagers.Length > 0)
            {
                uiManager = uiManagers[0];
            }
        }

        if (workflowManager == null)
        {
            if (uiManager != null)
            {
                workflowManager = uiManager.GetWorkflowManager();
            }

            if (workflowManager == null)
            {
                RoomBuildWorkflowManager[] managers = FindObjectsByType<RoomBuildWorkflowManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                if (managers != null && managers.Length > 0)
                {
                    workflowManager = managers[0];
                }
            }
        }
    }

    protected void ShowWarning(string message)
    {
        if (uiManager != null)
        {
            uiManager.ShowWarningMessage(message);
        }
        else
        {
            Debug.LogWarning(message);
        }
    }

    protected bool RequestWorkflowCommand(RoomBuildWorkflowManager.WorkflowCommand command)
    {
        EnsureCommonReferences();

        if (workflowManager == null)
        {
            ShowWarning("WorkflowManager가 연결되어 있지 않습니다.");
            return false;
        }

        return workflowManager.RequestCommand(command);
    }

    protected static void AddClick(PressableButton button, UnityAction action)
    {
        if (button == null || action == null)
        {
            return;
        }

        button.OnClicked.RemoveListener(action);
        button.OnClicked.AddListener(action);
    }

    protected static void RemoveClick(PressableButton button, UnityAction action)
    {
        if (button == null || action == null)
        {
            return;
        }

        button.OnClicked.RemoveListener(action);
    }
}

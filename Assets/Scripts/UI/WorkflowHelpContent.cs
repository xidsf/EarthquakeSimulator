using System.Collections.Generic;
using UnityEngine;

public class WorkflowHelpContent : MonoBehaviour
{
    public RoomBuildWorkflowManager.WorkflowState state;
    public List<GameObject> descriptions = new List<GameObject>();
    public bool showAutomaticallyOnFirstEnter = true;

    public int Count => descriptions != null ? descriptions.Count : 0;
    public bool HasDescriptions => Count > 0;

    public void SetAllActive(bool active)
    {
        if (descriptions == null)
        {
            return;
        }

        for (int i = 0; i < descriptions.Count; i++)
        {
            if (descriptions[i] != null)
            {
                descriptions[i].SetActive(active);
            }
        }
    }

    public void ShowOnly(int index)
    {
        if (descriptions == null)
        {
            return;
        }

        for (int i = 0; i < descriptions.Count; i++)
        {
            if (descriptions[i] != null)
            {
                descriptions[i].SetActive(i == index);
            }
        }
    }

    public GameObject GetAt(int index)
    {
        if (descriptions == null || index < 0 || index >= descriptions.Count)
        {
            return null;
        }

        return descriptions[index];
    }
}

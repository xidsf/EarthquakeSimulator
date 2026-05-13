using System.Collections.Generic;
using MixedReality.Toolkit.UX;
using UnityEngine;
using UnityEngine.Events;

public class PressableButtonClickGuard : MonoBehaviour
{
    public const float GlobalDefaultDebounceSeconds = 0.3f;

    [Tooltip("이 Panel 하위 버튼에서 두 번째 클릭을 무시할 시간입니다. 0이면 비활성화됩니다.")]
    [Min(0.0f)]
    public float debounceSeconds = GlobalDefaultDebounceSeconds;

    private static readonly Dictionary<PressableButton, float> LastInvokeTimes =
        new Dictionary<PressableButton, float>();

    private static readonly Dictionary<PressableButton, Dictionary<UnityAction, UnityAction>> GuardedActions =
        new Dictionary<PressableButton, Dictionary<UnityAction, UnityAction>>();

    public static void AddClick(PressableButton button, UnityAction action)
    {
        if (button == null || action == null)
        {
            return;
        }

        RemoveClick(button, action);

        UnityAction guardedAction = () =>
        {
            if (CanInvoke(button))
            {
                action.Invoke();
            }
        };

        if (!GuardedActions.TryGetValue(button, out Dictionary<UnityAction, UnityAction> actionsByOriginal))
        {
            actionsByOriginal = new Dictionary<UnityAction, UnityAction>();
            GuardedActions[button] = actionsByOriginal;
        }

        actionsByOriginal[action] = guardedAction;
        button.OnClicked.AddListener(guardedAction);
    }

    public static void RemoveClick(PressableButton button, UnityAction action)
    {
        if (button == null || action == null)
        {
            return;
        }

        if (GuardedActions.TryGetValue(button, out Dictionary<UnityAction, UnityAction> actionsByOriginal) &&
            actionsByOriginal.TryGetValue(action, out UnityAction guardedAction))
        {
            button.OnClicked.RemoveListener(guardedAction);
            actionsByOriginal.Remove(action);

            if (actionsByOriginal.Count == 0)
            {
                GuardedActions.Remove(button);
            }
        }

        button.OnClicked.RemoveListener(action);
    }

    private static bool CanInvoke(PressableButton button)
    {
        float debounceSeconds = GetDebounceSeconds(button);
        if (debounceSeconds <= 0.0f)
        {
            return true;
        }

        float now = Time.unscaledTime;
        if (LastInvokeTimes.TryGetValue(button, out float lastInvokeTime) &&
            now - lastInvokeTime < debounceSeconds)
        {
            return false;
        }

        LastInvokeTimes[button] = now;
        return true;
    }

    private static float GetDebounceSeconds(PressableButton button)
    {
        if (button == null)
        {
            return GlobalDefaultDebounceSeconds;
        }

        PressableButtonClickGuard guard = button.GetComponentInParent<PressableButtonClickGuard>(true);
        return guard != null ? guard.debounceSeconds : GlobalDefaultDebounceSeconds;
    }
}

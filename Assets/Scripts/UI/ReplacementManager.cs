using System;
using UnityEngine;

public class ReplacementManager : MonoBehaviour
{
    [Header("References")]
    public FurniturePlacementManager furniturePlacementManager;
    public FurnitureMoveModeController moveModeController;
    public bool autoFindReferences = true;

    public event Action<PlacedFurniture, string> SelectionEvaluationChanged;

    private bool previousAllowAllMovable;
    private bool previousSelectionOnlyMode;
    private bool hasPreviousAllowAllMovable;
    private bool hasPreviousSelectionOnlyMode;
    private bool replacementModeActive;

    private void Awake()
    {
        EnsureReferences();
    }

    private void OnEnable()
    {
        EnsureReferences();
    }

    private void OnDisable()
    {
        EndReplacementMode();
    }

    public void BeginReplacementMode()
    {
        EnsureReferences();
        if (moveModeController == null)
        {
            SelectionEvaluationChanged?.Invoke(null, "Furniture selection is not ready.");
            return;
        }

        if (!replacementModeActive)
        {
            previousAllowAllMovable = moveModeController.allowAllFurnitureMovableInMoveMode;
            previousSelectionOnlyMode = moveModeController.IsSelectionOnlyMode;
            hasPreviousAllowAllMovable = true;
            hasPreviousSelectionOnlyMode = true;
            moveModeController.SelectionChanged += HandleSelectionChanged;
        }

        replacementModeActive = true;
        moveModeController.allowAllFurnitureMovableInMoveMode = true;
        moveModeController.SetSelectionOnlyMode(true);
        moveModeController.SetMoveMode(true);
        moveModeController.ClearSelection();

        if (!SelectInitialFurniture())
        {
            HandleSelectionChanged(null);
        }
    }

    public void EndReplacementMode()
    {
        if (!replacementModeActive)
        {
            return;
        }

        replacementModeActive = false;
        if (moveModeController != null)
        {
            moveModeController.SelectionChanged -= HandleSelectionChanged;
            moveModeController.SetManipulationMode(FurnitureManipulationMode.None);
            if (hasPreviousSelectionOnlyMode)
            {
                moveModeController.SetSelectionOnlyMode(previousSelectionOnlyMode);
            }

            moveModeController.ClearSelection();
            if (hasPreviousAllowAllMovable)
            {
                moveModeController.allowAllFurnitureMovableInMoveMode = previousAllowAllMovable;
            }
        }

        hasPreviousAllowAllMovable = false;
        hasPreviousSelectionOnlyMode = false;
    }

    public void SelectNextFurniture()
    {
        EnsureReferences();
        moveModeController?.SelectNext();
    }

    public void SelectPreviousFurniture()
    {
        EnsureReferences();
        moveModeController?.SelectPrevious();
    }

    private bool SelectInitialFurniture()
    {
        if (moveModeController == null)
        {
            return false;
        }

        foreach (PlacedFurniture furniture in moveModeController.RegisteredFurnitures)
        {
            if (furniture == null)
            {
                continue;
            }

            moveModeController.SelectFurniture(furniture);
            return true;
        }

        return false;
    }

    public string BuildEvaluationText(PlacedFurniture furniture)
    {
        if (furniture == null)
        {
            return "가구를 선택하세요.";
        }

        SimulationResultResponse result = SimulationProcessManager.LastCompletedResult;
        string evaluation = FindEvaluation(result, furniture);
        return string.IsNullOrWhiteSpace(evaluation)
            ? $"{furniture.DisplayName}은(는) 현재 결과에서 별도 위험 평가가 없습니다. 안전한 것으로 표시합니다."
            : evaluation;
    }

    private void HandleSelectionChanged(PlacedFurniture furniture)
    {
        SelectionEvaluationChanged?.Invoke(furniture, BuildEvaluationText(furniture));
    }

    private string FindEvaluation(SimulationResultResponse result, PlacedFurniture furniture)
    {
        if (result == null || furniture == null)
        {
            return string.Empty;
        }

        SimulationTransformLog log = FindMatchingLog(result.selected_transform_log, furniture);
        if (log == null) log = FindMatchingLog(result.transform_logs, furniture);
        if (log == null && result.playback != null) log = FindMatchingLog(result.playback.transform_logs, furniture);
        if (log != null)
        {
            if (log.fallen)
            {
                return $"{furniture.DisplayName}: 시뮬레이션에서 전도 위험이 감지되었습니다.";
            }

            if (!string.IsNullOrWhiteSpace(log.state))
            {
                return $"{furniture.DisplayName}: {log.state}";
            }
        }

        if (MatchesAny(furniture, result.vlm != null ? result.vlm.fallen : null) || MatchesAny(furniture, result.fallen))
        {
            return $"{furniture.DisplayName}: 결과 분석에서 전도 또는 낙하 위험 대상으로 분류되었습니다.";
        }

        if (MatchesAny(furniture, result.vlm != null ? result.vlm.topple_risk_labels : null))
        {
            return $"{furniture.DisplayName}: VLM 분석에서 전도 위험 가능성이 있는 가구로 분류되었습니다.";
        }

        return string.Empty;
    }

    private static SimulationTransformLog FindMatchingLog(SimulationTransformLog log, PlacedFurniture furniture)
    {
        return IsMatchingLog(log, furniture) ? log : null;
    }

    private static SimulationTransformLog FindMatchingLog(SimulationTransformLog[] logs, PlacedFurniture furniture)
    {
        if (logs == null)
        {
            return null;
        }

        for (int i = 0; i < logs.Length; i++)
        {
            if (IsMatchingLog(logs[i], furniture))
            {
                return logs[i];
            }
        }

        return null;
    }

    private static bool IsMatchingLog(SimulationTransformLog log, PlacedFurniture furniture)
    {
        if (log == null || furniture == null)
        {
            return false;
        }

        return TokenMatchesFurniture(log.id, furniture) ||
               TokenMatchesFurniture(log.label, furniture) ||
               TokenMatchesFurniture(log.path, furniture);
    }

    private static bool MatchesAny(PlacedFurniture furniture, string[] tokens)
    {
        if (furniture == null || tokens == null)
        {
            return false;
        }

        for (int i = 0; i < tokens.Length; i++)
        {
            if (TokenMatchesFurniture(tokens[i], furniture))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TokenMatchesFurniture(string token, PlacedFurniture furniture)
    {
        string normalized = Normalize(token);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        return normalized == Normalize(furniture.FurnitureId) ||
               normalized == Normalize(StripScenePrefix(furniture.FurnitureId)) ||
               normalized == Normalize(furniture.SourceObjectId) ||
               normalized == Normalize(furniture.Label) ||
               normalized == Normalize(furniture.DisplayName) ||
               normalized == Normalize(furniture.gameObject.name);
    }

    private static string Normalize(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
    }

    private static string StripScenePrefix(string value)
    {
        return !string.IsNullOrWhiteSpace(value) && value.StartsWith("scene_", StringComparison.OrdinalIgnoreCase)
            ? value.Substring("scene_".Length)
            : value;
    }

    private void EnsureReferences()
    {
        if (!autoFindReferences)
        {
            return;
        }

        if (furniturePlacementManager == null) furniturePlacementManager = FindFirst<FurniturePlacementManager>();
        if (moveModeController == null) moveModeController = FindFirst<FurnitureMoveModeController>();
    }

    private static T FindFirst<T>() where T : UnityEngine.Object
    {
        T[] objects = FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        return objects != null && objects.Length > 0 ? objects[0] : null;
    }
}

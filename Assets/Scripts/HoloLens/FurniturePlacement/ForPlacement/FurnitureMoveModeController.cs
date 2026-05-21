using System;
using System.Collections.Generic;
using UnityEngine;

public enum FurnitureManipulationMode
{
    None,
    Move,
    Rotate,
    Scale
}

/// <summary>
/// HoloLens2 손 조작 오류를 줄이기 위한 이동 모드 컨트롤러입니다.
///
/// 기본값은 Move Mode ON일 때 등록된 모든 가구의 ObjectManipulator를 활성화합니다.
/// 이렇게 해야 사용자가 손가락으로 원하는 가구를 직접 터치해서 이동할 수 있습니다.
/// SelectNext/Previous는 Debug/Fallback용으로만 남깁니다.
/// </summary>
public class FurnitureMoveModeController : MonoBehaviour
{
    [Header("Move Mode")]
    [SerializeField] private bool isMoveModeEnabled;
    [SerializeField] private FurnitureManipulationMode currentMode = FurnitureManipulationMode.None;
    [SerializeField] private bool selectionOnlyMode;

    [Tooltip("true면 Move Mode ON일 때 모든 가구가 직접 터치/이동 가능합니다. false면 선택된 가구 1개만 이동 가능합니다.")]
    public bool allowAllFurnitureMovableInMoveMode = true;

    [Tooltip("allowAllFurnitureMovableInMoveMode가 false일 때, Move Mode가 켜질 때 선택된 가구가 없으면 첫 가구를 자동 선택합니다.")]
    public bool autoSelectFirstFurnitureWhenMoveModeEnabled = true;

    [Header("Selection Visual")]
    [Tooltip("선택된 가구의 BoxCollider 영역을 반투명하게 표시할 머티리얼입니다. 비워두면 표시하지 않습니다(투명 머티리얼 권장).")]
    public Material selectionBoxMaterial;

    [Tooltip("배치 완료 검증에서 조건 미충족으로 판정된 가구의 BoxCollider 영역에 표시할 머티리얼입니다. 선택/고정 머티리얼과 다른 색을 권장합니다(예: 반투명 빨강). 비워두면 표시하지 않습니다.")]
    public Material invalidPlacementBoxMaterial;

    private readonly List<PlacedFurniture> furnitures = new List<PlacedFurniture>();
    private int selectedIndex = -1;
    // 직전에 선택(=마지막으로 누른)했던 가구. 현재 선택이 고정되어 조작 불가가 될 때
    // 이 가구로 선택을 옮겨 패널 버튼이 계속 동작하도록 한다.
    private PlacedFurniture previousSelectedFurniture;

    public event Action<bool> MoveModeChanged;
    public event Action<PlacedFurniture> SelectionChanged;

    public bool IsMoveModeEnabled => isMoveModeEnabled;
    public bool IsRotateModeEnabled => currentMode == FurnitureManipulationMode.Rotate;
    public bool IsScaleModeEnabled => currentMode == FurnitureManipulationMode.Scale;
    public bool IsSelectionOnlyMode => selectionOnlyMode;
    public FurnitureManipulationMode CurrentMode => currentMode;
    public PlacedFurniture SelectedFurniture => IsValidIndex(selectedIndex) ? furnitures[selectedIndex] : null;
    public IReadOnlyList<PlacedFurniture> RegisteredFurnitures => furnitures;

    public void RegisterFurniture(PlacedFurniture furniture)
    {
        if (furniture == null || furnitures.Contains(furniture))
        {
            return;
        }

        furnitures.Add(furniture);
        furniture.SetMovable(false);

        if (selectedIndex < 0)
        {
            selectedIndex = 0;
            SelectionChanged?.Invoke(SelectedFurniture);
        }

        ApplyMovableState();
    }

    public void UnregisterFurniture(PlacedFurniture furniture)
    {
        if (furniture == null)
        {
            return;
        }

        int removedIndex = furnitures.IndexOf(furniture);
        if (removedIndex < 0)
        {
            return;
        }

        furniture.SetMovable(false);
        furnitures.RemoveAt(removedIndex);

        if (furnitures.Count == 0)
        {
            selectedIndex = -1;
        }
        else if (selectedIndex >= furnitures.Count)
        {
            selectedIndex = furnitures.Count - 1;
        }

        ApplyMovableState();
        SelectionChanged?.Invoke(SelectedFurniture);
    }

    public void ToggleMoveMode()
    {
        SetMoveMode(currentMode != FurnitureManipulationMode.Move);
    }

    public void SetMoveMode(bool enabled)
    {
        SetManipulationMode(enabled ? FurnitureManipulationMode.Move : FurnitureManipulationMode.None);
    }

    public void ToggleRotateMode()
    {
        SetRotateMode(currentMode != FurnitureManipulationMode.Rotate);
    }

    public void SetRotateMode(bool enabled)
    {
        SetManipulationMode(enabled ? FurnitureManipulationMode.Rotate : FurnitureManipulationMode.None);
    }

    public void ToggleScaleMode()
    {
        SetScaleMode(currentMode != FurnitureManipulationMode.Scale);
    }

    public void SetScaleMode(bool enabled)
    {
        SetManipulationMode(enabled ? FurnitureManipulationMode.Scale : FurnitureManipulationMode.None);
    }

    public void SetManipulationMode(FurnitureManipulationMode mode)
    {
        if (currentMode == mode)
        {
            ApplyMovableState();
            return;
        }

        currentMode = mode;
        isMoveModeEnabled = currentMode != FurnitureManipulationMode.None;

        if (isMoveModeEnabled && !allowAllFurnitureMovableInMoveMode && autoSelectFirstFurnitureWhenMoveModeEnabled && !IsValidIndex(selectedIndex) && furnitures.Count > 0)
        {
            selectedIndex = 0;
            SelectionChanged?.Invoke(SelectedFurniture);
        }

        ApplyMovableState();
        MoveModeChanged?.Invoke(isMoveModeEnabled);
        Debug.Log($"[FurnitureMoveModeController] Manipulation mode:{currentMode}, allowAll:{allowAllFurnitureMovableInMoveMode}");
    }

    public void SetSelectionOnlyMode(bool enabled)
    {
        if (selectionOnlyMode == enabled)
        {
            ApplyMovableState();
            return;
        }

        selectionOnlyMode = enabled;
        ApplyMovableState();
        Debug.Log($"[FurnitureMoveModeController] Selection-only mode:{selectionOnlyMode}");
    }

    public void SelectNext()
    {
        if (furnitures.Count == 0)
        {
            selectedIndex = -1;
            SelectionChanged?.Invoke(null);
            Debug.LogWarning("[FurnitureMoveModeController] SelectNext failed. No furniture registered.");
            return;
        }

        selectedIndex = selectedIndex < 0 ? 0 : (selectedIndex + 1) % furnitures.Count;
        ClearValidationIndicatorOnSelect(SelectedFurniture);
        ApplyMovableState();
        SelectionChanged?.Invoke(SelectedFurniture);
        SelectedFurniture?.NotifySelectedByController("SelectNext");
        Debug.Log($"[FurnitureMoveModeController] Selected next: {SelectedFurniture.DisplayName}", SelectedFurniture);
    }

    public void SelectPrevious()
    {
        if (furnitures.Count == 0)
        {
            selectedIndex = -1;
            SelectionChanged?.Invoke(null);
            Debug.LogWarning("[FurnitureMoveModeController] SelectPrevious failed. No furniture registered.");
            return;
        }

        selectedIndex = selectedIndex < 0 ? 0 : (selectedIndex - 1 + furnitures.Count) % furnitures.Count;
        ClearValidationIndicatorOnSelect(SelectedFurniture);
        ApplyMovableState();
        SelectionChanged?.Invoke(SelectedFurniture);
        SelectedFurniture?.NotifySelectedByController("SelectPrevious");
        Debug.Log($"[FurnitureMoveModeController] Selected previous: {SelectedFurniture.DisplayName}", SelectedFurniture);
    }

    public void SelectFurniture(PlacedFurniture furniture)
    {
        int index = furnitures.IndexOf(furniture);
        if (index < 0)
        {
            Debug.LogWarning("[FurnitureMoveModeController] SelectFurniture failed. Furniture is not registered.", furniture);
            return;
        }

        PlacedFurniture previous = SelectedFurniture;
        if (previous != null && previous != furniture)
        {
            previousSelectedFurniture = previous;
        }

        selectedIndex = index;
        ClearValidationIndicatorOnSelect(furniture);
        ApplyMovableState();
        SelectionChanged?.Invoke(SelectedFurniture);
        SelectedFurniture?.NotifySelectedByController("SelectFurniture");
        Debug.Log($"[FurnitureMoveModeController] Selected: {SelectedFurniture.DisplayName}", SelectedFurniture);
    }

    // 현재 선택이 고정되어 조작할 수 없을 때, 마지막으로 누른(없으면 등록된) 비고정 가구로
    // 선택을 옮긴다. 옮길 대상이 없으면 false를 반환하고 현재 선택을 유지한다.
    public bool SelectLastPressedNonFixedFurniture()
    {
        PlacedFurniture current = SelectedFurniture;

        if (CanFallbackTo(previousSelectedFurniture, current))
        {
            SelectFurniture(previousSelectedFurniture);
            return true;
        }

        foreach (PlacedFurniture candidate in furnitures)
        {
            if (CanFallbackTo(candidate, current))
            {
                SelectFurniture(candidate);
                return true;
            }
        }

        return false;
    }

    private bool CanFallbackTo(PlacedFurniture furniture, PlacedFurniture exclude)
    {
        return furniture != null && furniture != exclude && furnitures.Contains(furniture) && !furniture.IsFixed;
    }

    public void ClearSelection()
    {
        selectedIndex = -1;
        ApplyMovableState();
        SelectionChanged?.Invoke(null);
    }

    private void ApplyMovableState()
    {
        for (int i = 0; i < furnitures.Count; i++)
        {
            PlacedFurniture furniture = furnitures[i];
            if (furniture == null)
            {
                continue;
            }

            FurnitureManipulationMode mode = FurnitureManipulationMode.None;
            if (isMoveModeEnabled)
            {
                if (selectionOnlyMode)
                {
                    furniture.SetManipulationMode(FurnitureManipulationMode.None, true);
                    UpdateBoxVisual(i);
                    continue;
                }

                bool active = allowAllFurnitureMovableInMoveMode || i == selectedIndex;
                mode = active ? currentMode : FurnitureManipulationMode.None;
            }

            furniture.SetManipulationMode(mode);
            UpdateBoxVisual(i);
        }
    }

    // BoxCollider 영역 표시 우선순위: 미충족 > 선택됨 > 표시 안 함.
    // 미충족 플래그는 가구를 실제로 선택/이동(= 다른 작업)할 때만 해제되며, 그 전까지는
    // 선택 인덱스라도 미충족 material을 우선 표시한다. 한 번 해제되면 배치 완료 재검증
    // 전까지 미충족 material로 돌아가지 않는다.
    private void UpdateBoxVisual(int i)
    {
        PlacedFurniture furniture = furnitures[i];
        if (furniture == null) return;

        if (furniture.PlacementValidationFailed && invalidPlacementBoxMaterial != null)
        {
            furniture.SetSelectionBoxVisible(true, invalidPlacementBoxMaterial);
        }
        else if (i == selectedIndex)
        {
            furniture.SetSelectionBoxVisible(true, selectionBoxMaterial);
        }
        else
        {
            furniture.SetSelectionBoxVisible(false, null);
        }
    }

    // 사용자가 가구를 실제로 선택하는 것은 "다른 작업"이므로 미충족 표시를 해제한다.
    private static void ClearValidationIndicatorOnSelect(PlacedFurniture furniture)
    {
        if (furniture != null) furniture.SetPlacementValidationFailed(false);
    }

    // 배치 완료 검증 직후 패널에서 호출한다. 미충족 플래그에 따라 BoxCollider 표시를 갱신한다.
    public void RefreshPlacementValidityVisuals()
    {
        for (int i = 0; i < furnitures.Count; i++)
        {
            UpdateBoxVisual(i);
        }
    }

    private bool IsValidIndex(int index)
    {
        return index >= 0 && index < furnitures.Count && furnitures[index] != null;
    }
}

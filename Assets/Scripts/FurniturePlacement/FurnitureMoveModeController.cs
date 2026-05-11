using System;
using System.Collections.Generic;
using UnityEngine;

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

    [Tooltip("true면 Move Mode ON일 때 모든 가구가 직접 터치/이동 가능합니다. false면 선택된 가구 1개만 이동 가능합니다.")]
    public bool allowAllFurnitureMovableInMoveMode = true;

    [Tooltip("allowAllFurnitureMovableInMoveMode가 false일 때, Move Mode가 켜질 때 선택된 가구가 없으면 첫 가구를 자동 선택합니다.")]
    public bool autoSelectFirstFurnitureWhenMoveModeEnabled = true;

    private readonly List<PlacedFurniture> furnitures = new List<PlacedFurniture>();
    private int selectedIndex = -1;

    public event Action<bool> MoveModeChanged;
    public event Action<PlacedFurniture> SelectionChanged;

    public bool IsMoveModeEnabled => isMoveModeEnabled;
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
        SetMoveMode(!isMoveModeEnabled);
    }

    public void SetMoveMode(bool enabled)
    {
        if (isMoveModeEnabled == enabled)
        {
            ApplyMovableState();
            return;
        }

        isMoveModeEnabled = enabled;

        if (isMoveModeEnabled && !allowAllFurnitureMovableInMoveMode && autoSelectFirstFurnitureWhenMoveModeEnabled && !IsValidIndex(selectedIndex) && furnitures.Count > 0)
        {
            selectedIndex = 0;
            SelectionChanged?.Invoke(SelectedFurniture);
        }

        ApplyMovableState();
        MoveModeChanged?.Invoke(isMoveModeEnabled);
        Debug.Log($"[FurnitureMoveModeController] Move Mode {(isMoveModeEnabled ? "ON" : "OFF")}, allowAll:{allowAllFurnitureMovableInMoveMode}");
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
        ApplyMovableState();
        SelectionChanged?.Invoke(SelectedFurniture);
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
        ApplyMovableState();
        SelectionChanged?.Invoke(SelectedFurniture);
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

        selectedIndex = index;
        ApplyMovableState();
        SelectionChanged?.Invoke(SelectedFurniture);
        Debug.Log($"[FurnitureMoveModeController] Selected: {SelectedFurniture.DisplayName}", SelectedFurniture);
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

            bool movable = false;
            if (isMoveModeEnabled)
            {
                movable = allowAllFurnitureMovableInMoveMode || i == selectedIndex;
            }

            furniture.SetMovable(movable);
        }
    }

    private bool IsValidIndex(int index)
    {
        return index >= 0 && index < furnitures.Count && furnitures[index] != null;
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 실사용 가구 배치 관리자입니다.
/// 이 클래스는 label로 prefab을 찾거나 랜덤 배치를 하지 않습니다.
/// 서버/인식/디버그 파이프라인이 이미 생성한 GameObject를 RegisterRecognizedFurniture()로 등록합니다.
///
/// 등록 시 RuntimeFurnitureInteractionSetup을 통해 MeshCollider, Convex, Rigidbody, ObjectManipulator 등을 보강할 수 있습니다.
/// </summary>
public class FurniturePlacementManager : MonoBehaviour
{
    [Header("References")]
    public ConfirmedRoomGeometryProvider roomGeometryProvider;
    public FurnitureMoveModeController moveModeController;
    public FurnitureServerPlacementPipeline serverPlacementPipeline;
    public RuntimeFurnitureInteractionSetup runtimeInteractionSetup;

    [Tooltip("등록된 가구들을 이 Transform 하위로 정리합니다. 비워두면 현재 parent를 유지합니다.")]
    public Transform placedFurnitureRoot;

    [Header("Runtime Setup")]
    [Tooltip("등록 직전에 MeshCollider/Convex/Rigidbody/ObjectManipulator/PlacedFurniture를 자동 보강합니다.")]
    public bool configureRuntimeInteractionOnRegister = true;

    [Header("Registration")]
    [Tooltip("등록 직후 방 내부/벽 충돌 검증 및 바닥 스냅을 수행합니다.")]
    public bool validateOnRegister = true;

    [Tooltip("등록 검증에 실패한 서버/디버그 가구 오브젝트를 즉시 제거합니다.")]
    public bool destroyObjectWhenRegisterValidationFails = true;

    [Tooltip("참조가 비어 있으면 Scene에서 자동 탐색합니다.")]
    public bool autoFindReferences = true;

    private readonly List<PlacedFurniture> placedFurnitures = new List<PlacedFurniture>();
    private PlacedFurniture activeNotConfirmedFurniture;

    public event Action PlacementChanged;

    public IReadOnlyList<PlacedFurniture> PlacedFurnitures => placedFurnitures;
    public int PlacedCount => placedFurnitures.Count;
    public int ConfirmedCount => placedFurnitures.FindAll(f => f != null && f.State == FurniturePlacementState.Confirmed).Count;
    public PlacedFurniture ActiveNotConfirmedFurniture => activeNotConfirmedFurniture;
    public bool HasAnyFurniture => placedFurnitures.Count > 0;

    private void Awake()
    {
        EnsureReferences();
    }

    private void OnEnable()
    {
        EnsureReferences();
    }

    public bool RegisterRecognizedFurniture(GameObject furnitureObject)
    {
        return RegisterRecognizedFurniture(furnitureObject, null);
    }

    public bool RegisterRecognizedFurniture(GameObject furnitureObject, FurniturePlacementMetadata metadata)
    {
        EnsureReferences();

        if (furnitureObject == null)
        {
            Debug.LogWarning("[FurniturePlacementManager] Register failed. furnitureObject is null.");
            return false;
        }

        if (metadata == null)
        {
            metadata = FurniturePlacementMetadata.CreateDefault(furnitureObject, "server");
        }

        metadata.EnsureDefaults(furnitureObject, "server");

        if (placedFurnitureRoot != null && furnitureObject.transform.parent != placedFurnitureRoot)
        {
            furnitureObject.transform.SetParent(placedFurnitureRoot, true);
        }

        PlacedFurniture placedFurniture = null;
        if (configureRuntimeInteractionOnRegister)
        {
            EnsureRuntimeInteractionSetup();
            if (runtimeInteractionSetup != null)
            {
                placedFurniture = runtimeInteractionSetup.ConfigureFurnitureObject(furnitureObject);
            }
        }

        if (placedFurniture == null)
        {
            placedFurniture = furnitureObject.GetComponent<PlacedFurniture>();
        }

        if (placedFurniture == null)
        {
            placedFurniture = furnitureObject.AddComponent<PlacedFurniture>();
        }

        placedFurniture.Initialize(this, metadata);

        if (!placedFurnitures.Contains(placedFurniture))
        {
            placedFurnitures.Add(placedFurniture);
        }

        roomGeometryProvider?.RegisterPlacedFurniture(placedFurniture);
        moveModeController?.RegisterFurniture(placedFurniture);

        bool valid = true;
        if (validateOnRegister && roomGeometryProvider != null)
        {
            valid = roomGeometryProvider.ValidateAndCorrectFurniturePose(placedFurniture, out string reason);
            if (!valid)
            {
                placedFurniture.SetState(FurniturePlacementState.Invalid);
                Debug.LogWarning($"[FurniturePlacementManager] Registered furniture is invalid. {metadata.displayName} / {reason}", furnitureObject);

                UnregisterFurniture(placedFurniture, destroyObjectWhenRegisterValidationFails);
                PlacementChanged?.Invoke();
                return false;
            }
        }

        placedFurniture.SaveCurrentPoseAsValid();
        placedFurniture.SetState(FurniturePlacementState.Registered);
        Debug.Log($"[FurniturePlacementManager] Furniture registered. id:{placedFurniture.FurnitureId}, label:{placedFurniture.Label}, name:{placedFurniture.DisplayName}", furnitureObject);
        PlacementChanged?.Invoke();
        return true;
    }

    public void HandleFurnitureTouched(PlacedFurniture furniture)
    {
        HandleFurnitureTouched(furniture, "Touch");
    }

    public void HandleFurnitureTouched(PlacedFurniture furniture, string inputSource)
    {
        EnsureReferences();

        if (furniture == null)
        {
            return;
        }

        if (moveModeController == null)
        {
            Debug.LogWarning("[FurniturePlacementManager] Furniture touch ignored. MoveModeController is missing.", furniture);
            return;
        }

        if (!moveModeController.IsMoveModeEnabled)
        {
            Debug.Log($"[FurniturePlacementManager] Furniture touched but Move Mode is OFF. source:{inputSource}, furniture:{furniture.DisplayName}", furniture);
            return;
        }

        if (!SetActiveNotConfirmedFurniture(furniture, inputSource))
        {
            return;
        }

        moveModeController.SelectFurniture(furniture);
        Debug.Log($"[FurniturePlacementManager] Furniture selected by touch/move. source:{inputSource}, furniture:{furniture.DisplayName}", furniture);
    }

    public void HandleFurnitureSelected(PlacedFurniture furniture, string inputSource)
    {
        SetActiveNotConfirmedFurniture(furniture, inputSource);
    }

    public void HandleFurnitureMoveEnded(PlacedFurniture furniture)
    {
        if (furniture == null)
        {
            return;
        }

        if (roomGeometryProvider == null)
        {
            furniture.SaveCurrentPoseAsValid();
            SetActiveNotConfirmedFurniture(furniture, "MoveEndedNoRoomGeometry");
            Debug.LogWarning("[FurniturePlacementManager] Room geometry provider is missing. Move validation skipped.");
            PlacementChanged?.Invoke();
            return;
        }

        bool valid = roomGeometryProvider.ValidateAndCorrectFurniturePose(furniture, out string reason);
        if (valid)
        {
            furniture.SaveCurrentPoseAsValid();
            SetActiveNotConfirmedFurniture(furniture, "MoveEnded");
            Debug.Log($"[FurniturePlacementManager] Furniture move accepted. {furniture.DisplayName}", furniture);
        }
        else
        {
            furniture.RestoreLastValidPose();
            Debug.LogWarning($"[FurniturePlacementManager] Furniture move rejected. {furniture.DisplayName} / {reason}", furniture);
        }

        PlacementChanged?.Invoke();
    }

    public bool ConfirmSelectedFurniture()
    {
        EnsureReferences();

        PlacedFurniture selected = moveModeController != null ? moveModeController.SelectedFurniture : null;
        return ConfirmFurniture(selected);
    }

    public bool ConfirmActiveNotConfirmedFurniture()
    {
        return activeNotConfirmedFurniture == null || ConfirmFurniture(activeNotConfirmedFurniture);
    }

    public bool ConfirmFurniture(PlacedFurniture selected)
    {
        EnsureReferences();

        if (selected == null)
        {
            Debug.LogWarning("[FurniturePlacementManager] Confirm failed. No furniture.");
            return false;
        }

        if (roomGeometryProvider != null)
        {
            bool valid = roomGeometryProvider.ValidateAndCorrectFurniturePose(selected, out string reason);
            if (!valid)
            {
                Debug.LogWarning($"[FurniturePlacementManager] Confirm failed. {selected.DisplayName} / {reason}", selected);
                return false;
            }
        }

        selected.SetState(FurniturePlacementState.Confirmed);
        selected.SaveCurrentPoseAsValid();
        if (activeNotConfirmedFurniture == selected)
        {
            activeNotConfirmedFurniture = null;
        }

        serverPlacementPipeline?.ConfirmPlacedObject(selected);
        Debug.Log($"[FurniturePlacementManager] Furniture confirmed. {selected.DisplayName}", selected);
        PlacementChanged?.Invoke();
        return true;
    }

    public bool SetActiveNotConfirmedFurniture(PlacedFurniture furniture, string reason = "")
    {
        EnsureReferences();

        if (furniture == null || !placedFurnitures.Contains(furniture))
        {
            return false;
        }

        if (activeNotConfirmedFurniture != null && activeNotConfirmedFurniture != furniture)
        {
            if (!ConfirmFurniture(activeNotConfirmedFurniture))
            {
                return false;
            }
        }

        activeNotConfirmedFurniture = furniture;
        if (furniture.State != FurniturePlacementState.NotConfirmed)
        {
            furniture.SetState(FurniturePlacementState.NotConfirmed);
        }

        serverPlacementPipeline?.MarkPlacedObjectNotConfirmed(furniture);
        PlacementChanged?.Invoke();
        Debug.Log($"[FurniturePlacementManager] Furniture marked NotConfirmed. {furniture.DisplayName}, reason:{reason}", furniture);
        return true;
    }

    public void PrepareAllForSimulation()
    {
        foreach (PlacedFurniture furniture in placedFurnitures)
        {
            if (furniture != null)
            {
                furniture.PrepareForSimulation();
            }
        }

        Debug.Log($"[FurniturePlacementManager] Prepared furniture for simulation. count:{placedFurnitures.Count}");
    }

    public void ClearPlacedFurniture()
    {
        for (int i = placedFurnitures.Count - 1; i >= 0; i--)
        {
            UnregisterFurniture(placedFurnitures[i], true);
        }

        placedFurnitures.Clear();
        PlacementChanged?.Invoke();
        Debug.Log("[FurniturePlacementManager] Cleared all placed furniture.");
    }

    public void UnregisterFurniture(PlacedFurniture furniture, bool destroyObject)
    {
        if (furniture == null)
        {
            return;
        }

        placedFurnitures.Remove(furniture);
        if (activeNotConfirmedFurniture == furniture)
        {
            activeNotConfirmedFurniture = null;
        }

        roomGeometryProvider?.UnregisterPlacedFurniture(furniture);
        moveModeController?.UnregisterFurniture(furniture);

        if (destroyObject)
        {
            Destroy(furniture.gameObject);
        }
    }

    private void EnsureReferences()
    {
        if (!autoFindReferences)
        {
            return;
        }

        if (roomGeometryProvider == null)
        {
            roomGeometryProvider = FindFirst<ConfirmedRoomGeometryProvider>();
        }

        if (moveModeController == null)
        {
            moveModeController = FindFirst<FurnitureMoveModeController>();
        }

        if (serverPlacementPipeline == null)
        {
            serverPlacementPipeline = FindFirst<FurnitureServerPlacementPipeline>();
        }

        if (runtimeInteractionSetup == null)
        {
            runtimeInteractionSetup = FindFirst<RuntimeFurnitureInteractionSetup>();
        }
    }

    private void EnsureRuntimeInteractionSetup()
    {
        if (runtimeInteractionSetup != null)
        {
            return;
        }

        runtimeInteractionSetup = GetComponent<RuntimeFurnitureInteractionSetup>();
        if (runtimeInteractionSetup == null)
        {
            runtimeInteractionSetup = gameObject.AddComponent<RuntimeFurnitureInteractionSetup>();
        }
    }

    private static T FindFirst<T>() where T : UnityEngine.Object
    {
        T[] objects = FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        return objects != null && objects.Length > 0 ? objects[0] : null;
    }
}

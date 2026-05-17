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

    public event Action PlacementChanged;

    public IReadOnlyList<PlacedFurniture> PlacedFurnitures => placedFurnitures;
    public int PlacedCount => placedFurnitures.Count;
    public int ConfirmedCount => placedFurnitures.FindAll(f => f != null && f.State == FurniturePlacementState.Confirmed).Count;
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

        // 스폰 위치가 다른 가구와 겹치면 ① 그 가구 위에 쌓기 → ② 사용자 바로 아래 →
        // ③ 방 내부 빈 공간 순으로 재배치한다.
        SpawnOverlapResolution spawnResolution = SpawnOverlapResolution.NoOverlap;
        if (roomGeometryProvider != null && roomGeometryProvider.IsInitialized)
        {
            spawnResolution = roomGeometryProvider.ResolveInitialSpawnOverlap(placedFurniture);
        }
        bool stackedOnFurniture = spawnResolution == SpawnOverlapResolution.StackedOnTop;

        bool valid = true;
        if (!stackedOnFurniture && validateOnRegister && roomGeometryProvider != null)
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

        // 등록 검증을 건너뛰는 경로(수동 배치 등)에서도 최초 유효 포즈의 Y가
        // 바닥 기준으로 보정되도록 한다. 그렇지 않으면 raw 스폰 Y가 저장되어
        // 이후 거부될 때마다 RestoreLastValidPose가 바닥 아래로 되돌린다.
        // 가구 위에 쌓은 경우는 의도적으로 바닥에서 떨어져 있으므로 바닥 스냅을 건너뛴다.
        if (!stackedOnFurniture && roomGeometryProvider != null && roomGeometryProvider.IsInitialized)
        {
            roomGeometryProvider.CorrectFurnitureToFloor(placedFurniture);
        }

        placedFurniture.SaveCurrentPoseAsValid();

        // 💡 상태를 즉시 확정 처리합니다.
        placedFurniture.SetState(FurniturePlacementState.Confirmed);

        Debug.Log($"[FurniturePlacementManager] Furniture registered and auto-confirmed. id:{placedFurniture.FurnitureId}, label:{placedFurniture.Label}, name:{placedFurniture.DisplayName}", furnitureObject);
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

        if (furniture == null) return;

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

        // 💡 터치 시 NotConfirmed 상태 전환 없이 바로 선택만 진행합니다.
        moveModeController.SelectFurniture(furniture);
        Debug.Log($"[FurniturePlacementManager] Furniture selected by touch/move. source:{inputSource}, furniture:{furniture.DisplayName}", furniture);
    }

    public void HandleFurnitureSelected(PlacedFurniture furniture, string inputSource)
    {
        Debug.Log($"[FurniturePlacementManager] Furniture Selected. {furniture?.DisplayName}");
    }

    // 💡 손에서 놓았을 때(Move Ended) 자동 확정 또는 원상 복구를 처리하는 핵심 로직입니다.
    public void HandleFurnitureMoveEnded(PlacedFurniture furniture)
    {
        if (furniture == null) return;

        if (roomGeometryProvider == null)
        {
            furniture.SaveCurrentPoseAsValid();
            furniture.SetState(FurniturePlacementState.Confirmed);
            serverPlacementPipeline?.ConfirmPlacedObject(furniture);

            Debug.LogWarning("[FurniturePlacementManager] Room geometry provider is missing. Auto-confirmed without validation.");
            PlacementChanged?.Invoke();
            return;
        }

        bool valid = roomGeometryProvider.ValidateAndCorrectFurniturePose(furniture, out string reason);
        if (valid)
        {
            // 방 기하학적으로 유효하면 현재 위치를 저장하고 확정 상태로 만듭니다.
            furniture.SaveCurrentPoseAsValid();
            furniture.SetState(FurniturePlacementState.Confirmed);
            serverPlacementPipeline?.ConfirmPlacedObject(furniture);

            Debug.Log($"[FurniturePlacementManager] Furniture move accepted and AUTO-CONFIRMED. {furniture.DisplayName}", furniture);
        }
        else
        {
            // 유효하지 않으면 마지막으로 유효했던 위치로 되돌립니다.
            furniture.RestoreLastValidPose();
            Debug.LogWarning($"[FurniturePlacementManager] Furniture move rejected. Restored to last valid pose. {furniture.DisplayName} / {reason}", furniture);
        }

        PlacementChanged?.Invoke();
    }

    // 버튼식 스케일 조정 전용 처리. 이동 종료와 달리, 검증 실패해도 포즈/스케일을
    // 되돌리지 않는다. 방보다 큰 가구를 줄여 맞추려면 줄이는 스텝이 누적되어
    // 점진적으로 방 안에 들어와야 하기 때문이다.
    // 반환값: 검증/보정 후 유효한 위치에 안착했는지 여부. 호출자(스케일 스텝)는
    // 키우는 도중 무효면 스텝을 취소하고, 줄이는 도중 무효면 그대로 유지한다.
    public bool HandleFurnitureScaleChanged(PlacedFurniture furniture)
    {
        if (furniture == null) return false;

        if (roomGeometryProvider == null)
        {
            furniture.SaveCurrentPoseAsValid();
            furniture.SetState(FurniturePlacementState.Confirmed);
            serverPlacementPipeline?.ConfirmPlacedObject(furniture);
            PlacementChanged?.Invoke();
            return true;
        }

        // ValidateAndCorrectFurniturePose가 벽/모서리/다른 가구를 피해 위치를 이동시킨다.
        bool valid = roomGeometryProvider.ValidateAndCorrectFurniturePose(furniture, out string reason);
        if (valid)
        {
            furniture.SaveCurrentPoseAsValid();
            furniture.SetState(FurniturePlacementState.Confirmed);
            serverPlacementPipeline?.ConfirmPlacedObject(furniture);
        }
        else
        {
            Debug.LogWarning($"[FurniturePlacementManager] Furniture scale pose still invalid after correction. {furniture.DisplayName} / {reason}", furniture);
        }

        PlacementChanged?.Invoke();
        return valid;
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

    public bool DeleteFurniture(PlacedFurniture furniture)
    {
        if (furniture == null) return false;

        string furnitureName = furniture.DisplayName;
        UnregisterFurniture(furniture, true);
        PlacementChanged?.Invoke();
        Debug.Log($"[FurniturePlacementManager] Deleted furniture: {furnitureName}");
        return true;
    }

    public void UnregisterFurniture(PlacedFurniture furniture, bool destroyObject)
    {
        if (furniture == null) return;

        serverPlacementPipeline?.RemovePlacedObject(furniture);
        placedFurnitures.Remove(furniture);

        roomGeometryProvider?.UnregisterPlacedFurniture(furniture);
        moveModeController?.UnregisterFurniture(furniture);

        if (destroyObject)
        {
            Destroy(furniture.gameObject);
        }
    }

    private void EnsureReferences()
    {
        if (!autoFindReferences) return;

        if (roomGeometryProvider == null)
            roomGeometryProvider = FindFirst<ConfirmedRoomGeometryProvider>();

        if (moveModeController == null)
            moveModeController = FindFirst<FurnitureMoveModeController>();

        if (serverPlacementPipeline == null)
            serverPlacementPipeline = FindFirst<FurnitureServerPlacementPipeline>();

        if (runtimeInteractionSetup == null)
            runtimeInteractionSetup = FindFirst<RuntimeFurnitureInteractionSetup>();
    }

    private void EnsureRuntimeInteractionSetup()
    {
        if (runtimeInteractionSetup != null) return;

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

    // --- Placement Condition Validation ---
    // 배치 완료 시 모든 가구가 다음 조건을 만족하는지 검사한다.
    //  1) 고정이 아니면: 바닥 또는 다른 가구가 바로 아래에 있어야 한다.
    //  2) 벽걸이가 아니면: 지지 체인을 따라 결국 바닥과 연결되어 있어야 한다.
    //  3) 고정이면: 벽 또는 천장에 붙어 있거나, 고정 가구 체인을 통해 벽/천장에 연결되어야 한다.
    // 위반 가구 목록을 반환한다. 방 기하 정보가 없으면 검증을 건너뛴다(빈 목록).
    public List<PlacedFurniture> EvaluatePlacementConditions()
    {
        var invalid = new List<PlacedFurniture>();
        EnsureReferences();

        if (roomGeometryProvider == null || !roomGeometryProvider.IsInitialized)
        {
            Debug.LogWarning("[FurniturePlacementManager] Placement validation skipped. Room geometry is not initialized.");
            return invalid;
        }

        foreach (PlacedFurniture furniture in placedFurnitures)
        {
            if (furniture == null) continue;
            if (!IsPlacementConditionSatisfied(furniture, out string reason))
            {
                invalid.Add(furniture);
                Debug.LogWarning($"[FurniturePlacementManager] Placement condition not satisfied: {furniture.DisplayName} / {reason}", furniture);
            }
        }

        return invalid;
    }

    private bool IsPlacementConditionSatisfied(PlacedFurniture furniture, out string reason)
    {
        reason = string.Empty;
        ConfirmedRoomGeometryProvider geometry = roomGeometryProvider;

        // 1) 고정이 아니면 바닥 또는 다른 가구가 바로 아래에 있어야 한다(벽걸이라도 동일 적용).
        if (!furniture.IsFixed)
        {
            bool onFloor = geometry.IsFurnitureOnFloor(furniture);
            bool onFurniture = geometry.HasFurnitureSupportBelow(furniture, placedFurnitures);
            if (!onFloor && !onFurniture)
            {
                reason = "비고정 가구 아래에 바닥/다른 가구 지지가 없습니다.";
                return false;
            }
        }

        // 2) 벽걸이가 아니면 지지 체인을 따라 바닥과 연결되어 있어야 한다.
        if (!furniture.IsWallMounted)
        {
            if (!IsGroundedToFloor(furniture, new HashSet<PlacedFurniture>()))
            {
                reason = "비벽걸이 가구가 바닥과 연결되어 있지 않습니다.";
                return false;
            }
        }

        // 3) 고정이면 벽/천장(또는 고정 가구 체인)에 anchor 되어야 한다(공중부양 방지).
        if (furniture.IsFixed)
        {
            if (!IsFixedAnchored(furniture, new HashSet<PlacedFurniture>()))
            {
                reason = "고정 가구가 벽/천장(또는 고정 가구 체인)에 붙어 있지 않습니다.";
                return false;
            }
        }

        return true;
    }

    // 가구가 바닥에 직접 닿아 있거나, 자신을 떠받치는 가구를 따라 내려가 결국 바닥에 닿는지.
    private bool IsGroundedToFloor(PlacedFurniture furniture, HashSet<PlacedFurniture> visited)
    {
        if (furniture == null || !visited.Add(furniture)) return false;
        if (roomGeometryProvider.IsFurnitureOnFloor(furniture)) return true;

        foreach (PlacedFurniture other in placedFurnitures)
        {
            if (other == null || other == furniture) continue;
            if (roomGeometryProvider.IsFurnitureSupportedBy(furniture, other) &&
                IsGroundedToFloor(other, visited))
            {
                return true;
            }
        }

        return false;
    }

    // 고정 가구가 벽/천장에 닿아 있거나, 인접한 고정 가구 체인을 통해 벽/천장에 연결되는지.
    private bool IsFixedAnchored(PlacedFurniture furniture, HashSet<PlacedFurniture> visited)
    {
        if (furniture == null || !visited.Add(furniture)) return false;
        if (roomGeometryProvider.IsFurnitureTouchingWall(furniture) ||
            roomGeometryProvider.IsFurnitureTouchingCeiling(furniture))
        {
            return true;
        }

        foreach (PlacedFurniture other in placedFurnitures)
        {
            if (other == null || other == furniture || !other.IsFixed) continue;
            if (roomGeometryProvider.AreFurnituresConnected(furniture, other) &&
                IsFixedAnchored(other, visited))
            {
                return true;
            }
        }

        return false;
    }
}

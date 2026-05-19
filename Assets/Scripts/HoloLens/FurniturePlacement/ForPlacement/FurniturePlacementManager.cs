using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// ?�사??가�?배치 관리자?�니??
/// ???�래?�는 label�?prefab??찾거???�덤 배치�??��? ?�습?�다.
/// ?�버/?�식/?�버�??�이?�라?�이 ?��? ?�성??GameObject�?RegisterRecognizedFurniture()�??�록?�니??
///
/// ?�록 ??RuntimeFurnitureInteractionSetup???�해 MeshCollider, Convex, Rigidbody, ObjectManipulator ?�을 보강?????�습?�다.
/// </summary>
public class FurniturePlacementManager : MonoBehaviour
{
    [Header("References")]
    public ConfirmedRoomGeometryProvider roomGeometryProvider;
    public FurnitureMoveModeController moveModeController;
    public FurnitureServerPlacementPipeline serverPlacementPipeline;
    public RuntimeFurnitureInteractionSetup runtimeInteractionSetup;

    [Tooltip("?�록??가구들????Transform ?�위�??�리?�니?? 비워?�면 ?�재 parent�??��??�니??")]
    public Transform placedFurnitureRoot;

    [Header("Runtime Setup")]
    [Tooltip("?�록 직전??MeshCollider/Convex/Rigidbody/ObjectManipulator/PlacedFurniture�??�동 보강?�니??")]
    public bool configureRuntimeInteractionOnRegister = true;

    [Header("Registration")]
    [Tooltip("?�록 직후 �??��?/�?충돌 검�?�?바닥 ?�냅???�행?�니??")]
    public bool validateOnRegister = true;

    [Tooltip("?�록 검증에 ?�패???�버/?�버�?가�??�브?�트�?즉시 ?�거?�니??")]
    public bool destroyObjectWhenRegisterValidationFails = true;

    [Tooltip("참조가 비어 ?�으�?Scene?�서 ?�동 ?�색?�니??")]
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

        AssignPlacedInstanceIdentity(metadata);
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

        // ?�폰 ?�치가 ?�른 가구�? 겹치�???�?가�??�에 ?�기 ?????�용??바로 ?�래 ??
        // ??�??��? �?공간 ?�으�??�배치한??
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

        // ?�록 검증을 건너?�는 경로(?�동 배치 ???�서??최초 ?�효 ?�즈??Y가
        // 바닥 기�??�로 보정?�도�??�다. 그렇지 ?�으�?raw ?�폰 Y가 ?�?�되??
        // ?�후 거�????�마??RestoreLastValidPose가 바닥 ?�래�??�돌린다.
        // 가�??�에 ?��? 경우???�도?�으�?바닥?�서 ?�어???�으므�?바닥 ?�냅??건너?�다.
        if (!stackedOnFurniture && roomGeometryProvider != null && roomGeometryProvider.IsInitialized)
        {
            roomGeometryProvider.CorrectFurnitureToFloor(placedFurniture);
        }

        placedFurniture.SaveCurrentPoseAsValid();

        // ?�� ?�태�?즉시 ?�정 처리?�니??
        placedFurniture.SetState(FurniturePlacementState.Confirmed);

        Debug.Log($"[FurniturePlacementManager] Furniture registered and auto-confirmed. id:{placedFurniture.FurnitureId}, label:{placedFurniture.Label}, name:{placedFurniture.DisplayName}", furnitureObject);
        PlacementChanged?.Invoke();
        return true;
    }

    private void AssignPlacedInstanceIdentity(FurniturePlacementMetadata metadata)
    {
        if (metadata == null)
        {
            return;
        }

        string sourceObjectId = FirstNonEmpty(metadata.sourceObjectId, metadata.serverObjectId);
        if (string.IsNullOrWhiteSpace(sourceObjectId))
        {
            return;
        }

        metadata.sourceObjectId = sourceObjectId;
        metadata.furnitureId = CreateUniquePlacedInstanceId(sourceObjectId, metadata.label);
    }

    private string CreateUniquePlacedInstanceId(string sourceObjectId, string label)
    {
        string baseId = NormalizePlacedInstanceBaseId(sourceObjectId, label);
        int existingCount = CountPlacedInstancesForSourceBase(baseId);
        string candidate;

        do
        {
            candidate = $"{baseId}_{InstanceSuffix(existingCount)}";
            existingCount++;
        }
        while (IsFurnitureIdInUse(candidate));

        return candidate;
    }

    private int CountPlacedInstancesForSourceBase(string sourceBaseId)
    {
        int count = 0;
        for (int i = 0; i < placedFurnitures.Count; i++)
        {
            PlacedFurniture furniture = placedFurnitures[i];
            if (furniture == null)
            {
                continue;
            }

            string existingBase = NormalizePlacedInstanceBaseId(furniture.SourceObjectId, furniture.Label);
            if (string.Equals(existingBase, sourceBaseId, StringComparison.OrdinalIgnoreCase))
            {
                count++;
            }
        }

        return count;
    }

    private bool IsFurnitureIdInUse(string furnitureId)
    {
        for (int i = 0; i < placedFurnitures.Count; i++)
        {
            PlacedFurniture furniture = placedFurnitures[i];
            if (furniture != null &&
                string.Equals(furniture.FurnitureId, furnitureId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizePlacedInstanceBaseId(string sourceObjectId, string label)
    {
        string value = FirstNonEmpty(sourceObjectId, label, "furniture").Trim();
        if (value.StartsWith("scene_", StringComparison.OrdinalIgnoreCase))
        {
            value = value.Substring("scene_".Length);
        }
        else if (value.StartsWith("obj_", StringComparison.OrdinalIgnoreCase))
        {
            value = value.Substring("obj_".Length);
        }

        StringBuilder builder = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            char c = char.ToLowerInvariant(value[i]);
            builder.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
        }

        string normalized = builder.ToString().Trim('_');
        return string.IsNullOrWhiteSpace(normalized) ? "furniture" : normalized;
    }

    private static string InstanceSuffix(int zeroBasedIndex)
    {
        if (zeroBasedIndex >= 0 && zeroBasedIndex < 26)
        {
            return ((char)('a' + zeroBasedIndex)).ToString();
        }

        return $"p{zeroBasedIndex + 1:000}";
    }

    private static string FirstNonEmpty(params string[] values)
    {
        for (int i = 0; i < values.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(values[i]))
            {
                return values[i];
            }
        }

        return string.Empty;
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

        if (furniture.IsFixed)
        {
            moveModeController.SelectFurniture(furniture);
            Debug.Log($"[FurniturePlacementManager] Fixed furniture selected by touch. source:{inputSource}, furniture:{furniture.DisplayName}", furniture);
            return;
        }

        if (moveModeController.CurrentMode == FurnitureManipulationMode.Scale &&
            moveModeController.SelectedFurniture == furniture &&
            IsDirectFurnitureManipulationInput(inputSource))
        {
            moveModeController.SetMoveMode(true);
            Debug.Log($"[FurniturePlacementManager] Selected furniture drag switched Scale mode to Move mode. source:{inputSource}, furniture:{furniture.DisplayName}", furniture);
        }

        // ?�� ?�치 ??NotConfirmed ?�태 ?�환 ?�이 바로 ?�택�?진행?�니??
        moveModeController.SelectFurniture(furniture);
        Debug.Log($"[FurniturePlacementManager] Furniture selected by touch/move. source:{inputSource}, furniture:{furniture.DisplayName}", furniture);
    }

    public void HandleFurnitureSelected(PlacedFurniture furniture, string inputSource)
    {
        Debug.Log($"[FurniturePlacementManager] Furniture Selected. {furniture?.DisplayName}");
    }

    // ?�� ?�에???�았????Move Ended) ?�동 ?�정 ?�는 ?�상 복구�?처리?�는 ?�심 로직?�니??
    public void HandleFurnitureMoveEnded(PlacedFurniture furniture)
    {
        HandleFurnitureMoveEnded(furniture, furniture != null ? furniture.ManipulationMode : FurnitureManipulationMode.None);
    }

    public void HandleFurnitureMoveEnded(PlacedFurniture furniture, FurnitureManipulationMode endedMode)
    {
        if (furniture == null) return;

        if (endedMode == FurnitureManipulationMode.Move || endedMode == FurnitureManipulationMode.Rotate)
        {
            roomGeometryProvider?.NormalizeFurniturePoseForMoveOrRotate(furniture);
            furniture.SaveCurrentPoseAsValid();
            furniture.SetState(FurniturePlacementState.Confirmed);
            serverPlacementPipeline?.ConfirmPlacedObject(furniture);

            Debug.Log($"[FurniturePlacementManager] Furniture {endedMode} accepted without room boundary validation. {furniture.DisplayName}", furniture);
            PlacementChanged?.Invoke();
            return;
        }

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
            // �?기하?�적?�로 ?�효?�면 ?�재 ?�치�??�?�하�??�정 ?�태�?만듭?�다.
            furniture.SaveCurrentPoseAsValid();
            furniture.SetState(FurniturePlacementState.Confirmed);
            serverPlacementPipeline?.ConfirmPlacedObject(furniture);

            Debug.Log($"[FurniturePlacementManager] Furniture move accepted and AUTO-CONFIRMED. {furniture.DisplayName}", furniture);
        }
        else
        {
            // ?�효?��? ?�으�?마�?막으�??�효?�던 ?�치�??�돌립니??
            furniture.RestoreLastValidPose();
            Debug.LogWarning($"[FurniturePlacementManager] Furniture move rejected. Restored to last valid pose. {furniture.DisplayName} / {reason}", furniture);
        }

        PlacementChanged?.Invoke();
    }

    // 버튼???��???조정 ?�용 처리. ?�동 종료?� ?�리, 검�??�패?�도 ?�즈/?��??�을
    // ?�돌리�? ?�는?? 방보????가구�? 줄여 맞추?�면 줄이???�텝???�적?�어
    // ?�진?�으�?�??�에 ?�어?�???�기 ?�문?�다.
    // 반환�? 검�?보정 ???�효???�치???�착?�는지 ?��?. ?�출???��????�텝)??
    // ?�우???�중 무효�??�텝??취소?�고, 줄이???�중 무효�?그�?�??��??�다.
    public bool HandleFurnitureScaleChanged(PlacedFurniture furniture)
    {
        return HandleFurnitureScaleChanged(furniture, true);
    }

    public bool HandleFurnitureScaleChanged(PlacedFurniture furniture, bool validateGeometry)
    {
        if (furniture == null) return false;

        if (!validateGeometry || roomGeometryProvider == null)
        {
            furniture.SaveCurrentPoseAsValid();
            furniture.SetState(FurniturePlacementState.Confirmed);
            serverPlacementPipeline?.ConfirmPlacedObject(furniture);
            PlacementChanged?.Invoke();
            return true;
        }

        // ValidateAndCorrectFurniturePose가 �?모서�??�른 가구�? ?�해 ?�치�??�동?�킨??
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

    public bool TryApplyFurnitureScaleGrowth(PlacedFurniture furniture, Vector3 targetLocalScale, out string reason)
    {
        reason = string.Empty;
        if (furniture == null) return false;

        if (roomGeometryProvider == null)
        {
            furniture.transform.localScale = targetLocalScale;
            Physics.SyncTransforms();
            return HandleFurnitureScaleChanged(furniture, false);
        }

        bool applied = roomGeometryProvider.TryApplyFurnitureScaleGrowth(furniture, targetLocalScale, out reason);
        if (applied)
        {
            furniture.SaveCurrentPoseAsValid();
            furniture.SetState(FurniturePlacementState.Confirmed);
            serverPlacementPipeline?.ConfirmPlacedObject(furniture);
            PlacementChanged?.Invoke();
        }

        return applied;
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

    public void PrepareAllForPlacementEditing()
    {
        EnsureReferences();

        foreach (PlacedFurniture furniture in placedFurnitures)
        {
            if (furniture != null)
            {
                furniture.PrepareForPlacementEditing();
            }
        }

        if (moveModeController != null)
        {
            moveModeController.SetManipulationMode(placedFurnitures.Count > 0
                ? FurnitureManipulationMode.Move
                : FurnitureManipulationMode.None);
        }

        Debug.Log($"[FurniturePlacementManager] Prepared furniture for placement editing. count:{placedFurnitures.Count}");
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

    private static bool IsDirectFurnitureManipulationInput(string inputSource)
    {
        return !string.IsNullOrEmpty(inputSource) &&
               inputSource.IndexOf("ObjectManipulator", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    // --- Placement Condition Validation ---
    // 배치 ?�료 ??모든 가구�? ?�음 조건??만족?�는지 검?�한??
    //  1) 고정???�니�? 바닥 ?�는 ?�른 가구�? 바로 ?�래???�어???�다.
    //  2) 벽걸?��? ?�니�? 지지 체인???�라 결국 바닥�??�결?�어 ?�어???�다.
    //  3) 고정?�면: �??�는 천장??붙어 ?�거?? 고정 가�?체인???�해 �?천장???�결?�어???�다.
    // ?�반 가�?목록??반환?�다. �?기하 ?�보가 ?�으�?검증을 건너?�다(�?목록).
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

        // 1) 고정???�니�?바닥 ?�는 ?�른 가구�? 바로 ?�래???�어???�다(벽걸?�라???�일 ?�용).
        if (!furniture.IsFixed)
        {
            bool onFloor = geometry.IsFurnitureOnFloor(furniture);
            bool onFurniture = geometry.HasFurnitureSupportBelow(furniture, placedFurnitures);
            if (!onFloor && !onFurniture)
            {
                reason = "비고??가�??�래??바닥/?�른 가�?지지가 ?�습?�다.";
                return false;
            }
        }

        // 2) 벽걸?��? ?�니�?지지 체인???�라 바닥�??�결?�어 ?�어???�다.
        if (!furniture.IsFloorContactless)
        {
            if (!IsGroundedToFloor(furniture, new HashSet<PlacedFurniture>()))
            {
                reason = "Floor-contact furniture is not connected to the floor.";
                return false;
            }
        }

        // 3) 고정?�면 �?천장(?�는 고정 가�?체인)??anchor ?�어???�다(공중부??방�?).
        if (furniture.IsFixed)
        {
            if (!IsFixedAnchored(furniture, new HashSet<PlacedFurniture>()))
            {
                reason = "고정 가구�? �?천장(?�는 고정 가�?체인)??붙어 ?��? ?�습?�다.";
                return false;
            }
        }

        return true;
    }

    // 가구�? 바닥??직접 ?�아 ?�거?? ?�신???�받치는 가구�? ?�라 ?�려가 결국 바닥???�는지.
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

    // 고정 가구�? �?천장???�아 ?�거?? ?�접??고정 가�?체인???�해 �?천장???�결?�는지.
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

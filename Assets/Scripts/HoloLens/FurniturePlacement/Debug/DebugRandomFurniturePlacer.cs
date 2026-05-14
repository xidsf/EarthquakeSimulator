using UnityEngine;

/// <summary>
/// Debug 전용입니다.
/// 실제 서버 인식 없이 랜덤 prefab을 랜덤 위치에 생성한 뒤 FurniturePlacementManager에 등록합니다.
/// </summary>
public class DebugRandomFurniturePlacer : MonoBehaviour
{
    [Header("References")]
    public DebugFurniturePrefabPool prefabPool;
    public DebugRoomRandomPositionSampler positionSampler;
    public FurniturePlacementManager furniturePlacementManager;

    [Tooltip("참조가 비어 있으면 Scene에서 자동 탐색합니다.")]
    public bool autoFindReferences = true;

    [Header("Placement")]
    public int maxPlacementAttempts = 30;
    public Transform debugSpawnRoot;
    [Min(0.1f)] public float cameraForwardPlacementDistance = 1.5f;
    public bool enableMoveModeAfterManualDebugPlacement = true;
    public bool restrictManipulationToPlacedDebugObject = true;

    [Header("Manual Debug Selection")]
    [SerializeField] private int selectedPrefabIndex;

    public int SelectedPrefabIndex => selectedPrefabIndex;
    public int PrefabCount => prefabPool != null ? prefabPool.Count : 0;
    public string SelectedPrefabName => prefabPool != null ? prefabPool.GetPrefabName(selectedPrefabIndex) : string.Empty;

    private void Awake()
    {
        EnsureReferences();
    }

    private void OnEnable()
    {
        EnsureReferences();
    }

    public bool PlaceOneRandomFurniture()
    {
        EnsureReferences();

        if (prefabPool == null || positionSampler == null || furniturePlacementManager == null)
        {
            Debug.LogWarning("[DebugRandomFurniturePlacer] Required references are missing.");
            return false;
        }

        for (int i = 0; i < maxPlacementAttempts; i++)
        {
            if (!prefabPool.TryGetRandomPrefab(out GameObject prefab))
            {
                return false;
            }

            if (!positionSampler.TryGetRandomRootPosition(out Vector3 position))
            {
                return false;
            }

            GameObject instance = Instantiate(prefab, position, Quaternion.identity, debugSpawnRoot != null ? debugSpawnRoot : null);
            instance.name = $"DebugFurniture_{prefab.name}_{i}";

            FurniturePlacementMetadata metadata = new FurniturePlacementMetadata
            {
                label = prefab.name,
                source = "debug",
                displayName = prefab.name
            };

            bool registered = furniturePlacementManager.RegisterRecognizedFurniture(instance, metadata);
            if (registered)
            {
                Debug.Log($"[DebugRandomFurniturePlacer] Random furniture placed: {prefab.name}", instance);
                return true;
            }

            // RegisterRecognizedFurniture가 실패 시 제거하도록 설정되어 있지 않은 경우를 대비합니다.
            if (instance != null)
            {
                Destroy(instance);
            }
        }

        Debug.LogWarning("[DebugRandomFurniturePlacer] Failed to place random furniture after max attempts.");
        return false;
    }

    public bool SelectNextPrefab()
    {
        EnsureReferences();
        return SelectPrefabRelative(1);
    }

    public bool SelectPreviousPrefab()
    {
        EnsureReferences();
        return SelectPrefabRelative(-1);
    }

    public bool SelectPrefab(int index)
    {
        EnsureReferences();

        if (prefabPool == null || prefabPool.Count == 0)
        {
            Debug.LogWarning("[DebugRandomFurniturePlacer] SelectPrefab failed. No debug prefab assigned.");
            selectedPrefabIndex = -1;
            return false;
        }

        selectedPrefabIndex = Mathf.Clamp(index, 0, prefabPool.Count - 1);
        Debug.Log($"[DebugRandomFurniturePlacer] Debug prefab selected. index:{selectedPrefabIndex}, name:{SelectedPrefabName}");
        return prefabPool.GetPrefab(selectedPrefabIndex) != null;
    }

    public bool PlaceSelectedPrefabAtCameraForward()
    {
        EnsureReferences();

        if (prefabPool == null || furniturePlacementManager == null)
        {
            Debug.LogWarning("[DebugRandomFurniturePlacer] Place selected prefab failed. Required references are missing.");
            return false;
        }

        if (selectedPrefabIndex < 0 || selectedPrefabIndex >= prefabPool.Count)
        {
            selectedPrefabIndex = 0;
        }

        GameObject prefab = prefabPool.GetPrefab(selectedPrefabIndex);
        if (prefab == null)
        {
            Debug.LogWarning($"[DebugRandomFurniturePlacer] Place selected prefab failed. index:{selectedPrefabIndex}");
            return false;
        }

        GetCameraForwardPose(out Vector3 position, out Quaternion rotation);

        GameObject instance = Instantiate(prefab, position, rotation, debugSpawnRoot != null ? debugSpawnRoot : null);
        instance.name = $"DebugFurniture_{prefab.name}_Manual";

        FurniturePlacementMetadata metadata = new FurniturePlacementMetadata
        {
            label = prefab.name,
            source = "debug_manual",
            displayName = prefab.name
        };

        bool originalValidateOnRegister = furniturePlacementManager.validateOnRegister;
        furniturePlacementManager.validateOnRegister = false;
        bool registered = furniturePlacementManager.RegisterRecognizedFurniture(instance, metadata);
        furniturePlacementManager.validateOnRegister = originalValidateOnRegister;

        if (!registered)
        {
            Destroy(instance);
            return false;
        }

        PlacedFurniture placedFurniture = instance.GetComponent<PlacedFurniture>();
        furniturePlacementManager.SetActiveNotConfirmedFurniture(placedFurniture, "DebugManualPlacement");

        if (enableMoveModeAfterManualDebugPlacement && furniturePlacementManager.moveModeController != null)
        {
            furniturePlacementManager.moveModeController.allowAllFurnitureMovableInMoveMode = !restrictManipulationToPlacedDebugObject;
            furniturePlacementManager.moveModeController.SelectFurniture(placedFurniture);
            furniturePlacementManager.moveModeController.SetMoveMode(true);
        }

        Debug.Log($"[DebugRandomFurniturePlacer] Selected debug prefab placed: {prefab.name}", instance);
        return true;
    }

    public int PlaceRandomFurniture(int count)
    {
        int success = 0;
        int safeCount = Mathf.Max(1, count);

        for (int i = 0; i < safeCount; i++)
        {
            if (PlaceOneRandomFurniture())
            {
                success++;
            }
        }

        Debug.Log($"[DebugRandomFurniturePlacer] Random placement completed. success:{success}/{safeCount}");
        return success;
    }

    private bool SelectPrefabRelative(int direction)
    {
        if (prefabPool == null || prefabPool.Count == 0)
        {
            Debug.LogWarning("[DebugRandomFurniturePlacer] SelectPrefabRelative failed. No debug prefab assigned.");
            selectedPrefabIndex = -1;
            return false;
        }

        int step = direction >= 0 ? 1 : -1;
        int index = selectedPrefabIndex;
        if (index < 0 || index >= prefabPool.Count)
        {
            index = step > 0 ? -1 : 0;
        }

        for (int i = 0; i < prefabPool.Count; i++)
        {
            index = (index + step + prefabPool.Count) % prefabPool.Count;
            if (prefabPool.GetPrefab(index) != null)
            {
                selectedPrefabIndex = index;
                Debug.Log($"[DebugRandomFurniturePlacer] Debug prefab selected. index:{selectedPrefabIndex}, name:{SelectedPrefabName}");
                return true;
            }
        }

        selectedPrefabIndex = -1;
        return false;
    }

    private void GetCameraForwardPose(out Vector3 position, out Quaternion rotation)
    {
        ConfirmedRoomGeometryProvider geometryProvider = furniturePlacementManager != null
            ? furniturePlacementManager.roomGeometryProvider
            : null;

        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            Vector3 forward = Vector3.ProjectOnPlane(mainCamera.transform.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = mainCamera.transform.forward;
            }

            position = mainCamera.transform.position + forward.normalized * cameraForwardPlacementDistance;
            position.y = geometryProvider != null && geometryProvider.IsInitialized ? geometryProvider.FloorTopY : position.y;
            rotation = Quaternion.Euler(0f, mainCamera.transform.eulerAngles.y, 0f);
            return;
        }

        if (geometryProvider != null && geometryProvider.IsInitialized)
        {
            Bounds bounds = geometryProvider.RoomBounds;
            position = new Vector3(bounds.center.x, geometryProvider.FloorTopY, bounds.center.z);
            rotation = Quaternion.identity;
            return;
        }

        position = Vector3.zero;
        rotation = Quaternion.identity;
    }

    private void EnsureReferences()
    {
        if (!autoFindReferences)
        {
            return;
        }

        if (prefabPool == null)
        {
            prefabPool = FindFirst<DebugFurniturePrefabPool>();
        }

        if (positionSampler == null)
        {
            positionSampler = FindFirst<DebugRoomRandomPositionSampler>();
        }

        if (furniturePlacementManager == null)
        {
            furniturePlacementManager = FindFirst<FurniturePlacementManager>();
        }
    }

    private static T FindFirst<T>() where T : Object
    {
        T[] objects = FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        return objects != null && objects.Length > 0 ? objects[0] : null;
    }
}

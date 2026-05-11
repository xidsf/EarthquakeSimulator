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

using System;
using System.Collections;
using UnityEngine;

public enum FurnitureServerCoordinateSpace
{
    UnityWorld,
    RoomRootLocal,
    HoloLensCameraLocal
}

public enum FurnitureServerRotationMode
{
    QuaternionXyzw,
    EulerDegrees,
    EulerRadians
}

public enum FurnitureServerTargetSizeMode
{
    Ignore,
    WorldBoundsSizeMeters,
    LocalScaleValue
}

/// <summary>
/// /result/{scan_session_id} 응답의 objects 배열을 Unity 오브젝트로 생성하고 FurniturePlacementManager에 등록합니다.
/// </summary>
public class FurnitureServerResultPlacer : MonoBehaviour
{
    [Header("References")]
    public FurnitureServerMeshLoader meshLoader;
    public FurniturePlacementManager furniturePlacementManager;
    public RuntimeFurnitureInteractionSetup runtimeInteractionSetup;

    [Tooltip("서버 결과로 생성한 오브젝트를 임시/최종으로 모을 Root입니다. 비워두면 현재 Scene root에 생성합니다.")]
    public Transform serverSpawnRoot;

    [Tooltip("참조가 비어 있으면 Scene에서 자동 탐색합니다.")]
    public bool autoFindReferences = true;

    [Header("Coordinate Mapping - Confirm With Server")]
    public FurnitureServerCoordinateSpace coordinateSpace = FurnitureServerCoordinateSpace.UnityWorld;
    public FurnitureServerRotationMode rotationMode = FurnitureServerRotationMode.QuaternionXyzw;
    public FurnitureServerTargetSizeMode targetSizeMode = FurnitureServerTargetSizeMode.WorldBoundsSizeMeters;

    [Tooltip("coordinateSpace가 RoomRootLocal일 때 사용합니다.")]
    public Transform roomRoot;

    [Tooltip("coordinateSpace가 HoloLensCameraLocal일 때 사용합니다. 보통 촬영 당시 camera_to_world 기준이 확정되기 전에는 사용하지 않는 것을 권장합니다.")]
    public Transform hololensCameraReference;

    [Header("Runtime Setup Order")]
    [Tooltip("target_size 적용 전에 MeshCollider/ObjectManipulator 보정 및 큰 scale 1/4 축소를 먼저 수행합니다.")]
    public bool configureRuntimeInteractionBeforeTargetSize = true;

    [Header("Model Alignment")]
    [Tooltip("서버 position을 가구의 바닥 중심점으로 보고 GLB child들을 root 기준 바닥에 맞춥니다.")]
    public bool normalizeModelToGround = true;

    [Tooltip("normalizeModelToGround 적용 시 렌더러 중심을 root의 X/Z 원점으로 맞춥니다.")]
    public bool centerModelXZ = true;

    [Header("Placement")]
    [Tooltip("기존 서버 결과 가구를 지우고 새 result를 배치합니다.")]
    public bool clearPreviousServerResultFurniture = true;
    public bool ignoreServerTransformForManualPlacement = true;
    public bool spawnManualObjectAtRoomCenter = true;
    public bool enableMoveModeAfterManualObjectLoad = true;
    public bool validateManualObjectOnRegister = false;

    [Tooltip("result 객체별 로딩 실패 시 다음 객체를 계속 처리합니다.")]
    public bool continueWhenSingleObjectFails = true;

    [Header("Log")]
    public bool logPlacement = true;

    public bool IsPlacing { get; private set; }
    public int LastPlacedCount { get; private set; }
    public int LastFailedCount { get; private set; }

    private void Awake()
    {
        EnsureReferences();
    }

    private void OnEnable()
    {
        EnsureReferences();
    }

    public void PlaceResult(FurnitureServerResultResponse result)
    {
        StartCoroutine(PlaceResultRoutine(result, null));
    }

    public IEnumerator PlaceSingleObjectRoutine(FurnitureServerResultObject serverObject, Action<bool, GameObject, string> onComplete)
    {
        EnsureReferences();

        if (serverObject == null)
        {
            onComplete?.Invoke(false, null, "serverObject is null");
            yield break;
        }

        if (meshLoader == null || furniturePlacementManager == null)
        {
            onComplete?.Invoke(false, null, "Required references are missing.");
            yield break;
        }

        IsPlacing = true;
        bool placed = false;
        GameObject instance = null;
        string error = string.Empty;

        yield return PlaceServerObjectRoutine(serverObject, (ok, placedObject, placeError) =>
        {
            placed = ok;
            instance = placedObject;
            error = placeError;
        });

        IsPlacing = false;
        onComplete?.Invoke(placed, instance, error);
    }

    public IEnumerator PlaceResultRoutine(FurnitureServerResultResponse result, Action<int, int> onComplete)
    {
        EnsureReferences();

        LastPlacedCount = 0;
        LastFailedCount = 0;

        if (result == null)
        {
            Debug.LogWarning("[FurnitureServerResultPlacer] Result is null.");
            onComplete?.Invoke(0, 1);
            yield break;
        }

        if (result.objects == null || result.objects.Length == 0)
        {
            Debug.LogWarning($"[FurnitureServerResultPlacer] Result has no objects. session:{result.scan_session_id}, status:{result.status}");
            onComplete?.Invoke(0, 0);
            yield break;
        }

        if (meshLoader == null || furniturePlacementManager == null)
        {
            Debug.LogWarning("[FurnitureServerResultPlacer] Required references are missing.");
            onComplete?.Invoke(0, result.objects.Length);
            yield break;
        }

        IsPlacing = true;

        if (clearPreviousServerResultFurniture)
        {
            furniturePlacementManager.ClearPlacedFurniture();
        }

        foreach (FurnitureServerResultObject serverObject in result.objects)
        {
            bool placed = false;
            string placeError = string.Empty;

            yield return PlaceServerObjectRoutine(serverObject, (ok, placedObject, error) =>
            {
                placed = ok;
                placeError = error;
            });

            if (!placed)
            {
                LastFailedCount++;
                Debug.LogWarning($"[FurnitureServerResultPlacer] Server object placement failed. id:{serverObject?.id}, label:{serverObject?.label}, error:{placeError}");
                if (!continueWhenSingleObjectFails)
                {
                    break;
                }

                continue;
            }

            LastPlacedCount++;
        }

        IsPlacing = false;
        Debug.Log($"[FurnitureServerResultPlacer] Placement finished. placed:{LastPlacedCount}, failed:{LastFailedCount}");
        onComplete?.Invoke(LastPlacedCount, LastFailedCount);
    }

    private IEnumerator PlaceServerObjectRoutine(FurnitureServerResultObject serverObject, Action<bool, GameObject, string> onComplete)
    {
        bool loaded = false;
        GameObject instance = null;
        string loadError = string.Empty;

        yield return meshLoader.LoadFurnitureObject(serverObject, (loadedObject, error) =>
        {
            instance = loadedObject;
            loadError = error;
            loaded = loadedObject != null;
        });

        if (!loaded || instance == null)
        {
            onComplete?.Invoke(false, null, $"Mesh load failed. {loadError}");
            yield break;
        }

        if (serverSpawnRoot != null)
        {
            instance.transform.SetParent(serverSpawnRoot, true);
        }

        if (configureRuntimeInteractionBeforeTargetSize)
        {
            RuntimeFurnitureInteractionSetup setup = runtimeInteractionSetup != null
                ? runtimeInteractionSetup
                : furniturePlacementManager.runtimeInteractionSetup;

            if (setup != null)
            {
                setup.ConfigureFurnitureObject(instance);
            }
        }

        ApplyInitialTransform(instance, serverObject);
        NormalizeModelIfNeeded(instance);
        ApplyTargetSize(instance, serverObject);
        NormalizeModelIfNeeded(instance);
        ApplyServerCollider(instance, serverObject);

        FurniturePlacementMetadata metadata = new FurniturePlacementMetadata
        {
            furnitureId = string.IsNullOrWhiteSpace(serverObject.id) ? Guid.NewGuid().ToString("N") : serverObject.id,
            label = serverObject.label,
            source = "server_result",
            displayName = string.IsNullOrWhiteSpace(serverObject.label) ? instance.name : serverObject.label,
            serverObjectId = serverObject.id,
            meshUrl = serverObject.mesh_url,
            confidence = serverObject.confidence,
            detectionId = serverObject.detection_id,
            frameId = serverObject.frame_id,
            placementMode = serverObject.placement_mode,
            rawLabel = serverObject.raw_label,
            thumbnailUrl = !string.IsNullOrWhiteSpace(serverObject.thumbnail_url) ? serverObject.thumbnail_url : serverObject.image_url,
            physicsMode = serverObject.physics_mode,
            simulationEnabled = serverObject.simulation_enabled,
            rigidbodyMode = serverObject.rigidbody_mode,
            useGravity = serverObject.use_gravity
        };

        bool shouldSkipValidation = IsManualPlacement(serverObject) && !validateManualObjectOnRegister;
        bool originalValidateOnRegister = furniturePlacementManager.validateOnRegister;
        if (shouldSkipValidation)
        {
            furniturePlacementManager.validateOnRegister = false;
        }

        bool registered = furniturePlacementManager.RegisterRecognizedFurniture(instance, metadata);
        furniturePlacementManager.validateOnRegister = originalValidateOnRegister;

        if (!registered)
        {
            Destroy(instance);
            onComplete?.Invoke(false, null, "FurniturePlacementManager rejected the object.");
            yield break;
        }

        ApplyPhysicsMetadata(instance, serverObject);
        SelectForManualPlacementIfNeeded(instance, serverObject);

        if (logPlacement)
        {
            Debug.Log($"[FurnitureServerResultPlacer] Server object placed. id:{serverObject.id}, label:{serverObject.label}, confidence:{serverObject.confidence}", instance);
        }

        onComplete?.Invoke(true, instance, string.Empty);
    }

    private void ApplyInitialTransform(GameObject instance, FurnitureServerResultObject serverObject)
    {
        if (ignoreServerTransformForManualPlacement && IsManualPlacement(serverObject))
        {
            ApplyManualSpawnTransform(instance);
            return;
        }

        ApplyServerTransform(instance, serverObject);
    }

    private void ApplyServerTransform(GameObject instance, FurnitureServerResultObject serverObject)
    {
        Vector3 position = ToVector3(serverObject.position, Vector3.zero);
        Quaternion rotation = ConvertRotation(serverObject.rotation);
        if ((serverObject.rotation == null || serverObject.rotation.Length < 3) &&
            Mathf.Abs(serverObject.rotation_yaw_deg) > 0.0001f)
        {
            rotation = Quaternion.Euler(0f, serverObject.rotation_yaw_deg, 0f);
        }

        switch (coordinateSpace)
        {
            case FurnitureServerCoordinateSpace.RoomRootLocal:
                if (roomRoot != null)
                {
                    instance.transform.position = roomRoot.TransformPoint(position);
                    instance.transform.rotation = roomRoot.rotation * rotation;
                }
                else
                {
                    instance.transform.SetPositionAndRotation(position, rotation);
                }
                break;

            case FurnitureServerCoordinateSpace.HoloLensCameraLocal:
                if (hololensCameraReference != null)
                {
                    instance.transform.position = hololensCameraReference.TransformPoint(position);
                    instance.transform.rotation = hololensCameraReference.rotation * rotation;
                }
                else
                {
                    instance.transform.SetPositionAndRotation(position, rotation);
                }
                break;

            case FurnitureServerCoordinateSpace.UnityWorld:
            default:
                instance.transform.SetPositionAndRotation(position, rotation);
                break;
        }
    }

    private void ApplyManualSpawnTransform(GameObject instance)
    {
        Vector3 position = Vector3.zero;
        Quaternion rotation = Quaternion.identity;

        ConfirmedRoomGeometryProvider geometryProvider = furniturePlacementManager != null
            ? furniturePlacementManager.roomGeometryProvider
            : null;

        if (spawnManualObjectAtRoomCenter && geometryProvider != null && geometryProvider.IsInitialized)
        {
            Bounds roomBounds = geometryProvider.RoomBounds;
            position = new Vector3(roomBounds.center.x, geometryProvider.FloorTopY, roomBounds.center.z);
        }
        else
        {
            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                Vector3 forward = Vector3.ProjectOnPlane(mainCamera.transform.forward, Vector3.up);
                if (forward.sqrMagnitude < 0.0001f)
                {
                    forward = mainCamera.transform.forward;
                }

                position = mainCamera.transform.position + forward.normalized * 1.5f;
                position.y = geometryProvider != null && geometryProvider.IsInitialized
                    ? geometryProvider.FloorTopY
                    : 0f;
                rotation = Quaternion.Euler(0f, mainCamera.transform.eulerAngles.y, 0f);
            }
        }

        instance.transform.SetPositionAndRotation(position, rotation);
    }

    private Quaternion ConvertRotation(float[] serverRotation)
    {
        if (serverRotation == null || serverRotation.Length == 0)
        {
            return Quaternion.identity;
        }

        switch (rotationMode)
        {
            case FurnitureServerRotationMode.EulerDegrees:
                return serverRotation.Length >= 3
                    ? Quaternion.Euler(serverRotation[0], serverRotation[1], serverRotation[2])
                    : Quaternion.identity;

            case FurnitureServerRotationMode.EulerRadians:
                return serverRotation.Length >= 3
                    ? Quaternion.Euler(new Vector3(serverRotation[0], serverRotation[1], serverRotation[2]) * Mathf.Rad2Deg)
                    : Quaternion.identity;

            case FurnitureServerRotationMode.QuaternionXyzw:
            default:
                if (serverRotation.Length >= 4)
                {
                    return new Quaternion(serverRotation[0], serverRotation[1], serverRotation[2], serverRotation[3]);
                }

                if (serverRotation.Length >= 3)
                {
                    return Quaternion.Euler(serverRotation[0], serverRotation[1], serverRotation[2]);
                }

                if (Mathf.Abs(serverRotation[0]) > 0.0001f)
                {
                    return Quaternion.Euler(0f, serverRotation[0], 0f);
                }

                return Quaternion.identity;
        }
    }

    private void ApplyTargetSize(GameObject instance, FurnitureServerResultObject serverObject)
    {
        if (targetSizeMode == FurnitureServerTargetSizeMode.Ignore)
        {
            return;
        }

        Vector3 target = GetTargetSize(serverObject);
        if (target == Vector3.zero || !HasPositiveComponent(target))
        {
            return;
        }

        if (targetSizeMode == FurnitureServerTargetSizeMode.LocalScaleValue)
        {
            instance.transform.localScale = new Vector3(
                target.x > 0f ? target.x : instance.transform.localScale.x,
                target.y > 0f ? target.y : instance.transform.localScale.y,
                target.z > 0f ? target.z : instance.transform.localScale.z);
            return;
        }

        if (!TryGetRendererBounds(instance, out Bounds bounds))
        {
            return;
        }

        Vector3 currentSize = bounds.size;
        Vector3 scale = instance.transform.localScale;

        if (target.x > 0f && currentSize.x > 0.0001f)
        {
            scale.x *= target.x / currentSize.x;
        }

        if (target.y > 0f && currentSize.y > 0.0001f)
        {
            scale.y *= target.y / currentSize.y;
        }

        if (target.z > 0f && currentSize.z > 0.0001f)
        {
            scale.z *= target.z / currentSize.z;
        }

        instance.transform.localScale = scale;
    }

    private void ApplyServerCollider(GameObject instance, FurnitureServerResultObject serverObject)
    {
        if (instance == null || serverObject == null)
        {
            return;
        }

        string colliderType = string.IsNullOrWhiteSpace(serverObject.collider_type)
            ? "box"
            : serverObject.collider_type.Trim().ToLowerInvariant();

        if (colliderType != "box")
        {
            return;
        }

        Vector3 colliderSize = ToVector3(serverObject.collider_size, Vector3.zero);
        if (!HasAllPositiveComponents(colliderSize))
        {
            return;
        }

        BoxCollider boxCollider = instance.GetComponent<BoxCollider>();
        if (boxCollider == null)
        {
            boxCollider = instance.AddComponent<BoxCollider>();
        }

        boxCollider.center = ToVector3(serverObject.collider_center, new Vector3(0f, colliderSize.y * 0.5f, 0f));
        boxCollider.size = colliderSize;
        boxCollider.enabled = true;
    }

    private void ApplyPhysicsMetadata(GameObject instance, FurnitureServerResultObject serverObject)
    {
        Rigidbody rb = instance != null ? instance.GetComponent<Rigidbody>() : null;
        if (rb == null || serverObject == null)
        {
            return;
        }

        string rigidbodyMode = string.IsNullOrWhiteSpace(serverObject.rigidbody_mode)
            ? string.Empty
            : serverObject.rigidbody_mode.Trim().ToLowerInvariant();
        string physicsMode = string.IsNullOrWhiteSpace(serverObject.physics_mode)
            ? string.Empty
            : serverObject.physics_mode.Trim().ToLowerInvariant();

        bool wantsDynamicSimulation =
            serverObject.simulation_enabled &&
            (rigidbodyMode == "dynamic" ||
             physicsMode == "dynamic_hazard" ||
             physicsMode == "dynamic");

        rb.isKinematic = true;
        rb.useGravity = false;

        if (wantsDynamicSimulation && logPlacement)
        {
            Debug.Log($"[FurnitureServerResultPlacer] Dynamic simulation metadata stored. id:{serverObject.id}, physics:{serverObject.physics_mode}, rigidbody:{serverObject.rigidbody_mode}");
        }
    }

    private void SelectForManualPlacementIfNeeded(GameObject instance, FurnitureServerResultObject serverObject)
    {
        if (!enableMoveModeAfterManualObjectLoad || !IsManualPlacement(serverObject) || furniturePlacementManager == null)
        {
            return;
        }

        PlacedFurniture placedFurniture = instance.GetComponent<PlacedFurniture>();
        if (placedFurniture == null || furniturePlacementManager.moveModeController == null)
        {
            return;
        }

        furniturePlacementManager.moveModeController.SetMoveMode(true);
        furniturePlacementManager.moveModeController.SelectFurniture(placedFurniture);
    }

    private void NormalizeModelIfNeeded(GameObject root)
    {
        if (!normalizeModelToGround || root == null)
        {
            return;
        }

        if (!TryGetLocalRendererBounds(root, out Bounds localBounds))
        {
            return;
        }

        Vector3 offset = Vector3.zero;
        if (centerModelXZ)
        {
            offset.x = -localBounds.center.x;
            offset.z = -localBounds.center.z;
        }

        offset.y = -localBounds.min.y;

        for (int i = 0; i < root.transform.childCount; i++)
        {
            Transform child = root.transform.GetChild(i);
            child.localPosition += offset;
        }
    }

    private Vector3 GetTargetSize(FurnitureServerResultObject serverObject)
    {
        Vector3 sizeWorld = ToVector3(serverObject.size_world, Vector3.zero);
        if (HasAllPositiveComponents(sizeWorld))
        {
            return sizeWorld;
        }

        Vector3 targetSize = ToVector3(serverObject.target_size, Vector3.zero);
        if (HasAllPositiveComponents(targetSize))
        {
            return targetSize;
        }

        Vector3 colliderSize = ToVector3(serverObject.collider_size, Vector3.zero);
        if (HasAllPositiveComponents(colliderSize))
        {
            return colliderSize;
        }

        return ToVector3(serverObject.scale, Vector3.zero);
    }

    private static bool IsManualPlacement(FurnitureServerResultObject serverObject)
    {
        if (serverObject == null)
        {
            return false;
        }

        if (serverObject.manual_placement_required)
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(serverObject.placement_mode) &&
               serverObject.placement_mode.Trim().Equals("manual", StringComparison.OrdinalIgnoreCase);
    }

    private static Vector3 ToVector3(float[] values, Vector3 fallback)
    {
        if (values == null || values.Length < 3)
        {
            return fallback;
        }

        return new Vector3(values[0], values[1], values[2]);
    }

    private static bool HasPositiveComponent(Vector3 value)
    {
        return value.x > 0f || value.y > 0f || value.z > 0f;
    }

    private static bool HasAllPositiveComponents(Vector3 value)
    {
        return value.x > 0f && value.y > 0f && value.z > 0f;
    }

    private static bool TryGetRendererBounds(GameObject root, out Bounds bounds)
    {
        bounds = default;
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        bool hasBounds = false;

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
            {
                continue;
            }

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return hasBounds;
    }

    private static bool TryGetLocalRendererBounds(GameObject root, out Bounds localBounds)
    {
        localBounds = default;
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        bool hasBounds = false;

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
            {
                continue;
            }

            Bounds rendererLocalBounds = renderer.localBounds;
            Vector3 min = rendererLocalBounds.min;
            Vector3 max = rendererLocalBounds.max;

            Vector3[] localCorners =
            {
                new Vector3(min.x, min.y, min.z),
                new Vector3(min.x, min.y, max.z),
                new Vector3(min.x, max.y, min.z),
                new Vector3(min.x, max.y, max.z),
                new Vector3(max.x, min.y, min.z),
                new Vector3(max.x, min.y, max.z),
                new Vector3(max.x, max.y, min.z),
                new Vector3(max.x, max.y, max.z)
            };

            foreach (Vector3 rendererLocalCorner in localCorners)
            {
                Vector3 worldCorner = renderer.transform.TransformPoint(rendererLocalCorner);
                Vector3 rootLocalCorner = root.transform.InverseTransformPoint(worldCorner);

                if (!hasBounds)
                {
                    localBounds = new Bounds(rootLocalCorner, Vector3.zero);
                    hasBounds = true;
                }
                else
                {
                    localBounds.Encapsulate(rootLocalCorner);
                }
            }
        }

        return hasBounds;
    }

    private void EnsureReferences()
    {
        if (!autoFindReferences)
        {
            return;
        }

        if (meshLoader == null)
        {
            meshLoader = FindFirst<FurnitureServerMeshLoader>();
        }

        if (furniturePlacementManager == null)
        {
            furniturePlacementManager = FindFirst<FurniturePlacementManager>();
        }

        if (runtimeInteractionSetup == null)
        {
            runtimeInteractionSetup = FindFirst<RuntimeFurnitureInteractionSetup>();
        }
    }

    private static T FindFirst<T>() where T : UnityEngine.Object
    {
        T[] objects = FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        return objects != null && objects.Length > 0 ? objects[0] : null;
    }
}

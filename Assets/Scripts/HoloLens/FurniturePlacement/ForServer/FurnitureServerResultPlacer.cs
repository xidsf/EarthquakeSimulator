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
/// 수정 내역: 완전 겹침 방지를 위한 스폰 오프셋 난수 추가 및 벽걸이 가구(isWallMounted) 식별 추가
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

    [Tooltip("LocalScaleValue: serverObject.scale을 root localScale에 직접 적용 (서버 GLB bbox 기준, 권장).\n" +
             "WorldBoundsSizeMeters: size_world 기준으로 GLB Renderer bounds를 측정해 스케일 적용.\n" +
             "Ignore: 스케일 적용 안 함.")]
    public FurnitureServerTargetSizeMode targetSizeMode = FurnitureServerTargetSizeMode.LocalScaleValue;

    [Tooltip("coordinateSpace가 RoomRootLocal일 때 사용합니다.")]
    public Transform roomRoot;

    [Tooltip("coordinateSpace가 HoloLensCameraLocal일 때 사용합니다. 보통 촬영 당시 camera_to_world 기준이 확정되기 전에는 사용하지 않는 것을 권장합니다.")]
    public Transform hololensCameraReference;

    [Header("Runtime Setup Order")]
    [Tooltip("target_size 적용 전에 MeshCollider/ObjectManipulator 보정 및 큰 scale 1/4 축소를 먼저 수행합니다.")]
    public bool configureRuntimeInteractionBeforeTargetSize = false;

    [Tooltip("HoloLens2에서는 최종 scale/정렬 이후 서버 제공 MeshCollider를 보존한 상태로 interaction setup을 적용합니다.")]
    public bool forceRuntimeInteractionAfterTargetSizeOnDevice = true;

    [Tooltip("서버가 Unity Collider(MeshCollider 포함)를 이미 포함해 보낸 경우 collider metadata 기반 BoxCollider 추가를 건너뜁니다.")]
    public bool preferServerProvidedColliders = true;

    [Tooltip("서버 collider_mesh_url이 있으면 visual GLB의 MeshCollider를 만들지 않고 별도 convex hull collider GLB를 로드합니다.")]
    public bool useServerConvexHullCollider = true;

    [Tooltip("서버 convex hull collider를 붙이기 전 visual GLB 쪽 Collider를 제거합니다.")]
    public bool removeVisualCollidersWhenServerColliderExists = true;

    [Header("Model Alignment")]
    [Tooltip("서버 position을 가구의 바닥 중심점으로 보고 GLB child들을 root 기준 바닥에 맞춥니다.")]
    public bool normalizeModelToGround = true;

    [Tooltip("normalizeModelToGround 적용 시 렌더러 중심을 root의 X/Z 원점으로 맞춥니다.")]
    public bool centerModelXZ = true;

    [Header("Scale Guard")]
    [Tooltip("Unity localScale이 비정상적으로 크면(SAM3D가 크기를 보정하므로 정상적으로는 클 일이 없음) (1,1,1)로 되돌립니다. 실제 미터 크기는 보지 않습니다.")]
    public bool resetExtremeLocalScale = true;

    [Tooltip("localScale 한 축이라도 이 값을 넘으면 비정상으로 보고 (1,1,1)로 리셋합니다.")]
    [Min(1.0f)] public float maxLocalScaleBeforeReset = 5.0f;

    [Header("Placement")]
    [Tooltip("기존 서버 결과 가구를 지우고 새 result를 배치합니다.")]
    public bool clearPreviousServerResultFurniture = true;
    public bool ignoreServerTransformForManualPlacement = true;
    public bool preferCameraForwardForManualPlacement = true;
    public bool spawnManualObjectAtRoomCenter = true;
    [Min(0.1f)] public float cameraForwardManualPlacementDistance = 1.5f;
    public bool enableMoveModeAfterManualObjectLoad = true;
    public bool restrictManualPlacementManipulationToLoadedObject = true;
    public bool validateManualObjectOnRegister = false;

    [Tooltip("result 객체별 로딩 실패 시 다음 객체를 계속 처리합니다.")]
    public bool continueWhenSingleObjectFails = true;

    [Header("Log")]
    public bool logPlacement = true;
    public bool logMeshSizeValidation = true;

    public bool IsPlacing { get; private set; }
    public int LastPlacedCount { get; private set; }
    public int LastFailedCount { get; private set; }

    // 현재 처리 중인 객체에서 ClampExtremeScale이 (1,1,1) 리셋을 적용했는지 여부.
    // 리셋되면 서버 미터 메타데이터가 실제 메쉬 크기와 무관해지므로
    // interaction BoxCollider는 실제 렌더러 bounds로 산출해야 한다.
    private bool extremeScaleResetApplied;

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
        extremeScaleResetApplied = false;

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

        RuntimeFurnitureInteractionSetup setup = runtimeInteractionSetup != null
            ? runtimeInteractionSetup
            : furniturePlacementManager.runtimeInteractionSetup;

        bool configureBeforeTargetSize = ShouldConfigureRuntimeInteractionBeforeTargetSize();

        if (configureBeforeTargetSize)
        {
            if (setup != null)
            {
                setup.ConfigureFurnitureObject(instance);
            }
        }

        ApplyInitialTransform(instance, serverObject);
        Vector3 normalizationOffset = NormalizeModelIfNeeded(instance);
        ApplyTargetSize(instance, serverObject);
        normalizationOffset += NormalizeModelIfNeeded(instance);
        yield return ApplyServerColliderRoutine(instance, serverObject, normalizationOffset);
        LogMeshSizeValidation(instance, serverObject);

        if (!configureBeforeTargetSize && setup != null)
        {
            setup.ConfigureFurnitureObject(instance);
        }

        // 💡 [추가] 액자, 거울, 시계, 칠판 등 벽에 붙어야 하는 가구를 식별하여 속성 부여
        PlacedFurniture pf = instance.GetComponent<PlacedFurniture>();
        if (pf != null && serverObject != null)
        {
            string labelLower = (serverObject.label ?? "").ToLowerInvariant();
            string rawLabelLower = (serverObject.raw_label ?? "").ToLowerInvariant();

            if (labelLower.Contains("frame") || labelLower.Contains("액자") ||
                labelLower.Contains("clock") || labelLower.Contains("시계") ||
                labelLower.Contains("mirror") || labelLower.Contains("거울") ||
                labelLower.Contains("tv") || labelLower.Contains("board") ||
                labelLower.Contains("칠판") || labelLower.Contains("whiteboard") ||
                rawLabelLower.Contains("frame") || rawLabelLower.Contains("mirror"))
            {
                pf.isWallMounted = true;
            }
        }

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

    private bool ShouldConfigureRuntimeInteractionBeforeTargetSize()
    {
#if UNITY_WSA && !UNITY_EDITOR
        if (forceRuntimeInteractionAfterTargetSizeOnDevice)
        {
            return false;
        }
#endif

        return configureRuntimeInteractionBeforeTargetSize;
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

        Camera mainCamera = Camera.main;
        if (preferCameraForwardForManualPlacement && mainCamera != null)
        {
            Vector3 forward = Vector3.ProjectOnPlane(mainCamera.transform.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = mainCamera.transform.forward;
            }

            position = mainCamera.transform.position + forward.normalized * cameraForwardManualPlacementDistance;
            position.y = geometryProvider != null && geometryProvider.IsInitialized
                ? geometryProvider.FloorTopY
                : position.y;
            rotation = Quaternion.Euler(0f, mainCamera.transform.eulerAngles.y, 0f);
        }
        else if (spawnManualObjectAtRoomCenter && geometryProvider != null && geometryProvider.IsInitialized)
        {
            Bounds roomBounds = geometryProvider.RoomBounds;
            position = new Vector3(roomBounds.center.x, geometryProvider.FloorTopY, roomBounds.center.z);
        }
        else
        {
            if (mainCamera != null)
            {
                Vector3 forward = Vector3.ProjectOnPlane(mainCamera.transform.forward, Vector3.up);
                if (forward.sqrMagnitude < 0.0001f)
                {
                    forward = mainCamera.transform.forward;
                }

                position = mainCamera.transform.position + forward.normalized * cameraForwardManualPlacementDistance;
                position.y = geometryProvider != null && geometryProvider.IsInitialized
                    ? geometryProvider.FloorTopY
                    : 0f;
                rotation = Quaternion.Euler(0f, mainCamera.transform.eulerAngles.y, 0f);
            }
        }

        // 완전 겹침(Perfect Overlap) 방지를 위한 1~2cm의 미세 랜덤 오프셋 적용
        // 이 오프셋이 있어야 ConfirmedRoomGeometryProvider의 ComputePenetration이 방향을 계산해 가구들을 밀어냅니다.
        position += new Vector3(UnityEngine.Random.Range(-0.02f, 0.02f), 0, UnityEngine.Random.Range(-0.02f, 0.02f));

        // 카메라 전방 스폰이 방 밖(또는 벽 너머)이면 방 중앙으로 보정한다.
        // 밖에서 스폰되면 lastValidPose가 방 밖이 되어 이후 모든 이동/회전이 "outside boundary"로 거부된다.
        if (geometryProvider != null && geometryProvider.IsInitialized && !geometryProvider.IsInsideRoom(position))
        {
            Bounds roomBounds = geometryProvider.RoomBounds;
            position = new Vector3(roomBounds.center.x, geometryProvider.FloorTopY, roomBounds.center.z);
            if (logPlacement)
            {
                Debug.Log("[FurnitureServerResultPlacer] Manual spawn was outside room. Relocated to room center.", instance);
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

        if (targetSizeMode == FurnitureServerTargetSizeMode.LocalScaleValue)
        {
            Vector3 serverScale = ToVector3(serverObject.scale, Vector3.zero);
            if (HasAllPositiveComponents(serverScale))
            {
                instance.transform.localScale = serverScale;
                ClampExtremeScale(instance);
                return;
            }
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
            ClampExtremeScale(instance);
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
        ClampExtremeScale(instance);
    }

    // SAM3D가 크기를 보정해 보내므로 실제 미터 크기는 검사하지 않는다.
    // Unity localScale 값만 보고, 비정상적으로 큰 경우(예: 5,5,5)에만 (1,1,1)로 되돌린다.
    private void ClampExtremeScale(GameObject instance)
    {
        if (!resetExtremeLocalScale || instance == null)
        {
            return;
        }

        Vector3 localScale = instance.transform.localScale;
        float limit = Mathf.Max(1.0f, maxLocalScaleBeforeReset);

        if (Mathf.Abs(localScale.x) > limit ||
            Mathf.Abs(localScale.y) > limit ||
            Mathf.Abs(localScale.z) > limit)
        {
            Debug.LogWarning(
                $"[FurnitureServerResultPlacer] Extreme localScale {FormatVector(localScale)} exceeds limit {limit:F2}. " +
                "Reset to (1,1,1).",
                instance);
            instance.transform.localScale = Vector3.one;
            extremeScaleResetApplied = true;
        }
    }

    // extreme-scale 리셋이 적용된 경우, 서버 world 크기 메타데이터는 실제 메쉬와
    // 무관하므로 보이는 렌더러의 local bounds로 BoxCollider를 맞춰 메쉬와 일치시킨다.
    private bool TrySetInteractionBoxFromVisibleMesh(BoxCollider boxCollider, GameObject instance)
    {
        if (!extremeScaleResetApplied || boxCollider == null || instance == null)
        {
            return false;
        }

        if (!TryGetLocalVisibleRendererBounds(instance, out Bounds localBounds))
        {
            return false;
        }

        if (localBounds.size.x <= 0.0001f || localBounds.size.y <= 0.0001f || localBounds.size.z <= 0.0001f)
        {
            return false;
        }

        boxCollider.center = localBounds.center;
        boxCollider.size = localBounds.size;
        boxCollider.enabled = true;
        return true;
    }

    private IEnumerator ApplyServerColliderRoutine(GameObject instance, FurnitureServerResultObject serverObject, Vector3 visualNormalizationOffset)
    {
        if (instance == null || serverObject == null)
        {
            yield break;
        }

        bool hasServerColliderMesh =
            useServerConvexHullCollider &&
            !string.IsNullOrWhiteSpace(serverObject.collider_mesh_url) &&
            IsServerConvexHullCollider(serverObject);
        bool forceBoxFallback = false;

        if (hasServerColliderMesh)
        {
            if (removeVisualCollidersWhenServerColliderExists)
            {
                RemoveColliders(instance);
            }

            bool colliderLoaded = false;
            GameObject colliderRoot = null;
            string colliderError = string.Empty;

            yield return meshLoader.LoadColliderObject(serverObject, (loadedCollider, error) =>
            {
                colliderRoot = loadedCollider;
                colliderError = error;
                colliderLoaded = loadedCollider != null;
            });

            if (colliderLoaded && colliderRoot != null)
            {
                if (AttachConvexHullCollider(instance, colliderRoot, serverObject, visualNormalizationOffset))
                {
                    AddBroadInteractionBoxCollider(instance, serverObject);
                    yield break;
                }

                Destroy(colliderRoot);
                forceBoxFallback = true;
            }
            else
            {
                forceBoxFallback = true;
                Debug.LogWarning($"[FurnitureServerResultPlacer] Server collider mesh load failed. id:{serverObject.id}, error:{colliderError}. Falling back to box collider.", instance);
            }
        }

        string colliderType = string.IsNullOrWhiteSpace(serverObject.collider_type)
            ? "box"
            : serverObject.collider_type.Trim().ToLowerInvariant();

        if (!forceBoxFallback && preferServerProvidedColliders && instance.GetComponentInChildren<Collider>(true) != null)
        {
            yield break;
        }

        if (!forceBoxFallback && colliderType != "box" && colliderType != "unity_runtime_box_fallback")
        {
            yield break;
        }

        BoxCollider boxCollider = instance.GetComponent<BoxCollider>();
        if (boxCollider == null)
        {
            boxCollider = instance.AddComponent<BoxCollider>();
        }

        if (TrySetInteractionBoxFromVisibleMesh(boxCollider, instance))
        {
            yield break;
        }

        Vector3 colliderSizeWorld = GetFallbackColliderWorldSize(serverObject);
        if (!HasAllPositiveComponents(colliderSizeWorld))
        {
            yield break;
        }

        Vector3 colliderCenterWorld = ToVector3(
            serverObject.collider_center,
            new Vector3(0f, colliderSizeWorld.y * 0.5f, 0f));

        boxCollider.center = WorldSizeToLocalSize(colliderCenterWorld, instance.transform);
        boxCollider.size = WorldSizeToLocalSize(colliderSizeWorld, instance.transform);
        boxCollider.enabled = true;
    }

    private void AddBroadInteractionBoxCollider(GameObject instance, FurnitureServerResultObject serverObject)
    {
        BoxCollider boxCollider = instance.GetComponent<BoxCollider>();
        if (boxCollider == null)
        {
            boxCollider = instance.AddComponent<BoxCollider>();
        }

        if (TrySetInteractionBoxFromVisibleMesh(boxCollider, instance))
        {
            return;
        }

        Vector3 colliderSizeWorld = GetFallbackColliderWorldSize(serverObject);
        if (!HasAllPositiveComponents(colliderSizeWorld))
        {
            return;
        }

        Vector3 colliderCenterWorld = ToVector3(
            serverObject.collider_center,
            new Vector3(0f, colliderSizeWorld.y * 0.5f, 0f));

        boxCollider.center = WorldSizeToLocalSize(colliderCenterWorld, instance.transform);
        boxCollider.size = WorldSizeToLocalSize(colliderSizeWorld, instance.transform);
        boxCollider.enabled = true;
    }

    private void LogMeshSizeValidation(GameObject instance, FurnitureServerResultObject serverObject)
    {
        if (!logPlacement || !logMeshSizeValidation || instance == null || serverObject == null)
        {
            return;
        }

        string rendererBoundsText = "none";
        if (TryGetVisibleRendererBounds(instance, out Bounds rendererBounds))
        {
            rendererBoundsText =
                $"center:{FormatVector(rendererBounds.center)}, size:{FormatVector(rendererBounds.size)}";
        }

        Debug.Log(
            "[FurnitureServerResultPlacer] Mesh/size validation. " +
            $"id:{serverObject.id}, label:{serverObject.label}, " +
            $"mesh:{serverObject.mesh_filename}, mesh_cache_policy:{serverObject.mesh_cache_policy}, " +
            $"sam3d_status:{serverObject.sam3d_status}, sam3d_quality:{serverObject.sam3d_quality_score:F3}, " +
            $"quality_flags:{FormatStringArray(serverObject.quality_flags)}, " +
            $"server_scale:{FormatVector(serverObject.scale)}, " +
            $"glb_native_size:{FormatVector(serverObject.glb_native_size)}, " +
            $"size_world:{FormatVector(serverObject.size_world)}, " +
            $"target_size:{FormatVector(serverObject.target_size)}, " +
            $"size_adjustment:{FormatSizeAdjustment(serverObject.size_adjustment)}, " +
            $"unity_local_scale:{FormatVector(instance.transform.localScale)}, " +
            $"renderer_bounds:{rendererBoundsText}, " +
            $"collider_source:{serverObject.collider_source}, " +
            $"collider_type:{serverObject.collider_type}, " +
            $"collider_size:{FormatVector(serverObject.collider_size)}, " +
            $"collider_count:{CountColliders(instance)}",
            instance);
    }

    private static int CountColliders(GameObject root)
    {
        if (root == null)
        {
            return 0;
        }

        Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
        return colliders != null ? colliders.Length : 0;
    }

    private static string FormatStringArray(string[] values)
    {
        return values == null || values.Length == 0 ? "[]" : "[" + string.Join(",", values) + "]";
    }

    private static string FormatVector(float[] values)
    {
        if (values == null || values.Length == 0)
        {
            return "[]";
        }

        string[] parts = new string[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            parts[i] = values[i].ToString("F3");
        }

        return "[" + string.Join(",", parts) + "]";
    }

    private static string FormatVector(Vector3 value)
    {
        return $"[{value.x:F3},{value.y:F3},{value.z:F3}]";
    }

    private static string FormatSizeAdjustment(FurnitureServerSizeAdjustment adjustment)
    {
        if (adjustment == null || string.IsNullOrWhiteSpace(adjustment.reason))
        {
            return "none";
        }

        return
            $"{adjustment.reason}, " +
            $"raw:{FormatVector(adjustment.raw_size_world)}, " +
            $"mesh_axis:{adjustment.mesh_long_axis}, " +
            $"min:[{adjustment.min_width_m:F3},{adjustment.min_height_m:F3},{adjustment.min_length_m:F3}]";
    }

    private Vector3 GetFallbackColliderWorldSize(FurnitureServerResultObject serverObject)
    {
        Vector3 colliderSize = ToVector3(serverObject.collider_size, Vector3.zero);
        if (HasAllPositiveComponents(colliderSize))
        {
            return colliderSize;
        }

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

        return Vector3.zero;
    }

    private static Vector3 WorldSizeToLocalSize(Vector3 worldValue, Transform target)
    {
        if (target == null)
        {
            return worldValue;
        }

        Vector3 scale = target.lossyScale;
        return new Vector3(
            Mathf.Abs(scale.x) > 0.0001f ? worldValue.x / Mathf.Abs(scale.x) : worldValue.x,
            Mathf.Abs(scale.y) > 0.0001f ? worldValue.y / Mathf.Abs(scale.y) : worldValue.y,
            Mathf.Abs(scale.z) > 0.0001f ? worldValue.z / Mathf.Abs(scale.z) : worldValue.z);
    }

    private static bool IsServerConvexHullCollider(FurnitureServerResultObject serverObject)
    {
        string source = string.IsNullOrWhiteSpace(serverObject.collider_source)
            ? string.Empty
            : serverObject.collider_source.Trim().ToLowerInvariant();
        string type = string.IsNullOrWhiteSpace(serverObject.collider_type)
            ? string.Empty
            : serverObject.collider_type.Trim().ToLowerInvariant();

        return source == "server_convex_hull" ||
               type == "mesh_convex_hull" ||
               serverObject.collider_convex;
    }

    private static bool AttachConvexHullCollider(GameObject visualRoot, GameObject colliderRoot, FurnitureServerResultObject serverObject, Vector3 visualNormalizationOffset)
    {
        colliderRoot.name = $"{visualRoot.name}_ServerConvexCollider";
        colliderRoot.transform.SetParent(visualRoot.transform, false);
        // visual GLB 자식은 NormalizeModelIfNeeded에서 바닥 정렬 offset만큼 이동된다.
        // collider GLB는 그 이후에 붙으므로 동일 offset을 적용해야 visual world 좌표와 일치한다.
        colliderRoot.transform.localPosition = visualNormalizationOffset;
        colliderRoot.transform.localRotation = Quaternion.identity;
        colliderRoot.transform.localScale = Vector3.one;

        DisableRenderers(colliderRoot);

        int added = 0;
        MeshFilter[] meshFilters = colliderRoot.GetComponentsInChildren<MeshFilter>(true);
        foreach (MeshFilter meshFilter in meshFilters)
        {
            if (meshFilter == null || meshFilter.sharedMesh == null)
            {
                continue;
            }

            MeshCollider meshCollider = meshFilter.GetComponent<MeshCollider>();
            if (meshCollider == null)
            {
                meshCollider = meshFilter.gameObject.AddComponent<MeshCollider>();
            }

            meshCollider.sharedMesh = null;
            meshCollider.sharedMesh = meshFilter.sharedMesh;
            meshCollider.convex = true;
            meshCollider.enabled = true;
            added++;
        }

        if (added <= 0)
        {
            Debug.LogWarning($"[FurnitureServerResultPlacer] Collider GLB loaded but no MeshFilter was found. id:{serverObject.id}", visualRoot);
            return false;
        }

        return true;
    }

    private static void RemoveColliders(GameObject root)
    {
        Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
        foreach (Collider collider in colliders)
        {
            if (collider != null)
            {
                Destroy(collider);
            }
        }
    }

    private static void DisableRenderers(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in renderers)
        {
            if (renderer != null)
            {
                renderer.enabled = false;
            }
        }
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

        // 새 가구를 배치해도 이전에 배치한 가구를 계속 조작할 수 있도록 모든 가구를 이동 가능 상태로 유지한다.
        // (선택은 UI 포커스 용도로만 사용하고, 다른 가구의 manipulator를 비활성화하지 않는다.)
        furniturePlacementManager.moveModeController.allowAllFurnitureMovableInMoveMode = true;

        furniturePlacementManager.moveModeController.SetMoveMode(true);
        furniturePlacementManager.moveModeController.SelectFurniture(placedFurniture);
    }

    private Vector3 NormalizeModelIfNeeded(GameObject root)
    {
        if (!normalizeModelToGround || root == null)
        {
            return Vector3.zero;
        }

        if (!TryGetLocalRendererBounds(root, out Bounds localBounds))
        {
            return Vector3.zero;
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

        return offset;
    }

    private Vector3 GetTargetSize(FurnitureServerResultObject serverObject)
    {
        // size_world: 서버가 HoloLens depth 기반으로 추정한 실제 세계 크기 (미터 단위).
        Vector3 sizeWorld = ToVector3(serverObject.size_world, Vector3.zero);
        if (HasAllPositiveComponents(sizeWorld))
        {
            return sizeWorld;
        }

        // target_size: taxonomy 기반 기본 크기 (size_world가 없을 때 fallback).
        Vector3 targetSize = ToVector3(serverObject.target_size, Vector3.zero);
        if (HasAllPositiveComponents(targetSize))
        {
            return targetSize;
        }

        // collider_size: collider 기반 크기 참고값.
        Vector3 colliderSize = ToVector3(serverObject.collider_size, Vector3.zero);
        if (HasAllPositiveComponents(colliderSize))
        {
            return colliderSize;
        }

        // scale 필드는 Unity localScale 값이지 미터 단위 세계 크기가 아니므로
        // WorldBoundsSizeMeters 모드에서 사용하면 안 됨 → Vector3.zero 반환해서 skip.
        return Vector3.zero;
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

    private static bool TryGetVisibleRendererBounds(GameObject root, out Bounds bounds)
    {
        bounds = default;
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        bool hasBounds = false;

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null || !renderer.enabled)
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

    // 보이는(enabled) 렌더러만으로 root local 공간 bounds를 구한다.
    // collider GLB 렌더러는 DisableRenderers로 꺼져 있어 자연히 제외된다.
    private static bool TryGetLocalVisibleRendererBounds(GameObject root, out Bounds localBounds)
    {
        localBounds = default;
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        bool hasBounds = false;

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null || !renderer.enabled)
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
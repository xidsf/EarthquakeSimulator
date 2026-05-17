using System;
using System.Collections.Generic;
using MixedReality.Toolkit;
using MixedReality.Toolkit.SpatialManipulation;
using UnityEngine;

public enum FurnitureScaleStepResult
{
    Applied,
    BlockedByRoomHeight,
    BlockedByOverlap,
    BlockedByFixed,
    Ignored
}

/// <summary>
/// 서버/인식/디버그 파이프라인이 생성한 개별 가구 오브젝트에 붙는 런타임 관리 컴포넌트입니다.
/// 수정 내역: 벽걸이(Wall Mounted) 가구 식별 및 실시간 벽면 스냅 로직 추가
/// </summary>
public class PlacedFurniture : MonoBehaviour
{
    [Header("Runtime Metadata")]
    [SerializeField] private FurniturePlacementState state = FurniturePlacementState.None;
    [SerializeField] private string furnitureId;
    [SerializeField] private string label;
    [SerializeField] private string displayName;
    [SerializeField] private string source;
    [SerializeField] private string physicsMode;
    [SerializeField] private bool simulationEnabled = true;
    [SerializeField] private string rigidbodyMode;
    [SerializeField] private bool simulationUseGravity;

    [Header("Wall Mount")]
    [Tooltip("액자, 거울, 칠판 등 벽에 붙어야 하는 가구인지 여부입니다.")]
    public bool isWallMounted = false;
    [Tooltip("벽면 스냅을 시도할 최대 거리(미터)입니다.")]
    public float wallSnapDistance = 0.08f;
    [Min(0.0f)] public float wallSnapOffsetMeters = 0.0f;

    [Header("Fixed Placement")]
    [Tooltip("true이면 선택은 가능하지만 이동/회전/크기 조정을 잠급니다.")]
    public bool isFixed = false;
    public bool disableManipulationWhenFixed = true;

    [Header("Move Control")]
    public Behaviour[] moveBehaviours;
    public Collider[] heavyCollidersToDisableWhileMoving;
    public bool ensureKinematicRigidbody = true;

    [Tooltip("이동 중 비-벽걸이 가구의 바닥을 매 프레임 방 바닥에 고정해, 벽 쪽으로 끌 때 아래로 가라앉는 현상을 방지합니다.")]
    public bool lockToFloorWhileMoving = true;
    [Tooltip("이동 중 바닥뿐 아니라 책상/선반 같은 다른 가구 상단도 지지면으로 사용합니다.")]
    public bool lockToSupportSurfaceWhileMoving = true;
    [Min(0.0f)] public float supportSurfaceSnapDistance = 0.08f;

    [Header("Placement Assist")]
    [Min(0.0f)] public float nearbySurfaceSnapDistance = 0.04f;

    public bool autoCollectPlacementColliders = true;
    public Collider[] placementColliders;

    [Header("Touch Selection")]
    public bool selectWhenTouchedOrMoved = true;

    [Header("Transform Move Monitor")]
    public bool autoDetectTransformMoveEnd = true;
    public float moveEndIdleSeconds = 0.2f;
    public float movePositionEpsilon = 0.0005f;
    public float moveRotationEpsilonDegrees = 0.15f;
    public float moveScaleEpsilon = 0.0005f;

    [Header("Scale Limit")]
    [Tooltip("두 손 스케일 조작 시 가구 localScale 한 축의 최대값입니다. 한계 적용 시에도 비율(GLB 원본)은 유지됩니다.")]
    [Min(1f)] public float maxScalePerAxis = 10f;
    [Tooltip("가구 localScale 한 축의 최소값입니다. 너무 작아지는 것을 방지합니다.")]
    [Min(0.001f)] public float minScalePerAxis = 0.1f;

    private readonly List<Collider> runtimePlacementColliders = new List<Collider>();
    // 바닥 정렬용 시각 렌더러를 1회 캐시한다. 매 프레임 GetComponentsInChildren로
    // 계층 순회/배열 할당이 일어나지 않도록 placement collider와 동일하게 캐시.
    private Renderer[] cachedVisualRenderers = Array.Empty<Renderer>();
    private FurniturePlacementManager ownerManager;
    private Vector3 lastValidPosition;
    private Quaternion lastValidRotation;
    private Vector3 lastObservedPosition;
    private Quaternion lastObservedRotation;
    private Vector3 lastObservedScale = Vector3.one;
    private float lastTransformChangeTime;
    private bool initialized;
    private bool isMovable;
    private bool isMoving;
    private GameObject selectionBoxObject;
    private FurnitureManipulationMode manipulationMode = FurnitureManipulationMode.None;
    private bool placementValidationFailed;

    public event Action<PlacedFurniture> MoveStarted;
    public event Action<PlacedFurniture> MoveEnded;
    public event Action<PlacedFurniture> Touched;

    public FurniturePlacementState State => state;
    public string FurnitureId => furnitureId;
    public string Label => label;
    public string DisplayName => !string.IsNullOrWhiteSpace(displayName) ? displayName : gameObject.name;
    public string Source => source;
    public bool IsMovable => isMovable;
    public bool IsMoving => isMoving;
    public FurnitureManipulationMode ManipulationMode => manipulationMode;
    public bool IsFixed => isFixed;
    public bool IsWallMounted => isWallMounted;

    // 배치 완료 검증에서 조건 미충족으로 판정되어 boxcollider에 미충족 material을
    // 표시 중인지 여부. 가구를 선택/이동하는 등 다른 작업을 하면 해제되며,
    // 배치 완료 버튼을 다시 눌러 재검증할 때만 다시 설정된다.
    public bool PlacementValidationFailed => placementValidationFailed;
    public void SetPlacementValidationFailed(bool failed) => placementValidationFailed = failed;

    public void Initialize(FurniturePlacementManager manager, FurniturePlacementMetadata metadata)
    {
        ownerManager = manager;

        if (metadata == null) metadata = FurniturePlacementMetadata.CreateDefault(gameObject);
        metadata.EnsureDefaults(gameObject);

        furnitureId = metadata.furnitureId;
        label = metadata.label;
        displayName = metadata.displayName;
        source = metadata.source;
        physicsMode = metadata.physicsMode;
        simulationEnabled = metadata.simulationEnabled;
        rigidbodyMode = metadata.rigidbodyMode;
        simulationUseGravity = metadata.useGravity;

        CollectPlacementColliders();
        CollectVisualRenderers();
        ConfigureRigidbody();
        WarnIfMeshCollidersAreNotConvex();

        SaveCurrentPoseAsValid();
        ResetTransformMonitor();
        SetState(FurniturePlacementState.Registered);
        SetMovable(false);
        initialized = true;
    }

    private void Update()
    {
        if (!initialized || !autoDetectTransformMoveEnd || !isMovable) return;


        // 이동 중이고 벽걸이 가구라면 실시간으로 벽에 스냅시킵니다.
        // 두 손 스케일 조작 중에는 비율(GLB 원본)을 유지한 채 최소/최대 크기로 제한한다.
        if (isMoving && manipulationMode == FurnitureManipulationMode.Scale)
        {
            ClampScaleToLimits();
        }

        // 이동 중 비-벽걸이 가구는 바닥에 고정해 아래로 가라앉거나 떠오르는 것을 방지한다.
        if (isMoving && lockToFloorWhileMoving && !isWallMounted && ownerManager?.roomGeometryProvider != null)
        {
            ConfirmedRoomGeometryProvider geometry = ownerManager.roomGeometryProvider;
            if (geometry.IsInitialized &&
                (geometry.TryGetFurnitureVisualBounds(this, out Bounds floorBounds) ||
                 geometry.TryGetFurnitureBounds(this, out floorBounds)))
            {
                float supportTopY = geometry.FloorTopY;
                if (lockToSupportSurfaceWhileMoving)
                {
                    geometry.TryGetBestSupportTopY(this, supportSurfaceSnapDistance, out supportTopY);
                }

                float deltaY = supportTopY - floorBounds.min.y;
                if (Mathf.Abs(deltaY) > 0.0005f)
                {
                    transform.position += Vector3.up * deltaY;
                }
            }
        }

        bool changed = HasTransformChangedSinceLastObservation();
        if (changed)
        {
            if (!isMoving)
            {
                lastValidPosition = lastObservedPosition;
                lastValidRotation = lastObservedRotation;
                BeginMove("TransformChanged", saveCurrentAsValid: false);
            }

            lastTransformChangeTime = Time.time;
            lastObservedPosition = transform.position;
            lastObservedRotation = transform.rotation;
            return;
        }

        if (isMoving && Time.time - lastTransformChangeTime >= moveEndIdleSeconds)
        {
            EndMove("TransformIdle");
        }
    }

    public void SetState(FurniturePlacementState nextState) => state = nextState;
    public void SetMovable(bool movable) => SetManipulationMode(movable ? FurnitureManipulationMode.Move : FurnitureManipulationMode.None);

    public void SetManipulationMode(FurnitureManipulationMode mode)
    {
        if (disableManipulationWhenFixed && isFixed)
        {
            mode = FurnitureManipulationMode.None;
        }

        if (manipulationMode != FurnitureManipulationMode.None && mode == FurnitureManipulationMode.None && isMoving) EndMove("MoveDisabled");

        manipulationMode = mode;
        isMovable = mode != FurnitureManipulationMode.None;
        TransformFlags allowedManipulations = ToTransformFlags(mode);

        if (moveBehaviours != null)
        {
            foreach (Behaviour behaviour in moveBehaviours)
            {
                if (behaviour != null)
                {
                    if (behaviour is ObjectManipulator manipulator) manipulator.AllowedManipulations = allowedManipulations;
                    behaviour.enabled = isMovable;
                }
            }
        }

        ResetTransformMonitor();
    }

    private static TransformFlags ToTransformFlags(FurnitureManipulationMode mode)
    {
        return mode switch
        {
            FurnitureManipulationMode.Move => TransformFlags.Move,
            FurnitureManipulationMode.Rotate => TransformFlags.Rotate,
            FurnitureManipulationMode.Scale => TransformFlags.Scale,
            _ => TransformFlags.None,
        };
    }

    public void NotifyTouched() => NotifyTouched("Touch");
    public void NotifyTouched(string inputSource)
    {
        if (!initialized) return;
        Touched?.Invoke(this);
        ownerManager?.HandleFurnitureTouched(this, inputSource);
    }

    public void NotifySelectedByController(string inputSource)
    {
        if (!initialized) return;
        ownerManager?.HandleFurnitureSelected(this, inputSource);
    }

    public void NotifyMoveStarted() { if (initialized) BeginMove("ExternalEvent", saveCurrentAsValid: true); }
    public void NotifyMoveEnded() { if (initialized) EndMove("ExternalEvent"); }

    public void SetFixed(bool fixedState)
    {
        if (isFixed == fixedState) return;

        isFixed = fixedState;
        if (isFixed)
        {
            SetManipulationMode(FurnitureManipulationMode.None);
            Debug.Log($"[PlacedFurniture] Furniture fixed. Manipulation locked: {DisplayName}", this);
        }
        else
        {
            Debug.Log($"[PlacedFurniture] Furniture unfixed. Manipulation available: {DisplayName}", this);
        }
    }

    public void ToggleFixed()
    {
        SetFixed(!isFixed);
    }

    public void SetWallMounted(bool wallMounted)
    {
        if (isWallMounted == wallMounted) return;
        isWallMounted = wallMounted;
        Debug.Log($"[PlacedFurniture] Wall mount {(isWallMounted ? "enabled" : "disabled")}: {DisplayName}", this);
    }

    public void ToggleWallMounted()
    {
        SetWallMounted(!isWallMounted);
    }

    public void SaveCurrentPoseAsValid()
    {
        lastValidPosition = transform.position;
        lastValidRotation = transform.rotation;
        ResetTransformMonitor();
    }

    // 스케일은 의도적으로 복원하지 않는다. 방보다 큰 가구를 줄여 맞추는 도중 검증 실패로
    // 스케일이 원래(큰) 값으로 되돌아가면 영영 줄일 수 없는 교착이 발생하기 때문이다.
    public void RestoreLastValidPose()
    {
        transform.position = lastValidPosition;
        transform.rotation = lastValidRotation;
        ResetTransformMonitor();
    }

    // 버튼식 균일 스케일: 비율을 유지한 채 factor를 곱하고 한계로 클램프 후 즉시 재검증/보정한다.
    public FurnitureScaleStepResult ApplyUniformScaleStep(float factor)
    {
        if (isFixed)
        {
            return FurnitureScaleStepResult.BlockedByFixed;
        }

        if (!initialized || factor <= 0f)
        {
            return FurnitureScaleStepResult.Ignored;
        }

        Vector3 previousScale = transform.localScale;
        Vector3 previousPosition = transform.position;
        Quaternion previousRotation = transform.rotation;

        transform.localScale *= factor;
        ClampScaleToLimits();
        Physics.SyncTransforms();

        bool growing = factor > 1f;

        // 키우는 스텝에서 가구가 천장~바닥 높이를 넘으면 되돌리고 한계임을 알린다.
        if (growing && ExceedsRoomHeight())
        {
            transform.localScale = previousScale;
            Physics.SyncTransforms();
            return FurnitureScaleStepResult.BlockedByRoomHeight;
        }

        // 벽/모서리/다른 가구와 겹치면 HandleFurnitureScaleChanged 내부의
        // ValidateAndCorrectFurniturePose가 위치를 이동시켜 수용을 시도한다.
        bool valid = ownerManager == null || ownerManager.HandleFurnitureScaleChanged(this);

        // 키우는데 이동해도 공간이 안 나오면 이번 스텝만 취소한다.
        // (축소는 항상 허용해 큰 가구가 줄지 못하는 교착을 막는다.)
        if (growing && !valid)
        {
            transform.localScale = previousScale;
            transform.SetPositionAndRotation(previousPosition, previousRotation);
            Physics.SyncTransforms();
            return FurnitureScaleStepResult.BlockedByOverlap;
        }

        return FurnitureScaleStepResult.Applied;
    }

    // 한 축만 미세 조정한다. 버튼식 축별 조정용이므로 의도적으로 비율을 깨고 해당 축만 [min,max]로 클램프한다.
    public FurnitureScaleStepResult ApplyAxisScaleStep(int axis, float factor)
    {
        if (isFixed)
        {
            return FurnitureScaleStepResult.BlockedByFixed;
        }

        if (!initialized || factor <= 0f || axis < 0 || axis > 2)
        {
            return FurnitureScaleStepResult.Ignored;
        }

        Vector3 previousScale = transform.localScale;
        Vector3 previousPosition = transform.position;
        Quaternion previousRotation = transform.rotation;

        Vector3 scale = previousScale;
        scale[axis] = Mathf.Clamp(scale[axis] * factor, minScalePerAxis, maxScalePerAxis);
        transform.localScale = scale;
        Physics.SyncTransforms();

        bool growing = factor > 1f;

        if (growing && ExceedsRoomHeight())
        {
            transform.localScale = previousScale;
            Physics.SyncTransforms();
            return FurnitureScaleStepResult.BlockedByRoomHeight;
        }

        bool valid = ownerManager == null || ownerManager.HandleFurnitureScaleChanged(this);

        if (growing && !valid)
        {
            transform.localScale = previousScale;
            transform.SetPositionAndRotation(previousPosition, previousRotation);
            Physics.SyncTransforms();
            return FurnitureScaleStepResult.BlockedByOverlap;
        }

        return FurnitureScaleStepResult.Applied;
    }

    private bool ExceedsRoomHeight()
    {
        ConfirmedRoomGeometryProvider geometry = ownerManager != null ? ownerManager.roomGeometryProvider : null;
        return geometry != null && geometry.IsInitialized && geometry.ExceedsRoomHeight(this);
    }

    public void PrepareForSimulation()
    {
        SetMovable(false);
        SetHeavyCollidersEnabled(true);
        ApplySimulationRigidbodyMode();
    }

    public Collider[] GetActivePlacementColliders()
    {
        if (runtimePlacementColliders.Count == 0) CollectPlacementColliders();
        return runtimePlacementColliders.ToArray();
    }

    // 선택된 가구의 BoxCollider 영역을 반투명 박스로 표시해 크기/위치 조절 UX를 돕는다.
    public void SetSelectionBoxVisible(bool show, Material boxMaterial)
    {
        BoxCollider box = GetComponent<BoxCollider>();
        if (!show || box == null || boxMaterial == null)
        {
            if (selectionBoxObject != null) selectionBoxObject.SetActive(false);
            return;
        }

        if (selectionBoxObject == null)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "SelectionBoxVisual";

            Collider cubeCollider = cube.GetComponent<Collider>();
            if (cubeCollider != null) Destroy(cubeCollider);

            cube.transform.SetParent(transform, false);
            selectionBoxObject = cube;
        }

        // BoxCollider center/size는 root local 값이고 기본 Cube 메쉬는 1유닛이므로 그대로 매핑하면 일치한다.
        selectionBoxObject.transform.localPosition = box.center;
        selectionBoxObject.transform.localRotation = Quaternion.identity;
        selectionBoxObject.transform.localScale = box.size;

        Renderer boxRenderer = selectionBoxObject.GetComponent<Renderer>();
        if (boxRenderer != null) boxRenderer.sharedMaterial = boxMaterial;

        selectionBoxObject.SetActive(true);
    }

    private void BeginMove(string reason, bool saveCurrentAsValid)
    {
        if (isMoving) return;
        if (disableManipulationWhenFixed && isFixed) return;

        // 이동은 "다른 작업"이므로 미충족 표시를 해제한다(배치 완료 재검증 전까지 복귀하지 않음).
        placementValidationFailed = false;

        if (selectWhenTouchedOrMoved) ownerManager?.HandleFurnitureTouched(this, reason);

        isMoving = true;
        lastTransformChangeTime = Time.time;
        if (saveCurrentAsValid) SaveCurrentPoseAsValid();

        SetHeavyCollidersEnabled(false);
        MoveStarted?.Invoke(this);
    }

    private void EndMove(string reason)
    {
        if (!isMoving) return;

        isMoving = false;
        SetHeavyCollidersEnabled(true);
        MoveEnded?.Invoke(this);
        ownerManager?.HandleFurnitureMoveEnded(this);
        ResetTransformMonitor();
    }

    private bool HasTransformChangedSinceLastObservation()
    {
        return Vector3.Distance(transform.position, lastObservedPosition) > movePositionEpsilon ||
               Quaternion.Angle(transform.rotation, lastObservedRotation) > moveRotationEpsilonDegrees ||
               Vector3.Distance(transform.localScale, lastObservedScale) > moveScaleEpsilon;
    }

    private void ResetTransformMonitor()
    {
        lastObservedPosition = transform.position;
        lastObservedRotation = transform.rotation;
        lastObservedScale = transform.localScale;
        lastTransformChangeTime = Time.time;
    }

    // 비율을 깨지 않도록 한 축 기준으로 다른 축을 정하지 않고, 전체 벡터에 동일 factor를 곱해 제한한다.
    private void ClampScaleToLimits()
    {
        Vector3 scale = transform.localScale;
        float maxComponent = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        float minComponent = Mathf.Min(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));

        float factor = 1f;
        if (maxComponent > maxScalePerAxis && maxComponent > 0.0001f)
        {
            factor = maxScalePerAxis / maxComponent;
        }
        else if (minComponent < minScalePerAxis && minComponent > 0.0001f)
        {
            factor = minScalePerAxis / minComponent;
        }

        if (!Mathf.Approximately(factor, 1f))
        {
            transform.localScale = scale * factor;
        }
    }

    // 시각 렌더러를 1회 수집한다. SelectionBoxVisual은 Initialize 이후 생성되므로
    // 이 캐시에 포함되지 않는다(자연 제외). collider GLB 렌더러는 비활성 상태라
    // 사용 시 enabled 필터로 제외된다.
    private void CollectVisualRenderers()
    {
        cachedVisualRenderers = GetComponentsInChildren<Renderer>(true);
    }

    public Renderer[] GetCachedVisualRenderers() => cachedVisualRenderers;

    private void CollectPlacementColliders()
    {
        runtimePlacementColliders.Clear();
        if (placementColliders != null && placementColliders.Length > 0)
        {
            foreach (Collider c in placementColliders) if (c != null && !runtimePlacementColliders.Contains(c)) runtimePlacementColliders.Add(c);
            return;
        }

        if (!autoCollectPlacementColliders) return;

        Collider[] childColliders = GetComponentsInChildren<Collider>(true);

        // 서버 convex hull MeshCollider가 있으면 검증/충돌은 그 정밀 collider만 사용한다.
        // 넓은 interaction용 BoxCollider(AddBroadInteractionBoxCollider)를 검증에서 제외해
        // 방 내/외부·침투 판정이 과도하게 보수적으로 되어 정상 배치가 거부되는 것을 막는다.
        foreach (Collider c in childColliders)
        {
            if (c is MeshCollider meshCollider && meshCollider.convex && !runtimePlacementColliders.Contains(c))
            {
                runtimePlacementColliders.Add(c);
            }
        }

        if (runtimePlacementColliders.Count > 0) return;

        // 서버 MeshCollider를 받지 못한 경우에만 BoxCollider 등으로 fallback한다.
        foreach (Collider c in childColliders)
        {
            if (c != null && !runtimePlacementColliders.Contains(c)) runtimePlacementColliders.Add(c);
        }
    }

    private void ConfigureRigidbody()
    {
        if (!ensureKinematicRigidbody) return;
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();

        rb.isKinematic = true;
        rb.useGravity = false;
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
    }

    private void ApplySimulationRigidbodyMode()
    {
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb == null) return;

        string normalizedRigidbodyMode = string.IsNullOrWhiteSpace(rigidbodyMode) ? string.Empty : rigidbodyMode.Trim().ToLowerInvariant();
        string normalizedPhysicsMode = string.IsNullOrWhiteSpace(physicsMode) ? string.Empty : physicsMode.Trim().ToLowerInvariant();

        bool shouldBeDynamic = simulationEnabled && (normalizedRigidbodyMode == "dynamic" || normalizedPhysicsMode == "dynamic_hazard" || normalizedPhysicsMode == "dynamic");

        rb.isKinematic = !shouldBeDynamic;
        rb.useGravity = shouldBeDynamic && simulationUseGravity;
    }

    private void WarnIfMeshCollidersAreNotConvex()
    {
        foreach (Collider collider in GetActivePlacementColliders())
        {
            if (collider is MeshCollider meshCollider && !meshCollider.convex)
                Debug.LogWarning($"[PlacedFurniture] MeshCollider is not convex: {meshCollider.name}. HoloLens2 이동/검증에서는 Convex MeshCollider 또는 단순 Proxy Collider를 권장합니다.", meshCollider);
        }
    }

    private void SetHeavyCollidersEnabled(bool enabled)
    {
        if (heavyCollidersToDisableWhileMoving == null) return;
        foreach (Collider collider in heavyCollidersToDisableWhileMoving) if (collider != null) collider.enabled = enabled;
    }
}

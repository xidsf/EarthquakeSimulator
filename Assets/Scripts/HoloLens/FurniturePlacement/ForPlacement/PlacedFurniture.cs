using System;
using System.Collections.Generic;
using MixedReality.Toolkit;
using MixedReality.Toolkit.SpatialManipulation;
using UnityEngine;

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
    public float wallSnapDistance = 1.5f;

    [Header("Move Control")]
    public Behaviour[] moveBehaviours;
    public Collider[] heavyCollidersToDisableWhileMoving;
    public bool ensureKinematicRigidbody = true;
    public bool autoCollectPlacementColliders = true;
    public Collider[] placementColliders;

    [Header("Touch Selection")]
    public bool selectWhenTouchedOrMoved = true;

    [Header("Transform Move Monitor")]
    public bool autoDetectTransformMoveEnd = true;
    public float moveEndIdleSeconds = 0.2f;
    public float movePositionEpsilon = 0.0005f;
    public float moveRotationEpsilonDegrees = 0.15f;

    private readonly List<Collider> runtimePlacementColliders = new List<Collider>();
    private FurniturePlacementManager ownerManager;
    private Vector3 lastValidPosition;
    private Quaternion lastValidRotation;
    private Vector3 lastObservedPosition;
    private Quaternion lastObservedRotation;
    private float lastTransformChangeTime;
    private bool initialized;
    private bool isMovable;
    private bool isMoving;
    private FurnitureManipulationMode manipulationMode = FurnitureManipulationMode.None;

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
        if (isMoving && isWallMounted && ownerManager?.roomGeometryProvider != null)
        {
            if (ownerManager.roomGeometryProvider.TrySnapToNearestWall(transform.position, wallSnapDistance, out Vector3 snapPos, out Vector3 snapNormal))
            {
                // 벽면에서 1.5cm 띄워서 파묻히지 않게 함
                transform.position = Vector3.Lerp(transform.position, snapPos + snapNormal * 0.015f, Time.deltaTime * 15f);

                // 가구의 앞면(Forward)이 벽의 법선(Normal)과 같은 방향이 되도록 회전 (벽을 등짐)
                Quaternion targetRotation = Quaternion.LookRotation(snapNormal);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * 15f);
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
        if (manipulationMode != FurnitureManipulationMode.None && mode == FurnitureManipulationMode.None && isMoving) EndMove("MoveDisabled");

        manipulationMode = mode;
        isMovable = mode != FurnitureManipulationMode.None;
        TransformFlags allowedManipulations = ToTransformFlags(mode);

        int behaviourCount = 0;
        int manipulatorCount = 0;
        bool anyManipulatorEnabled = false;
        if (moveBehaviours != null)
        {
            foreach (Behaviour behaviour in moveBehaviours)
            {
                if (behaviour != null)
                {
                    behaviourCount++;
                    if (behaviour is ObjectManipulator manipulator)
                    {
                        manipulator.AllowedManipulations = allowedManipulations;
                        manipulatorCount++;
                    }
                    behaviour.enabled = isMovable;
                    if (behaviour is ObjectManipulator m2 && m2.enabled) anyManipulatorEnabled = true;
                }
            }
        }

        int colliderCount = 0;
        foreach (Collider c in GetComponentsInChildren<Collider>(true))
        {
            if (c != null && c.enabled) colliderCount++;
        }

        Debug.Log(
            $"[PlacedFurniture] SetManipulationMode '{DisplayName}' mode:{mode}, " +
            $"moveBehaviours:{behaviourCount}, manipulators:{manipulatorCount}, " +
            $"manipulatorEnabled:{anyManipulatorEnabled}, activeColliders:{colliderCount}",
            this);

        ResetTransformMonitor();
    }

    private static TransformFlags ToTransformFlags(FurnitureManipulationMode mode)
    {
        return mode switch
        {
            FurnitureManipulationMode.Move => TransformFlags.Move,
            FurnitureManipulationMode.Rotate => TransformFlags.Rotate,
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

    public void SaveCurrentPoseAsValid()
    {
        lastValidPosition = transform.position;
        lastValidRotation = transform.rotation;
        ResetTransformMonitor();
    }

    public void RestoreLastValidPose()
    {
        transform.position = lastValidPosition;
        transform.rotation = lastValidRotation;
        ResetTransformMonitor();
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

    private void BeginMove(string reason, bool saveCurrentAsValid)
    {
        if (isMoving) return;
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
               Quaternion.Angle(transform.rotation, lastObservedRotation) > moveRotationEpsilonDegrees;
    }

    private void ResetTransformMonitor()
    {
        lastObservedPosition = transform.position;
        lastObservedRotation = transform.rotation;
        lastTransformChangeTime = Time.time;
    }

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
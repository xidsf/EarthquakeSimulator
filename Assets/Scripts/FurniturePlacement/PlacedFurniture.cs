using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 서버/인식/디버그 파이프라인이 생성한 개별 가구 오브젝트에 붙는 런타임 관리 컴포넌트입니다.
///
/// 이동 방식:
/// - Move Mode ON일 때 moveBehaviours(ObjectManipulator 등)가 켜집니다.
/// - 사용자가 손으로 가구를 움직이면 transform 변경을 감지해 이동 시작/종료를 자동 처리합니다.
/// - ObjectManipulator 이벤트를 직접 연결할 수 있다면 NotifyMoveStarted/NotifyMoveEnded를 연결해도 됩니다.
/// </summary>
public class PlacedFurniture : MonoBehaviour
{
    [Header("Runtime Metadata")]
    [SerializeField] private FurniturePlacementState state = FurniturePlacementState.None;
    [SerializeField] private string furnitureId;
    [SerializeField] private string label;
    [SerializeField] private string displayName;
    [SerializeField] private string source;

    [Header("Move Control")]
    [Tooltip("MRTK ObjectManipulator, BoundsControl 등 이동 모드에서만 켤 Behaviour들을 연결합니다. RuntimeFurnitureInteractionSetup이 ObjectManipulator를 자동 추가하면 여기에 자동 등록합니다.")]
    public Behaviour[] moveBehaviours;

    [Tooltip("이동 중 반드시 꺼야 하는 무거운 collider가 있을 때만 연결합니다. 현재 테스트 prefab은 MeshCollider가 interaction에도 필요할 수 있으므로 기본적으로 비워두는 것을 권장합니다.")]
    public Collider[] heavyCollidersToDisableWhileMoving;

    [Tooltip("true면 Initialize 시 Rigidbody를 추가/설정하고 kinematic 상태로 둡니다.")]
    public bool ensureKinematicRigidbody = true;

    [Tooltip("true면 서버/디버그가 만든 Convex MeshCollider를 포함한 자식 colliders를 배치 검증용으로 자동 수집합니다.")]
    public bool autoCollectPlacementColliders = true;

    [Tooltip("수동으로 지정하고 싶을 때 사용합니다. 비워두면 자식 Collider 전체를 자동 수집합니다.")]
    public Collider[] placementColliders;

    [Header("Touch Selection")]
    [Tooltip("Move Mode ON에서 터치/움직임이 감지되면 해당 가구를 선택합니다.")]
    public bool selectWhenTouchedOrMoved = true;

    [Header("Transform Move Monitor")]
    [Tooltip("ObjectManipulator 이벤트 연결이 없어도 transform 변경을 감지해 이동 종료 검증을 수행합니다.")]
    public bool autoDetectTransformMoveEnd = true;

    [Tooltip("이 시간 동안 transform 변화가 없으면 이동이 끝난 것으로 판단합니다.")]
    public float moveEndIdleSeconds = 0.2f;

    [Tooltip("이 값보다 작은 위치 변화는 무시합니다.")]
    public float movePositionEpsilon = 0.0005f;

    [Tooltip("이 값보다 작은 회전 변화는 무시합니다.")]
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

    public void Initialize(FurniturePlacementManager manager, FurniturePlacementMetadata metadata)
    {
        ownerManager = manager;

        if (metadata == null)
        {
            metadata = FurniturePlacementMetadata.CreateDefault(gameObject);
        }

        metadata.EnsureDefaults(gameObject);
        furnitureId = metadata.furnitureId;
        label = metadata.label;
        displayName = metadata.displayName;
        source = metadata.source;

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
        if (!initialized || !autoDetectTransformMoveEnd || !isMovable)
        {
            return;
        }

        bool changed = HasTransformChangedSinceLastObservation();
        if (changed)
        {
            if (!isMoving)
            {
                // transform monitor는 이미 한 프레임 움직인 뒤에 감지되므로,
                // 직전 관측 pose를 last valid pose로 저장합니다.
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

    public void SetState(FurniturePlacementState nextState)
    {
        state = nextState;
    }

    public void SetMovable(bool movable)
    {
        if (isMovable && !movable && isMoving)
        {
            EndMove("MoveDisabled");
        }

        isMovable = movable;

        if (moveBehaviours != null)
        {
            foreach (Behaviour behaviour in moveBehaviours)
            {
                if (behaviour != null)
                {
                    behaviour.enabled = movable;
                }
            }
        }

        ResetTransformMonitor();
    }

    public void NotifyTouched()
    {
        NotifyTouched("Touch");
    }

    public void NotifyTouched(string inputSource)
    {
        if (!initialized)
        {
            return;
        }

        Touched?.Invoke(this);
        ownerManager?.HandleFurnitureTouched(this, inputSource);
    }

    public void NotifyMoveStarted()
    {
        if (!initialized)
        {
            return;
        }

        BeginMove("ExternalEvent", saveCurrentAsValid: true);
    }

    public void NotifyMoveEnded()
    {
        if (!initialized)
        {
            return;
        }

        EndMove("ExternalEvent");
    }

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
    }

    public Collider[] GetActivePlacementColliders()
    {
        if (runtimePlacementColliders.Count == 0)
        {
            CollectPlacementColliders();
        }

        return runtimePlacementColliders.ToArray();
    }

    private void BeginMove(string reason, bool saveCurrentAsValid)
    {
        if (isMoving)
        {
            return;
        }

        if (selectWhenTouchedOrMoved)
        {
            ownerManager?.HandleFurnitureTouched(this, reason);
        }

        isMoving = true;
        lastTransformChangeTime = Time.time;

        if (saveCurrentAsValid)
        {
            SaveCurrentPoseAsValid();
        }

        SetHeavyCollidersEnabled(false);
        MoveStarted?.Invoke(this);
        Debug.Log($"[PlacedFurniture] Move started. {DisplayName}, reason:{reason}", this);
    }

    private void EndMove(string reason)
    {
        if (!isMoving)
        {
            return;
        }

        isMoving = false;
        SetHeavyCollidersEnabled(true);
        MoveEnded?.Invoke(this);
        ownerManager?.HandleFurnitureMoveEnded(this);
        ResetTransformMonitor();
        Debug.Log($"[PlacedFurniture] Move ended. {DisplayName}, reason:{reason}", this);
    }

    private bool HasTransformChangedSinceLastObservation()
    {
        float positionDelta = Vector3.Distance(transform.position, lastObservedPosition);
        float rotationDelta = Quaternion.Angle(transform.rotation, lastObservedRotation);

        return positionDelta > movePositionEpsilon || rotationDelta > moveRotationEpsilonDegrees;
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
            foreach (Collider collider in placementColliders)
            {
                if (collider != null && !runtimePlacementColliders.Contains(collider))
                {
                    runtimePlacementColliders.Add(collider);
                }
            }
        }

        if (runtimePlacementColliders.Count == 0 && autoCollectPlacementColliders)
        {
            Collider[] colliders = GetComponentsInChildren<Collider>(true);
            foreach (Collider collider in colliders)
            {
                if (collider != null && !runtimePlacementColliders.Contains(collider))
                {
                    runtimePlacementColliders.Add(collider);
                }
            }
        }
    }

    private void ConfigureRigidbody()
    {
        if (!ensureKinematicRigidbody)
        {
            return;
        }

        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody>();
        }

        rb.isKinematic = true;
        rb.useGravity = false;
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
    }

    private void WarnIfMeshCollidersAreNotConvex()
    {
        Collider[] colliders = GetActivePlacementColliders();
        foreach (Collider collider in colliders)
        {
            MeshCollider meshCollider = collider as MeshCollider;
            if (meshCollider != null && !meshCollider.convex)
            {
                Debug.LogWarning($"[PlacedFurniture] MeshCollider is not convex: {meshCollider.name}. HoloLens2 이동/검증에서는 Convex MeshCollider 또는 단순 Proxy Collider를 권장합니다.", meshCollider);
            }
        }
    }

    private void SetHeavyCollidersEnabled(bool enabled)
    {
        if (heavyCollidersToDisableWhileMoving == null)
        {
            return;
        }

        foreach (Collider collider in heavyCollidersToDisableWhileMoving)
        {
            if (collider != null)
            {
                collider.enabled = enabled;
            }
        }
    }
}

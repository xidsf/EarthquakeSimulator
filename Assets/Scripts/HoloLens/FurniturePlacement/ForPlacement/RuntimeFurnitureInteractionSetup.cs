using System.Collections.Generic;
using MixedReality.Toolkit;
using MixedReality.Toolkit.SpatialManipulation;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// 서버/디버그 파이프라인에서 생성된 단순 가구 오브젝트를 런타임에 HoloLens2 조작 가능한 형태로 보강합니다.
///
/// 현재 테스트 prefab 전제:
/// Object(root)
/// ㄴ Mesh child(MeshFilter + MeshRenderer)
///
/// 수행 작업:
/// - 서버가 제공한 MeshCollider를 보존
/// - 필요 시에만 Mesh가 있는 자식 오브젝트에 MeshCollider 추가
/// - 필요 시에만 MeshCollider.convex = true 설정
/// - root에 Rigidbody 추가 및 kinematic 설정
/// - root에 PlacedFurniture 추가
/// - root에 MRTK3 ObjectManipulator 추가
/// - root에 FurnitureObjectManipulatorBridge 추가
/// - scale이 비정상적으로 큰 mesh child는 1/4로 축소
///
/// 주의:
/// - UnityEngine.EventSystems.IPointerDownHandler 계열은 사용하지 않습니다.
/// - HoloLens2 손 조작은 MRTK3 ObjectManipulator의 selectEntered/selectExited 흐름으로 연결합니다.
/// </summary>
[DisallowMultipleComponent]
public class RuntimeFurnitureInteractionSetup : MonoBehaviour
{
    [Header("Mesh Collider Setup")]
    public bool addMeshColliderToMeshChildren = false;
    public bool forceConvexMeshCollider = false;
    public bool includeInactiveChildren = true;

    [Tooltip("서버가 이미 MeshCollider(convex 포함)를 붙여서 보낸다는 전제입니다. 클라이언트는 기존 MeshCollider를 보존하고 새로 만들지 않습니다.")]
    public bool useServerProvidedMeshColliders = true;

    [Tooltip("HoloLens2에서는 복잡한 GLB MeshCollider/convex cooking을 강제로 건너뜁니다. 서버 제공 MeshCollider는 유지합니다.")]
    public bool suppressMeshCollidersOnDevice = true;

    [Header("Scale Correction")]
    [Tooltip("Mesh child의 localScale 중 하나라도 threshold 이상이면 scaleMultiplier를 곱합니다.")]
    public bool shrinkLargeMeshChildScale = true;
    public float largeScaleThreshold = 100f;
    public float largeScaleMultiplier = 0.25f;
    public bool applyScaleCorrectionOnlyOnce = true;

    [Header("Interaction Setup")]
    public bool addKinematicRigidbodyToRoot = true;
    public bool addPlacedFurnitureToRoot = true;
    public bool addObjectManipulatorToRoot = true;
    public bool addObjectManipulatorBridgeToRoot = true;
    public bool addYawOnlyRotationConstraintToRoot = true;

    [Min(0.0f)]
    [Tooltip("Rotate Mode에서 손 회전 입력 각도를 몇 배로 적용할지 설정합니다. 1이면 MRTK 기본 회전 속도입니다.")]
    public float rotationSensitivityMultiplier = 2.5f;

    [Tooltip("자동 추가된 ObjectManipulator는 PlacedFurniture.moveBehaviours에 등록되고 Move Mode가 켜질 때만 enabled 됩니다.")]
    public bool disableManipulatorInitially = true;

    [Header("Rigidbody")]
    public bool useGravity = false;
    public bool isKinematic = true;
    public RigidbodyConstraints rigidbodyConstraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

    [Header("Log")]
    public bool logSetupSummary = true;

    public PlacedFurniture ConfigureFurnitureObject(GameObject root)
    {
        if (root == null)
        {
            Debug.LogWarning("[RuntimeFurnitureInteractionSetup] Configure failed. root is null.");
            return null;
        }

        RuntimeFurnitureSetupMarker marker = root.GetComponent<RuntimeFurnitureSetupMarker>();
        if (marker == null)
        {
            marker = root.AddComponent<RuntimeFurnitureSetupMarker>();
        }

        int scaledChildren = 0;
        if (shrinkLargeMeshChildScale && (!applyScaleCorrectionOnlyOnce || !marker.scaleCorrectionApplied))
        {
            scaledChildren = ShrinkLargeMeshChildren(root);
            marker.scaleCorrectionApplied = true;
        }

        int meshColliderCount = 0;
        if (ShouldAddMeshCollidersToMeshChildren())
        {
            meshColliderCount = EnsureMeshColliders(root);
        }

        if (addKinematicRigidbodyToRoot)
        {
            EnsureRigidbody(root);
        }

        PlacedFurniture placedFurniture = null;
        if (addPlacedFurnitureToRoot)
        {
            placedFurniture = root.GetComponent<PlacedFurniture>();
            if (placedFurniture == null)
            {
                placedFurniture = root.AddComponent<PlacedFurniture>();
            }
        }

        ObjectManipulator manipulator = null;
        if (addObjectManipulatorToRoot)
        {
            manipulator = EnsureObjectManipulator(root);
            if (manipulator != null)
            {
                ConfigureRotationSensitivity(manipulator);
                manipulator.AllowedManipulations = TransformFlags.Move;

                // 런타임 AddComponent 시 XRI 자동 collider 수집이 깊은 자식(server convex hull)을
                // 누락하는 경우가 있어, 모든 자식 collider를 명시적으로 등록한다.
                // 이후 Move 모드의 enable 시 OnEnable이 이 목록으로 InteractionManager에 재등록한다.
                RefreshInteractableColliders(root, manipulator);

                if (disableManipulatorInitially)
                {
                    manipulator.enabled = false;
                }

                if (placedFurniture != null)
                {
                    AppendMoveBehaviour(placedFurniture, manipulator);
                }
            }
        }

        if (addYawOnlyRotationConstraintToRoot)
        {
            EnsureYawOnlyRotationConstraint(root);
        }

        FurnitureObjectManipulatorBridge bridge = null;
        if (addObjectManipulatorBridgeToRoot && placedFurniture != null && manipulator != null)
        {
            bridge = EnsureObjectManipulatorBridge(root, placedFurniture, manipulator);
        }

        marker.meshColliderConfigured = addMeshColliderToMeshChildren;
        marker.interactionConfigured = true;

        if (logSetupSummary)
        {
            Debug.Log(
                $"[RuntimeFurnitureInteractionSetup] Configured furniture '{root.name}'. " +
                $"scaledChildren:{scaledChildren}, meshColliders:{meshColliderCount}, " +
                $"placedFurniture:{placedFurniture != null}, manipulator:{manipulator != null}, " +
                $"manipulatorBridge:{bridge != null}",
                root);
        }

        return placedFurniture;
    }

    private int ShrinkLargeMeshChildren(GameObject root)
    {
        int count = 0;
        MeshFilter[] meshFilters = root.GetComponentsInChildren<MeshFilter>(includeInactiveChildren);

        foreach (MeshFilter meshFilter in meshFilters)
        {
            if (meshFilter == null)
            {
                continue;
            }

            Transform child = meshFilter.transform;
            Vector3 scale = child.localScale;

            float maxAxis = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            if (maxAxis < largeScaleThreshold)
            {
                continue;
            }

            child.localScale = scale * largeScaleMultiplier;
            count++;

            Debug.Log(
                $"[RuntimeFurnitureInteractionSetup] Large mesh child scale corrected. " +
                $"object:{child.name}, before:{scale}, after:{child.localScale}",
                child);
        }

        return count;
    }

    private int EnsureMeshColliders(GameObject root)
    {
        int count = 0;
        MeshFilter[] meshFilters = root.GetComponentsInChildren<MeshFilter>(includeInactiveChildren);

        foreach (MeshFilter meshFilter in meshFilters)
        {
            if (meshFilter == null || meshFilter.sharedMesh == null)
            {
                continue;
            }

            GameObject meshObject = meshFilter.gameObject;
            MeshCollider meshCollider = meshObject.GetComponent<MeshCollider>();
            if (meshCollider == null)
            {
                meshCollider = meshObject.AddComponent<MeshCollider>();
            }

            if (meshCollider.sharedMesh != meshFilter.sharedMesh)
            {
                meshCollider.sharedMesh = null;
                meshCollider.sharedMesh = meshFilter.sharedMesh;
            }

            if (forceConvexMeshCollider)
            {
                meshCollider.convex = true;
            }

            meshCollider.enabled = true;
            count++;
        }

        return count;
    }

    private bool ShouldAddMeshCollidersToMeshChildren()
    {
        if (useServerProvidedMeshColliders)
        {
            return false;
        }

#if UNITY_WSA && !UNITY_EDITOR
        if (suppressMeshCollidersOnDevice)
        {
            return false;
        }
#endif

        return addMeshColliderToMeshChildren;
    }

    private void EnsureRigidbody(GameObject root)
    {
        Rigidbody rb = root.GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = root.AddComponent<Rigidbody>();
        }

        rb.isKinematic = isKinematic;
        rb.useGravity = useGravity;
        rb.constraints = rigidbodyConstraints;
    }

    private ObjectManipulator EnsureObjectManipulator(GameObject root)
    {
        ObjectManipulator existing = root.GetComponent<ObjectManipulator>();
        if (existing != null)
        {
            return existing;
        }

        return root.AddComponent<ObjectManipulator>();
    }

    private void ConfigureRotationSensitivity(ObjectManipulator manipulator)
    {
        if (manipulator == null)
        {
            return;
        }

        FurnitureRotationSensitivityLogic.SensitivityMultiplier = rotationSensitivityMultiplier;

        ObjectManipulator.LogicType logicTypes = manipulator.ManipulationLogicTypes;
        if (logicTypes.rotateLogicType != null && logicTypes.rotateLogicType.Type == typeof(FurnitureRotationSensitivityLogic))
        {
            return;
        }

        logicTypes.rotateLogicType = typeof(FurnitureRotationSensitivityLogic);
        manipulator.ManipulationLogicTypes = logicTypes;
    }

    private static void RefreshInteractableColliders(GameObject root, ObjectManipulator manipulator)
    {
        Collider[] childColliders = root.GetComponentsInChildren<Collider>(true);

        manipulator.colliders.Clear();
        foreach (Collider collider in childColliders)
        {
            if (collider != null)
            {
                manipulator.colliders.Add(collider);
            }
        }

        // 이미 enable 상태라면 InteractionManager가 예전(빈) collider 목록으로 등록돼 있으므로
        // enable을 토글해 OnDisable/OnEnable로 collider→interactable 맵을 재구성한다.
        if (manipulator.enabled)
        {
            manipulator.enabled = false;
            manipulator.enabled = true;
        }
    }

    private RotationAxisConstraint EnsureYawOnlyRotationConstraint(GameObject root)
    {
        RotationAxisConstraint constraint = root.GetComponent<RotationAxisConstraint>();
        if (constraint == null)
        {
            constraint = root.AddComponent<RotationAxisConstraint>();
        }

        constraint.ConstraintOnRotation = AxisFlags.XAxis | AxisFlags.ZAxis;
        constraint.UseLocalSpaceForConstraint = false;
        return constraint;
    }

    private FurnitureObjectManipulatorBridge EnsureObjectManipulatorBridge(
        GameObject root,
        PlacedFurniture placedFurniture,
        ObjectManipulator objectManipulator)
    {
        FurnitureObjectManipulatorBridge bridge = root.GetComponent<FurnitureObjectManipulatorBridge>();
        if (bridge == null)
        {
            bridge = root.AddComponent<FurnitureObjectManipulatorBridge>();
        }

        bridge.placedFurniture = placedFurniture;
        bridge.objectManipulator = objectManipulator;
        bridge.EnsureReferences();
        return bridge;
    }

    private static void AppendMoveBehaviour(PlacedFurniture placedFurniture, Behaviour behaviour)
    {
        if (placedFurniture == null || behaviour == null)
        {
            return;
        }

        List<Behaviour> behaviours = new List<Behaviour>();
        if (placedFurniture.moveBehaviours != null)
        {
            foreach (Behaviour item in placedFurniture.moveBehaviours)
            {
                if (item != null && !behaviours.Contains(item))
                {
                    behaviours.Add(item);
                }
            }
        }

        if (!behaviours.Contains(behaviour))
        {
            behaviours.Add(behaviour);
        }

        placedFurniture.moveBehaviours = behaviours.ToArray();
    }
}

/// <summary>
/// 런타임 세팅이 중복 적용되는 것을 막기 위한 내부 marker입니다.
/// </summary>
[DisallowMultipleComponent]
public class RuntimeFurnitureSetupMarker : MonoBehaviour
{
    public bool scaleCorrectionApplied;
    public bool meshColliderConfigured;
    public bool interactionConfigured;
}

/// <summary>
/// MRTK ObjectManipulator rotation logic with an adjustable angle multiplier.
/// </summary>
public class FurnitureRotationSensitivityLogic : ManipulationLogic<Quaternion>
{
    public static float SensitivityMultiplier { get; set; } = 2.5f;

    private Vector3 startHandlebar;
    private Quaternion startInputRotation;
    private Quaternion startRotation;

    public override void Setup(List<IXRSelectInteractor> interactors, IXRSelectInteractable interactable, MixedRealityTransform currentTarget)
    {
        base.Setup(interactors, interactable, currentTarget);

        if (NumInteractors >= 2)
        {
            startHandlebar = GetHandlebarDirection(interactors, interactable);
        }

        startInputRotation = interactors[0].GetAttachTransform(interactable).rotation;
        startRotation = currentTarget.Rotation;
    }

    public override Quaternion Update(List<IXRSelectInteractor> interactors, IXRSelectInteractable interactable, MixedRealityTransform currentTarget, bool centeredAnchor)
    {
        base.Update(interactors, interactable, currentTarget, centeredAnchor);

        if (SelectedBySocket)
        {
            return interactors[0].GetAttachTransform(interactable).rotation;
        }

        Quaternion inputDelta = NumInteractors == 1
            ? interactors[0].GetAttachTransform(interactable).rotation * Quaternion.Inverse(startInputRotation)
            : Quaternion.FromToRotation(startHandlebar, GetHandlebarDirection(interactors, interactable));

        return ScaleRotationDelta(inputDelta, Mathf.Max(0.0f, SensitivityMultiplier)) * startRotation;
    }

    private static Quaternion ScaleRotationDelta(Quaternion delta, float multiplier)
    {
        if (Mathf.Approximately(multiplier, 1.0f))
        {
            return delta;
        }

        delta.ToAngleAxis(out float angle, out Vector3 axis);
        if (axis.sqrMagnitude < Mathf.Epsilon)
        {
            return delta;
        }

        if (angle > 180.0f)
        {
            angle -= 360.0f;
        }

        return Quaternion.AngleAxis(angle * multiplier, axis.normalized);
    }

    private static Vector3 GetHandlebarDirection(List<IXRSelectInteractor> interactors, IXRSelectInteractable interactable)
    {
        Debug.Assert(interactors.Count >= 2, $"GetHandlebarDirection called with less than 2 interactors ({interactors.Count}).");
        return interactors[1].GetAttachTransform(interactable).position - interactors[0].GetAttachTransform(interactable).position;
    }
}

using System.Collections.Generic;
using MixedReality.Toolkit;
using MixedReality.Toolkit.SpatialManipulation;
using UnityEngine;

/// <summary>
/// 서버/디버그 파이프라인에서 생성된 단순 가구 오브젝트를 런타임에 HoloLens2 조작 가능한 형태로 보강합니다.
///
/// 현재 테스트 prefab 전제:
/// Object(root)
/// ㄴ Mesh child(MeshFilter + MeshRenderer)
///
/// 수행 작업:
/// - Mesh가 있는 자식 오브젝트에 MeshCollider 추가
/// - MeshCollider.convex = true 설정
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
    public bool addMeshColliderToMeshChildren = true;
    public bool forceConvexMeshCollider = true;
    public bool includeInactiveChildren = true;

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
        if (addMeshColliderToMeshChildren)
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
                manipulator.AllowedManipulations = TransformFlags.Move;

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

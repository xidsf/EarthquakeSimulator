using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class RoomMeshExporter : MonoBehaviour
{
    [Header("Export Roots")]
    [Tooltip("가구 오브젝트들이 모두 포함된 Root GameObject입니다. 하위 Rigidbody를 dynamic object로 export합니다.")]
    public GameObject furnitureRoot;

    [Tooltip("벽, 바닥 등 방 구조가 포함된 Root GameObject입니다. 하위 Collider를 static structure로 export합니다.")]
    public GameObject roomStructureRoot;

    [Header("Output")]
    [Tooltip("Application.dataPath 기준 상대 경로 또는 절대 경로입니다.")]
    public string outputJsonPath = "../PythonSimulation/simulation_request.json";

    [Tooltip("true면 MeshCollider vertices/indices를 JSON에 포함합니다. 서버 전송 payload가 커질 수 있습니다.")]
    public bool includeMeshGeometry = true;

    [Tooltip("true면 inactive child도 export합니다.")]
    public bool includeInactiveObjects = false;

    [Header("Defaults")]
    public float defaultDynamicMass = 10f;
    public float defaultFriction = 0.6f;
    public float defaultRestitution = 0f;

    [Serializable]
    public class SimulationRequest
    {
        public string schema = "earthquake-simulation-request";
        public string version = "1.0";
        public string coordinateSystem = "unity_y_up";
        public string unit = "meter";
        public List<PhysicsBodyDto> roomStructures = new List<PhysicsBodyDto>();
        public List<PhysicsBodyDto> dynamicObjects = new List<PhysicsBodyDto>();
    }

    [Serializable]
    public class PhysicsBodyDto
    {
        public string id;
        public string name;
        public string bodyType;
        public float mass;
        public float friction;
        public float restitution;
        public Vector3Dto position;
        public QuaternionDto rotation;
        public Vector3Dto centerOfMass;
        public List<ColliderDto> colliders = new List<ColliderDto>();
    }

    [Serializable]
    public class ColliderDto
    {
        public string id;
        public string type;
        public bool convex;
        public Vector3Dto center;
        public QuaternionDto rotation;
        public Vector3Dto size;
        public float radius;
        public float height;
        public List<Vector3Dto> vertices;
        public List<int> indices;
    }

    [Serializable]
    public struct Vector3Dto
    {
        public float x;
        public float y;
        public float z;

        public Vector3Dto(Vector3 value)
        {
            x = value.x;
            y = value.y;
            z = value.z;
        }
    }

    [Serializable]
    public struct QuaternionDto
    {
        public float x;
        public float y;
        public float z;
        public float w;

        public QuaternionDto(Quaternion value)
        {
            x = value.x;
            y = value.y;
            z = value.z;
            w = value.w;
        }
    }

    [ContextMenu("Export Room Mesh JSON")]
    public void ExportToConfiguredPath()
    {
        string json = BuildSimulationRequestJson();
        string resolvedPath = ResolveOutputPath(outputJsonPath);
        string directory = Path.GetDirectoryName(resolvedPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(resolvedPath, json);
        Debug.Log($"[RoomMeshExporter] Exported simulation request: {resolvedPath}");
    }

    public string BuildSimulationRequestJson()
    {
        SimulationRequest request = BuildSimulationRequest();
        return JsonUtility.ToJson(request, true);
    }

    public SimulationRequest BuildSimulationRequest()
    {
        SimulationRequest request = new SimulationRequest();

        ExportRoomStructures(request);
        ExportDynamicObjects(request);

        Debug.Log($"[RoomMeshExporter] Built request. structures:{request.roomStructures.Count}, dynamicObjects:{request.dynamicObjects.Count}");
        return request;
    }

    private void ExportRoomStructures(SimulationRequest request)
    {
        if (roomStructureRoot == null)
        {
            Debug.LogWarning("[RoomMeshExporter] roomStructureRoot is missing.");
            return;
        }

        Collider[] colliders = roomStructureRoot.GetComponentsInChildren<Collider>(includeInactiveObjects);
        foreach (Collider collider in colliders)
        {
            if (!ShouldExportCollider(collider))
                continue;

            PhysicsBodyDto body = CreateBodyDto(
                collider.gameObject.GetInstanceID().ToString(),
                collider.gameObject.name,
                "static",
                collider.transform,
                0f,
                null);

            ColliderDto colliderDto = CreateColliderDto(collider, collider.transform);
            if (colliderDto == null)
                continue;

            body.colliders.Add(colliderDto);
            request.roomStructures.Add(body);
        }
    }

    private void ExportDynamicObjects(SimulationRequest request)
    {
        if (furnitureRoot == null)
        {
            Debug.LogWarning("[RoomMeshExporter] furnitureRoot is missing.");
            return;
        }

        Rigidbody[] rigidbodies = furnitureRoot.GetComponentsInChildren<Rigidbody>(includeInactiveObjects);
        HashSet<Rigidbody> exported = new HashSet<Rigidbody>();

        foreach (Rigidbody rb in rigidbodies)
        {
            if (rb == null || exported.Contains(rb) || rb.isKinematic)
                continue;

            Collider[] colliders = rb.GetComponentsInChildren<Collider>(includeInactiveObjects);
            PhysicsBodyDto body = CreateBodyDto(
                rb.gameObject.GetInstanceID().ToString(),
                rb.gameObject.name,
                "dynamic",
                rb.transform,
                rb.mass > 0f ? rb.mass : defaultDynamicMass,
                rb);

            foreach (Collider collider in colliders)
            {
                if (!ShouldExportCollider(collider))
                    continue;

                if (collider.attachedRigidbody != rb)
                    continue;

                ColliderDto colliderDto = CreateColliderDto(collider, rb.transform);
                if (colliderDto != null)
                    body.colliders.Add(colliderDto);
            }

            if (body.colliders.Count > 0)
            {
                request.dynamicObjects.Add(body);
                exported.Add(rb);
            }
        }
    }

    private PhysicsBodyDto CreateBodyDto(string id, string name, string bodyType, Transform bodyTransform, float mass, Rigidbody rb)
    {
        PhysicsBodyDto body = new PhysicsBodyDto
        {
            id = id,
            name = name,
            bodyType = bodyType,
            mass = mass,
            friction = defaultFriction,
            restitution = defaultRestitution,
            position = new Vector3Dto(bodyTransform.position),
            rotation = new QuaternionDto(bodyTransform.rotation),
            centerOfMass = new Vector3Dto(Vector3.zero)
        };

        if (rb != null)
            body.centerOfMass = new Vector3Dto(rb.centerOfMass);

        return body;
    }

    private ColliderDto CreateColliderDto(Collider collider, Transform bodyTransform)
    {
        if (collider is BoxCollider box)
            return CreateBoxColliderDto(box, bodyTransform);

        if (collider is SphereCollider sphere)
            return CreateSphereColliderDto(sphere, bodyTransform);

        if (collider is CapsuleCollider capsule)
            return CreateCapsuleColliderDto(capsule, bodyTransform);

        if (collider is MeshCollider meshCollider)
            return CreateMeshColliderDto(meshCollider, bodyTransform);

        Debug.LogWarning($"[RoomMeshExporter] Unsupported collider type: {collider.GetType().Name}, object:{collider.name}");
        return null;
    }

    private ColliderDto CreateBoxColliderDto(BoxCollider collider, Transform bodyTransform)
    {
        return new ColliderDto
        {
            id = collider.GetInstanceID().ToString(),
            type = "box",
            convex = true,
            center = new Vector3Dto(WorldPointToBodyLocal(bodyTransform, collider.transform.TransformPoint(collider.center))),
            rotation = new QuaternionDto(Quaternion.Inverse(bodyTransform.rotation) * collider.transform.rotation),
            size = new Vector3Dto(Vector3.Scale(collider.size, AbsVector(collider.transform.lossyScale))),
            radius = 0f,
            height = 0f
        };
    }

    private ColliderDto CreateSphereColliderDto(SphereCollider collider, Transform bodyTransform)
    {
        Vector3 scale = AbsVector(collider.transform.lossyScale);
        float radiusScale = Mathf.Max(scale.x, scale.y, scale.z);
        return new ColliderDto
        {
            id = collider.GetInstanceID().ToString(),
            type = "sphere",
            convex = true,
            center = new Vector3Dto(WorldPointToBodyLocal(bodyTransform, collider.transform.TransformPoint(collider.center))),
            rotation = new QuaternionDto(Quaternion.identity),
            size = new Vector3Dto(Vector3.zero),
            radius = collider.radius * radiusScale,
            height = 0f
        };
    }

    private ColliderDto CreateCapsuleColliderDto(CapsuleCollider collider, Transform bodyTransform)
    {
        Vector3 scale = AbsVector(collider.transform.lossyScale);
        float radiusScale = collider.direction == 0 ? Mathf.Max(scale.y, scale.z) :
                            collider.direction == 1 ? Mathf.Max(scale.x, scale.z) :
                            Mathf.Max(scale.x, scale.y);
        float heightScale = collider.direction == 0 ? scale.x : collider.direction == 1 ? scale.y : scale.z;

        return new ColliderDto
        {
            id = collider.GetInstanceID().ToString(),
            type = "capsule",
            convex = true,
            center = new Vector3Dto(WorldPointToBodyLocal(bodyTransform, collider.transform.TransformPoint(collider.center))),
            rotation = new QuaternionDto(Quaternion.Inverse(bodyTransform.rotation) * collider.transform.rotation),
            size = new Vector3Dto(Vector3.zero),
            radius = collider.radius * radiusScale,
            height = collider.height * heightScale
        };
    }

    private ColliderDto CreateMeshColliderDto(MeshCollider collider, Transform bodyTransform)
    {
        if (collider.sharedMesh == null)
        {
            Debug.LogWarning($"[RoomMeshExporter] MeshCollider has no sharedMesh: {collider.name}");
            return null;
        }

        if (!collider.convex)
            Debug.LogWarning($"[RoomMeshExporter] MeshCollider is not convex. Server dynamic simulation may be unstable: {collider.name}");

        ColliderDto dto = new ColliderDto
        {
            id = collider.GetInstanceID().ToString(),
            type = "convex_mesh",
            convex = collider.convex,
            center = new Vector3Dto(Vector3.zero),
            rotation = new QuaternionDto(Quaternion.identity),
            size = new Vector3Dto(Vector3.zero),
            radius = 0f,
            height = 0f,
            vertices = new List<Vector3Dto>(),
            indices = new List<int>()
        };

        if (!includeMeshGeometry)
            return dto;

        Mesh mesh = collider.sharedMesh;
        Vector3[] vertices = mesh.vertices;
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 world = collider.transform.TransformPoint(vertices[i]);
            dto.vertices.Add(new Vector3Dto(WorldPointToBodyLocal(bodyTransform, world)));
        }

        int[] triangles = mesh.triangles;
        for (int i = 0; i < triangles.Length; i++)
            dto.indices.Add(triangles[i]);

        return dto;
    }

    private bool ShouldExportCollider(Collider collider)
    {
        if (collider == null || !collider.enabled)
            return false;

        if (!includeInactiveObjects && !collider.gameObject.activeInHierarchy)
            return false;

        return true;
    }

    private Vector3 WorldPointToBodyLocal(Transform bodyTransform, Vector3 worldPoint)
    {
        return bodyTransform.InverseTransformPoint(worldPoint);
    }

    private Vector3 AbsVector(Vector3 value)
    {
        return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
    }

    private string ResolveOutputPath(string path)
    {
        if (Path.IsPathRooted(path))
            return path;

        return Path.GetFullPath(Path.Combine(Application.dataPath, path));
    }
}

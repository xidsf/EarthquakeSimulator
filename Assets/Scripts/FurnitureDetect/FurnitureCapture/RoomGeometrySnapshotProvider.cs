using System;
using System.Collections.Generic;
using UnityEngine;

public class RoomGeometrySnapshotProvider : MonoBehaviour
{
    [Header("Room")]
    [SerializeField] private string roomId = "manual_room";

    [Header("Generated Room Objects - Explicit References")]
    [Tooltip("선택 사항입니다. Confirmed Room 전체 Root를 알고 있다면 넣어주세요. metadata 기록용입니다.")]
    [SerializeField] private Transform roomRoot;

    [Tooltip("동적으로 생성된 ConfirmedRoom_Floor 오브젝트의 Transform입니다.")]
    [SerializeField] private Transform floor;

    [Tooltip("동적으로 생성된 ConfirmedRoom_Ceiling 오브젝트의 Transform입니다.")]
    [SerializeField] private Transform ceiling;

    [Tooltip("동적으로 생성된 ConfirmedRoom_Wall 오브젝트들의 Transform입니다.")]
    [SerializeField] private List<Transform> walls = new List<Transform>();

    [Header("Mesh Export Option")]
    [SerializeField] private bool includeMeshVertices = true;
    [SerializeField] private bool includeMeshTriangles = false;

    public void SetRoomObjects(
        Transform root,
        Transform floorObject,
        Transform ceilingObject,
        IList<Transform> wallObjects)
    {
        roomRoot = root;
        floor = floorObject;
        ceiling = ceilingObject;

        walls = new List<Transform>();
        if (wallObjects != null)
        {
            for (int i = 0; i < wallObjects.Count; i++)
            {
                if (wallObjects[i] != null)
                    walls.Add(wallObjects[i]);
            }
        }

        Debug.Log(
            "[RoomSnapshot] Room objects set\n" +
            $"root: {(roomRoot != null ? roomRoot.name : "null")}\n" +
            $"floor: {(floor != null ? floor.name : "null")}\n" +
            $"ceiling: {(ceiling != null ? ceiling.name : "null")}\n" +
            $"walls: {walls.Count}"
        );
    }

    public void SetRoomObjects(
        Transform floorObject,
        Transform ceilingObject,
        IList<Transform> wallObjects)
    {
        SetRoomObjects(null, floorObject, ceilingObject, wallObjects);
    }

    public void SetRoomGameObjects(
        GameObject root,
        GameObject floorObject,
        GameObject ceilingObject,
        IList<GameObject> wallObjects)
    {
        List<Transform> wallTransforms = new List<Transform>();

        if (wallObjects != null)
        {
            for (int i = 0; i < wallObjects.Count; i++)
            {
                if (wallObjects[i] != null)
                    wallTransforms.Add(wallObjects[i].transform);
            }
        }

        SetRoomObjects(
            root != null ? root.transform : null,
            floorObject != null ? floorObject.transform : null,
            ceilingObject != null ? ceilingObject.transform : null,
            wallTransforms
        );
    }

    public void SetRoomRoot(Transform root)
    {
        roomRoot = root;
    }

    public void SetFloorObject(Transform floorObject)
    {
        floor = floorObject;
    }

    public void SetCeilingObject(Transform ceilingObject)
    {
        ceiling = ceilingObject;
    }

    public void SetWallObjects(IList<Transform> wallObjects)
    {
        walls = new List<Transform>();

        if (wallObjects == null)
            return;

        for (int i = 0; i < wallObjects.Count; i++)
        {
            if (wallObjects[i] != null)
                walls.Add(wallObjects[i]);
        }
    }

    public void AddWallObject(Transform wall)
    {
        if (wall == null)
            return;

        if (walls == null)
            walls = new List<Transform>();

        if (!walls.Contains(wall))
            walls.Add(wall);
    }


    public bool SetRoomObjectsFromConfirmRoomManager(ConfirmRoomManager manager)
    {
        if (manager == null)
        {
            Debug.LogWarning("[RoomSnapshot] ConfirmRoomManager가 null입니다.");
            return false;
        }

        if (manager.ConfirmedRoomFloorObject == null || manager.ConfirmedRoomCeilingObject == null)
        {
            Debug.LogWarning("[RoomSnapshot] Confirmed room floor/ceiling이 아직 생성되지 않았습니다.");
            return false;
        }

        List<Transform> wallTransforms = new List<Transform>();
        IReadOnlyList<GameObject> wallObjects = manager.ConfirmedRoomWallObjects;

        if (wallObjects != null)
        {
            for (int i = 0; i < wallObjects.Count; i++)
            {
                if (wallObjects[i] != null)
                    wallTransforms.Add(wallObjects[i].transform);
            }
        }

        if (wallTransforms.Count == 0)
        {
            Debug.LogWarning("[RoomSnapshot] Confirmed room wall 목록이 비어 있습니다.");
            return false;
        }

        SetRoomObjects(
            manager.confirmedRoomRoot,
            manager.ConfirmedRoomFloorObject.transform,
            manager.ConfirmedRoomCeilingObject.transform,
            wallTransforms
        );

        return true;
    }

    public void ClearRoomObjects()
    {
        roomRoot = null;
        floor = null;
        ceiling = null;
        walls = new List<Transform>();
    }

    public RoomGeometrySnapshot BuildSnapshot()
    {
        RoomGeometrySnapshot snapshot = new RoomGeometrySnapshot
        {
            room_id = roomId,
            timestamp_utc = DateTime.UtcNow.ToString("o"),
            coordinate_space = "Unity world space",
            root_object_name = roomRoot != null ? roomRoot.name : null,
            root_path = roomRoot != null ? GetTransformPath(roomRoot) : null,
            has_floor = floor != null,
            has_ceiling = ceiling != null,
            assigned_wall_count = walls != null ? CountNonNullWalls() : 0
        };

        if (floor != null)
        {
            snapshot.surfaces.Add(BuildSurfaceSnapshot("floor", floor, 0));
        }
        else
        {
            Debug.LogWarning("[RoomSnapshot] floor가 설정되지 않았습니다.");
        }

        if (ceiling != null)
        {
            snapshot.surfaces.Add(BuildSurfaceSnapshot("ceiling", ceiling, 0));
        }
        else
        {
            Debug.LogWarning("[RoomSnapshot] ceiling이 설정되지 않았습니다.");
        }

        int wallIndex = 0;
        if (walls != null)
        {
            for (int i = 0; i < walls.Count; i++)
            {
                if (walls[i] == null)
                    continue;

                snapshot.surfaces.Add(BuildSurfaceSnapshot("wall", walls[i], wallIndex));
                wallIndex++;
            }
        }

        if (wallIndex == 0)
        {
            Debug.LogWarning("[RoomSnapshot] wall 목록이 비어있습니다.");
        }

        Debug.Log(
            "[RoomSnapshot] Snapshot 생성 완료\n" +
            $"root: {(roomRoot != null ? roomRoot.name : "null")}\n" +
            $"floor: {(floor != null ? 1 : 0)}, ceiling: {(ceiling != null ? 1 : 0)}, wall: {wallIndex}"
        );

        return snapshot;
    }

    private int CountNonNullWalls()
    {
        int count = 0;

        if (walls == null)
            return 0;

        for (int i = 0; i < walls.Count; i++)
        {
            if (walls[i] != null)
                count++;
        }

        return count;
    }

    private RoomSurfaceSnapshot BuildSurfaceSnapshot(string surfaceType, Transform target, int index)
    {
        RoomSurfaceSnapshot surface = new RoomSurfaceSnapshot
        {
            surface_type = surfaceType,
            index = index,
            object_name = target.name,
            object_path = GetTransformPath(target),

            position_world = SerializableVector3.From(target.position),
            rotation_world = SerializableQuaternion.From(target.rotation),
            lossy_scale = SerializableVector3.From(target.lossyScale),
            local_to_world = FurnitureCaptureSerialization.MatrixToRowMajorArray(target.localToWorldMatrix)
        };

        Renderer renderer = target.GetComponent<Renderer>();
        if (renderer != null)
        {
            surface.has_renderer_bounds = true;
            surface.bounds_center_world = SerializableVector3.From(renderer.bounds.center);
            surface.bounds_size_world = SerializableVector3.From(renderer.bounds.size);
        }

        BoxCollider boxCollider = target.GetComponent<BoxCollider>();
        if (boxCollider != null)
        {
            surface.has_box_collider = true;
            surface.box_collider_center_local = SerializableVector3.From(boxCollider.center);
            surface.box_collider_size_local = SerializableVector3.From(boxCollider.size);
        }

        MeshCollider meshCollider = target.GetComponent<MeshCollider>();
        if (meshCollider != null)
        {
            surface.has_mesh_collider = true;
            surface.mesh_collider_convex = meshCollider.convex;
        }

        Mesh mesh = null;

        MeshFilter meshFilter = target.GetComponent<MeshFilter>();
        if (meshFilter != null && meshFilter.sharedMesh != null)
        {
            mesh = meshFilter.sharedMesh;
        }
        else if (meshCollider != null && meshCollider.sharedMesh != null)
        {
            mesh = meshCollider.sharedMesh;
        }

        if (mesh != null)
        {
            surface.has_mesh = true;
            surface.mesh_vertex_count = mesh.vertexCount;
            surface.mesh_triangle_count = mesh.triangles != null ? mesh.triangles.Length / 3 : 0;

            if (includeMeshVertices)
            {
                Vector3[] vertices = mesh.vertices;

                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 worldVertex = target.TransformPoint(vertices[i]);
                    surface.mesh_vertices_world.Add(SerializableVector3.From(worldVertex));
                }
            }

            if (includeMeshTriangles)
            {
                surface.mesh_triangles = mesh.triangles;
            }
        }

        return surface;
    }

    private string GetTransformPath(Transform target)
    {
        if (target == null)
            return string.Empty;

        List<string> names = new List<string>();
        Transform current = target;

        while (current != null)
        {
            names.Add(current.name);
            current = current.parent;
        }

        names.Reverse();
        return string.Join("/", names.ToArray());
    }
}

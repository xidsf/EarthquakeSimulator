using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class FurnitureCaptureMetadata
{
    public string capture_id;
    public string timestamp_utc;
    public string source;

    public string image_file_name;
    public int image_width;
    public int image_height;

    public bool has_camera_to_world;
    public bool has_projection;

    public float[] camera_to_world;
    public float[] projection;

    public ApproxCameraIntrinsics approx_intrinsics;

    public string matrix_layout = "row-major: m00,m01,m02,m03,m10,...,m33";
    public string coordinate_space = "Unity world space";

    public RoomGeometrySnapshot room;
}

[Serializable]
public class ApproxCameraIntrinsics
{
    public float fx;
    public float fy;
    public float cx;
    public float cy;

    public string note =
        "Approximate intrinsics derived from Unity projection matrix. " +
        "For placement, prefer projection + camera_to_world matrix based ray reconstruction.";
}

[Serializable]
public class RoomGeometrySnapshot
{
    public string room_id;
    public string timestamp_utc;
    public string coordinate_space = "Unity world space";

    public string root_object_name;
    public string root_path;

    public bool has_floor;
    public bool has_ceiling;
    public int assigned_wall_count;

    public List<RoomSurfaceSnapshot> surfaces = new List<RoomSurfaceSnapshot>();
}

[Serializable]
public class RoomSurfaceSnapshot
{
    public string surface_type; // floor, ceiling, wall, unknown
    public int index;
    public string object_name;
    public string object_path;

    public SerializableVector3 position_world;
    public SerializableQuaternion rotation_world;
    public SerializableVector3 lossy_scale;

    public float[] local_to_world;

    public bool has_renderer_bounds;
    public SerializableVector3 bounds_center_world;
    public SerializableVector3 bounds_size_world;

    public bool has_box_collider;
    public SerializableVector3 box_collider_center_local;
    public SerializableVector3 box_collider_size_local;

    public bool has_mesh_collider;
    public bool mesh_collider_convex;

    public bool has_mesh;
    public int mesh_vertex_count;
    public int mesh_triangle_count;

    public List<SerializableVector3> mesh_vertices_world = new List<SerializableVector3>();
    public int[] mesh_triangles;
}

[Serializable]
public struct SerializableVector3
{
    public float x;
    public float y;
    public float z;

    public SerializableVector3(float x, float y, float z)
    {
        this.x = x;
        this.y = y;
        this.z = z;
    }

    public static SerializableVector3 From(Vector3 v)
    {
        return new SerializableVector3(v.x, v.y, v.z);
    }
}

[Serializable]
public struct SerializableQuaternion
{
    public float x;
    public float y;
    public float z;
    public float w;

    public SerializableQuaternion(float x, float y, float z, float w)
    {
        this.x = x;
        this.y = y;
        this.z = z;
        this.w = w;
    }

    public static SerializableQuaternion From(Quaternion q)
    {
        return new SerializableQuaternion(q.x, q.y, q.z, q.w);
    }
}

public static class FurnitureCaptureSerialization
{
    public static float[] MatrixToRowMajorArray(Matrix4x4 m)
    {
        return new float[]
        {
            m.m00, m.m01, m.m02, m.m03,
            m.m10, m.m11, m.m12, m.m13,
            m.m20, m.m21, m.m22, m.m23,
            m.m30, m.m31, m.m32, m.m33
        };
    }

    public static ApproxCameraIntrinsics BuildApproxIntrinsicsFromProjection(
        Matrix4x4 projection,
        int imageWidth,
        int imageHeight)
    {
        return new ApproxCameraIntrinsics
        {
            fx = projection.m00 * imageWidth * 0.5f,
            fy = projection.m11 * imageHeight * 0.5f,
            cx = (1.0f - projection.m02) * imageWidth * 0.5f,
            cy = (1.0f + projection.m12) * imageHeight * 0.5f
        };
    }
}

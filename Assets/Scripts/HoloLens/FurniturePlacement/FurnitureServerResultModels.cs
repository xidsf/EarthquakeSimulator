using System;

/// <summary>
/// Models for the furniture server API. The current server sends transform and
/// size values as float arrays, for example position: [x, y, z].
/// </summary>
[Serializable]
public class FurnitureServerStatusResponse
{
    public string scan_session_id;
    public string status;
    public string stage;
    public string message;
    public string error;
    public string stderr_tail;
    public string status_url;
    public string result_url;
    public int image_count;
    public int metadata_count;
    public int object_count;

    public bool IsProcessingStatus()
    {
        string normalized = NormalizeStatus(status);
        return normalized == "queued" ||
               normalized == "processing" ||
               normalized == "collecting_frames" ||
               normalized == "running" ||
               normalized == "pending";
    }

    public bool IsDoneStatus()
    {
        string normalized = NormalizeStatus(status);
        return normalized == "done" ||
               normalized == "completed" ||
               normalized == "success" ||
               normalized == "ready";
    }

    public bool IsFailedStatus()
    {
        string normalized = NormalizeStatus(status);
        return normalized == "failed" ||
               normalized == "error";
    }

    private static string NormalizeStatus(string raw)
    {
        return string.IsNullOrWhiteSpace(raw) ? string.Empty : raw.Trim().ToLowerInvariant();
    }
}

[Serializable]
public class FurnitureServerDetectionResponse
{
    public string scan_session_id;
    public string status;
    public string message;
    public string error;
    public FurnitureServerDetectionObject[] objects;
}

[Serializable]
public class FurnitureServerResultResponse
{
    public string job_id;
    public string scan_session_id;
    public string status;
    public string mode;
    public bool manual_placement_required;
    public bool server_auto_placement_enabled;
    public int object_count;
    public string message;
    public string error;
    public int frame_count;
    public string[] frames;
    public FurnitureServerResultObject[] objects;
    public FurnitureServerSkippedObject[] skipped_objects;
    public string detector_mode;
    public int detection_count;
    public bool used_confirmed_detections;
    public FurnitureServerPlacementItem[] placement_items;
    public string placement_metadata_source;

    public bool HasObjects => objects != null && objects.Length > 0;

    public bool IsProcessingStatus()
    {
        string normalized = NormalizeStatus(status);
        return normalized == "processing" ||
               normalized == "collecting_frames" ||
               normalized == "queued" ||
               normalized == "running" ||
               normalized == "pending";
    }

    public bool IsDoneStatus()
    {
        string normalized = NormalizeStatus(status);
        return normalized == "done" ||
               normalized == "completed" ||
               normalized == "success" ||
               normalized == "ready";
    }

    public bool IsFailedStatus()
    {
        string normalized = NormalizeStatus(status);
        return normalized == "failed" ||
               normalized == "error" ||
               normalized == "empty" ||
               normalized == "empty_result" ||
               normalized == "no_result";
    }

    private static string NormalizeStatus(string raw)
    {
        return string.IsNullOrWhiteSpace(raw) ? string.Empty : raw.Trim().ToLowerInvariant();
    }
}

[Serializable]
public class FurnitureServerDetectionObject
{
    public string id;
    public string label;
    public string raw_label;
    public string prompt;
    public float confidence;
    public float[] bbox;
}

[Serializable]
public class FurnitureServerResultObject
{
    public string id;
    public string scene_id;
    public string label;
    public string raw_label;
    public string prompt;
    public float confidence;
    public float[] bbox;

    public string mesh_url;
    public string mesh_filename;
    public string mesh_format;
    public string mesh_source;
    public string mesh_role;
    public bool mesh_texture_required;
    public string mesh_decimation_policy;
    public string mesh_cache_policy;
    public string mesh_strategy;
    public string mesh_strategy_reason;
    public string sam3d_status;
    public float sam3d_quality_score;
    public string[] quality_flags;
    public string source;
    public string detection_id;
    public string frame_id;
    public string thumbnail_url;
    public string image_url;
    public int instance_index;

    public float[] position;
    public float[] rotation;
    public float rotation_yaw_deg;

    // scale = Unity localScale (서버 사전 계산값).
    // 직접 transform.localScale에 적용하지 말 것 — WorldBoundsSizeMeters 모드에서는
    // size_world 기준으로 Unity가 bounds를 재측정해 적용하므로 scale 필드는 무시됨.
    public float[] scale;

    // glb_native_size: GLB bounding box 실측값 [x, y, z] (trimesh 측정, meters).
    // scale_source: "glb_bbox" = 서버 실측, "default_1_1_1" = fallback.
    public float[] glb_native_size;
    public string scale_source;

    public float[] size_world;
    public float[] target_size;
    public FurnitureServerSizeAdjustment size_adjustment;

    public string collider_type;
    public float[] collider_center;
    public float[] collider_size;
    // collider_source = "server_convex_hull" → collider_mesh_url을 로드해 convex MeshCollider로 붙임.
    // collider_source = "unity_runtime_box_fallback" → collider_size 기반 BoxCollider fallback.
    public string collider_source;
    public bool collider_required_from_server;
    public string collider_mesh_url;
    public string collider_mesh_filename;
    public string collider_mesh_format;
    public bool collider_convex;
    public string collider_mesh_space;
    public bool collider_apply_object_scale;
    public int collider_face_count;
    public int collider_vertex_count;
    public float[] collider_native_size;
    public float collider_inflate_ratio;
    public int collider_max_faces;
    public string collider_error;

    public string placement_mode;
    public bool manual_placement_required;
    public string placement_status;
    public float placement_confidence;

    // role: "hazard_object" | "support_surface"
    public string role;
    // placement_group: "hazard_objects" | "support_objects"
    public string placement_group;
    // placement_order: 1=support 먼저 배치, 2=hazard 나중 배치
    public int placement_order;
    public bool is_required_support_asset;

    public string physics_mode;
    public bool is_support_surface;
    public bool is_fixed;
    public bool simulation_enabled;
    public string spawn_policy;
    public string support_id;
    public string rigidbody_mode;
    public bool use_gravity;
    public bool earthquake_simulation;

    // mass 정보 (서버 참조표 기반 추정값).
    public float mass_kg;
    public float mass_lbs;
    public string mass_source;
    public string mass_reference_item;
    public string mass_confidence;

    public string fixture_type;
    public float fixture_confidence;
    public string fixture_reason;
    public float wall_contact_ratio;
    public float nearest_wall_distance;

    public float[] wall_normal;
    public float[] wall_anchor;
    public string wall_id;
    public float[] hinge_axis;
    public float[] hinge_anchor_local;
    public string joint_type;
    public bool joint_required;
    public string connected_wall_policy;
    public float[] placement_pixel;
    public float width_estimate;
}

[Serializable]
public class FurnitureServerSizeAdjustment
{
    public string reason;
    public float[] raw_size_world;
    public string mesh_long_axis;
    public float min_width_m;
    public float min_length_m;
    public float min_height_m;
}

[Serializable]
public class FurnitureServerSkippedObject
{
    public string id;
    public string label;
    public string raw_label;
    public string reason;
    public string status;
    public string hint;
    public float[] bbox;
    public float confidence;
    public string source;
    public string detection_id;
    public string frame_id;
    public string image_filename;
    public string metadata_filename;
    public int instance_index;
    public string mesh_strategy;
    public string mesh_strategy_reason;
    public string sam3d_status;
    public float sam3d_quality_score;
    public string[] quality_flags;
}

[Serializable]
public class FurnitureServerPlacementItem
{
    public string label;
    public float[] bbox;
    public string frame_id;
    public string image_filename;
    public string metadata_filename;
    public float confidence;
    public string source;
    public string detection_id;
    public string raw_label;
    public int instance_index;
}

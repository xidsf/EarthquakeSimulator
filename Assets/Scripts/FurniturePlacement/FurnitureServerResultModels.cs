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
    public string[] skipped_objects;
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
    public string mesh_strategy;
    public string mesh_strategy_reason;
    public string sam3d_status;
    public string source;
    public string detection_id;
    public string frame_id;
    public string thumbnail_url;
    public string image_url;
    public int instance_index;

    public float[] position;
    public float[] rotation;
    public float rotation_yaw_deg;
    public float[] scale;

    public float[] size_world;
    public float[] target_size;

    public string collider_type;
    public float[] collider_center;
    public float[] collider_size;

    public string placement_mode;
    public bool manual_placement_required;
    public string placement_status;
    public float placement_confidence;

    public string physics_mode;
    public bool is_support_surface;
    public bool is_fixed;
    public bool simulation_enabled;
    public string spawn_policy;
    public string support_id;
    public string rigidbody_mode;
    public bool use_gravity;
    public bool earthquake_simulation;

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

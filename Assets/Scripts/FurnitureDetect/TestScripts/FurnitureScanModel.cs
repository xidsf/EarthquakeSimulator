using System;
using System.Collections.Generic;

[Serializable]
public class FurnitureScanResponse
{
    public string job_id;
    public string status;
}

[Serializable]
public class FurnitureResultResponse
{
    public string job_id;
    public string scan_session_id;
    public string status;
    public string mode;

    public int frame_count;
    public string[] frames;

    public List<FurnitureObjectResult> objects;
}

[Serializable]
public class FurnitureObjectResult
{
    public string id;
    public string scene_id;
    public string label;
    public string raw_label;
    public string prompt;
    public float confidence;

    // Server may send decimal bbox values, so this must be float[].
    public float[] bbox;

    public string mesh_url;
    public string mesh_filename;
    public string mesh_source;
    public string mesh_strategy;
    public string mesh_strategy_reason;
    public string sam3d_status;

    public float[] position;
    public float[] rotation;
    public float rotation_yaw_deg;
    public float[] scale;

    // New server field. Prefer this over target_size/collider_size for Unity scaling.
    public float[] size_world;
    public float[] target_size;

    public string collider_type;
    public float[] collider_center;
    public float[] collider_size;

    public string placement_mode;
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

    // Wall-hanging hazard fields for picture_frame / wall_clock.
    public float[] wall_normal;
    public float[] wall_anchor;
    public string wall_id;
    public float[] hinge_axis;
    public float[] hinge_anchor_local;
    public string joint_type;
    public bool joint_required;
    public string connected_wall_policy;
}

[Serializable]
public class ScanSessionStartResponse
{
    public string scan_session_id;
    public string status;
}

[Serializable]
public class ScanFrameUploadResponse
{
    public string scan_session_id;
    public string frame_id;
    public string status;
    public string image_url;
}

[Serializable]
public class ProcessScanResponse
{
    public string scan_session_id;
    public string status;
    public string stage;
    public int frame_count;
    public int object_count;
    public string status_url;
    public string result_url;
    public string error;
    public string stderr_tail;
}

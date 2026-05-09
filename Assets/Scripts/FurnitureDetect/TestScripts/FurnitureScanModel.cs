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
    public string label;
    public string prompt;
    public float confidence;

    public int[] bbox;
    public string mesh_url;

    public float[] position;
    public float[] rotation;
    public float[] scale;

    public string collider_type;
    public float[] collider_center;
    public float[] collider_size;
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
    public int frame_count;
    public string result_url;
}
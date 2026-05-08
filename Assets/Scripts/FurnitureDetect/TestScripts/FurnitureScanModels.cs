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
    public string status;
    public List<FurnitureObjectResult> objects;
}

[Serializable]
public class FurnitureObjectResult
{
    public string id;
    public string label;
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
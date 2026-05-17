using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class SimulationInput
{
    public string session_id;
    public RoomData room;
    public List<FurnitureData> furnitures; // [수정됨] json 키와 맞추기 위해 's' 추가!
}

[Serializable]
public class RoomData
{
    public List<WallData> walls;
    public WallData floor;   // [추가됨] JSON의 바닥 데이터
    public WallData ceiling; // [추가됨] JSON의 천장 데이터
}

[Serializable]
public class WallData
{
    public float[] center;
    public float[] size;
    public float[] euler;

    public Vector3 GetCenter() => new Vector3(center[0], center[1], center[2]);
    public Vector3 GetSize() => new Vector3(size[0], size[1], size[2]);
    public Vector3 GetEuler() => new Vector3(euler[0], euler[1], euler[2]);
}

[Serializable]
public class FurnitureData
{
    public string furniture_id;
    public int instance_index;
    public float[] position;
    public float[] rotation;
    public float[] scale;
    public bool is_fixed;
    public bool is_floor_contactless;
    public string label;

    public Vector3 GetPosition() => new Vector3(position[0], position[1], position[2]);
    public Vector3 GetScale() => new Vector3(scale[0], scale[1], scale[2]);
    public Quaternion GetRotation()
    {
        if (rotation != null && rotation.Length == 4)
            return new Quaternion(rotation[0], rotation[1], rotation[2], rotation[3]);
        return Quaternion.identity;
    }
}
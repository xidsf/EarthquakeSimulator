using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class SimulationInput
{
    public string session_id;
    public RoomData room;
    public List<FurnitureData> furnitures;
    public BuildingData building;
}

[Serializable]
public class BuildingData
{
    public string building_type = "Apartment";
    public int floor = 1;
    public int total_floors = 0;
    public string piloti = "Unknown";
}

[Serializable]
public class RoomData
{
    public List<WallData> walls;
    public WallData floor;
    public WallData ceiling;
    public EntranceData entrance;
}

[Serializable]
public class EntranceData
{
    public float[] position;
    public float radius = 1.5f;

    public Vector3 GetPosition() => ToVector3(position, Vector3.zero);

    private static Vector3 ToVector3(float[] values, Vector3 fallback)
    {
        return values != null && values.Length >= 3
            ? new Vector3(values[0], values[1], values[2])
            : fallback;
    }
}

[Serializable]
public class WallData
{
    public float[] center;
    public float[] size;
    public float[] euler;

    public Vector3 GetCenter() => ToVector3(center, Vector3.zero);
    public Vector3 GetSize() => ToVector3(size, Vector3.one);
    public Vector3 GetEuler() => ToVector3(euler, Vector3.zero);

    private static Vector3 ToVector3(float[] values, Vector3 fallback)
    {
        return values != null && values.Length >= 3
            ? new Vector3(values[0], values[1], values[2])
            : fallback;
    }
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
    public float mass_kg;
    public string label;
    public string mesh_file;
    public string collider_file;

    public Vector3 GetPosition() => ToVector3(position, Vector3.zero);
    public Vector3 GetScale() => ToVector3(scale, Vector3.one);

    public Quaternion GetRotation()
    {
        return rotation != null && rotation.Length >= 4
            ? new Quaternion(rotation[0], rotation[1], rotation[2], rotation[3])
            : Quaternion.identity;
    }

    private static Vector3 ToVector3(float[] values, Vector3 fallback)
    {
        return values != null && values.Length >= 3
            ? new Vector3(values[0], values[1], values[2])
            : fallback;
    }
}

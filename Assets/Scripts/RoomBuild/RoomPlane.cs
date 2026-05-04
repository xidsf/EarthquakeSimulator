using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// AR Foundation의 ARPlane과 Scene Understanding의 SceneQuad를 통합하여 다루기 위한 데이터 구조
/// </summary>
public class RoomPlane
{
    public string id;
    public Vector3 center;
    public Quaternion rotation;
    public Vector2 size;
    public List<Vector3> boundary = new List<Vector3>(); // Local coordinates
    public Vector3 normal;
    public PlaneKind kind;

    public enum PlaneKind { Wall, Floor, Ceiling, Platform, Unknown }

    public Matrix4x4 GetLocalToWorldMatrix()
    {
        return Matrix4x4.TRS(center, rotation, Vector3.one);
    }
}

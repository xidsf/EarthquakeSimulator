using System;
using UnityEngine;

public enum FurniturePlacementState
{
    None,
    Registered,
    Moved,
    Confirmed,
    Invalid
}

[Serializable]
public class FurniturePlacementMetadata
{
    [Tooltip("개별 가구 고유 ID입니다. 비어 있으면 FurniturePlacementManager가 자동 생성합니다.")]
    public string furnitureId;

    [Tooltip("선택 metadata입니다. 예: chair, table, sofa. 현재 MVP에서는 prefab 선택용이 아니라 종류 기록용입니다.")]
    public string label;

    [Tooltip("선택 metadata입니다. 예: server, debug, manual.")]
    public string source;

    [Tooltip("로그에 표시할 이름입니다. 비어 있으면 label 또는 GameObject 이름을 사용합니다.")]
    public string displayName;

    [Tooltip("서버 /result objects[].id 원본입니다. furnitureId와 같을 수 있습니다.")]
    public string serverObjectId;

    [Tooltip("서버 /result objects[].mesh_url 원본입니다.")]
    public string meshUrl;

    [Tooltip("서버 /result objects[].confidence 원본입니다.")]
    public float confidence;

    [Tooltip("서버 /result objects[].detection_id 원본입니다.")]
    public string detectionId;

    [Tooltip("서버 /result objects[].frame_id 원본입니다.")]
    public string frameId;

    [Tooltip("서버 /result objects[].placement_mode 원본입니다.")]
    public string placementMode;

    [Tooltip("서버 /result objects[].raw_label 원본입니다.")]
    public string rawLabel;
    public string thumbnailUrl;
    public string physicsMode;
    public bool simulationEnabled = true;
    public string rigidbodyMode;
    public bool useGravity;

    public FurniturePlacementMetadata()
    {
    }

    public FurniturePlacementMetadata(string furnitureId, string label, string source, string displayName)
    {
        this.furnitureId = furnitureId;
        this.label = label;
        this.source = source;
        this.displayName = displayName;
    }

    public static FurniturePlacementMetadata CreateDefault(GameObject target, string source = "server")
    {
        string objectName = target != null ? target.name : "Furniture";

        return new FurniturePlacementMetadata
        {
            furnitureId = Guid.NewGuid().ToString("N"),
            label = string.Empty,
            source = source,
            displayName = objectName
        };
    }

    public void EnsureDefaults(GameObject target, string defaultSource = "server")
    {
        if (string.IsNullOrWhiteSpace(furnitureId))
        {
            furnitureId = Guid.NewGuid().ToString("N");
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            source = defaultSource;
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            if (!string.IsNullOrWhiteSpace(label))
            {
                displayName = label;
            }
            else if (target != null)
            {
                displayName = target.name;
            }
            else
            {
                displayName = "Furniture";
            }
        }
    }
}

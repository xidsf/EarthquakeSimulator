using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// Î∞∞Ïπò ?ÑÎ£å ?úÏ†ê??Î∞∞Ïπò??Í∞ÄÍµ¨Îì§Í≥??ïÏ†ï??Î∞??ïÏÉÅ??confirmedRoomRoot Î°úÏª¨ Ï¢åÌëúÎ°?/// ÏßÅÎ†¨?îÌï¥ ?úÎ≤ÑÎ°?Î≥¥ÎÇº JSON(SimulationInputDocument)???ùÏÑ±?©Îãà??
///
/// Ï¢åÌëú Í∑úÏïΩ: Î™®Îì† transform Í∞íÏ? confirmedRoomRoot Î°úÏª¨ Í∏∞Ï??ÖÎãà?? roomRootÍ∞Ä
/// ?®ÏúÑ ?§Ï???Í±∞Ïùò 1,1,1)?¥Îùº???ºÎ∞ò?ÅÏù∏ ?ÑÏ†úÎ•??¨Ïö©?©Îãà??
/// </summary>
public static class SimulationInputBuilder
{
    public static SimulationInputDocument Build(
        string sessionId,
        IReadOnlyList<PlacedFurniture> placedFurnitures,
        ConfirmRoomManager confirmRoomManager)
    {
        SimulationInputDocument doc = new SimulationInputDocument
        {
            session_id = sessionId
        };

        Transform roomRoot = confirmRoomManager != null ? confirmRoomManager.confirmedRoomRoot : null;
        if (roomRoot == null)
        {
            Debug.LogWarning("[SimulationInputBuilder] confirmedRoomRoot is null. Falling back to world space coordinates.");
        }

        doc.room = BuildRoom(confirmRoomManager, roomRoot);
        doc.furnitures = BuildFurnitures(placedFurnitures, roomRoot);

        // BuildingInfoController?êÏÑú ?ÖÎ†•??Í±¥Î¨º ?ïÎ≥¥Î•??®Íªò ?ÑÏÜ°. ÎØ∏ÏûÖ????        // SimulationBuildingInfo Í∏∞Î≥∏Í∞?Apartment/1/0/Unknown)???†Ï??úÎã§.
        if (ClientBuildingInfo.HasValue)
        {
            doc.building = ClientBuildingInfo.ToContract();
        }

        return doc;
    }

    public static string BuildJson(
        string sessionId,
        IReadOnlyList<PlacedFurniture> placedFurnitures,
        ConfirmRoomManager confirmRoomManager)
    {
        SimulationInputDocument doc = Build(sessionId, placedFurnitures, confirmRoomManager);
        return SerializeClientInput(doc);
    }

    private static string SerializeClientInput(SimulationInputDocument doc)
    {
        JObject root = JObject.FromObject(doc);
        JArray furnitures = root["furnitures"] as JArray;
        if (furnitures != null)
        {
            foreach (JToken token in furnitures)
            {
                if (token is not JObject furniture) continue;
                furniture.Remove("mass_kg");
                furniture.Remove("mesh_file");
                furniture.Remove("collider_file");
            }
        }

        return root.ToString(Formatting.None);
    }

    private static SimulationRoomGeometry BuildRoom(ConfirmRoomManager manager, Transform roomRoot)
    {
        SimulationRoomGeometry room = new SimulationRoomGeometry();
        if (manager == null) return room;

        if (manager.ConfirmedRoomWallObjects != null)
        {
            foreach (GameObject wall in manager.ConfirmedRoomWallObjects)
            {
                if (wall == null) continue;
                if (TryBuildBox(wall, roomRoot, out SimulationBox box)) room.walls.Add(box);
            }
        }

        if (manager.ConfirmedRoomFloorObject != null &&
            TryBuildBox(manager.ConfirmedRoomFloorObject, roomRoot, out SimulationBox floorBox))
        {
            room.floor = floorBox;
        }

        if (manager.ConfirmedRoomCeilingObject != null &&
            TryBuildBox(manager.ConfirmedRoomCeilingObject, roomRoot, out SimulationBox ceilingBox))
        {
            room.ceiling = ceilingBox;
        }

        if (manager.hasEntrance && TryGetEntranceWorldPosition(manager, out Vector3 entranceWorld))
        {
            Vector3 entranceLocal = roomRoot != null
                ? roomRoot.InverseTransformPoint(entranceWorld)
                : entranceWorld;

            room.entrance = new SimulationEntrance
            {
                position = ToArray(entranceLocal),
                radius = manager.entranceRadiusMeters > 0f ? manager.entranceRadiusMeters : 1.5f
            };
        }

        return room;
    }

    // ?ÖÍµ¨ ?ÑÏπò???åÏä§??ConfirmRoomManager.entranceMarkerRoot ?òÏúÑ???§Ï†ú ÎßàÏª§
    // ?§Î∏å?ùÌä∏(ConfirmedRoom_EntrancePoint) ?ÑÏπò?? ÎßàÏª§Í∞Ä ?ÜÏúºÎ©?entrancePointWorldÎ°??¥Î∞±.
    private static bool TryGetEntranceWorldPosition(ConfirmRoomManager manager, out Vector3 worldPosition)
    {
        worldPosition = Vector3.zero;
        if (manager == null) return false;

        Transform markerRoot = manager.entranceMarkerRoot;
        if (markerRoot != null && markerRoot.childCount > 0)
        {
            Transform marker = markerRoot.Find("ConfirmedRoom_EntrancePoint");
            if (marker == null) marker = markerRoot.GetChild(0);
            if (marker != null)
            {
                worldPosition = marker.position;
                return true;
            }
        }

        if (manager.entrancePointWorld != Vector3.zero)
        {
            worldPosition = manager.entrancePointWorld;
            return true;
        }

        return false;
    }

    private static bool TryBuildBox(GameObject source, Transform roomRoot, out SimulationBox box)
    {
        box = null;
        if (source == null) return false;

        BoxCollider boxCollider = source.GetComponent<BoxCollider>();
        if (boxCollider == null) boxCollider = source.GetComponentInChildren<BoxCollider>(true);

        Vector3 worldCenter;
        Vector3 worldSize;
        Quaternion worldRotation;

        if (boxCollider != null)
        {
            Transform t = boxCollider.transform;
            worldCenter = t.TransformPoint(boxCollider.center);
            worldSize = Vector3.Scale(boxCollider.size, AbsScale(t.lossyScale));
            worldRotation = t.rotation;
        }
        else
        {
            Collider anyCollider = source.GetComponentInChildren<Collider>(true);
            if (anyCollider == null) return false;
            // Îπ?Box collider??world AABB Í∑ºÏÇ¨(?åÏ†Ñ ?ïÎ≥¥ ?ÜÏùå).
            Bounds b = anyCollider.bounds;
            worldCenter = b.center;
            worldSize = b.size;
            worldRotation = Quaternion.identity;
        }

        Vector3 localCenter;
        Quaternion localRotation;
        Vector3 localSize;

        if (roomRoot != null)
        {
            localCenter = roomRoot.InverseTransformPoint(worldCenter);
            localRotation = Quaternion.Inverse(roomRoot.rotation) * worldRotation;
            localSize = SafeDivide(worldSize, AbsScale(roomRoot.lossyScale));
        }
        else
        {
            localCenter = worldCenter;
            localRotation = worldRotation;
            localSize = worldSize;
        }

        box = new SimulationBox
        {
            center = ToArray(localCenter),
            size = ToArray(localSize),
            euler = ToArray(localRotation.eulerAngles)
        };
        return true;
    }

    private static List<SimulationFurnitureInstance> BuildFurnitures(
        IReadOnlyList<PlacedFurniture> placedFurnitures,
        Transform roomRoot)
    {
        List<SimulationFurnitureInstance> list = new List<SimulationFurnitureInstance>();
        if (placedFurnitures == null) return list;

        Dictionary<string, int> instanceCounter = new Dictionary<string, int>();

        foreach (PlacedFurniture furniture in placedFurnitures)
        {
            if (furniture == null) continue;

            string id = furniture.FurnitureId;
            if (string.IsNullOrWhiteSpace(id)) id = furniture.name;

            if (!instanceCounter.TryGetValue(id, out int index)) index = 0;
            instanceCounter[id] = index + 1;

            Transform ft = furniture.transform;
            Vector3 localPos;
            Quaternion localRot;
            Vector3 localScale;

            if (roomRoot != null)
            {
                localPos = roomRoot.InverseTransformPoint(ft.position);
                localRot = Quaternion.Inverse(roomRoot.rotation) * ft.rotation;
                localScale = SafeDivide(AbsScale(ft.lossyScale), AbsScale(roomRoot.lossyScale));
            }
            else
            {
                localPos = ft.position;
                localRot = ft.rotation;
                localScale = AbsScale(ft.lossyScale);
            }

            list.Add(new SimulationFurnitureInstance
            {
                furniture_id = id,
                source_object_id = furniture.SourceObjectId,
                vlm_label = id,
                category = furniture.Label,
                instance_index = index,
                position = ToArray(localPos),
                rotation = ToArray(localRot),
                scale = ToArray(localScale),
                is_fixed = furniture.IsFixed,
                is_floor_contactless = furniture.IsFloorContactless,
                label = furniture.Label
            });
        }

        return list;
    }

    private static Vector3 AbsScale(Vector3 scale)
    {
        return new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
    }

    private static Vector3 SafeDivide(Vector3 value, Vector3 divisor)
    {
        return new Vector3(
            divisor.x != 0f ? value.x / divisor.x : value.x,
            divisor.y != 0f ? value.y / divisor.y : value.y,
            divisor.z != 0f ? value.z / divisor.z : value.z);
    }

    private static float[] ToArray(Vector3 v) => new[] { v.x, v.y, v.z };
    private static float[] ToArray(Quaternion q) => new[] { q.x, q.y, q.z, q.w };
}

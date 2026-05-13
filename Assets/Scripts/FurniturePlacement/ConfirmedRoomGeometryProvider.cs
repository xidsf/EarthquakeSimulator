using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// ConfirmRoomManager가 완성한 벽/바닥/천장 오브젝트를 보관하고,
/// 등록된 가구의 이동 결과가 방 내부에 있는지 검증합니다.
///
/// 중요:
/// - 이 클래스는 랜덤 배치를 수행하지 않습니다.
/// - 랜덤 위치 샘플링은 Debug/DebugRoomRandomPositionSampler.cs에서만 수행합니다.
/// - 실사용에서는 서버/인식 파이프라인이 만든 가구 오브젝트를 검증하고 보정하는 역할만 합니다.
/// </summary>
public class ConfirmedRoomGeometryProvider : MonoBehaviour
{
    [Header("Optional Source")]
    [Tooltip("비워두어도 됩니다. BindFromConfirmRoomManager() 호출 시 직접 전달할 수도 있습니다.")]
    public ConfirmRoomManager confirmRoomManager;

    [Tooltip("OnEnable 시 confirmRoomManager가 있으면 자동으로 현재 ConfirmedRoom 정보를 바인딩합니다.")]
    public bool autoBindFromConfirmRoomManagerOnEnable = false;

    [Header("Pose Correction")]
    [Tooltip("이동 종료/등록 시 가구를 바닥 위에 맞춥니다.")]
    public bool snapFurnitureToFloor = true;

    [Tooltip("이동 종료/등록 시 X/Z 회전을 제거하고 Yaw만 유지합니다.")]
    public bool keepFurnitureUpright = true;

    [Tooltip("천장이 존재할 때 가구 bounds가 천장을 뚫는지 검사합니다.")]
    public bool validateCeilingHeight = true;

    [Tooltip("벽과의 충돌 판정 전에 가구 bounds를 이 정도 축소합니다. 아주 작은 접촉으로 invalid 처리되는 것을 줄입니다.")]
    public float boundsShrinkForWallCheck = 0.01f;

    [Tooltip("방 내부 판정 ray를 바닥 위로 띄울 높이입니다.")]
    public float insideRoomRayHeightFromFloor = 0.25f;

    [Tooltip("Point inside 검사 시 벽 내부에 찍힌 후보를 제외하기 위한 작은 반경입니다.")]
    public float wallOverlapSphereRadius = 0.025f;

    [Header("Placement Assist")]
    [Tooltip("If a moved or rotated furniture object overlaps a wall or another furniture object, try to push it to the nearest valid pose before restoring the previous pose.")]
    public bool resolvePoseOnValidationFailure = true;

    [Min(1)] public int collisionResolutionIterations = 8;
    [Min(0.0f)] public float collisionResolutionSkinMeters = 0.015f;
    [Min(0.05f)] public float maxCollisionResolutionStepMeters = 0.35f;
    [Min(0.05f)] public float nearbyInsideSearchStepMeters = 0.05f;
    [Min(0.05f)] public float nearbyInsideSearchMaxRadiusMeters = 0.8f;

    [Header("Runtime State - Read Only")]
    [SerializeField] private bool initialized;
    [SerializeField] private int wallColliderCount;
    [SerializeField] private GameObject floorObject;
    [SerializeField] private GameObject ceilingObject;

    private readonly List<BoxCollider> wallColliders = new List<BoxCollider>();
    private readonly HashSet<Collider> wallColliderSet = new HashSet<Collider>();
    private readonly List<PlacedFurniture> placedFurnitures = new List<PlacedFurniture>();

    private BoxCollider floorCollider;
    private Collider ceilingCollider;
    private Bounds roomBounds;

    public bool IsInitialized => initialized;
    public Bounds RoomBounds => roomBounds;
    public float FloorTopY => floorCollider != null ? floorCollider.bounds.max.y : 0f;
    public float CeilingBottomY => ceilingCollider != null ? ceilingCollider.bounds.min.y : float.PositiveInfinity;
    public IReadOnlyList<BoxCollider> WallColliders => wallColliders;

    private void OnEnable()
    {
        if (autoBindFromConfirmRoomManagerOnEnable && confirmRoomManager != null)
        {
            BindFromConfirmRoomManager(confirmRoomManager);
        }
    }

    public bool BindFromConfirmRoomManager(ConfirmRoomManager source)
    {
        if (source == null)
        {
            Debug.LogWarning("[ConfirmedRoomGeometryProvider] ConfirmRoomManager is null.");
            return false;
        }

        confirmRoomManager = source;
        return SetConfirmedRoom(
            source.ConfirmedRoomWallObjects,
            source.ConfirmedRoomFloorObject,
            source.ConfirmedRoomCeilingObject
        );
    }

    public bool SetConfirmedRoom(IEnumerable<GameObject> wallObjects, GameObject floor, GameObject ceiling)
    {
        wallColliders.Clear();
        wallColliderSet.Clear();

        if (wallObjects != null)
        {
            foreach (GameObject wallObject in wallObjects)
            {
                if (wallObject == null)
                {
                    continue;
                }

                BoxCollider wallCollider = wallObject.GetComponent<BoxCollider>();
                if (wallCollider == null)
                {
                    wallCollider = wallObject.GetComponentInChildren<BoxCollider>(true);
                }

                if (wallCollider != null)
                {
                    wallColliders.Add(wallCollider);
                    wallColliderSet.Add(wallCollider);
                }
            }
        }

        floorObject = floor;
        ceilingObject = ceiling;
        floorCollider = floorObject != null ? floorObject.GetComponent<BoxCollider>() : null;
        ceilingCollider = ceilingObject != null ? ceilingObject.GetComponent<Collider>() : null;

        wallColliderCount = wallColliders.Count;

        if (wallColliders.Count == 0 || floorCollider == null)
        {
            initialized = false;
            Debug.LogWarning($"[ConfirmedRoomGeometryProvider] Initialization failed. walls:{wallColliders.Count}, floor:{floorCollider != null}");
            return false;
        }

        roomBounds = wallColliders[0].bounds;
        for (int i = 1; i < wallColliders.Count; i++)
        {
            roomBounds.Encapsulate(wallColliders[i].bounds);
        }

        initialized = true;
        Debug.Log($"[ConfirmedRoomGeometryProvider] Room geometry initialized. walls:{wallColliders.Count}, floor:{floorObject.name}, ceiling:{(ceilingObject != null ? ceilingObject.name : "None")}");
        return true;
    }

    public void RegisterPlacedFurniture(PlacedFurniture furniture)
    {
        if (furniture == null)
        {
            return;
        }

        if (!placedFurnitures.Contains(furniture))
        {
            placedFurnitures.Add(furniture);
        }
    }

    public void UnregisterPlacedFurniture(PlacedFurniture furniture)
    {
        if (furniture == null)
        {
            return;
        }

        placedFurnitures.Remove(furniture);
    }

    public bool ValidateAndCorrectFurniturePose(PlacedFurniture furniture, out string reason)
    {
        reason = string.Empty;

        if (furniture == null)
        {
            reason = "Furniture is null.";
            return false;
        }

        if (!initialized)
        {
            reason = "Room geometry is not initialized.";
            return false;
        }

        if (keepFurnitureUpright)
        {
            ForceUpright(furniture.transform);
        }

        if (snapFurnitureToFloor)
        {
            SnapFurnitureToFloor(furniture);
        }

        if (IsFurniturePoseValid(furniture, out reason))
        {
            return true;
        }

        if (resolvePoseOnValidationFailure && TryResolveFurniturePose(furniture, out reason))
        {
            return true;
        }

        return false;
    }

    private bool IsFurniturePoseValid(PlacedFurniture furniture, out string reason)
    {
        reason = string.Empty;

        if (!TryGetFurnitureBounds(furniture, out Bounds furnitureBounds))
        {
            reason = "Furniture has no valid collider or renderer bounds.";
            return false;
        }

        if (!AreFurnitureBoundsInsideRoom(furnitureBounds))
        {
            reason = "Furniture is outside the confirmed room boundary.";
            return false;
        }

        if (validateCeilingHeight && ceilingCollider != null && furnitureBounds.max.y > ceilingCollider.bounds.min.y)
        {
            reason = "Furniture is higher than the ceiling.";
            return false;
        }

        if (IntersectsAnyWall(furniture, furnitureBounds, out Collider wall))
        {
            reason = $"Furniture intersects wall: {wall.name}.";
            return false;
        }

        if (IntersectsAnyOtherFurniture(furniture, out PlacedFurniture other))
        {
            reason = $"Furniture intersects other furniture: {other.DisplayName}.";
            return false;
        }

        return true;
    }

    private bool TryResolveFurniturePose(PlacedFurniture furniture, out string reason)
    {
        reason = string.Empty;

        Vector3 originalPosition = furniture.transform.position;
        Quaternion originalRotation = furniture.transform.rotation;

        for (int i = 0; i < collisionResolutionIterations; i++)
        {
            if (keepFurnitureUpright)
            {
                ForceUpright(furniture.transform);
            }

            if (snapFurnitureToFloor)
            {
                SnapFurnitureToFloor(furniture);
            }

            if (IsFurniturePoseValid(furniture, out reason))
            {
                return true;
            }

            bool changed = false;

            if (TryGetFurnitureBounds(furniture, out Bounds bounds) && !AreFurnitureBoundsInsideRoom(bounds))
            {
                changed |= TryMoveFurnitureToNearbyInsideRoom(furniture);
            }

            changed |= TryResolveFurniturePenetrations(furniture);

            if (!changed)
            {
                break;
            }
        }

        if (snapFurnitureToFloor)
        {
            SnapFurnitureToFloor(furniture);
        }

        if (IsFurniturePoseValid(furniture, out reason))
        {
            return true;
        }

        furniture.transform.SetPositionAndRotation(originalPosition, originalRotation);
        if (keepFurnitureUpright)
        {
            ForceUpright(furniture.transform);
        }

        if (snapFurnitureToFloor)
        {
            SnapFurnitureToFloor(furniture);
        }

        IsFurniturePoseValid(furniture, out reason);
        return false;
    }

    private bool TryMoveFurnitureToNearbyInsideRoom(PlacedFurniture furniture)
    {
        Vector3 startPosition = furniture.transform.position;
        Quaternion startRotation = furniture.transform.rotation;
        float step = Mathf.Max(0.01f, nearbyInsideSearchStepMeters);
        float maxRadius = Mathf.Max(step, nearbyInsideSearchMaxRadiusMeters);
        Vector3 roomCenter = new Vector3(roomBounds.center.x, startPosition.y, roomBounds.center.z);
        Vector3 towardCenter = Vector3.ProjectOnPlane(roomCenter - startPosition, Vector3.up);

        if (TryApplyInsideRoomCandidate(furniture, startPosition, startRotation))
        {
            return true;
        }

        if (towardCenter.sqrMagnitude > 0.0001f)
        {
            Vector3 direction = towardCenter.normalized;
            for (float radius = step; radius <= maxRadius + 0.0001f; radius += step)
            {
                if (TryApplyInsideRoomCandidate(furniture, startPosition + direction * radius, startRotation))
                {
                    return true;
                }
            }
        }

        const int directionCount = 16;
        for (float radius = step; radius <= maxRadius + 0.0001f; radius += step)
        {
            for (int i = 0; i < directionCount; i++)
            {
                float angle = Mathf.PI * 2f * i / directionCount;
                Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                if (TryApplyInsideRoomCandidate(furniture, startPosition + offset, startRotation))
                {
                    return true;
                }
            }
        }

        furniture.transform.SetPositionAndRotation(startPosition, startRotation);
        return false;
    }

    private bool TryApplyInsideRoomCandidate(PlacedFurniture furniture, Vector3 candidatePosition, Quaternion rotation)
    {
        furniture.transform.SetPositionAndRotation(candidatePosition, rotation);

        if (keepFurnitureUpright)
        {
            ForceUpright(furniture.transform);
        }

        if (snapFurnitureToFloor)
        {
            SnapFurnitureToFloor(furniture);
        }

        return TryGetFurnitureBounds(furniture, out Bounds candidateBounds) &&
               AreFurnitureBoundsInsideRoom(candidateBounds);
    }

    private bool TryResolveFurniturePenetrations(PlacedFurniture furniture)
    {
        Vector3 totalMove = Vector3.zero;

        foreach (BoxCollider wallCollider in wallColliders)
        {
            if (wallCollider == null || !wallCollider.enabled)
            {
                continue;
            }

            totalMove += ComputeSeparationMove(furniture, wallCollider);
        }

        foreach (PlacedFurniture other in placedFurnitures)
        {
            if (other == null || other == furniture)
            {
                continue;
            }

            Collider[] otherColliders = other.GetActivePlacementColliders();
            if (otherColliders == null)
            {
                continue;
            }

            foreach (Collider otherCollider in otherColliders)
            {
                if (otherCollider == null || !otherCollider.enabled)
                {
                    continue;
                }

                totalMove += ComputeSeparationMove(furniture, otherCollider);
            }
        }

        totalMove = Vector3.ProjectOnPlane(totalMove, Vector3.up);
        float maxStep = Mathf.Max(0.01f, maxCollisionResolutionStepMeters);
        if (totalMove.magnitude > maxStep)
        {
            totalMove = totalMove.normalized * maxStep;
        }

        if (totalMove.sqrMagnitude <= 0.000001f)
        {
            return false;
        }

        furniture.transform.position += totalMove;
        return true;
    }

    private Vector3 ComputeSeparationMove(PlacedFurniture furniture, Collider targetCollider)
    {
        Vector3 move = Vector3.zero;
        Collider[] colliders = furniture.GetActivePlacementColliders();
        if (colliders == null || targetCollider == null || !targetCollider.enabled)
        {
            return move;
        }

        foreach (Collider collider in colliders)
        {
            if (collider == null || !collider.enabled || !collider.bounds.Intersects(targetCollider.bounds))
            {
                continue;
            }

            if (Physics.ComputePenetration(
                    collider,
                    collider.transform.position,
                    collider.transform.rotation,
                    targetCollider,
                    targetCollider.transform.position,
                    targetCollider.transform.rotation,
                    out Vector3 direction,
                    out float distance))
            {
                move += direction * (distance + collisionResolutionSkinMeters);
            }
        }

        return move;
    }

    public bool IsInsideRoom(Vector3 point)
    {
        if (!initialized)
        {
            return false;
        }

        float y = floorCollider.bounds.max.y + insideRoomRayHeightFromFloor;
        Vector3 origin = new Vector3(point.x, y, point.z);

        if (IsPointInsideWall(origin))
        {
            return false;
        }

        float rayDistance = Mathf.Max(0.1f, roomBounds.max.x - origin.x + 2f);
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.right, rayDistance);

        int wallHitCount = hits
            .Where(hit => hit.collider != null && wallColliderSet.Contains(hit.collider))
            .Select(hit => hit.collider)
            .Distinct()
            .Count();

        return wallHitCount % 2 == 1;
    }

    public bool TryGetFurnitureBounds(PlacedFurniture furniture, out Bounds bounds)
    {
        bounds = default;

        if (furniture == null)
        {
            return false;
        }

        Collider[] colliders = furniture.GetActivePlacementColliders();
        bool hasBounds = false;

        if (colliders != null)
        {
            foreach (Collider collider in colliders)
            {
                if (collider == null || !collider.enabled)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = collider.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(collider.bounds);
                }
            }
        }

        if (hasBounds)
        {
            return true;
        }

        Renderer[] renderers = furniture.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null || !renderer.enabled)
            {
                continue;
            }

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return hasBounds;
    }

    private void ForceUpright(Transform target)
    {
        Vector3 euler = target.eulerAngles;
        target.rotation = Quaternion.Euler(0f, euler.y, 0f);
    }

    private void SnapFurnitureToFloor(PlacedFurniture furniture)
    {
        if (floorCollider == null)
        {
            return;
        }

        if (!TryGetFurnitureBounds(furniture, out Bounds furnitureBounds))
        {
            return;
        }

        float deltaY = floorCollider.bounds.max.y - furnitureBounds.min.y;
        if (Mathf.Abs(deltaY) > 0.0001f)
        {
            furniture.transform.position += Vector3.up * deltaY;
        }
    }

    private bool AreFurnitureBoundsInsideRoom(Bounds bounds)
    {
        Vector3 min = bounds.min;
        Vector3 max = bounds.max;
        float y = floorCollider.bounds.max.y + insideRoomRayHeightFromFloor;

        Vector3[] checkPoints =
        {
            new Vector3(bounds.center.x, y, bounds.center.z),
            new Vector3(min.x, y, min.z),
            new Vector3(min.x, y, max.z),
            new Vector3(max.x, y, min.z),
            new Vector3(max.x, y, max.z)
        };

        foreach (Vector3 point in checkPoints)
        {
            if (!IsInsideRoom(point))
            {
                return false;
            }
        }

        return true;
    }

    private bool IsPointInsideWall(Vector3 point)
    {
        Collider[] overlaps = Physics.OverlapSphere(point, wallOverlapSphereRadius);
        foreach (Collider overlap in overlaps)
        {
            if (overlap != null && wallColliderSet.Contains(overlap))
            {
                return true;
            }
        }

        return false;
    }

    private bool IntersectsAnyWall(PlacedFurniture furniture, Bounds furnitureBounds, out Collider hitWall)
    {
        hitWall = null;

        Bounds shrunkenBounds = furnitureBounds;
        float shrink = Mathf.Max(0f, boundsShrinkForWallCheck);
        shrunkenBounds.Expand(new Vector3(-shrink, -shrink, -shrink));

        foreach (BoxCollider wallCollider in wallColliders)
        {
            if (wallCollider == null || !wallCollider.enabled)
            {
                continue;
            }

            if (!shrunkenBounds.Intersects(wallCollider.bounds))
            {
                continue;
            }

            if (ComputeAnyPenetration(furniture, wallCollider))
            {
                hitWall = wallCollider;
                return true;
            }
        }

        return false;
    }

    private bool IntersectsAnyOtherFurniture(PlacedFurniture furniture, out PlacedFurniture hitFurniture)
    {
        hitFurniture = null;

        foreach (PlacedFurniture other in placedFurnitures)
        {
            if (other == null || other == furniture)
            {
                continue;
            }

            if (!TryGetFurnitureBounds(other, out Bounds otherBounds))
            {
                continue;
            }

            if (!TryGetFurnitureBounds(furniture, out Bounds furnitureBounds))
            {
                continue;
            }

            if (!furnitureBounds.Intersects(otherBounds))
            {
                continue;
            }

            if (ComputeAnyPenetration(furniture, other))
            {
                hitFurniture = other;
                return true;
            }
        }

        return false;
    }

    private bool ComputeAnyPenetration(PlacedFurniture furniture, Collider targetCollider)
    {
        Collider[] colliders = furniture.GetActivePlacementColliders();
        if (colliders == null)
        {
            return false;
        }

        foreach (Collider collider in colliders)
        {
            if (collider == null || !collider.enabled || targetCollider == null || !targetCollider.enabled)
            {
                continue;
            }

            if (!collider.bounds.Intersects(targetCollider.bounds))
            {
                continue;
            }

            if (Physics.ComputePenetration(
                    collider,
                    collider.transform.position,
                    collider.transform.rotation,
                    targetCollider,
                    targetCollider.transform.position,
                    targetCollider.transform.rotation,
                    out _,
                    out _))
            {
                return true;
            }
        }

        return false;
    }

    private bool ComputeAnyPenetration(PlacedFurniture a, PlacedFurniture b)
    {
        Collider[] aColliders = a.GetActivePlacementColliders();
        Collider[] bColliders = b.GetActivePlacementColliders();

        if (aColliders == null || bColliders == null)
        {
            return false;
        }

        foreach (Collider aCollider in aColliders)
        {
            if (aCollider == null || !aCollider.enabled)
            {
                continue;
            }

            foreach (Collider bCollider in bColliders)
            {
                if (bCollider == null || !bCollider.enabled)
                {
                    continue;
                }

                if (!aCollider.bounds.Intersects(bCollider.bounds))
                {
                    continue;
                }

                if (Physics.ComputePenetration(
                        aCollider,
                        aCollider.transform.position,
                        aCollider.transform.rotation,
                        bCollider,
                        bCollider.transform.position,
                        bCollider.transform.rotation,
                        out _,
                        out _))
                {
                    return true;
                }
            }
        }

        return false;
    }
}

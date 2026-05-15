using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// ConfirmRoomManager가 완성한 벽/바닥/천장 오브젝트를 보관하고,
/// 등록된 가구의 이동 결과가 방 내부에 있는지 검증합니다.
/// 수정 내역: 벽걸이 가구(액자 등) 바닥 스냅 예외 처리 및 벽면 스냅 유틸리티 추가
/// </summary>
public class ConfirmedRoomGeometryProvider : MonoBehaviour
{
    [Header("Optional Source")]
    public ConfirmRoomManager confirmRoomManager;
    public bool autoBindFromConfirmRoomManagerOnEnable = false;

    [Header("Pose Correction")]
    public bool snapFurnitureToFloor = true;
    public bool keepFurnitureUpright = true;
    public bool validateCeilingHeight = true;
    public float boundsShrinkForWallCheck = 0.01f;
    public float insideRoomRayHeightFromFloor = 0.25f;
    public float wallOverlapSphereRadius = 0.025f;

    [Header("Placement Assist")]
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
        if (autoBindFromConfirmRoomManagerOnEnable && confirmRoomManager != null) BindFromConfirmRoomManager(confirmRoomManager);
    }

    public bool BindFromConfirmRoomManager(ConfirmRoomManager source)
    {
        if (source == null) return false;
        confirmRoomManager = source;
        return SetConfirmedRoom(source.ConfirmedRoomWallObjects, source.ConfirmedRoomFloorObject, source.ConfirmedRoomCeilingObject);
    }

    public bool SetConfirmedRoom(IEnumerable<GameObject> wallObjects, GameObject floor, GameObject ceiling)
    {
        wallColliders.Clear();
        wallColliderSet.Clear();

        if (wallObjects != null)
        {
            foreach (GameObject wallObject in wallObjects)
            {
                if (wallObject == null) continue;
                BoxCollider wallCollider = wallObject.GetComponent<BoxCollider>();
                if (wallCollider == null) wallCollider = wallObject.GetComponentInChildren<BoxCollider>(true);

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
            return false;
        }

        roomBounds = wallColliders[0].bounds;
        for (int i = 1; i < wallColliders.Count; i++) roomBounds.Encapsulate(wallColliders[i].bounds);

        initialized = true;
        return true;
    }

    public void RegisterPlacedFurniture(PlacedFurniture furniture) { if (furniture != null && !placedFurnitures.Contains(furniture)) placedFurnitures.Add(furniture); }
    public void UnregisterPlacedFurniture(PlacedFurniture furniture) { if (furniture != null) placedFurnitures.Remove(furniture); }

    public bool ValidateAndCorrectFurniturePose(PlacedFurniture furniture, out string reason)
    {
        reason = string.Empty;
        if (furniture == null) { reason = "Furniture is null."; return false; }
        if (!initialized) { reason = "Room geometry is not initialized."; return false; }

        // 벽걸이 가구가 아닐 때만 바닥으로 내리고 똑바로 세움
        if (!furniture.isWallMounted)
        {
            if (keepFurnitureUpright) ForceUpright(furniture.transform);
            if (snapFurnitureToFloor) SnapFurnitureToFloor(furniture);
        }
        else
        {
            // 액자는 Y 회전만 유지하고 흔들리지 않게 보정
            Vector3 euler = furniture.transform.eulerAngles;
            furniture.transform.rotation = Quaternion.Euler(0, euler.y, 0);
        }

        if (IsFurniturePoseValid(furniture, out reason)) return true;
        if (resolvePoseOnValidationFailure && TryResolveFurniturePose(furniture, out reason)) return true;

        return false;
    }

    private bool IsFurniturePoseValid(PlacedFurniture furniture, out string reason)
    {
        reason = string.Empty;
        if (!TryGetFurnitureBounds(furniture, out Bounds furnitureBounds)) { reason = "Furniture has no valid collider or renderer bounds."; return false; }
        if (!AreFurnitureBoundsInsideRoom(furnitureBounds)) { reason = "Furniture is outside the confirmed room boundary."; return false; }
        if (validateCeilingHeight && ceilingCollider != null && furnitureBounds.max.y > ceilingCollider.bounds.min.y) { reason = "Furniture is higher than the ceiling."; return false; }

        // 벽걸이 가구는 벽과 교차하는 것을 어느정도 허용해야 붙을 수 있음
        if (!furniture.isWallMounted && IntersectsAnyWall(furniture, furnitureBounds, out Collider wall)) { reason = $"Furniture intersects wall: {wall.name}."; return false; }
        if (IntersectsAnyOtherFurniture(furniture, out PlacedFurniture other)) { reason = $"Furniture intersects other furniture: {other.DisplayName}."; return false; }

        return true;
    }

    private bool TryResolveFurniturePose(PlacedFurniture furniture, out string reason)
    {
        reason = string.Empty;
        Vector3 originalPosition = furniture.transform.position;
        Quaternion originalRotation = furniture.transform.rotation;

        for (int i = 0; i < collisionResolutionIterations; i++)
        {
            if (!furniture.isWallMounted)
            {
                if (keepFurnitureUpright) ForceUpright(furniture.transform);
                if (snapFurnitureToFloor) SnapFurnitureToFloor(furniture);
            }

            if (IsFurniturePoseValid(furniture, out reason)) return true;

            bool changed = false;

            // 벽걸이는 억지로 방 중앙으로 밀어넣지 않음 (벽에 붙어있어야 하므로)
            if (!furniture.isWallMounted && TryGetFurnitureBounds(furniture, out Bounds bounds) && !AreFurnitureBoundsInsideRoom(bounds))
            {
                changed |= TryMoveFurnitureToNearbyInsideRoom(furniture);
            }

            changed |= TryResolveFurniturePenetrations(furniture);

            if (!changed) break;
        }

        if (!furniture.isWallMounted && snapFurnitureToFloor) SnapFurnitureToFloor(furniture);
        if (IsFurniturePoseValid(furniture, out reason)) return true;

        // 원상 복구
        furniture.transform.SetPositionAndRotation(originalPosition, originalRotation);
        if (!furniture.isWallMounted)
        {
            if (keepFurnitureUpright) ForceUpright(furniture.transform);
            if (snapFurnitureToFloor) SnapFurnitureToFloor(furniture);
        }

        IsFurniturePoseValid(furniture, out reason);
        return false;
    }

    // 가장 가까운 벽면을 찾아 스냅 위치와 법선을 반환합니다.
    public bool TrySnapToNearestWall(Vector3 currentPos, float maxDistance, out Vector3 snapPos, out Vector3 snapNormal)
    {
        snapPos = currentPos;
        snapNormal = Vector3.forward;

        if (!initialized || wallColliders == null || wallColliders.Count == 0) return false;

        float minDistance = float.MaxValue;
        bool found = false;

        foreach (BoxCollider wallCollider in wallColliders)
        {
            if (wallCollider == null || !wallCollider.enabled) continue;

            Vector3 closestPoint = wallCollider.ClosestPoint(currentPos);
            float dist = Vector3.Distance(currentPos, closestPoint);

            if (dist < minDistance && dist <= maxDistance)
            {
                minDistance = dist;
                snapPos = closestPoint;

                Vector3 localDir = wallCollider.transform.InverseTransformPoint(closestPoint);
                Vector3 extents = wallCollider.size * 0.5f;

                Vector3 localNormal = Vector3.zero;
                if (Mathf.Abs(localDir.x) > extents.x * 0.99f) localNormal = Vector3.right * Mathf.Sign(localDir.x);
                else if (Mathf.Abs(localDir.z) > extents.z * 0.99f) localNormal = Vector3.forward * Mathf.Sign(localDir.z);
                else localNormal = Vector3.up * Mathf.Sign(localDir.y);

                snapNormal = wallCollider.transform.TransformDirection(localNormal).normalized;
                found = true;
            }
        }

        return found;
    }

    private bool TryMoveFurnitureToNearbyInsideRoom(PlacedFurniture furniture)
    {
        Vector3 startPosition = furniture.transform.position;
        Quaternion startRotation = furniture.transform.rotation;
        float step = Mathf.Max(0.01f, nearbyInsideSearchStepMeters);
        float maxRadius = Mathf.Max(step, nearbyInsideSearchMaxRadiusMeters);
        Vector3 roomCenter = new Vector3(roomBounds.center.x, startPosition.y, roomBounds.center.z);
        Vector3 towardCenter = Vector3.ProjectOnPlane(roomCenter - startPosition, Vector3.up);

        if (TryApplyInsideRoomCandidate(furniture, startPosition, startRotation)) return true;

        if (towardCenter.sqrMagnitude > 0.0001f)
        {
            Vector3 direction = towardCenter.normalized;
            for (float radius = step; radius <= maxRadius + 0.0001f; radius += step)
            {
                if (TryApplyInsideRoomCandidate(furniture, startPosition + direction * radius, startRotation)) return true;
            }
        }

        const int directionCount = 16;
        for (float radius = step; radius <= maxRadius + 0.0001f; radius += step)
        {
            for (int i = 0; i < directionCount; i++)
            {
                float angle = Mathf.PI * 2f * i / directionCount;
                Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                if (TryApplyInsideRoomCandidate(furniture, startPosition + offset, startRotation)) return true;
            }
        }

        furniture.transform.SetPositionAndRotation(startPosition, startRotation);
        return false;
    }

    private bool TryApplyInsideRoomCandidate(PlacedFurniture furniture, Vector3 candidatePosition, Quaternion rotation)
    {
        furniture.transform.SetPositionAndRotation(candidatePosition, rotation);
        if (!furniture.isWallMounted)
        {
            if (keepFurnitureUpright) ForceUpright(furniture.transform);
            if (snapFurnitureToFloor) SnapFurnitureToFloor(furniture);
        }

        return TryGetFurnitureBounds(furniture, out Bounds candidateBounds) && AreFurnitureBoundsInsideRoom(candidateBounds);
    }

    private bool TryResolveFurniturePenetrations(PlacedFurniture furniture)
    {
        Vector3 totalMove = Vector3.zero;

        // 벽면 충돌 밀어내기 (액자는 예외)
        if (!furniture.isWallMounted)
        {
            foreach (BoxCollider wallCollider in wallColliders)
            {
                if (wallCollider == null || !wallCollider.enabled) continue;
                totalMove += ComputeSeparationMove(furniture, wallCollider);
            }
        }

        foreach (PlacedFurniture other in placedFurnitures)
        {
            if (other == null || other == furniture) continue;
            Collider[] otherColliders = other.GetActivePlacementColliders();
            if (otherColliders == null) continue;

            foreach (Collider otherCollider in otherColliders)
            {
                if (otherCollider == null || !otherCollider.enabled) continue;
                totalMove += ComputeSeparationMove(furniture, otherCollider);
            }
        }

        totalMove = Vector3.ProjectOnPlane(totalMove, Vector3.up);
        float maxStep = Mathf.Max(0.01f, maxCollisionResolutionStepMeters);
        if (totalMove.magnitude > maxStep) totalMove = totalMove.normalized * maxStep;

        if (totalMove.sqrMagnitude <= 0.000001f) return false;

        furniture.transform.position += totalMove;
        return true;
    }

    private Vector3 ComputeSeparationMove(PlacedFurniture furniture, Collider targetCollider)
    {
        Vector3 move = Vector3.zero;
        Collider[] colliders = furniture.GetActivePlacementColliders();
        if (colliders == null || targetCollider == null || !targetCollider.enabled) return move;

        foreach (Collider collider in colliders)
        {
            if (collider == null || !collider.enabled || !collider.bounds.Intersects(targetCollider.bounds)) continue;

            if (Physics.ComputePenetration(collider, collider.transform.position, collider.transform.rotation,
                    targetCollider, targetCollider.transform.position, targetCollider.transform.rotation, out Vector3 direction, out float distance))
            {
                move += direction * (distance + collisionResolutionSkinMeters);
            }
        }
        return move;
    }

    public bool IsInsideRoom(Vector3 point)
    {
        if (!initialized) return false;

        float y = floorCollider.bounds.max.y + insideRoomRayHeightFromFloor;
        Vector3 origin = new Vector3(point.x, y, point.z);

        if (IsPointInsideWall(origin)) return false;

        float rayDistance = Mathf.Max(0.1f, roomBounds.max.x - origin.x + 2f);
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.right, rayDistance);

        int wallHitCount = hits.Where(hit => hit.collider != null && wallColliderSet.Contains(hit.collider)).Select(hit => hit.collider).Distinct().Count();
        return wallHitCount % 2 == 1;
    }

    public bool TryGetFurnitureBounds(PlacedFurniture furniture, out Bounds bounds)
    {
        bounds = default;
        if (furniture == null) return false;

        Collider[] colliders = furniture.GetActivePlacementColliders();
        bool hasBounds = false;

        if (colliders != null)
        {
            foreach (Collider collider in colliders)
            {
                if (collider == null || !collider.enabled) continue;
                if (!hasBounds) { bounds = collider.bounds; hasBounds = true; }
                else bounds.Encapsulate(collider.bounds);
            }
        }

        if (hasBounds) return true;

        Renderer[] renderers = furniture.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null || !renderer.enabled) continue;
            if (!hasBounds) { bounds = renderer.bounds; hasBounds = true; }
            else bounds.Encapsulate(renderer.bounds);
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
        if (floorCollider == null) return;
        if (!TryGetFurnitureBounds(furniture, out Bounds furnitureBounds)) return;

        float deltaY = floorCollider.bounds.max.y - furnitureBounds.min.y;
        if (Mathf.Abs(deltaY) > 0.0001f) furniture.transform.position += Vector3.up * deltaY;
    }

    private bool AreFurnitureBoundsInsideRoom(Bounds bounds)
    {
        Vector3 min = bounds.min;
        Vector3 max = bounds.max;
        float y = floorCollider.bounds.max.y + insideRoomRayHeightFromFloor;

        Vector3[] checkPoints = {
            new Vector3(bounds.center.x, y, bounds.center.z),
            new Vector3(min.x, y, min.z),
            new Vector3(min.x, y, max.z),
            new Vector3(max.x, y, min.z),
            new Vector3(max.x, y, max.z)
        };

        foreach (Vector3 point in checkPoints) if (!IsInsideRoom(point)) return false;
        return true;
    }

    private bool IsPointInsideWall(Vector3 point)
    {
        Collider[] overlaps = Physics.OverlapSphere(point, wallOverlapSphereRadius);
        foreach (Collider overlap in overlaps) if (overlap != null && wallColliderSet.Contains(overlap)) return true;
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
            if (wallCollider == null || !wallCollider.enabled) continue;
            if (!shrunkenBounds.Intersects(wallCollider.bounds)) continue;

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
            if (other == null || other == furniture) continue;
            if (!TryGetFurnitureBounds(other, out Bounds otherBounds)) continue;
            if (!TryGetFurnitureBounds(furniture, out Bounds furnitureBounds)) continue;
            if (!furnitureBounds.Intersects(otherBounds)) continue;

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
        if (colliders == null) return false;

        foreach (Collider collider in colliders)
        {
            if (collider == null || !collider.enabled || targetCollider == null || !targetCollider.enabled) continue;
            if (!collider.bounds.Intersects(targetCollider.bounds)) continue;

            if (Physics.ComputePenetration(collider, collider.transform.position, collider.transform.rotation,
                    targetCollider, targetCollider.transform.position, targetCollider.transform.rotation, out _, out _)) return true;
        }
        return false;
    }

    private bool ComputeAnyPenetration(PlacedFurniture a, PlacedFurniture b)
    {
        Collider[] aColliders = a.GetActivePlacementColliders();
        Collider[] bColliders = b.GetActivePlacementColliders();

        if (aColliders == null || bColliders == null) return false;

        foreach (Collider aCollider in aColliders)
        {
            if (aCollider == null || !aCollider.enabled) continue;
            foreach (Collider bCollider in bColliders)
            {
                if (bCollider == null || !bCollider.enabled) continue;
                if (!aCollider.bounds.Intersects(bCollider.bounds)) continue;

                if (Physics.ComputePenetration(aCollider, aCollider.transform.position, aCollider.transform.rotation,
                        bCollider, bCollider.transform.position, bCollider.transform.rotation, out _, out _)) return true;
            }
        }
        return false;
    }
}
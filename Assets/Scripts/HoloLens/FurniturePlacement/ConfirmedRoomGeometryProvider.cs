using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public enum SpawnOverlapResolution
{
    NoOverlap,
    StackedOnTop,
    BelowUser,
    FreeSpace,
    Unresolved
}

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
    [Min(0.0f)] public float ceilingBoundaryTolerance = 0.12f;
    public float boundsShrinkForWallCheck = 0.01f;
    public float insideRoomRayHeightFromFloor = 0.25f;
    public float wallOverlapSphereRadius = 0.025f;
    public bool allowBoundsCornersNearWalls = true;
    [Min(0.0f)] public float wallBoundaryCornerTolerance = 0.12f;
    [Tooltip("벽걸이 가구는 벽/모서리에 의도적으로 붙으므로 방 경계 판정에 더 넓은 허용오차를 사용합니다(벽 두께/관통 흡수). 중심점 검사에도 근접-벽 폴백을 적용합니다.")]
    [Min(0.0f)] public float wallMountedBoundaryTolerance = 0.25f;
    public bool checkUpperBoundsCornersForWallBoundary = true;
    public bool allowShallowWallContact = true;
    [Min(0.0f)] public float allowedWallPenetrationDepth = 0.05f;

    [Header("Placement Assist")]
    public bool resolvePoseOnValidationFailure = true;
    [Min(1)] public int collisionResolutionIterations = 8;
    [Min(0.0f)] public float collisionResolutionSkinMeters = 0.0f;
    [Min(0.05f)] public float maxCollisionResolutionStepMeters = 0.35f;
    [Min(0.05f)] public float nearbyInsideSearchStepMeters = 0.05f;
    [Min(0.05f)] public float nearbyInsideSearchMaxRadiusMeters = 0.8f;

    [Tooltip("벽으로 끝까지 끌어 살짝 침투했을 때, 마지막 유효 위치로 통째 원복하는 대신 접촉한 벽에서 살짝 떨어진 지점으로 보정해 사용자가 끌어둔 위치를 유지합니다.")]
    public bool releaseFromWallInsteadOfRevert = true;

    [Tooltip("releaseFromWallInsteadOfRevert가 켜졌을 때 접촉한 벽 표면에서 가구를 떨어뜨릴 여유(미터)입니다.")]
    [Min(0.0f)] public float wallContactReleaseMargin = 0.0f;

    [Header("Placement Condition Validation")]
    [Tooltip("배치 완료 검증에서 바닥/가구/벽/천장에 '붙어 있다'고 판단하는 최대 간격(미터)입니다.")]
    [Min(0.0f)] public float placementContactTolerance = 0.05f;

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
        if (!AreFurnitureBoundsInsideRoom(furnitureBounds, furniture.isWallMounted)) { reason = "Furniture is outside the confirmed room boundary."; return false; }
        if (ExceedsCeiling(furnitureBounds)) { reason = "Furniture is higher than the ceiling."; return false; }

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

        // 통째 원복 전에, 접촉한 벽에서만 살짝 떨어뜨려 사용자가 끌어둔 위치를 유지한다.
        if (releaseFromWallInsteadOfRevert && !furniture.isWallMounted &&
            TryReleaseFromContactingWalls(furniture, out reason))
        {
            return true;
        }

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

    // 접촉(침투)한 벽에서만 법선 방향으로 margin만큼 떨어뜨려, 통째 원복 없이 사용자가 끌어둔 위치를 유지한다.
    private bool TryReleaseFromContactingWalls(PlacedFurniture furniture, out string reason)
    {
        reason = string.Empty;

        if (furniture.isWallMounted)
        {
            reason = "Wall-mounted furniture is not released from walls.";
            return false;
        }

        const int maxReleaseIterations = 4;
        for (int i = 0; i < maxReleaseIterations; i++)
        {
            Vector3 move = ComputeWallReleaseMove(furniture);
            if (move.sqrMagnitude <= 0.000001f) break;

            float maxStep = Mathf.Max(0.01f, maxCollisionResolutionStepMeters);
            if (move.magnitude > maxStep) move = move.normalized * maxStep;

            furniture.transform.position += move;
            if (keepFurnitureUpright) ForceUpright(furniture.transform);
            if (snapFurnitureToFloor) SnapFurnitureToFloor(furniture);
        }

        return IsFurniturePoseValidAfterWallRelease(furniture, out reason);
    }

    private Vector3 ComputeWallReleaseMove(PlacedFurniture furniture)
    {
        Vector3 totalMove = Vector3.zero;
        Collider[] colliders = furniture.GetActivePlacementColliders();
        if (colliders == null) return totalMove;

        foreach (BoxCollider wallCollider in wallColliders)
        {
            if (wallCollider == null || !wallCollider.enabled) continue;

            foreach (Collider collider in colliders)
            {
                if (collider == null || !collider.enabled) continue;
                if (!collider.bounds.Intersects(wallCollider.bounds)) continue;

                if (Physics.ComputePenetration(collider, collider.transform.position, collider.transform.rotation,
                        wallCollider, wallCollider.transform.position, wallCollider.transform.rotation,
                        out Vector3 direction, out float distance))
                {
                    totalMove += direction * (distance + wallContactReleaseMargin);
                }
            }
        }

        return Vector3.ProjectOnPlane(totalMove, Vector3.up);
    }

    // release 직후 수락 판정: 불안정한 AABB 모서리+레이 검사 대신 침투 없음 + 중심 inside 기반.
    private bool IsFurniturePoseValidAfterWallRelease(PlacedFurniture furniture, out string reason)
    {
        reason = string.Empty;

        if (!TryGetFurnitureBounds(furniture, out Bounds furnitureBounds))
        {
            reason = "Furniture has no valid collider or renderer bounds.";
            return false;
        }

        foreach (BoxCollider wallCollider in wallColliders)
        {
            if (wallCollider == null || !wallCollider.enabled) continue;
            float penetrationDepth = ComputeMaxPenetrationDepth(furniture, wallCollider);
            if (penetrationDepth > 0f && !IsAllowedShallowWallContact(furnitureBounds, penetrationDepth))
            {
                reason = $"Furniture still penetrates wall after release: {wallCollider.name}.";
                return false;
            }
        }

        if (ExceedsCeiling(furnitureBounds))
        {
            reason = "Furniture is higher than the ceiling.";
            return false;
        }

        if (IntersectsAnyOtherFurniture(furniture, out PlacedFurniture other))
        {
            reason = $"Furniture intersects other furniture: {other.DisplayName}.";
            return false;
        }

        if (!IsInsideRoom(furnitureBounds.center))
        {
            reason = "Furniture center is outside the confirmed room boundary.";
            return false;
        }

        return true;
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

            if (!TryProjectPointToWallSidePlane(wallCollider, currentPos, out Vector3 projectedPoint, out Vector3 normal, out float dist))
            {
                continue;
            }

            if (dist < minDistance && dist <= maxDistance)
            {
                minDistance = dist;
                snapPos = projectedPoint;
                snapNormal = normal;
                found = true;
            }
        }

        return found;
    }

    private static bool TryProjectPointToWallSidePlane(BoxCollider wallCollider, Vector3 point, out Vector3 projectedPoint, out Vector3 normal, out float distance)
    {
        projectedPoint = point;
        normal = Vector3.forward;
        distance = float.PositiveInfinity;

        if (wallCollider == null) return false;

        Vector3 localPoint = wallCollider.transform.InverseTransformPoint(point) - wallCollider.center;
        Vector3 extents = wallCollider.size * 0.5f;
        if (extents.x <= 0f || extents.z <= 0f) return false;

        Vector3 bestLocalNormal = Vector3.right;
        float bestSignedDistance = localPoint.x - extents.x;
        float bestAbsDistance = Mathf.Abs(bestSignedDistance);

        TryChooseWallSide(-(localPoint.x + extents.x), Vector3.left, ref bestAbsDistance, ref bestSignedDistance, ref bestLocalNormal);
        TryChooseWallSide(localPoint.z - extents.z, Vector3.forward, ref bestAbsDistance, ref bestSignedDistance, ref bestLocalNormal);
        TryChooseWallSide(-(localPoint.z + extents.z), Vector3.back, ref bestAbsDistance, ref bestSignedDistance, ref bestLocalNormal);

        Vector3 projectedLocalPoint = localPoint - bestLocalNormal * bestSignedDistance;
        projectedPoint = wallCollider.transform.TransformPoint(wallCollider.center + projectedLocalPoint);
        normal = wallCollider.transform.TransformDirection(bestLocalNormal).normalized;
        distance = bestAbsDistance;
        return true;
    }

    private static void TryChooseWallSide(float signedDistance, Vector3 localNormal, ref float bestAbsDistance, ref float bestSignedDistance, ref Vector3 bestLocalNormal)
    {
        float absDistance = Mathf.Abs(signedDistance);
        if (absDistance >= bestAbsDistance) return;

        bestAbsDistance = absDistance;
        bestSignedDistance = signedDistance;
        bestLocalNormal = localNormal;
    }

    public bool TryGetBestSupportTopY(PlacedFurniture furniture, float maxSnapDistance, out float supportTopY)
    {
        supportTopY = FloorTopY;
        if (!initialized || furniture == null) return false;

        if (!TryGetFurnitureVisualBounds(furniture, out Bounds selfBounds) &&
            !TryGetFurnitureBounds(furniture, out selfBounds))
        {
            return false;
        }

        float bottomY = selfBounds.min.y;
        float maxDistance = Mathf.Max(0.0f, maxSnapDistance);
        float bestDistance = Mathf.Abs(bottomY - supportTopY);
        bool foundFurnitureSupport = false;

        foreach (PlacedFurniture other in placedFurnitures)
        {
            if (other == null || other == furniture) continue;
            if (!TryGetFurnitureBounds(other, out Bounds otherBounds)) continue;
            if (!HorizontalBoundsOverlap(selfBounds, otherBounds, 0.01f)) continue;

            float distance = Mathf.Abs(bottomY - otherBounds.max.y);
            if (distance <= maxDistance && distance < bestDistance)
            {
                supportTopY = otherBounds.max.y;
                bestDistance = distance;
                foundFurnitureSupport = true;
            }
        }

        return foundFurnitureSupport || bestDistance <= maxDistance;
    }

    public bool ApplyNearbySurfaceSnap(PlacedFurniture furniture, float snapDistance)
    {
        return ApplyNearbySurfaceSnap(furniture, snapDistance, out _, out _);
    }

    public bool ApplyNearbySurfaceSnap(PlacedFurniture furniture, float snapDistance, out bool snappedToWall, out Vector3 snappedWallNormal)
    {
        return ApplyNearbySurfaceSnap(furniture, snapDistance, Vector3.zero, 0.0f, out snappedToWall, out snappedWallNormal, out _);
    }

    public bool ApplyNearbySurfaceSnap(
        PlacedFurniture furniture,
        float snapDistance,
        Vector3 preferredMoveDirection,
        float switchMargin,
        out bool snappedToWall,
        out Vector3 snappedWallNormal,
        out Vector3 appliedMoveDirection)
    {
        snappedToWall = false;
        snappedWallNormal = Vector3.zero;
        appliedMoveDirection = Vector3.zero;

        if (!initialized || furniture == null) return false;

        BoxCollider selfBox = GetSnapBoxCollider(furniture);
        if (selfBox == null) return false;

        float maxDistance = Mathf.Max(0.0f, snapDistance);
        Vector3 preferredDirection = preferredMoveDirection.sqrMagnitude > 0.0001f ? preferredMoveDirection.normalized : Vector3.zero;
        float directionSwitchMargin = Mathf.Max(0.0f, switchMargin);
        Vector3 bestMove = Vector3.zero;
        Vector3 bestMoveDirection = Vector3.zero;
        float bestSqr = float.PositiveInfinity;
        bool bestIsWall = false;
        Vector3 bestWallNormal = Vector3.zero;

        foreach (BoxCollider wallCollider in wallColliders)
        {
            if (wallCollider == null || !wallCollider.enabled) continue;
            if (TryComputeBoxSideSnapMove(selfBox.bounds, wallCollider.bounds, maxDistance, preferredDirection, directionSwitchMargin, out Vector3 move, out Vector3 moveDirection, out Vector3 wallNormal) &&
                move.sqrMagnitude < bestSqr)
            {
                bestMove = move;
                bestMoveDirection = moveDirection;
                bestSqr = move.sqrMagnitude;
                bestIsWall = true;
                bestWallNormal = wallNormal;
            }
        }

        foreach (PlacedFurniture other in placedFurnitures)
        {
            if (other == null || other == furniture) continue;
            BoxCollider otherBox = GetSnapBoxCollider(other);
            if (otherBox == null || !otherBox.enabled) continue;

            if (TryComputeBoxSideSnapMove(selfBox.bounds, otherBox.bounds, maxDistance, preferredDirection, directionSwitchMargin, out Vector3 move, out Vector3 moveDirection, out _) &&
                move.sqrMagnitude < bestSqr)
            {
                bestMove = move;
                bestMoveDirection = moveDirection;
                bestSqr = move.sqrMagnitude;
                bestIsWall = false;
                bestWallNormal = Vector3.zero;
            }
        }

        if (float.IsPositiveInfinity(bestSqr)) return false;

        if (bestMove.sqrMagnitude > 0.0000001f)
        {
            furniture.transform.position += bestMove;
        }

        snappedToWall = bestIsWall;
        snappedWallNormal = bestWallNormal;
        appliedMoveDirection = bestMoveDirection;
        return true;
    }

    private static BoxCollider GetSnapBoxCollider(PlacedFurniture furniture)
    {
        if (furniture == null) return null;

        BoxCollider rootBox = furniture.GetComponent<BoxCollider>();
        if (rootBox != null && rootBox.enabled) return rootBox;

        BoxCollider[] boxes = furniture.GetComponentsInChildren<BoxCollider>(true);
        foreach (BoxCollider box in boxes)
        {
            if (box != null && box.enabled) return box;
        }

        return null;
    }

    private static bool TryComputeBoxSideSnapMove(
        Bounds selfBounds,
        Bounds targetBounds,
        float maxDistance,
        Vector3 preferredMoveDirection,
        float switchMargin,
        out Vector3 move,
        out Vector3 moveDirection,
        out Vector3 targetOutwardNormal)
    {
        move = Vector3.zero;
        moveDirection = Vector3.zero;
        targetOutwardNormal = Vector3.zero;
        SnapCandidate bestCandidate = default;
        SnapCandidate preferredCandidate = default;
        bool hasBestCandidate = false;
        bool hasPreferredCandidate = false;
        bool hasPreferredDirection = preferredMoveDirection.sqrMagnitude > 0.0001f;
        Vector3 preferredDirection = hasPreferredDirection ? preferredMoveDirection.normalized : Vector3.zero;

        bool yOverlap = selfBounds.min.y < targetBounds.max.y && selfBounds.max.y > targetBounds.min.y;
        if (!yOverlap) return false;

        float zOverlap = Mathf.Min(selfBounds.max.z, targetBounds.max.z) - Mathf.Max(selfBounds.min.z, targetBounds.min.z);
        if (zOverlap > 0.0f)
        {
            TryRegisterSnapCandidate(targetBounds.min.x - selfBounds.max.x, Vector3.right, Vector3.left, maxDistance, preferredDirection, hasPreferredDirection, ref hasBestCandidate, ref bestCandidate, ref hasPreferredCandidate, ref preferredCandidate);
            TryRegisterSnapCandidate(selfBounds.min.x - targetBounds.max.x, Vector3.left, Vector3.right, maxDistance, preferredDirection, hasPreferredDirection, ref hasBestCandidate, ref bestCandidate, ref hasPreferredCandidate, ref preferredCandidate);
        }

        float xOverlap = Mathf.Min(selfBounds.max.x, targetBounds.max.x) - Mathf.Max(selfBounds.min.x, targetBounds.min.x);
        if (xOverlap > 0.0f)
        {
            TryRegisterSnapCandidate(targetBounds.min.z - selfBounds.max.z, Vector3.forward, Vector3.back, maxDistance, preferredDirection, hasPreferredDirection, ref hasBestCandidate, ref bestCandidate, ref hasPreferredCandidate, ref preferredCandidate);
            TryRegisterSnapCandidate(selfBounds.min.z - targetBounds.max.z, Vector3.back, Vector3.forward, maxDistance, preferredDirection, hasPreferredDirection, ref hasBestCandidate, ref bestCandidate, ref hasPreferredCandidate, ref preferredCandidate);
        }

        if (!hasBestCandidate) return false;

        SnapCandidate selectedCandidate = bestCandidate;
        if (hasPreferredCandidate && preferredCandidate.absDistance <= bestCandidate.absDistance + Mathf.Max(0.0f, switchMargin))
        {
            selectedCandidate = preferredCandidate;
        }

        moveDirection = selectedCandidate.moveDirection;
        move = selectedCandidate.moveDirection * selectedCandidate.signedDistance;
        targetOutwardNormal = selectedCandidate.targetOutwardNormal;
        return true;
    }

    private struct SnapCandidate
    {
        public float signedDistance;
        public float absDistance;
        public Vector3 moveDirection;
        public Vector3 targetOutwardNormal;
    }

    private static void TryRegisterSnapCandidate(
        float gap,
        Vector3 moveDirection,
        Vector3 targetOutwardDirection,
        float maxDistance,
        Vector3 preferredMoveDirection,
        bool hasPreferredDirection,
        ref bool hasBestCandidate,
        ref SnapCandidate bestCandidate,
        ref bool hasPreferredCandidate,
        ref SnapCandidate preferredCandidate)
    {
        float absGap = Mathf.Abs(gap);
        if (absGap > maxDistance) return;

        SnapCandidate candidate = new SnapCandidate
        {
            signedDistance = gap,
            absDistance = absGap,
            moveDirection = moveDirection,
            targetOutwardNormal = targetOutwardDirection
        };

        if (!hasBestCandidate || absGap < bestCandidate.absDistance)
        {
            hasBestCandidate = true;
            bestCandidate = candidate;
        }

        if (hasPreferredDirection && Vector3.Dot(moveDirection, preferredMoveDirection) > 0.95f &&
            (!hasPreferredCandidate || absGap < preferredCandidate.absDistance))
        {
            hasPreferredCandidate = true;
            preferredCandidate = candidate;
        }
    }

    private static bool HorizontalBoundsOverlap(Bounds a, Bounds b, float tolerance)
    {
        return a.min.x <= b.max.x + tolerance &&
               a.max.x >= b.min.x - tolerance &&
               a.min.z <= b.max.z + tolerance &&
               a.max.z >= b.min.z - tolerance;
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

    // 합산하면 (침투 collider 수 × 대상 수)만큼 과보정되어 gap이 커진다.
    // 매 반복에서 "가장 깊은 단일 침투"만 해소하고, 남은 접촉은 다음 반복(collisionResolutionIterations)이 처리한다.
    private bool TryResolveFurniturePenetrations(PlacedFurniture furniture)
    {
        Vector3 bestMove = Vector3.zero;
        float bestSqr = 0f;

        // 벽면 충돌 밀어내기 (액자는 예외)
        if (!furniture.isWallMounted)
        {
            foreach (BoxCollider wallCollider in wallColliders)
            {
                if (wallCollider == null || !wallCollider.enabled) continue;
                Vector3 m = ComputeSeparationMove(furniture, wallCollider);
                if (m.sqrMagnitude > bestSqr) { bestSqr = m.sqrMagnitude; bestMove = m; }
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
                Vector3 m = ComputeSeparationMove(furniture, otherCollider);
                if (m.sqrMagnitude > bestSqr) { bestSqr = m.sqrMagnitude; bestMove = m; }
            }
        }

        Vector3 totalMove = Vector3.ProjectOnPlane(bestMove, Vector3.up);
        float maxStep = Mathf.Max(0.01f, maxCollisionResolutionStepMeters);
        if (totalMove.magnitude > maxStep) totalMove = totalMove.normalized * maxStep;

        if (totalMove.sqrMagnitude <= 0.000001f) return false;

        furniture.transform.position += totalMove;
        return true;
    }

    // 한 대상에 대해 가구의 자식 collider를 합산하지 않고, 가장 깊은 침투 하나만 기준으로 밀어낸다.
    // (다조각 convex-hull이 같은 대상에 닿아도 skin/거리가 collider 수만큼 중복 합산되지 않음)
    private Vector3 ComputeSeparationMove(PlacedFurniture furniture, Collider targetCollider)
    {
        Collider[] colliders = furniture.GetActivePlacementColliders();
        if (colliders == null || targetCollider == null || !targetCollider.enabled) return Vector3.zero;

        Vector3 worstDirection = Vector3.zero;
        float worstDistance = 0f;

        foreach (Collider collider in colliders)
        {
            if (collider == null || !collider.enabled || !collider.bounds.Intersects(targetCollider.bounds)) continue;

            if (Physics.ComputePenetration(collider, collider.transform.position, collider.transform.rotation,
                    targetCollider, targetCollider.transform.position, targetCollider.transform.rotation, out Vector3 direction, out float distance))
            {
                if (distance > worstDistance)
                {
                    worstDistance = distance;
                    worstDirection = direction;
                }
            }
        }

        if (worstDistance <= 0f) return Vector3.zero;
        return worstDirection * (worstDistance + collisionResolutionSkinMeters);
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

    // 바닥 정렬은 collider(서버 convex hull)가 아니라 "보이는 메쉬" 기준이어야 한다.
    // collider는 시각 메쉬와 수직 기준점이 다를 수 있어 collider 기준으로 바닥에 맞추면
    // 실제 보이는 가구가 바닥 아래로 가라앉는다. 비활성 렌더러(collider GLB 등)와
    // 선택 표시용 SelectionBoxVisual은 제외한다.
    public bool TryGetFurnitureVisualBounds(PlacedFurniture furniture, out Bounds bounds)
    {
        bounds = default;
        if (furniture == null) return false;

        Renderer[] renderers = furniture.GetCachedVisualRenderers();
        if (renderers == null) return false;
        bool hasBounds = false;

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null || !renderer.enabled) continue;
            if (renderer.gameObject.name == "SelectionBoxVisual") continue;

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

    // 가구가 천장과 바닥에 동시에 닿을 만큼 커졌는지(=실내 높이 이상) 판정한다.
    // 천장 정보가 없으면 판단 불가로 보고 false를 반환한다.
    public bool ExceedsRoomHeight(PlacedFurniture furniture)
    {
        if (!initialized || furniture == null || ceilingCollider == null) return false;
        if (!TryGetFurnitureBounds(furniture, out Bounds bounds)) return false;

        float interiorHeight = CeilingBottomY - FloorTopY;
        if (interiorHeight <= 0f) return false;

        const float touchTolerance = 0.005f;
        return bounds.size.y >= interiorHeight - touchTolerance;
    }

    // 스폰 위치가 다른 가구와 겹칠 때 재배치한다.
    // ① 겹친 가구 위에 쌓기 → ② (천장 초과 시) 사용자 바로 아래 → ③ 방 내부 빈 공간.
    public SpawnOverlapResolution ResolveInitialSpawnOverlap(PlacedFurniture furniture)
    {
        if (!initialized || furniture == null || furniture.isWallMounted) return SpawnOverlapResolution.NoOverlap;
        if (!IntersectsAnyOtherFurniture(furniture, out PlacedFurniture other)) return SpawnOverlapResolution.NoOverlap;

        Vector3 originalPosition = furniture.transform.position;

        if (TryStackOnTop(furniture, other)) return SpawnOverlapResolution.StackedOnTop;
        if (TryPlaceBelowUser(furniture)) return SpawnOverlapResolution.BelowUser;
        if (TryPlaceAtAnyFreeSpace(furniture)) return SpawnOverlapResolution.FreeSpace;

        furniture.transform.position = originalPosition;
        return SpawnOverlapResolution.Unresolved;
    }

    private bool ExceedsCeiling(Bounds bounds)
    {
        return validateCeilingHeight &&
               ceilingCollider != null &&
               bounds.max.y > ceilingCollider.bounds.min.y + Mathf.Max(0.0f, ceilingBoundaryTolerance);
    }

    private bool IsSpawnCandidateValid(PlacedFurniture furniture)
    {
        return TryGetFurnitureBounds(furniture, out Bounds b)
               && AreFurnitureBoundsInsideRoom(b)
               && !ExceedsCeiling(b)
               && !IntersectsAnyOtherFurniture(furniture, out _);
    }

    private bool TryStackOnTop(PlacedFurniture furniture, PlacedFurniture target)
    {
        if (!TryGetFurnitureBounds(target, out Bounds targetBounds)) return false;
        if (!TryGetFurnitureBounds(furniture, out Bounds selfBounds)) return false;

        const float stackGap = 0.0f;
        float deltaY = targetBounds.max.y - selfBounds.min.y + stackGap;
        furniture.transform.position += Vector3.up * deltaY;

        return IsSpawnCandidateValid(furniture);
    }

    private bool TryPlaceBelowUser(PlacedFurniture furniture)
    {
        Camera cam = Camera.main;
        if (cam == null) return false;

        Vector3 prev = furniture.transform.position;
        Vector3 camPos = cam.transform.position;
        furniture.transform.position = new Vector3(camPos.x, prev.y, camPos.z);
        SnapFurnitureToFloor(furniture);

        if (IsSpawnCandidateValid(furniture)) return true;

        furniture.transform.position = prev;
        return false;
    }

    private bool TryPlaceAtAnyFreeSpace(PlacedFurniture furniture)
    {
        Vector3 prev = furniture.transform.position;
        Bounds rb = roomBounds;
        float step = Mathf.Max(0.25f, nearbyInsideSearchStepMeters * 2f);
        const float margin = 0.05f;
        float y = prev.y;

        for (float x = rb.min.x + margin; x <= rb.max.x - margin; x += step)
        {
            for (float z = rb.min.z + margin; z <= rb.max.z - margin; z += step)
            {
                furniture.transform.position = new Vector3(x, y, z);
                SnapFurnitureToFloor(furniture);
                if (IsSpawnCandidateValid(furniture)) return true;
            }
        }

        furniture.transform.position = prev;
        return false;
    }

    // 등록 검증을 건너뛰는 경로에서도 외부에서 바닥 정렬만 적용할 수 있게 노출한다.
    public bool CorrectFurnitureToFloor(PlacedFurniture furniture)
    {
        if (!initialized || furniture == null || furniture.isWallMounted) return false;
        SnapFurnitureToFloor(furniture);
        return true;
    }

    private void SnapFurnitureToFloor(PlacedFurniture furniture)
    {
        if (floorCollider == null) return;

        // 보이는 메쉬 기준으로 바닥에 맞춘다. 렌더러가 없을 때만 collider로 폴백한다.
        if (!TryGetFurnitureVisualBounds(furniture, out Bounds furnitureBounds) &&
            !TryGetFurnitureBounds(furniture, out furnitureBounds))
        {
            return;
        }

        float deltaY = floorCollider.bounds.max.y - furnitureBounds.min.y;
        if (Mathf.Abs(deltaY) > 0.0001f) furniture.transform.position += Vector3.up * deltaY;
    }

    // tolerateWallContact=true(벽걸이 가구)일 때는 벽/모서리에 의도적으로 붙는 특성상
    // 더 넓은 경계 허용오차를 쓰고, 엄격한 IsInsideRoom 단독이던 중심점 검사에도
    // 근접-벽 폴백을 적용한다. 비벽걸이 경로(기본값)는 기존 동작과 동일하다.
    private bool AreFurnitureBoundsInsideRoom(Bounds bounds, bool tolerateWallContact = false)
    {
        Vector3 min = bounds.min;
        Vector3 max = bounds.max;
        float y = floorCollider.bounds.max.y + insideRoomRayHeightFromFloor;

        float boundaryTolerance = tolerateWallContact
            ? Mathf.Max(wallBoundaryCornerTolerance, wallMountedBoundaryTolerance)
            : wallBoundaryCornerTolerance;

        Vector3 centerPoint = new Vector3(bounds.center.x, y, bounds.center.z);
        if (tolerateWallContact)
        {
            if (!IsInsideRoomOrNearWallBoundary(centerPoint, boundaryTolerance)) return false;
        }
        else if (!IsInsideRoom(centerPoint))
        {
            return false;
        }

        Vector3[] lowerCheckPoints = {
            new Vector3(min.x, y, min.z),
            new Vector3(min.x, y, max.z),
            new Vector3(max.x, y, min.z),
            new Vector3(max.x, y, max.z)
        };

        foreach (Vector3 point in lowerCheckPoints)
        {
            if (IsInsideRoomOrNearWallBoundary(point, boundaryTolerance)) continue;
            return false;
        }

        if (checkUpperBoundsCornersForWallBoundary && ceilingCollider != null)
        {
            float upperY = Mathf.Min(max.y, ceilingCollider.bounds.min.y) - 0.001f;
            if (upperY > y + 0.001f)
            {
                Vector3[] upperCheckPoints = {
                    new Vector3(min.x, upperY, min.z),
                    new Vector3(min.x, upperY, max.z),
                    new Vector3(max.x, upperY, min.z),
                    new Vector3(max.x, upperY, max.z)
                };

                foreach (Vector3 point in upperCheckPoints)
                {
                    if (IsInsideRoomOrNearWallBoundary(point, boundaryTolerance)) continue;
                    return false;
                }
            }
        }

        return true;
    }

    private bool IsInsideRoomOrNearWallBoundary(Vector3 point, float tolerance)
    {
        if (IsInsideRoom(point)) return true;
        return allowBoundsCornersNearWalls && IsNearAnyWallBoundary(point, tolerance);
    }

    private bool IsNearAnyWallBoundary(Vector3 point, float tolerance)
    {
        float maxDistance = Mathf.Max(0.0f, tolerance);
        foreach (BoxCollider wallCollider in wallColliders)
        {
            if (wallCollider == null || !wallCollider.enabled) continue;

            Vector3 closest = wallCollider.ClosestPoint(point);
            if (Vector3.Distance(point, closest) <= maxDistance) return true;
        }

        return false;
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

            float penetrationDepth = ComputeMaxPenetrationDepth(furniture, wallCollider);
            if (penetrationDepth > 0f && !IsAllowedShallowWallContact(furnitureBounds, penetrationDepth))
            {
                hitWall = wallCollider;
                return true;
            }
        }
        return false;
    }

    private bool IsAllowedShallowWallContact(Bounds furnitureBounds, float penetrationDepth)
    {
        if (!allowShallowWallContact) return false;
        if (penetrationDepth > Mathf.Max(0.0f, allowedWallPenetrationDepth)) return false;

        Vector3 checkPoint = new Vector3(
            furnitureBounds.center.x,
            floorCollider.bounds.max.y + insideRoomRayHeightFromFloor,
            furnitureBounds.center.z);

        return IsInsideRoom(checkPoint);
    }

    // 외부(스케일 조정 등)에서 위치/스케일 변경 전후로 다른 가구와의 충돌 여부만 비파괴적으로 검사한다.
    public bool OverlapsOtherFurniture(PlacedFurniture furniture)
    {
        return IntersectsAnyOtherFurniture(furniture, out _);
    }

    // 스냅 후 가구가 벽을 파고들면 벽의 "방 안쪽 면"을 향해, 가구 AABB 전체가
    // 그 면을 벗어날 때까지 민다.
    // ComputePenetration(MTV)을 쓰지 않는 이유:
    //  - 회전/이동 직후 물리 미동기화로 stale 결과가 나옴
    //  - 가구가 얇은 벽을 절반 이상 통과하면 MTV가 반대편(관통 방향)으로 밀어 벽을 뚫고 나감
    //    (특정 축으로 긴 가구가 스냅 시 90도 회전하면 빈발)
    // collider.bounds(AABB)는 transform 변경 즉시 반영되므로 물리 동기화가 필요 없고,
    // 항상 방 중심을 향하는 법선으로 밀어 관통 방향 모호성을 제거한다.
    public bool ResolveWallPenetrationAfterSnap(PlacedFurniture furniture)
    {
        if (!initialized || furniture == null) return false;

        const int maxIterations = 6;
        const float wallClearanceMeters = 0.003f;
        bool moved = false;

        for (int iter = 0; iter < maxIterations; iter++)
        {
            if (!TryGetFurnitureBounds(furniture, out Bounds fb)) break;

            Vector3 bestPush = Vector3.zero;
            float bestDelta = 0f;

            foreach (BoxCollider wallCollider in wallColliders)
            {
                if (wallCollider == null || !wallCollider.enabled) continue;
                if (!fb.Intersects(wallCollider.bounds)) continue;

                if (!TryProjectPointToWallSidePlane(wallCollider, fb.center, out Vector3 planePoint, out Vector3 n, out _))
                {
                    continue;
                }

                // 항상 방 안쪽으로 밀도록 법선이 방 중심을 향하게 한다(관통 방향 제거).
                if (Vector3.Dot(n, roomBounds.center - planePoint) < 0f) n = -n;

                // n 방향으로 AABB의 최소 투영(=벽을 가장 깊이 파고든 지점)이
                // 벽면을 wallClearance만큼 벗어나도록 미는 거리.
                Vector3 e = fb.extents;
                float support = Mathf.Abs(n.x) * e.x + Mathf.Abs(n.y) * e.y + Mathf.Abs(n.z) * e.z;
                float minProjection = Vector3.Dot(fb.center - planePoint, n) - support;
                float delta = wallClearanceMeters - minProjection;

                if (delta > bestDelta)
                {
                    bestDelta = delta;
                    bestPush = n * delta;
                }
            }

            if (bestDelta <= 0.0000001f) break;
            furniture.transform.position += bestPush;
            moved = true;
        }

        return moved;
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
        return ComputeMaxPenetrationDepth(furniture, targetCollider) > 0f;
    }

    private float ComputeMaxPenetrationDepth(PlacedFurniture furniture, Collider targetCollider)
    {
        Collider[] colliders = furniture.GetActivePlacementColliders();
        if (colliders == null) return 0f;

        float maxDepth = 0f;

        foreach (Collider collider in colliders)
        {
            if (collider == null || !collider.enabled || targetCollider == null || !targetCollider.enabled) continue;
            if (!collider.bounds.Intersects(targetCollider.bounds)) continue;

            if (Physics.ComputePenetration(collider, collider.transform.position, collider.transform.rotation,
                    targetCollider, targetCollider.transform.position, targetCollider.transform.rotation, out _, out float distance))
            {
                if (distance > maxDepth) maxDepth = distance;
            }
        }

        return maxDepth;
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

    // --- Placement Condition Validation Helpers ---
    // 배치 완료 시 가구가 바닥/다른 가구/벽/천장에 "붙어 있는지"를 판정한다.
    // 시각 메쉬 기준 bounds(없으면 collider)로 placementContactTolerance 이내 근접을 접촉으로 본다.

    public bool TryGetValidationBounds(PlacedFurniture furniture, out Bounds bounds)
    {
        bounds = default;
        if (furniture == null) return false;
        return TryGetFurnitureVisualBounds(furniture, out bounds) || TryGetFurnitureBounds(furniture, out bounds);
    }

    // 가구 바닥이 방 바닥 윗면에 닿아 있는지(약간 파고든 경우도 포함).
    public bool IsFurnitureOnFloor(PlacedFurniture furniture)
    {
        if (!initialized || floorCollider == null || !TryGetValidationBounds(furniture, out Bounds b)) return false;
        return b.min.y <= floorCollider.bounds.max.y + placementContactTolerance;
    }

    // bottom 가구가 top 가구를 바로 아래에서 떠받치고 있는지.
    public bool IsFurnitureSupportedBy(PlacedFurniture top, PlacedFurniture bottom)
    {
        if (top == null || bottom == null || top == bottom) return false;
        if (!TryGetValidationBounds(top, out Bounds tb) || !TryGetValidationBounds(bottom, out Bounds bb)) return false;
        if (!HorizontalBoundsOverlap(tb, bb, placementContactTolerance)) return false;

        // bottom의 윗면이 top의 아랫면 근처에 있고, bottom이 top보다 아래에 있어야 한다.
        float gap = tb.min.y - bb.max.y;
        return gap <= placementContactTolerance &&
               gap >= -Mathf.Max(placementContactTolerance, tb.size.y) &&
               bb.center.y < tb.center.y;
    }

    public bool HasFurnitureSupportBelow(PlacedFurniture furniture, IReadOnlyList<PlacedFurniture> all)
    {
        if (furniture == null || all == null) return false;
        foreach (PlacedFurniture other in all)
        {
            if (other == null || other == furniture) continue;
            if (IsFurnitureSupportedBy(furniture, other)) return true;
        }
        return false;
    }

    public bool IsFurnitureTouchingWall(PlacedFurniture furniture)
    {
        if (!initialized || !TryGetValidationBounds(furniture, out Bounds b)) return false;
        Bounds expanded = b;
        expanded.Expand(placementContactTolerance * 2f);
        foreach (BoxCollider wallCollider in wallColliders)
        {
            if (wallCollider == null || !wallCollider.enabled) continue;
            if (expanded.Intersects(wallCollider.bounds)) return true;
        }
        return false;
    }

    public bool IsFurnitureTouchingCeiling(PlacedFurniture furniture)
    {
        if (!initialized || ceilingCollider == null || !TryGetValidationBounds(furniture, out Bounds b)) return false;
        return b.max.y >= ceilingCollider.bounds.min.y - placementContactTolerance;
    }

    // 두 가구가 서로 맞닿아(근접해) 있는지. 면 방향 무관한 인접 판정.
    public bool AreFurnituresConnected(PlacedFurniture a, PlacedFurniture b)
    {
        if (a == null || b == null || a == b) return false;
        if (!TryGetValidationBounds(a, out Bounds ba) || !TryGetValidationBounds(b, out Bounds bb)) return false;
        Bounds expanded = ba;
        expanded.Expand(placementContactTolerance * 2f);
        return expanded.Intersects(bb);
    }
}

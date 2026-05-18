using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Serialization;

public enum SpawnOverlapResolution
{
    NoOverlap,
    StackedOnTop,
    BelowUser,
    FreeSpace,
    Unresolved
}

/// <summary>
/// ConfirmRoomManager媛 ?꾩꽦??踰?諛붾떏/泥쒖옣 ?ㅻ툕?앺듃瑜?蹂닿??섍퀬,
/// ?깅줉??媛援ъ쓽 ?대룞 寃곌낵媛 諛??대????덈뒗吏 寃利앺빀?덈떎.
/// ?섏젙 ?댁뿭: 踰쎄구??媛援??≪옄 ?? 諛붾떏 ?ㅻ깄 ?덉쇅 泥섎━ 諛?踰쎈㈃ ?ㅻ깄 ?좏떥由ы떚 異붽?
/// </summary>
public class ConfirmedRoomGeometryProvider : MonoBehaviour
{
    [Header("Optional Source")]
    public ConfirmRoomManager confirmRoomManager;
    public bool autoBindFromConfirmRoomManagerOnEnable = false;

    [Header("Pose Correction")]
    public bool snapFurnitureToFloor = true;
    public bool keepFurnitureUpright = true;
    [Tooltip("When enabled, furniture movement/scale validation rejects poses outside the confirmed room boundary. Disable this if boundary checks are too aggressive while placing furniture.")]
    public bool validateFurnitureInsideRoomBoundary = true;
    public bool validateCeilingHeight = true;
    [Min(0.0f)] public float ceilingBoundaryTolerance = 0.12f;
    public float boundsShrinkForWallCheck = 0.01f;
    public float insideRoomRayHeightFromFloor = 0.25f;
    public float wallOverlapSphereRadius = 0.025f;
    public bool allowBoundsCornersNearWalls = true;
    [Min(0.0f)] public float wallBoundaryCornerTolerance = 0.12f;
    [Tooltip("踰쎄구??媛援щ뒗 踰?紐⑥꽌由ъ뿉 ?섎룄?곸쑝濡?遺숈쑝誘濡?諛?寃쎄퀎 ?먯젙?????볦? ?덉슜?ㅼ감瑜??ъ슜?⑸땲??踰??먭퍡/愿???≪닔). 以묒떖??寃?ъ뿉??洹쇱젒-踰??대갚???곸슜?⑸땲??")]
    [FormerlySerializedAs("wallMountedBoundaryTolerance")]
    [Min(0.0f)] public float floorContactlessBoundaryTolerance = 0.25f;
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

    [Tooltip("踰쎌쑝濡??앷퉴吏 ?뚯뼱 ?댁쭩 移⑦닾?덉쓣 ?? 留덉?留??좏슚 ?꾩튂濡??듭㎏ ?먮났?섎뒗 ????묒큺??踰쎌뿉???댁쭩 ?⑥뼱吏?吏?먯쑝濡?蹂댁젙???ъ슜?먭? ?뚯뼱???꾩튂瑜??좎??⑸땲??")]
    public bool releaseFromWallInsteadOfRevert = true;

    [Tooltip("releaseFromWallInsteadOfRevert媛 耳쒖죱?????묒큺??踰??쒕㈃?먯꽌 媛援щ? ?⑥뼱?⑤┫ ?ъ쑀(誘명꽣)?낅땲??")]
    [Min(0.0f)] public float wallContactReleaseMargin = 0.0f;

    [Header("Placement Condition Validation")]
    [Tooltip("諛곗튂 ?꾨즺 寃利앹뿉??諛붾떏/媛援?踰?泥쒖옣??'遺숈뼱 ?덈떎'怨??먮떒?섎뒗 理쒕? 媛꾧꺽(誘명꽣)?낅땲??")]
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

        // 踰쎄구??媛援ш? ?꾨땺 ?뚮쭔 諛붾떏?쇰줈 ?대━怨??묐컮濡??몄?
        if (!furniture.isFloorContactless)
        {
            if (keepFurnitureUpright) ForceUpright(furniture.transform);
            if (snapFurnitureToFloor) SnapFurnitureToFloor(furniture);
        }
        else
        {
            // ?≪옄??Y ?뚯쟾留??좎??섍퀬 ?붾뱾由ъ? ?딄쾶 蹂댁젙
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
        if (validateFurnitureInsideRoomBoundary && !AreFurnitureBoundsInsideRoom(furnitureBounds, furniture.isFloorContactless)) { reason = "Furniture is outside the confirmed room boundary."; return false; }
        if (ExceedsCeiling(furnitureBounds)) { reason = "Furniture is higher than the ceiling."; return false; }

        // 踰쎄구??媛援щ뒗 踰쎄낵 援먯감?섎뒗 寃껋쓣 ?대뒓?뺣룄 ?덉슜?댁빞 遺숈쓣 ???덉쓬
        if (!furniture.isFloorContactless && IntersectsAnyWall(furniture, furnitureBounds, out Collider wall)) { reason = $"Furniture intersects wall: {wall.name}."; return false; }
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
            if (!furniture.isFloorContactless)
            {
                if (keepFurnitureUpright) ForceUpright(furniture.transform);
                if (snapFurnitureToFloor) SnapFurnitureToFloor(furniture);
            }

            if (IsFurniturePoseValid(furniture, out reason)) return true;

            bool changed = false;

            // 踰쎄구?대뒗 ?듭?濡?諛?以묒븰?쇰줈 諛?대꽔吏 ?딆쓬 (踰쎌뿉 遺숈뼱?덉뼱???섎?濡?
            if (validateFurnitureInsideRoomBoundary && !furniture.isFloorContactless && TryGetFurnitureBounds(furniture, out Bounds bounds) && !AreFurnitureBoundsInsideRoom(bounds))
            {
                changed |= TryMoveFurnitureToNearbyInsideRoom(furniture);
            }

            changed |= TryResolveFurniturePenetrations(furniture);

            if (!changed) break;
        }

        if (!furniture.isFloorContactless && snapFurnitureToFloor) SnapFurnitureToFloor(furniture);
        if (IsFurniturePoseValid(furniture, out reason)) return true;

        // ?듭㎏ ?먮났 ?꾩뿉, ?묒큺??踰쎌뿉?쒕쭔 ?댁쭩 ?⑥뼱?⑤젮 ?ъ슜?먭? ?뚯뼱???꾩튂瑜??좎??쒕떎.
        if (releaseFromWallInsteadOfRevert && !furniture.isFloorContactless &&
            TryReleaseFromContactingWalls(furniture, out reason))
        {
            return true;
        }

        // ?먯긽 蹂듦뎄
        furniture.transform.SetPositionAndRotation(originalPosition, originalRotation);
        if (!furniture.isFloorContactless)
        {
            if (keepFurnitureUpright) ForceUpright(furniture.transform);
            if (snapFurnitureToFloor) SnapFurnitureToFloor(furniture);
        }

        IsFurniturePoseValid(furniture, out reason);
        return false;
    }

    // ?묒큺(移⑦닾)??踰쎌뿉?쒕쭔 踰뺤꽑 諛⑺뼢?쇰줈 margin留뚰겮 ?⑥뼱?⑤젮, ?듭㎏ ?먮났 ?놁씠 ?ъ슜?먭? ?뚯뼱???꾩튂瑜??좎??쒕떎.
    private bool TryReleaseFromContactingWalls(PlacedFurniture furniture, out string reason)
    {
        reason = string.Empty;

        if (furniture.isFloorContactless)
        {
            reason = "Floor-contactless furniture is not released from walls.";
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

    // release 吏곹썑 ?섎씫 ?먯젙: 遺덉븞?뺥븳 AABB 紐⑥꽌由??덉씠 寃?????移⑦닾 ?놁쓬 + 以묒떖 inside 湲곕컲.
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

        if (validateFurnitureInsideRoomBoundary && !IsInsideRoom(furnitureBounds.center))
        {
            reason = "Furniture center is outside the confirmed room boundary.";
            return false;
        }

        return true;
    }

    // 媛??媛源뚯슫 踰쎈㈃??李얠븘 ?ㅻ깄 ?꾩튂? 踰뺤꽑??諛섑솚?⑸땲??
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
        if (!furniture.isFloorContactless)
        {
            if (keepFurnitureUpright) ForceUpright(furniture.transform);
            if (snapFurnitureToFloor) SnapFurnitureToFloor(furniture);
        }

        return TryGetFurnitureBounds(furniture, out Bounds candidateBounds) && AreFurnitureBoundsInsideRoom(candidateBounds);
    }

    // ?⑹궛?섎㈃ (移⑦닾 collider ??횞 ?????留뚰겮 怨쇰낫?뺣릺??gap??而ㅼ쭊??
    // 留?諛섎났?먯꽌 "媛??源딆? ?⑥씪 移⑦닾"留??댁냼?섍퀬, ?⑥? ?묒큺? ?ㅼ쓬 諛섎났(collisionResolutionIterations)??泥섎━?쒕떎.
    private bool TryResolveFurniturePenetrations(PlacedFurniture furniture)
    {
        Vector3 bestMove = Vector3.zero;
        float bestSqr = 0f;

        // 踰쎈㈃ 異⑸룎 諛?대궡湲?(?≪옄???덉쇅)
        if (!furniture.isFloorContactless)
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

    // ????곸뿉 ???媛援ъ쓽 ?먯떇 collider瑜??⑹궛?섏? ?딄퀬, 媛??源딆? 移⑦닾 ?섎굹留?湲곗??쇰줈 諛?대궦??
    // (?ㅼ“媛?convex-hull??媛숈? ??곸뿉 ?우븘??skin/嫄곕━媛 collider ?섎쭔??以묐났 ?⑹궛?섏? ?딆쓬)
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

    // 諛붾떏 ?뺣젹? collider(?쒕쾭 convex hull)媛 ?꾨땲??"蹂댁씠??硫붿돩" 湲곗??댁뼱???쒕떎.
    // collider???쒓컖 硫붿돩? ?섏쭅 湲곗??먯씠 ?ㅻ? ???덉뼱 collider 湲곗??쇰줈 諛붾떏??留욎텛硫?
    // ?ㅼ젣 蹂댁씠??媛援ш? 諛붾떏 ?꾨옒濡?媛?쇱븠?붾떎. 鍮꾪솢???뚮뜑??collider GLB ???
    // ?좏깮 ?쒖떆??SelectionBoxVisual? ?쒖쇅?쒕떎.
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

    // 媛援ш? 泥쒖옣怨?諛붾떏???숈떆???우쓣 留뚰겮 而ㅼ죱?붿?(=?ㅻ궡 ?믪씠 ?댁긽) ?먯젙?쒕떎.
    // 泥쒖옣 ?뺣낫媛 ?놁쑝硫??먮떒 遺덇?濡?蹂닿퀬 false瑜?諛섑솚?쒕떎.
    public bool ExceedsRoomHeight(PlacedFurniture furniture)
    {
        if (!initialized || furniture == null || ceilingCollider == null) return false;
        if (!TryGetFurnitureBounds(furniture, out Bounds bounds)) return false;

        float interiorHeight = CeilingBottomY - FloorTopY;
        if (interiorHeight <= 0f) return false;

        const float touchTolerance = 0.005f;
        return bounds.size.y >= interiorHeight - touchTolerance;
    }

    // ?ㅽ룿 ?꾩튂媛 ?ㅻⅨ 媛援ъ? 寃뱀튌 ???щ같移섑븳??
    // ??寃뱀튇 媛援??꾩뿉 ?볤린 ????(泥쒖옣 珥덇낵 ?? ?ъ슜??諛붾줈 ?꾨옒 ????諛??대? 鍮?怨듦컙.
    public SpawnOverlapResolution ResolveInitialSpawnOverlap(PlacedFurniture furniture)
    {
        if (!initialized || furniture == null || furniture.isFloorContactless) return SpawnOverlapResolution.NoOverlap;
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
               && (!validateFurnitureInsideRoomBoundary || AreFurnitureBoundsInsideRoom(b))
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

    // ?깅줉 寃利앹쓣 嫄대꼫?곕뒗 寃쎈줈?먯꽌???몃??먯꽌 諛붾떏 ?뺣젹留??곸슜?????덇쾶 ?몄텧?쒕떎.
    public bool CorrectFurnitureToFloor(PlacedFurniture furniture)
    {
        if (!initialized || furniture == null || furniture.isFloorContactless) return false;
        SnapFurnitureToFloor(furniture);
        return true;
    }

    private void SnapFurnitureToFloor(PlacedFurniture furniture)
    {
        if (floorCollider == null) return;

        // 蹂댁씠??硫붿돩 湲곗??쇰줈 諛붾떏??留욎텣?? ?뚮뜑?ш? ?놁쓣 ?뚮쭔 collider濡??대갚?쒕떎.
        if (!TryGetFurnitureVisualBounds(furniture, out Bounds furnitureBounds) &&
            !TryGetFurnitureBounds(furniture, out furnitureBounds))
        {
            return;
        }

        float deltaY = floorCollider.bounds.max.y - furnitureBounds.min.y;
        if (Mathf.Abs(deltaY) > 0.0001f) furniture.transform.position += Vector3.up * deltaY;
    }

    // tolerateWallContact=true(踰쎄구??媛援????뚮뒗 踰?紐⑥꽌由ъ뿉 ?섎룄?곸쑝濡?遺숇뒗 ?뱀꽦??
    // ???볦? 寃쎄퀎 ?덉슜?ㅼ감瑜??곌퀬, ?꾧꺽??IsInsideRoom ?⑤룆?대뜕 以묒떖??寃?ъ뿉??
    // 洹쇱젒-踰??대갚???곸슜?쒕떎. 鍮꾨꼍嫄몄씠 寃쎈줈(湲곕낯媛???湲곗〈 ?숈옉怨??숈씪?섎떎.
    private bool AreFurnitureBoundsInsideRoom(Bounds bounds, bool tolerateWallContact = false)
    {
        Vector3 min = bounds.min;
        Vector3 max = bounds.max;
        float y = floorCollider.bounds.max.y + insideRoomRayHeightFromFloor;

        float boundaryTolerance = tolerateWallContact
            ? Mathf.Max(wallBoundaryCornerTolerance, floorContactlessBoundaryTolerance)
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

    // ?몃?(?ㅼ???議곗젙 ???먯꽌 ?꾩튂/?ㅼ???蹂寃??꾪썑濡??ㅻⅨ 媛援ъ???異⑸룎 ?щ?留?鍮꾪뙆愿댁쟻?쇰줈 寃?ы븳??
    public bool OverlapsOtherFurniture(PlacedFurniture furniture)
    {
        return IntersectsAnyOtherFurniture(furniture, out _);
    }

    // ?ㅻ깄 ??媛援ш? 踰쎌쓣 ?뚭퀬?ㅻ㈃ 踰쎌쓽 "諛??덉そ 硫????ν빐, 媛援?AABB ?꾩껜媛
    // 洹?硫댁쓣 踰쀬뼱???뚭퉴吏 誘쇰떎.
    // ComputePenetration(MTV)???곗? ?딅뒗 ?댁쑀:
    //  - ?뚯쟾/?대룞 吏곹썑 臾쇰━ 誘몃룞湲고솕濡?stale 寃곌낵媛 ?섏샂
    //  - 媛援ш? ?뉗? 踰쎌쓣 ?덈컲 ?댁긽 ?듦낵?섎㈃ MTV媛 諛섎???愿??諛⑺뼢)?쇰줈 諛??踰쎌쓣 ?リ퀬 ?섍컧
    //    (?뱀젙 異뺤쑝濡?湲?媛援ш? ?ㅻ깄 ??90???뚯쟾?섎㈃ 鍮덈컻)
    // collider.bounds(AABB)??transform 蹂寃?利됱떆 諛섏쁺?섎?濡?臾쇰━ ?숆린?붽? ?꾩슂 ?녾퀬,
    // ??긽 諛?以묒떖???ν븯??踰뺤꽑?쇰줈 諛??愿??諛⑺뼢 紐⑦샇?깆쓣 ?쒓굅?쒕떎.
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

                // ??긽 諛??덉そ?쇰줈 諛?꾨줉 踰뺤꽑??諛?以묒떖???ν븯寃??쒕떎(愿??諛⑺뼢 ?쒓굅).
                if (Vector3.Dot(n, roomBounds.center - planePoint) < 0f) n = -n;

                // n 諛⑺뼢?쇰줈 AABB??理쒖냼 ?ъ쁺(=踰쎌쓣 媛??源딆씠 ?뚭퀬??吏????
                // 踰쎈㈃??wallClearance留뚰겮 踰쀬뼱?섎룄濡?誘몃뒗 嫄곕━.
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
    // 諛곗튂 ?꾨즺 ??媛援ш? 諛붾떏/?ㅻⅨ 媛援?踰?泥쒖옣??"遺숈뼱 ?덈뒗吏"瑜??먯젙?쒕떎.
    // ?쒓컖 硫붿돩 湲곗? bounds(?놁쑝硫?collider)濡?placementContactTolerance ?대궡 洹쇱젒???묒큺?쇰줈 蹂몃떎.

    public bool TryGetValidationBounds(PlacedFurniture furniture, out Bounds bounds)
    {
        bounds = default;
        if (furniture == null) return false;
        return TryGetFurnitureVisualBounds(furniture, out bounds) || TryGetFurnitureBounds(furniture, out bounds);
    }

    // 媛援?諛붾떏??諛?諛붾떏 ?쀫㈃???우븘 ?덈뒗吏(?쎄컙 ?뚭퀬??寃쎌슦???ы븿).
    public bool IsFurnitureOnFloor(PlacedFurniture furniture)
    {
        if (!initialized || floorCollider == null || !TryGetValidationBounds(furniture, out Bounds b)) return false;
        return b.min.y <= floorCollider.bounds.max.y + placementContactTolerance;
    }

    // bottom 媛援ш? top 媛援щ? 諛붾줈 ?꾨옒?먯꽌 ?좊컺移섍퀬 ?덈뒗吏.
    public bool IsFurnitureSupportedBy(PlacedFurniture top, PlacedFurniture bottom)
    {
        if (top == null || bottom == null || top == bottom) return false;
        if (!TryGetValidationBounds(top, out Bounds tb) || !TryGetValidationBounds(bottom, out Bounds bb)) return false;
        if (!HorizontalBoundsOverlap(tb, bb, placementContactTolerance)) return false;

        // bottom???쀫㈃??top???꾨옯硫?洹쇱쿂???덇퀬, bottom??top蹂대떎 ?꾨옒???덉뼱???쒕떎.
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

    // ??媛援ш? ?쒕줈 留욌떯??洹쇱젒?? ?덈뒗吏. 硫?諛⑺뼢 臾닿????몄젒 ?먯젙.
    public bool AreFurnituresConnected(PlacedFurniture a, PlacedFurniture b)
    {
        if (a == null || b == null || a == b) return false;
        if (!TryGetValidationBounds(a, out Bounds ba) || !TryGetValidationBounds(b, out Bounds bb)) return false;
        Bounds expanded = ba;
        expanded.Expand(placementContactTolerance * 2f);
        return expanded.Intersects(bb);
    }
}

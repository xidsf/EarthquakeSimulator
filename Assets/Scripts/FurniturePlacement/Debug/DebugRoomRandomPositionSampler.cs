using UnityEngine;

/// <summary>
/// Debug 전용입니다.
/// ConfirmedRoomGeometryProvider의 방 내부 검사만 재사용해서 랜덤 root 위치를 샘플링합니다.
/// 최종 MVP의 실사용 배치는 서버/인식 결과 위치를 사용하므로 랜덤 샘플링을 사용하지 않습니다.
/// </summary>
public class DebugRoomRandomPositionSampler : MonoBehaviour
{
    [Header("References")]
    public ConfirmedRoomGeometryProvider roomGeometryProvider;

    [Tooltip("참조가 비어 있으면 Scene에서 자동 탐색합니다.")]
    public bool autoFindReferences = true;

    [Header("Sampling")]
    public int maxTryCount = 100;

    [Tooltip("생성 시 root y를 floorTopY 위로 살짝 띄웁니다. 등록 후 ConfirmedRoomGeometryProvider가 실제 bounds 기준으로 바닥에 스냅합니다.")]
    public float initialRootHeightAboveFloor = 0.1f;

    private void Awake()
    {
        EnsureReferences();
    }

    private void OnEnable()
    {
        EnsureReferences();
    }

    public bool TryGetRandomRootPosition(out Vector3 rootPosition)
    {
        EnsureReferences();
        rootPosition = Vector3.zero;

        if (roomGeometryProvider == null || !roomGeometryProvider.IsInitialized)
        {
            Debug.LogWarning("[DebugRoomRandomPositionSampler] Room geometry is not initialized.");
            return false;
        }

        Bounds roomBounds = roomGeometryProvider.RoomBounds;
        float y = roomGeometryProvider.FloorTopY + initialRootHeightAboveFloor;

        for (int i = 0; i < maxTryCount; i++)
        {
            float x = Random.Range(roomBounds.min.x, roomBounds.max.x);
            float z = Random.Range(roomBounds.min.z, roomBounds.max.z);
            Vector3 candidate = new Vector3(x, y, z);

            if (roomGeometryProvider.IsInsideRoom(candidate))
            {
                rootPosition = candidate;
                return true;
            }
        }

        Debug.LogWarning("[DebugRoomRandomPositionSampler] Failed to sample random position inside room.");
        return false;
    }

    private void EnsureReferences()
    {
        if (!autoFindReferences)
        {
            return;
        }

        if (roomGeometryProvider == null)
        {
            ConfirmedRoomGeometryProvider[] providers = FindObjectsByType<ConfirmedRoomGeometryProvider>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (providers != null && providers.Length > 0)
            {
                roomGeometryProvider = providers[0];
            }
        }
    }
}

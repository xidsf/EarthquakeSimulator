using System.Collections;
using UnityEngine;

/// <summary>
/// Debug 전용입니다.
/// 실제 서버/촬영 파이프라인이 연결되기 전까지 "촬영 버튼 → 일정 시간 대기 → 랜덤 가구 배치"를 흉내냅니다.
/// </summary>
public class DebugCaptureSimulationController : MonoBehaviour
{
    [Header("References")]
    public DebugRandomFurniturePlacer randomFurniturePlacer;

    [Tooltip("참조가 비어 있으면 Scene에서 자동 탐색합니다.")]
    public bool autoFindReferences = true;

    [Header("Simulated Capture")]
    [Min(0f)] public float simulatedCaptureDelaySeconds = 2f;
    [Min(1)] public int furnitureCountAfterCapture = 1;

    private bool captureRunning;

    public bool IsCaptureRunning => captureRunning;

    private void Awake()
    {
        EnsureReferences();
    }

    private void OnEnable()
    {
        EnsureReferences();
    }

    public void StartSimulatedCapture()
    {
        if (captureRunning)
        {
            Debug.LogWarning("[DebugCaptureSimulationController] Simulated capture is already running.");
            return;
        }

        EnsureReferences();
        StartCoroutine(SimulatedCaptureRoutine());
    }

    private IEnumerator SimulatedCaptureRoutine()
    {
        captureRunning = true;
        Debug.Log($"[DebugCaptureSimulationController] Simulated capture started. delay:{simulatedCaptureDelaySeconds}s");

        if (simulatedCaptureDelaySeconds > 0f)
        {
            yield return new WaitForSeconds(simulatedCaptureDelaySeconds);
        }

        if (randomFurniturePlacer == null)
        {
            Debug.LogWarning("[DebugCaptureSimulationController] RandomFurniturePlacer is missing.");
        }
        else
        {
            int placed = randomFurniturePlacer.PlaceRandomFurniture(furnitureCountAfterCapture);
            Debug.Log($"[DebugCaptureSimulationController] Simulated capture completed. placed:{placed}");
        }

        captureRunning = false;
    }

    private void EnsureReferences()
    {
        if (!autoFindReferences)
        {
            return;
        }

        if (randomFurniturePlacer == null)
        {
            DebugRandomFurniturePlacer[] placers = FindObjectsByType<DebugRandomFurniturePlacer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (placers != null && placers.Length > 0)
            {
                randomFurniturePlacer = placers[0];
            }
        }
    }
}

using System.Collections;
using UnityEngine;

/// <summary>
/// FurnitureCaptureManager.CurrentScanSessionId를 이어받아 서버 detect/process/result 흐름을 실행합니다.
/// 배치 단계에서 새 scan session을 만들면 안 됩니다.
/// </summary>
public class FurnitureServerPlacementPipeline : MonoBehaviour
{
    [Header("References")]
    public FurnitureCaptureManager furnitureCaptureManager;
    public RoomBuildWorkflowManager workflowManager;
    public FurnitureServerApiClient apiClient;
    public FurnitureServerResultPlacer resultPlacer;

    [Tooltip("참조가 비어 있으면 Scene에서 자동 탐색합니다.")]
    public bool autoFindReferences = true;

    [Header("Session")]
    [Tooltip("테스트용 수동 scan_session_id입니다. 비어 있으면 FurnitureCaptureManager.CurrentScanSessionId를 사용합니다.")]
    public string debugOverrideScanSessionId;

    [Header("Server Flow")]
    public bool callDetectSessionBeforeProcess = true;
    public bool getDetectionsAfterDetect = true;
    public bool callProcessScanBeforeResult = true;

    [Tooltip("같은 scan_session_id에 detect/process를 다시 호출할 수 있는지 서버와 합의되기 전에는 true로 두고 버튼 연타를 막는 용도로만 사용합니다.")]
    public bool blockWhileRunning = true;

    [Header("Polling")]
    [Min(1)] public int maxResultPollAttempts = 20;
    [Min(0.1f)] public float resultPollIntervalSeconds = 2.0f;

    [Header("Log")]
    public bool logPipeline = true;

    public bool IsRunning { get; private set; }
    public string ActiveScanSessionId { get; private set; }
    public string LastStatus { get; private set; }
    public FurnitureServerResultResponse LastResult { get; private set; }

    private Coroutine runningCoroutine;

    private void Awake()
    {
        EnsureReferences();
    }

    private void OnEnable()
    {
        EnsureReferences();
    }

    public void StartPlacementFromCurrentScanSession()
    {
        EnsureReferences();
        string scanSessionId = ResolveCurrentScanSessionId();
        StartPlacementForSession(scanSessionId);
    }

    public void StartPlacementForSession(string scanSessionId)
    {
        if (blockWhileRunning && IsRunning)
        {
            Debug.LogWarning($"[FurnitureServerPlacementPipeline] Already running. session:{ActiveScanSessionId}");
            return;
        }

        if (string.IsNullOrWhiteSpace(scanSessionId))
        {
            Debug.LogWarning("[FurnitureServerPlacementPipeline] scan_session_id is empty. RoomCapture must create a session before placement.");
            return;
        }

        if (runningCoroutine != null)
        {
            StopCoroutine(runningCoroutine);
        }

        runningCoroutine = StartCoroutine(RunPlacementFlow(scanSessionId));
    }

    public void StopRunningFlow()
    {
        if (runningCoroutine != null)
        {
            StopCoroutine(runningCoroutine);
            runningCoroutine = null;
        }

        IsRunning = false;
        LastStatus = "stopped";
        Debug.Log("[FurnitureServerPlacementPipeline] Flow stopped.");
    }

    private IEnumerator RunPlacementFlow(string scanSessionId)
    {
        EnsureReferences();

        if (apiClient == null || resultPlacer == null)
        {
            Debug.LogWarning("[FurnitureServerPlacementPipeline] Required references are missing.");
            yield break;
        }

        IsRunning = true;
        ActiveScanSessionId = scanSessionId;
        LastStatus = "started";
        LastResult = null;

        if (logPipeline)
        {
            Debug.Log($"[FurnitureServerPlacementPipeline] Start server placement flow. scan_session_id:{scanSessionId}");
        }

        if (callDetectSessionBeforeProcess)
        {
            bool detectOk = false;
            FurnitureServerStatusResponse detectResponse = null;
            string detectError = string.Empty;

            yield return apiClient.PostDetectSession(scanSessionId, (ok, response, error) =>
            {
                detectOk = ok;
                detectResponse = response;
                detectError = error;
            });

            LastStatus = detectResponse != null && !string.IsNullOrWhiteSpace(detectResponse.status)
                ? detectResponse.status
                : (detectOk ? "detect_requested" : "detect_failed");

            if (!detectOk)
            {
                FinishWithFailure($"detect-session failed. {detectError}");
                yield break;
            }

            if (getDetectionsAfterDetect)
            {
                yield return apiClient.GetDetections(scanSessionId, (ok, response, error) =>
                {
                    if (ok && response != null)
                    {
                        int count = response.objects != null ? response.objects.Length : 0;
                        Debug.Log($"[FurnitureServerPlacementPipeline] detections received. status:{response.status}, count:{count}");
                    }
                    else
                    {
                        Debug.LogWarning($"[FurnitureServerPlacementPipeline] get detections failed or not ready. {error}");
                    }
                });
            }
        }

        if (callProcessScanBeforeResult)
        {
            bool processOk = false;
            FurnitureServerStatusResponse processResponse = null;
            string processError = string.Empty;

            yield return apiClient.PostProcessScan(scanSessionId, (ok, response, error) =>
            {
                processOk = ok;
                processResponse = response;
                processError = error;
            });

            LastStatus = processResponse != null && !string.IsNullOrWhiteSpace(processResponse.status)
                ? processResponse.status
                : (processOk ? "process_requested" : "process_failed");

            if (!processOk)
            {
                FinishWithFailure($"process-scan failed. {processError}");
                yield break;
            }
        }

        FurnitureServerResultResponse result = null;
        bool resultReady = false;

        for (int attempt = 1; attempt <= maxResultPollAttempts; attempt++)
        {
            bool getOk = false;
            string getError = string.Empty;
            FurnitureServerResultResponse current = null;

            yield return apiClient.GetResult(scanSessionId, (ok, response, error) =>
            {
                getOk = ok;
                current = response;
                getError = error;
            });

            if (!getOk)
            {
                Debug.LogWarning($"[FurnitureServerPlacementPipeline] result poll failed. attempt:{attempt}/{maxResultPollAttempts}, error:{getError}");
            }
            else if (current != null)
            {
                LastStatus = current.status;
                int count = current.objects != null ? current.objects.Length : 0;
                Debug.Log($"[FurnitureServerPlacementPipeline] result poll. attempt:{attempt}/{maxResultPollAttempts}, status:{current.status}, objects:{count}");

                if (current.IsFailedStatus())
                {
                    FinishWithFailure($"result status failed. status:{current.status}, error:{current.error}");
                    yield break;
                }

                if (current.IsDoneStatus() || current.HasObjects || string.IsNullOrWhiteSpace(current.status))
                {
                    result = current;
                    resultReady = true;
                    break;
                }
            }

            yield return new WaitForSeconds(resultPollIntervalSeconds);
        }

        if (!resultReady || result == null)
        {
            FinishWithFailure($"result polling timed out. attempts:{maxResultPollAttempts}");
            yield break;
        }

        LastResult = result;
        bool placementDone = false;
        int placed = 0;
        int failed = 0;

        yield return resultPlacer.PlaceResultRoutine(result, (placedCount, failedCount) =>
        {
            placementDone = true;
            placed = placedCount;
            failed = failedCount;
        });

        LastStatus = "placement_done";
        IsRunning = false;
        runningCoroutine = null;

        Debug.Log($"[FurnitureServerPlacementPipeline] Server placement flow completed. session:{scanSessionId}, placed:{placed}, failed:{failed}, callback:{placementDone}");
    }

    private void FinishWithFailure(string reason)
    {
        LastStatus = "failed";
        IsRunning = false;
        runningCoroutine = null;
        Debug.LogWarning($"[FurnitureServerPlacementPipeline] Flow failed. {reason}");
    }

    private string ResolveCurrentScanSessionId()
    {
        if (!string.IsNullOrWhiteSpace(debugOverrideScanSessionId))
        {
            return debugOverrideScanSessionId.Trim();
        }

        EnsureReferences();

        if (furnitureCaptureManager == null)
        {
            return string.Empty;
        }

        return furnitureCaptureManager.CurrentScanSessionId;
    }

    private void EnsureReferences()
    {
        if (!autoFindReferences)
        {
            return;
        }

        if (workflowManager == null)
        {
            workflowManager = FindFirst<RoomBuildWorkflowManager>();
        }

        if (furnitureCaptureManager == null && workflowManager != null)
        {
            furnitureCaptureManager = workflowManager.furnitureCaptureManager;
        }

        if (furnitureCaptureManager == null)
        {
            furnitureCaptureManager = FindFirst<FurnitureCaptureManager>();
        }

        if (apiClient == null)
        {
            apiClient = FindFirst<FurnitureServerApiClient>();
        }

        if (resultPlacer == null)
        {
            resultPlacer = FindFirst<FurnitureServerResultPlacer>();
        }
    }

    private static T FindFirst<T>() where T : UnityEngine.Object
    {
        T[] objects = FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        return objects != null && objects.Length > 0 ? objects[0] : null;
    }
}

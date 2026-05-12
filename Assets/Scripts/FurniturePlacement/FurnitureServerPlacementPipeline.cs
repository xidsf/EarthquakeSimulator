using System;
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
    [Tooltip("Calls POST /scan-session/{scan_session_id}/finish before polling. Keep this enabled for normal HoloLens capture flow.")]
    public bool callFinishScanBeforePolling = true;

    [Tooltip("Optional JSON body for /scan-session/{scan_session_id}/finish. Leave empty to use the server defaults.")]
    [TextArea(2, 6)] public string finishScanRequestJson = string.Empty;

    [HideInInspector] public bool callDetectSessionBeforeProcess = false;
    [HideInInspector] public bool getDetectionsAfterDetect = false;
    [HideInInspector] public bool callProcessScanBeforeResult = false;

    [Tooltip("true이면 서버 result를 받자마자 모든 가구를 자동 배치합니다. false이면 result만 캐시하고 UI 선택 후 PlaceSelectedPendingObject/PlacePendingObject를 호출해야 합니다.")]
    public bool autoPlaceResultObjects = false;

    [Tooltip("같은 scan_session_id에 detect/process를 다시 호출할 수 있는지 서버와 합의되기 전에는 true로 두고 버튼 연타를 막는 용도로만 사용합니다.")]
    public bool blockWhileRunning = true;

    [Header("Polling")]
    [Min(1)] public int maxJobPollAttempts = 120;
    [Min(0.1f)] public float jobPollIntervalSeconds = 2.0f;

    [Header("Log")]
    public bool logPipeline = true;

    public bool IsRunning { get; private set; }
    public string ActiveScanSessionId { get; private set; }
    public string LastStatus { get; private set; }
    public FurnitureServerResultResponse LastResult { get; private set; }
    public int PendingObjectCount => LastResult != null && LastResult.objects != null ? LastResult.objects.Length : 0;
    public int SelectedPendingObjectIndex { get; private set; } = -1;
    public FurnitureServerResultObject SelectedPendingObject => GetPendingObject(SelectedPendingObjectIndex);
    public int PendingConfirmationIndex { get; private set; } = -1;
    public bool HasPendingUnconfirmedPlacement => PendingConfirmationIndex >= 0;
    public event Action<FurnitureServerPlacementPipeline> ResultListChanged;
    public event Action<int, FurnitureServerResultObject> PendingSelectionChanged;
    public event Action<int, FurnitureServerResultObject, GameObject> PendingObjectLoadedForPlacement;
    public event Action<int, FurnitureServerResultObject> PendingObjectConfirmed;

    private Coroutine runningCoroutine;
    private bool[] confirmedPendingObjects;
    private GameObject[] placedPendingInstances;

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

    public FurnitureServerResultObject GetPendingObject(int index)
    {
        if (LastResult == null || LastResult.objects == null || index < 0 || index >= LastResult.objects.Length)
        {
            return null;
        }

        return LastResult.objects[index];
    }

    public string GetPendingObjectLabel(int index)
    {
        FurnitureServerResultObject obj = GetPendingObject(index);
        if (obj == null)
        {
            return string.Empty;
        }

        return string.IsNullOrWhiteSpace(obj.label) ? obj.id : obj.label;
    }

    public bool IsPendingObjectConfirmed(int index)
    {
        return confirmedPendingObjects != null &&
               index >= 0 &&
               index < confirmedPendingObjects.Length &&
               confirmedPendingObjects[index];
    }

    public bool IsPendingObjectLoadedForPlacement(int index)
    {
        return placedPendingInstances != null &&
               index >= 0 &&
               index < placedPendingInstances.Length &&
               placedPendingInstances[index] != null;
    }

    public void SelectPendingObject(int index)
    {
        if (HasPendingUnconfirmedPlacement && index != PendingConfirmationIndex)
        {
            Debug.LogWarning($"[FurnitureServerPlacementPipeline] Selection locked until current placed object is confirmed. pendingIndex:{PendingConfirmationIndex}");
            return;
        }

        if (GetPendingObject(index) == null)
        {
            Debug.LogWarning($"[FurnitureServerPlacementPipeline] SelectPendingObject failed. index:{index}, count:{PendingObjectCount}");
            return;
        }

        if (IsPendingObjectConfirmed(index))
        {
            Debug.LogWarning($"[FurnitureServerPlacementPipeline] SelectPendingObject ignored. Object already confirmed. index:{index}");
            return;
        }

        SelectedPendingObjectIndex = index;
        FurnitureServerResultObject selected = SelectedPendingObject;
        Debug.Log($"[FurnitureServerPlacementPipeline] Pending object selected. index:{index}, id:{selected.id}, label:{selected.label}");
        PendingSelectionChanged?.Invoke(index, selected);
    }

    public void SelectNextPendingObject()
    {
        if (PendingObjectCount == 0)
        {
            Debug.LogWarning("[FurnitureServerPlacementPipeline] No pending server objects to select.");
            return;
        }

        int next = FindSelectablePendingIndex(SelectedPendingObjectIndex, 1);
        if (next < 0)
        {
            Debug.LogWarning("[FurnitureServerPlacementPipeline] No unconfirmed server objects remain.");
            return;
        }

        SelectPendingObject(next);
    }

    public void SelectPreviousPendingObject()
    {
        if (PendingObjectCount == 0)
        {
            Debug.LogWarning("[FurnitureServerPlacementPipeline] No pending server objects to select.");
            return;
        }

        int previous = FindSelectablePendingIndex(SelectedPendingObjectIndex, -1);
        if (previous < 0)
        {
            Debug.LogWarning("[FurnitureServerPlacementPipeline] No unconfirmed server objects remain.");
            return;
        }

        SelectPendingObject(previous);
    }

    public void PlaceSelectedPendingObject()
    {
        PlacePendingObject(SelectedPendingObjectIndex);
    }

    public void PlacePendingObject(int index)
    {
        if (HasPendingUnconfirmedPlacement)
        {
            Debug.LogWarning($"[FurnitureServerPlacementPipeline] PlacePendingObject ignored. Confirm the current object before placing another one. pendingIndex:{PendingConfirmationIndex}");
            return;
        }

        FurnitureServerResultObject obj = GetPendingObject(index);
        if (obj == null)
        {
            Debug.LogWarning($"[FurnitureServerPlacementPipeline] PlacePendingObject failed. index:{index}, count:{PendingObjectCount}");
            return;
        }

        if (IsPendingObjectConfirmed(index))
        {
            Debug.LogWarning($"[FurnitureServerPlacementPipeline] PlacePendingObject ignored. Object already confirmed. index:{index}");
            return;
        }

        if (resultPlacer == null)
        {
            EnsureReferences();
        }

        if (resultPlacer == null)
        {
            Debug.LogWarning("[FurnitureServerPlacementPipeline] PlacePendingObject failed. FurnitureServerResultPlacer is missing.");
            return;
        }

        StartCoroutine(PlacePendingObjectRoutine(index, obj));
    }

    public void PlacePendingObjectById(string objectId)
    {
        if (LastResult == null || LastResult.objects == null)
        {
            Debug.LogWarning("[FurnitureServerPlacementPipeline] PlacePendingObjectById failed. No cached server result.");
            return;
        }

        for (int i = 0; i < LastResult.objects.Length; i++)
        {
            FurnitureServerResultObject obj = LastResult.objects[i];
            if (obj != null && obj.id == objectId)
            {
                PlacePendingObject(i);
                return;
            }
        }

        Debug.LogWarning($"[FurnitureServerPlacementPipeline] PlacePendingObjectById failed. id:{objectId}");
    }

    private IEnumerator PlacePendingObjectRoutine(int index, FurnitureServerResultObject obj)
    {
        bool placed = false;
        GameObject instance = null;
        string error = string.Empty;

        yield return resultPlacer.PlaceSingleObjectRoutine(obj, (ok, placedObject, placeError) =>
        {
            placed = ok;
            instance = placedObject;
            error = placeError;
        });

        LastStatus = placed ? "manual_object_placed" : "manual_object_place_failed";
        if (placed)
        {
            EnsurePendingTrackingArrays();
            if (placedPendingInstances != null && index >= 0 && index < placedPendingInstances.Length)
            {
                placedPendingInstances[index] = instance;
            }

            PendingConfirmationIndex = index;
            SelectedPendingObjectIndex = index;
            PendingObjectLoadedForPlacement?.Invoke(index, obj, instance);
            ResultListChanged?.Invoke(this);
            Debug.Log($"[FurnitureServerPlacementPipeline] Manual server object placed. index:{index}, id:{obj.id}, label:{obj.label}", instance);
        }
        else
        {
            Debug.LogWarning($"[FurnitureServerPlacementPipeline] Manual server object placement failed. index:{index}, id:{obj.id}, label:{obj.label}, error:{error}");
        }
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
            SelectedPendingObjectIndex = -1;
            ResetPendingTracking();

        if (logPipeline)
        {
            Debug.Log($"[FurnitureServerPlacementPipeline] Start server placement flow. scan_session_id:{scanSessionId}");
        }

        if (callFinishScanBeforePolling)
        {
            bool finishOk = false;
            FurnitureServerStatusResponse finishResponse = null;
            string finishError = string.Empty;

            if (string.IsNullOrWhiteSpace(finishScanRequestJson))
            {
                yield return apiClient.PostFinishScanSession(scanSessionId, (ok, response, error) =>
                {
                    finishOk = ok;
                    finishResponse = response;
                    finishError = error;
                });
            }
            else
            {
                yield return apiClient.PostFinishScanSession(scanSessionId, finishScanRequestJson, (ok, response, error) =>
                {
                    finishOk = ok;
                    finishResponse = response;
                    finishError = error;
                });
            }

            LastStatus = finishResponse != null && !string.IsNullOrWhiteSpace(finishResponse.status)
                ? finishResponse.status
                : (finishOk ? "finish_requested" : "finish_failed");

            if (!finishOk)
            {
                FinishWithFailure($"finish scan-session failed. {finishError}");
                yield break;
            }
        }

        bool jobCompleted = false;

        for (int attempt = 1; attempt <= maxJobPollAttempts; attempt++)
        {
            bool statusOk = false;
            string statusError = string.Empty;
            FurnitureServerStatusResponse current = null;

            yield return apiClient.GetScanJobStatus(scanSessionId, (ok, response, error) =>
            {
                statusOk = ok;
                current = response;
                statusError = error;
            });

            if (!statusOk)
            {
                Debug.LogWarning($"[FurnitureServerPlacementPipeline] scan-job poll failed. attempt:{attempt}/{maxJobPollAttempts}, error:{statusError}");
            }
            else if (current != null)
            {
                LastStatus = current.status;
                Debug.Log($"[FurnitureServerPlacementPipeline] scan-job poll. attempt:{attempt}/{maxJobPollAttempts}, status:{current.status}, stage:{current.stage}, objects:{current.object_count}");

                if (current.IsFailedStatus())
                {
                    FinishWithFailure($"scan-job failed. stage:{current.stage}, error:{current.error}, stderr:{current.stderr_tail}");
                    yield break;
                }

                if (current.IsDoneStatus())
                {
                    jobCompleted = true;
                    break;
                }
            }

            yield return new WaitForSeconds(jobPollIntervalSeconds);
        }

        if (!jobCompleted)
        {
            FinishWithFailure($"scan-job polling timed out. attempts:{maxJobPollAttempts}");
            yield break;
        }

        FurnitureServerResultResponse result = null;
        bool getResultOk = false;
        string getResultError = string.Empty;

        yield return apiClient.GetResult(scanSessionId, (ok, response, error) =>
        {
            getResultOk = ok;
            result = response;
            getResultError = error;
        });

        if (!getResultOk || result == null)
        {
            FinishWithFailure($"get result failed. {getResultError}");
            yield break;
        }

        if (!string.IsNullOrWhiteSpace(result.scan_session_id) && result.scan_session_id != scanSessionId)
        {
            FinishWithFailure($"wrong scan result received. expected:{scanSessionId}, actual:{result.scan_session_id}");
            yield break;
        }

        if (result.IsFailedStatus())
        {
            FinishWithFailure($"result status failed. status:{result.status}, error:{result.error}");
            yield break;
        }

        LastResult = result;
        EnsurePendingTrackingArrays();

        if (PendingObjectCount > 0)
        {
            SelectedPendingObjectIndex = 0;
            PendingSelectionChanged?.Invoke(SelectedPendingObjectIndex, SelectedPendingObject);
        }
        ResultListChanged?.Invoke(this);

        if (!autoPlaceResultObjects)
        {
            LastStatus = "result_ready_manual_placement";
            IsRunning = false;
            runningCoroutine = null;
            Debug.Log($"[FurnitureServerPlacementPipeline] Server result cached for manual placement. session:{scanSessionId}, objects:{PendingObjectCount}");
            yield break;
        }

        int placed = 0;
        int failed = 0;

        yield return resultPlacer.PlaceResultRoutine(result, (placedCount, failedCount) =>
        {
            placed = placedCount;
            failed = failedCount;
        });

        LastStatus = "placement_done";
        IsRunning = false;
        runningCoroutine = null;

        Debug.Log($"[FurnitureServerPlacementPipeline] Server placement flow completed. session:{scanSessionId}, placed:{placed}, failed:{failed}");
    }

    public bool ConfirmPlacedPendingObjectAndSelectNext()
    {
        if (!HasPendingUnconfirmedPlacement)
        {
            Debug.LogWarning("[FurnitureServerPlacementPipeline] Confirm ignored. No pending placed object is waiting for confirmation.");
            return false;
        }

        int confirmedIndex = PendingConfirmationIndex;
        FurnitureServerResultObject confirmedObject = GetPendingObject(confirmedIndex);

        EnsurePendingTrackingArrays();
        if (confirmedPendingObjects != null && confirmedIndex >= 0 && confirmedIndex < confirmedPendingObjects.Length)
        {
            confirmedPendingObjects[confirmedIndex] = true;
        }

        PendingConfirmationIndex = -1;
        PendingObjectConfirmed?.Invoke(confirmedIndex, confirmedObject);

        int next = FindSelectablePendingIndex(confirmedIndex, 1);
        if (next >= 0)
        {
            SelectedPendingObjectIndex = next;
            PendingSelectionChanged?.Invoke(next, SelectedPendingObject);
        }
        else
        {
            SelectedPendingObjectIndex = -1;
            PendingSelectionChanged?.Invoke(-1, null);
        }

        ResultListChanged?.Invoke(this);
        Debug.Log($"[FurnitureServerPlacementPipeline] Pending object confirmed. index:{confirmedIndex}, next:{SelectedPendingObjectIndex}");
        return true;
    }

    public int GetConfirmedPendingObjectCount()
    {
        if (confirmedPendingObjects == null)
        {
            return 0;
        }

        int count = 0;
        for (int i = 0; i < confirmedPendingObjects.Length; i++)
        {
            if (confirmedPendingObjects[i])
            {
                count++;
            }
        }

        return count;
    }

    private int FindSelectablePendingIndex(int startIndex, int direction)
    {
        if (PendingObjectCount == 0)
        {
            return -1;
        }

        if (HasPendingUnconfirmedPlacement)
        {
            return PendingConfirmationIndex;
        }

        int step = direction >= 0 ? 1 : -1;
        int index = startIndex;
        if (index < 0 || index >= PendingObjectCount)
        {
            index = step > 0 ? -1 : 0;
        }

        for (int i = 0; i < PendingObjectCount; i++)
        {
            index = (index + step + PendingObjectCount) % PendingObjectCount;
            if (!IsPendingObjectConfirmed(index))
            {
                return index;
            }
        }

        return -1;
    }

    private void EnsurePendingTrackingArrays()
    {
        int count = PendingObjectCount;
        if (count <= 0)
        {
            confirmedPendingObjects = null;
            placedPendingInstances = null;
            PendingConfirmationIndex = -1;
            return;
        }

        if (confirmedPendingObjects == null || confirmedPendingObjects.Length != count)
        {
            confirmedPendingObjects = new bool[count];
        }

        if (placedPendingInstances == null || placedPendingInstances.Length != count)
        {
            placedPendingInstances = new GameObject[count];
        }
    }

    private void ResetPendingTracking()
    {
        confirmedPendingObjects = null;
        placedPendingInstances = null;
        PendingConfirmationIndex = -1;
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

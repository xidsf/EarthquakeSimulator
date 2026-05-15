using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// FurnitureCaptureManager.CurrentScanSessionId를 이어받아 서버 detect/process/result 흐름을 실행합니다.
/// 수정 내역: 다중 배치(무한 도장 찍기) 허용 및 불필요한 UI 락 해제
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

    [Tooltip("true이면 서버 result를 받자마자 모든 가구를 자동 배치합니다.")]
    public bool autoPlaceResultObjects = false;

    [Tooltip("같은 scan_session_id에 detect/process를 다시 호출할 수 있는지 서버와 합의되기 전에는 true로 두고 버튼 연타를 막는 용도로만 사용합니다.")]
    public bool blockWhileRunning = true;

    [Header("Polling")]
    [Min(1)] public int maxJobPollAttempts = 300;
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
    public event Action<int, FurnitureServerResultObject> PendingObjectConfirmed; // 💡 복구됨

    private Coroutine runningCoroutine;
    private bool[] confirmedPendingObjects;
    private GameObject[] placedPendingInstances;

    private void Awake() => EnsureReferences();
    private void OnEnable() => EnsureReferences();

    public void StartPlacementFromCurrentScanSession()
    {
        EnsureReferences();
        string scanSessionId = ResolveCurrentScanSessionId(); // 💡 복구됨
        StartPlacementForSession(scanSessionId);
    }

    public void StartPlacementForSession(string scanSessionId)
    {
        if (blockWhileRunning && IsRunning) return;
        if (string.IsNullOrWhiteSpace(scanSessionId)) return;
        if (runningCoroutine != null) StopCoroutine(runningCoroutine);

        IsRunning = true;
        ActiveScanSessionId = scanSessionId;
        LastStatus = "started";
        LastResult = null;
        SelectedPendingObjectIndex = -1;
        ResetPendingTracking();
        ResultListChanged?.Invoke(this);

        runningCoroutine = StartCoroutine(RunPlacementFlow(scanSessionId));
    }

    public void StopRunningFlow()
    {
        if (runningCoroutine != null) { StopCoroutine(runningCoroutine); runningCoroutine = null; }
        IsRunning = false;
        LastStatus = "stopped";
    }

    public FurnitureServerResultObject GetPendingObject(int index)
    {
        if (LastResult == null || LastResult.objects == null || index < 0 || index >= LastResult.objects.Length) return null;
        return LastResult.objects[index];
    }

    public string GetPendingObjectLabel(int index)
    {
        FurnitureServerResultObject obj = GetPendingObject(index);
        if (obj == null) return string.Empty;
        return string.IsNullOrWhiteSpace(obj.label) ? obj.id : obj.label;
    }

    public bool IsPendingObjectConfirmed(int index)
    {
        return confirmedPendingObjects != null && index >= 0 && index < confirmedPendingObjects.Length && confirmedPendingObjects[index];
    }

    public bool IsPendingObjectLoadedForPlacement(int index)
    {
        return placedPendingInstances != null && index >= 0 && index < placedPendingInstances.Length && placedPendingInstances[index] != null;
    }

    public int GetPlacedPendingObjectCount()
    {
        if (placedPendingInstances == null) return 0;
        int count = 0;
        for (int i = 0; i < placedPendingInstances.Length; i++) { if (placedPendingInstances[i] != null) count++; }
        return count;
    }

    public int GetUnplacedPendingObjectCount() => Mathf.Max(0, PendingObjectCount - GetPlacedPendingObjectCount());

    public bool AreAllPendingObjectsPlaced() => PendingObjectCount > 0 && GetPlacedPendingObjectCount() >= PendingObjectCount;

    public void SelectPendingObject(int index)
    {
        if (GetPendingObject(index) == null) return;

        // 💡 [수정] 다중 배치를 위해 "이미 배치되었으면 건너뛰는" 로직 제거
        SelectedPendingObjectIndex = index;
        FurnitureServerResultObject selected = SelectedPendingObject;
        PendingSelectionChanged?.Invoke(index, selected);
    }

    public void SelectNextPendingObject()
    {
        if (PendingObjectCount == 0) return;
        int next = FindSelectablePendingIndex(SelectedPendingObjectIndex, 1);
        if (next < 0) return;
        SelectPendingObject(next);
    }

    public void SelectPreviousPendingObject()
    {
        if (PendingObjectCount == 0) return;
        int previous = FindSelectablePendingIndex(SelectedPendingObjectIndex, -1);
        if (previous < 0) return;
        SelectPendingObject(previous);
    }

    public void PlaceSelectedPendingObject() => PlacePendingObject(SelectedPendingObjectIndex);

    public void PlacePendingObject(int index)
    {
        FurnitureServerResultObject obj = GetPendingObject(index);
        if (obj == null) return;

        if (HasPendingUnconfirmedPlacement)
        {
            ConfirmPlacedPendingObjectAndSelectNext();
        }

        if (resultPlacer == null) EnsureReferences();
        if (resultPlacer == null) return;

        StartCoroutine(PlacePendingObjectRoutine(index, obj));
    }

    public void PlacePendingObjectById(string objectId)
    {
        if (LastResult == null || LastResult.objects == null) return;
        for (int i = 0; i < LastResult.objects.Length; i++)
        {
            FurnitureServerResultObject obj = LastResult.objects[i];
            if (obj != null && obj.id == objectId)
            {
                PlacePendingObject(i);
                return;
            }
        }
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
                placedPendingInstances[index] = instance; // 다중 배치라도 최근 인스턴스로 갱신
            }

            PendingConfirmationIndex = index;
            SelectedPendingObjectIndex = index;

            PendingObjectLoadedForPlacement?.Invoke(index, obj, instance);
            ResultListChanged?.Invoke(this);
            Debug.Log($"[FurnitureServerPlacementPipeline] Manual server object placed. index:{index}, id:{obj.id}, label:{obj.label}", instance);
        }
    }

    private IEnumerator RunPlacementFlow(string scanSessionId)
    {
        EnsureReferences();
        if (apiClient == null || resultPlacer == null) { FinishWithFailure("Required references are missing."); yield break; }

        IsRunning = true;
        ActiveScanSessionId = scanSessionId;
        LastStatus = "started";
        LastResult = null;
        SelectedPendingObjectIndex = -1;
        ResetPendingTracking();

        if (callFinishScanBeforePolling)
        {
            bool finishOk = false;
            FurnitureServerStatusResponse finishResponse = null;
            string finishError = string.Empty;
            string resolvedFinishJson = ResolveFinishScanRequestJson();

            if (string.IsNullOrWhiteSpace(resolvedFinishJson))
            {
                yield return apiClient.PostFinishScanSession(scanSessionId, (ok, response, error) => { finishOk = ok; finishResponse = response; finishError = error; });
            }
            else
            {
                yield return apiClient.PostFinishScanSession(scanSessionId, resolvedFinishJson, (ok, response, error) => { finishOk = ok; finishResponse = response; finishError = error; });
            }

            LastStatus = finishResponse != null && !string.IsNullOrWhiteSpace(finishResponse.status) ? finishResponse.status : (finishOk ? "finish_requested" : "finish_failed");

            if (!finishOk)
            {
                bool is409 = !string.IsNullOrWhiteSpace(finishError) && finishError.Contains("409");
                bool isUploadIncomplete = is409 && !string.IsNullOrWhiteSpace(finishError) && finishError.Contains("upload_incomplete");

                if (isUploadIncomplete) { FinishWithFailure($"finish scan-session failed: upload not complete. {finishError}"); yield break; }
                if (!is409) { FinishWithFailure($"finish scan-session failed. {finishError}"); yield break; }
                LastStatus = "already_processing";
            }
        }

        bool jobCompleted = false;
        for (int attempt = 1; attempt <= maxJobPollAttempts; attempt++)
        {
            FurnitureServerStatusResponse current = null;
            yield return apiClient.GetScanJobStatus(scanSessionId, (ok, response, error) => current = response);
            if (current != null)
            {
                LastStatus = current.status;
                if (current.IsFailedStatus()) { FinishWithFailure($"scan-job failed. error:{current.error}"); yield break; }
                if (current.IsDoneStatus()) { jobCompleted = true; break; }
            }
            yield return new WaitForSeconds(jobPollIntervalSeconds);
        }

        if (!jobCompleted) { FinishWithFailure("scan-job polling timed out."); yield break; }

        FurnitureServerResultResponse result = null;
        yield return apiClient.GetResult(scanSessionId, (ok, response, error) => result = response);

        if (result == null || result.IsFailedStatus()) { FinishWithFailure("get result failed."); yield break; }

        LastResult = result;
        EnsurePendingTrackingArrays();

        if (!autoPlaceResultObjects) LastStatus = "result_ready_manual_placement";

        if (PendingObjectCount > 0)
        {
            SelectedPendingObjectIndex = 0;
            PendingSelectionChanged?.Invoke(SelectedPendingObjectIndex, SelectedPendingObject);
        }

        if (!autoPlaceResultObjects)
        {
            IsRunning = false;
            runningCoroutine = null;
            ResultListChanged?.Invoke(this);
            yield break;
        }

        ResultListChanged?.Invoke(this);
        yield return resultPlacer.PlaceResultRoutine(result, (placedCount, failedCount) => { });

        LastStatus = "placement_done";
        IsRunning = false;
        runningCoroutine = null;
    }

    public bool ConfirmPlacedPendingObjectAndSelectNext()
    {
        if (!HasPendingUnconfirmedPlacement) return false;

        int confirmedIndex = PendingConfirmationIndex;
        FurnitureServerResultObject confirmedObject = GetPendingObject(confirmedIndex);

        EnsurePendingTrackingArrays();
        if (confirmedPendingObjects != null && confirmedIndex >= 0 && confirmedIndex < confirmedPendingObjects.Length)
        {
            confirmedPendingObjects[confirmedIndex] = true;
        }

        PendingConfirmationIndex = -1;
        PendingObjectConfirmed?.Invoke(confirmedIndex, confirmedObject);

        // 💡 [수정] 자동 확정 후에도 다음 가구로 강제로 넘어가지 않고 현재 인덱스 유지
        int next = confirmedIndex;
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
        return true;
    }

    public bool MarkPlacedObjectNotConfirmed(PlacedFurniture furniture)
    {
        int index = FindPlacedFurnitureIndex(furniture);
        if (index < 0) return false;

        EnsurePendingTrackingArrays();
        if (confirmedPendingObjects != null && index < confirmedPendingObjects.Length) confirmedPendingObjects[index] = false;

        PendingConfirmationIndex = index;
        SelectedPendingObjectIndex = index;
        PendingSelectionChanged?.Invoke(SelectedPendingObjectIndex, SelectedPendingObject);
        ResultListChanged?.Invoke(this);
        return true;
    }

    public bool ConfirmPlacedObject(PlacedFurniture furniture)
    {
        int index = FindPlacedFurnitureIndex(furniture);
        if (index < 0) return false;

        EnsurePendingTrackingArrays();
        if (confirmedPendingObjects != null && index < confirmedPendingObjects.Length) confirmedPendingObjects[index] = true;
        if (PendingConfirmationIndex == index) PendingConfirmationIndex = -1;

        PendingObjectConfirmed?.Invoke(index, GetPendingObject(index));
        ResultListChanged?.Invoke(this);
        return true;
    }

    public int GetConfirmedPendingObjectCount()
    {
        if (confirmedPendingObjects == null) return 0;
        int count = 0;
        for (int i = 0; i < confirmedPendingObjects.Length; i++) { if (confirmedPendingObjects[i]) count++; }
        return count;
    }

    private int FindSelectablePendingIndex(int startIndex, int direction)
    {
        if (PendingObjectCount == 0) return -1;
        int step = direction >= 0 ? 1 : -1;
        int index = startIndex;
        if (index < 0 || index >= PendingObjectCount) index = step > 0 ? -1 : 0;

        // 💡 [수정] "이미 배치된 오브젝트를 건너뛰는" 로직 삭제. 바로 다음 인덱스 리턴.
        return (index + step + PendingObjectCount) % PendingObjectCount;
    }

    private void EnsurePendingTrackingArrays()
    {
        int count = PendingObjectCount;
        if (count <= 0) { confirmedPendingObjects = null; placedPendingInstances = null; PendingConfirmationIndex = -1; return; }
        if (confirmedPendingObjects == null || confirmedPendingObjects.Length != count) confirmedPendingObjects = new bool[count];
        if (placedPendingInstances == null || placedPendingInstances.Length != count) placedPendingInstances = new GameObject[count];
    }

    private int FindPlacedFurnitureIndex(PlacedFurniture furniture)
    {
        if (furniture == null || placedPendingInstances == null) return -1;
        for (int i = 0; i < placedPendingInstances.Length; i++)
        {
            GameObject instance = placedPendingInstances[i];
            if (instance == null) continue;
            if (instance.GetComponent<PlacedFurniture>() == furniture) return i;
        }
        return -1;
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
        ResultListChanged?.Invoke(this);
        Debug.LogWarning($"[FurnitureServerPlacementPipeline] Flow failed. {reason}");
    }

    private string ResolveFinishScanRequestJson()
    {
        if (!string.IsNullOrWhiteSpace(finishScanRequestJson)) return finishScanRequestJson;
        if (furnitureCaptureManager == null) return string.Empty;
        int frameCount = furnitureCaptureManager.NextFrameNumber - 1;
        if (frameCount <= 0) return string.Empty;
        return $"{{\"expected_frame_count\":{frameCount}}}";
    }

    private string ResolveCurrentScanSessionId() // 💡 복구됨
    {
        if (!string.IsNullOrWhiteSpace(debugOverrideScanSessionId)) return debugOverrideScanSessionId.Trim();
        EnsureReferences();
        if (furnitureCaptureManager == null) return string.Empty;
        return furnitureCaptureManager.CurrentScanSessionId;
    }

    private void EnsureReferences()
    {
        if (!autoFindReferences) return;
        if (workflowManager == null) workflowManager = FindFirst<RoomBuildWorkflowManager>();
        if (furnitureCaptureManager == null && workflowManager != null) furnitureCaptureManager = workflowManager.furnitureCaptureManager;
        if (furnitureCaptureManager == null) furnitureCaptureManager = FindFirst<FurnitureCaptureManager>();
        if (apiClient == null) apiClient = FindFirst<FurnitureServerApiClient>();
        if (resultPlacer == null) resultPlacer = FindFirst<FurnitureServerResultPlacer>();
    }

    private static T FindFirst<T>() where T : UnityEngine.Object
    {
        T[] objects = FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        return objects != null && objects.Length > 0 ? objects[0] : null;
    }
}
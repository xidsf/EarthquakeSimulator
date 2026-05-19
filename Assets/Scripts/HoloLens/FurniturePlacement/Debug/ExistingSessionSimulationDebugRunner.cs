using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Editor/play-mode helper for reusing an existing session without running capture or SAM3D again.
/// </summary>
public class ExistingSessionSimulationDebugRunner : MonoBehaviour
{
    [Header("Session")]
    public string debugSessionId = "room_scan_2ca98151-494d-498c-97f2-ae198ed6b87c";

    [Header("References")]
    public FurnitureServerApiClient apiClient;
    public FurnitureServerPlacementPipeline serverPlacementPipeline;
    public FurnitureServerResultPlacer resultPlacer;
    public ConfirmedRoomGeometryProvider roomGeometryProvider;
    public SimulationProcessManager simulationProcessManager;
    public SimulationResultUI simulationResultUI;
    public SimulationClientPlaybackController playbackController;
    public bool autoFindReferences = true;

    [Header("Room Geometry")]
    public Transform debugRoomRoot;
    public bool loadRoomGeometryBeforeFurniture = true;
    public bool loadRoomGeometryBeforeSimulation = true;
    public bool clearPreviousDebugRoomGeometry = true;
    public bool showDebugRoomGeometry = true;

    [Header("Load Furniture")]
    public bool autoPlaceFetchedFurniture = true;

    [Header("Start Simulation")]
    public bool bypassWorkflowStateForDebug = true;
    public bool disablePlacementValidationForDebug = true;
    public bool fetchPlaybackAfterSimulation = true;

    [Header("Fetch Result")]
    public bool fetchPlaybackWithResult = true;
    public bool showResultOnFetch = true;
    public bool autoStartPlaybackAfterFetch;

    private Coroutine runningCoroutine;
    private readonly List<GameObject> debugRoomObjects = new List<GameObject>();

    private void Awake()
    {
        EnsureReferences();
    }

    private void OnEnable()
    {
        EnsureReferences();
    }

    [ContextMenu("Debug/Load Existing Session Room Geometry")]
    public void LoadExistingSessionRoomGeometry()
    {
        StartDebugRoutine(FetchAndBuildRoomGeometryRoutine());
    }

    [ContextMenu("Debug/Load Existing Session Result List")]
    public void LoadExistingSessionResultList()
    {
        StartDebugRoutine(FetchExistingSessionFurnitureRoutine(false));
    }

    [ContextMenu("Debug/Load And Place Existing Session Furniture")]
    public void LoadAndPlaceExistingSessionFurniture()
    {
        StartDebugRoutine(FetchExistingSessionFurnitureRoutine(true));
    }

    [ContextMenu("Debug/Start Simulation For Existing Session")]
    public void StartSimulationForExistingSession()
    {
        StartDebugRoutine(StartSimulationForExistingSessionRoutine());
    }

    private IEnumerator StartSimulationForExistingSessionRoutine()
    {
        EnsureReferences();
        ApplyDebugSessionToManagers();

        if (loadRoomGeometryBeforeSimulation)
        {
            yield return FetchAndBuildRoomGeometryRoutine();
        }

        if (simulationProcessManager == null)
        {
            Debug.LogWarning("[ExistingSessionSimulationDebugRunner] SimulationProcessManager is missing.");
            yield break;
        }

        simulationProcessManager.debugBypassWorkflowState = bypassWorkflowStateForDebug;
        simulationProcessManager.fetchSimulationPlaybackAfterResult = fetchPlaybackAfterSimulation;

        if (disablePlacementValidationForDebug)
        {
            simulationProcessManager.validatePlacementBeforeStart = false;
        }

        bool started = simulationProcessManager.TryStartSimulation();
        Debug.Log($"[ExistingSessionSimulationDebugRunner] Start debug simulation result:{started}, session:{ResolveDebugSessionId()}");
    }

    [ContextMenu("Debug/Fetch And Show Existing Simulation Result")]
    public void FetchAndShowExistingSimulationResult()
    {
        StartDebugRoutine(FetchAndShowSimulationResultRoutine());
    }

    [ContextMenu("Debug/Load Furniture Then Fetch Result")]
    public void LoadFurnitureThenFetchResult()
    {
        StartDebugRoutine(LoadFurnitureThenFetchResultRoutine());
    }

    private IEnumerator LoadFurnitureThenFetchResultRoutine()
    {
        yield return FetchExistingSessionFurnitureRoutine(autoPlaceFetchedFurniture);
        yield return FetchAndShowSimulationResultRoutine();
    }

    private IEnumerator FetchExistingSessionFurnitureRoutine(bool placeObjects)
    {
        EnsureReferences();
        ApplyDebugSessionToManagers();

        if (loadRoomGeometryBeforeFurniture)
        {
            yield return FetchAndBuildRoomGeometryRoutine();
        }

        string sessionId = ResolveDebugSessionId();
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            Debug.LogWarning("[ExistingSessionSimulationDebugRunner] Debug session id is empty.");
            yield break;
        }

        if (apiClient == null)
        {
            Debug.LogWarning("[ExistingSessionSimulationDebugRunner] FurnitureServerApiClient is missing.");
            yield break;
        }

        bool ok = false;
        FurnitureServerResultResponse result = null;
        string error = string.Empty;
        yield return apiClient.GetResult(sessionId, (success, response, requestError) =>
        {
            ok = success;
            result = response;
            error = requestError;
        });

        if (!ok || result == null || result.IsFailedStatus())
        {
            Debug.LogWarning($"[ExistingSessionSimulationDebugRunner] Existing session result fetch failed. session:{sessionId}, error:{error}, status:{result?.status}");
            yield break;
        }

        serverPlacementPipeline?.InjectResultForDebug(result);

        if (!placeObjects)
        {
            Debug.Log($"[ExistingSessionSimulationDebugRunner] Existing session result list loaded. session:{sessionId}, objects:{result.object_count}");
            yield break;
        }

        if (resultPlacer == null)
        {
            Debug.LogWarning("[ExistingSessionSimulationDebugRunner] FurnitureServerResultPlacer is missing.");
            yield break;
        }

        int placedCount = 0;
        int failedCount = 0;
        yield return resultPlacer.PlaceResultRoutine(result, (placed, failed) =>
        {
            placedCount = placed;
            failedCount = failed;
        });

        Debug.Log($"[ExistingSessionSimulationDebugRunner] Existing session furniture placed. session:{sessionId}, placed:{placedCount}, failed:{failedCount}");
    }

    private IEnumerator FetchAndBuildRoomGeometryRoutine()
    {
        EnsureReferences();
        ApplyDebugSessionToManagers();

        string sessionId = ResolveDebugSessionId();
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            Debug.LogWarning("[ExistingSessionSimulationDebugRunner] Debug session id is empty.");
            yield break;
        }

        if (apiClient == null)
        {
            Debug.LogWarning("[ExistingSessionSimulationDebugRunner] FurnitureServerApiClient is missing.");
            yield break;
        }

        bool ok = false;
        SimulationRoomGeometryResponse response = null;
        string error = string.Empty;
        yield return apiClient.GetRoomGeometry(sessionId, (success, roomResponse, requestError) =>
        {
            ok = success;
            response = roomResponse;
            error = requestError;
        });

        if (!ok || response == null || response.IsFailedStatus() || response.room == null)
        {
            Debug.LogWarning($"[ExistingSessionSimulationDebugRunner] Room geometry fetch failed. session:{sessionId}, error:{error}, status:{response?.status}");
            yield break;
        }

        BuildDebugRoomGeometry(response.room);
        Debug.Log($"[ExistingSessionSimulationDebugRunner] Room geometry loaded. session:{sessionId}, walls:{(response.room.walls != null ? response.room.walls.Count : 0)}, floor:{response.room.floor != null}, ceiling:{response.room.ceiling != null}");
    }

    private void BuildDebugRoomGeometry(SimulationRoomGeometry room)
    {
        if (room == null)
        {
            return;
        }

        if (clearPreviousDebugRoomGeometry)
        {
            ClearDebugRoomGeometry();
        }

        Transform root = ResolveDebugRoomRoot();
        List<GameObject> walls = new List<GameObject>();
        GameObject floor = null;
        GameObject ceiling = null;

        if (room.floor != null)
        {
            floor = CreateRoomBox("DebugRoom_Floor", room.floor, root);
        }

        if (room.ceiling != null)
        {
            ceiling = CreateRoomBox("DebugRoom_Ceiling", room.ceiling, root);
        }

        if (room.walls != null)
        {
            for (int i = 0; i < room.walls.Count; i++)
            {
                GameObject wall = CreateRoomBox($"DebugRoom_Wall_{i:00}", room.walls[i], root);
                if (wall != null)
                {
                    walls.Add(wall);
                }
            }
        }

        if (roomGeometryProvider != null)
        {
            bool bound = roomGeometryProvider.SetConfirmedRoom(walls, floor, ceiling);
            Debug.Log($"[ExistingSessionSimulationDebugRunner] Debug room geometry bound to provider:{bound}");
        }
    }

    private Transform ResolveDebugRoomRoot()
    {
        if (debugRoomRoot != null)
        {
            return debugRoomRoot;
        }

        GameObject rootObject = new GameObject("DebugExistingSessionRoomRoot");
        debugRoomRoot = rootObject.transform;
        return debugRoomRoot;
    }

    private GameObject CreateRoomBox(string objectName, SimulationBox box, Transform root)
    {
        if (box == null)
        {
            return null;
        }

        GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obj.name = objectName;
        if (root != null)
        {
            obj.transform.SetParent(root, false);
        }

        obj.transform.localPosition = ToVector3(box.center);
        obj.transform.localEulerAngles = ToVector3(box.euler);
        obj.transform.localScale = ToVector3(box.size, Vector3.one);

        Renderer renderer = obj.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.enabled = showDebugRoomGeometry;
        }

        debugRoomObjects.Add(obj);
        return obj;
    }

    private void ClearDebugRoomGeometry()
    {
        for (int i = debugRoomObjects.Count - 1; i >= 0; i--)
        {
            GameObject obj = debugRoomObjects[i];
            if (obj == null)
            {
                continue;
            }

            Destroy(obj);
        }

        debugRoomObjects.Clear();
    }

    private IEnumerator FetchAndShowSimulationResultRoutine()
    {
        EnsureReferences();
        ApplyDebugSessionToManagers();

        string sessionId = ResolveDebugSessionId();
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            Debug.LogWarning("[ExistingSessionSimulationDebugRunner] Debug session id is empty.");
            yield break;
        }

        if (apiClient == null)
        {
            Debug.LogWarning("[ExistingSessionSimulationDebugRunner] FurnitureServerApiClient is missing.");
            yield break;
        }

        bool resultOk = false;
        SimulationResultResponse result = null;
        string resultError = string.Empty;
        yield return apiClient.GetSimulationResult(sessionId, (success, response, requestError) =>
        {
            resultOk = success;
            result = response;
            resultError = requestError;
        });

        if (!resultOk || result == null || result.IsFailedStatus())
        {
            Debug.LogWarning($"[ExistingSessionSimulationDebugRunner] Simulation result fetch failed. session:{sessionId}, error:{resultError}, status:{result?.status}");
            yield break;
        }

        if (fetchPlaybackWithResult)
        {
            bool playbackOk = false;
            SimulationPlaybackResponse playback = null;
            string playbackError = string.Empty;
            yield return apiClient.GetSimulationPlayback(sessionId, (success, response, requestError) =>
            {
                playbackOk = success;
                playback = response;
                playbackError = requestError;
            });

            if (playbackOk)
            {
                result.playback = playback;
            }
            else
            {
                result.playback_error = playbackError;
                Debug.LogWarning($"[ExistingSessionSimulationDebugRunner] Simulation playback fetch failed. session:{sessionId}, error:{playbackError}");
            }
        }

        if (simulationProcessManager != null)
        {
            simulationProcessManager.InjectCompletedResultForDebug(result, showResultOnFetch);
        }

        simulationResultUI?.UpdateUI(result);

        if (autoStartPlaybackAfterFetch)
        {
            EnsureReferences();
            playbackController?.PlayLatestSimulation();
        }

        Debug.Log($"[ExistingSessionSimulationDebugRunner] Simulation result loaded. session:{sessionId}, status:{result.status}");
    }

    private void StartDebugRoutine(IEnumerator routine)
    {
        if (runningCoroutine != null)
        {
            StopCoroutine(runningCoroutine);
        }

        runningCoroutine = StartCoroutine(RunDebugRoutine(routine));
    }

    private IEnumerator RunDebugRoutine(IEnumerator routine)
    {
        yield return routine;
        runningCoroutine = null;
    }

    private void ApplyDebugSessionToManagers()
    {
        string sessionId = ResolveDebugSessionId();
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        if (serverPlacementPipeline != null)
        {
            serverPlacementPipeline.debugOverrideScanSessionId = sessionId;
        }

        if (simulationProcessManager != null)
        {
            simulationProcessManager.debugOverrideSessionId = sessionId;
        }
    }

    private string ResolveDebugSessionId()
    {
        if (!string.IsNullOrWhiteSpace(debugSessionId))
        {
            return debugSessionId.Trim();
        }

        if (simulationProcessManager != null && !string.IsNullOrWhiteSpace(simulationProcessManager.debugOverrideSessionId))
        {
            return simulationProcessManager.debugOverrideSessionId.Trim();
        }

        if (serverPlacementPipeline != null && !string.IsNullOrWhiteSpace(serverPlacementPipeline.debugOverrideScanSessionId))
        {
            return serverPlacementPipeline.debugOverrideScanSessionId.Trim();
        }

        return string.Empty;
    }

    private void EnsureReferences()
    {
        if (!autoFindReferences)
        {
            return;
        }

        if (apiClient == null) apiClient = FindFirst<FurnitureServerApiClient>();
        if (serverPlacementPipeline == null) serverPlacementPipeline = FindFirst<FurnitureServerPlacementPipeline>();
        if (resultPlacer == null) resultPlacer = FindFirst<FurnitureServerResultPlacer>();
        if (roomGeometryProvider == null) roomGeometryProvider = FindFirst<ConfirmedRoomGeometryProvider>();
        if (simulationProcessManager == null) simulationProcessManager = FindFirst<SimulationProcessManager>();
        if (simulationResultUI == null) simulationResultUI = FindFirst<SimulationResultUI>();
        if (playbackController == null) playbackController = FindFirst<SimulationClientPlaybackController>();
    }

    private static Vector3 ToVector3(float[] values)
    {
        return ToVector3(values, Vector3.zero);
    }

    private static Vector3 ToVector3(float[] values, Vector3 fallback)
    {
        if (values == null || values.Length < 3)
        {
            return fallback;
        }

        return new Vector3(values[0], values[1], values[2]);
    }

    private static T FindFirst<T>() where T : Object
    {
        T[] objects = FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        return objects != null && objects.Length > 0 ? objects[0] : null;
    }
}

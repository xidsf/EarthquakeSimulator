using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEngine;

public class SimulationProcessManager : MonoBehaviour
{
    [Header("References")]
    public FurniturePlacementManager furniturePlacementManager;
    public FurnitureMoveModeController moveModeController;
    public ConfirmedRoomGeometryProvider roomGeometryProvider;
    public FurnitureServerPlacementPipeline serverPlacementPipeline;
    public FurnitureCaptureManager furnitureCaptureManager;
    public FurnitureServerApiClient apiClient;
    public ConfirmRoomManager confirmRoomManager;
    public RoomBuildWorkflowManager workflowManager;
    public UIManager uiManager;
    public bool autoFindReferences = true;

    [Header("Start Conditions")]
    public bool requireAtLeastOneFurniture = true;
    public bool validatePlacementBeforeStart = true;

    [Header("Server Simulation Request")]
    [Min(0.1f)] public float durationSec = 8.0f;
    [Min(1)] public int fps = 30;
    public bool enableVlm = true;
    public bool noGraphics = false;

    [Header("Debug Save")]
    public bool fetchSimulationPlaybackAfterResult = true;
    public bool saveSimulationDebugFiles = true;
    public string simulationDebugFolderName = "SimulationDebug";

    [Header("Job Polling")]
    [Min(1)] public int maxJobPollAttempts = 120;
    [Min(0.1f)] public float jobPollIntervalSeconds = 2.0f;

    public event Action<string> SimulationStarted;
    public event Action<string> SimulationCompleted;
    public event Action<string> SimulationFailed;
    public event Action<string> SimulationStatusChanged;
    public event Action<SimulationResultResponse> SimulationResultReceived;

    private Coroutine runningCoroutine;

    public static SimulationResultResponse LastCompletedResult { get; private set; }
    public SimulationResultResponse LastResult { get; private set; }
    public bool IsRunning => runningCoroutine != null;

    private void Awake()
    {
        EnsureReferences();
    }

    private void OnEnable()
    {
        EnsureReferences();
    }

    public bool TryStartSimulation()
    {
        EnsureReferences();

        if (runningCoroutine != null)
        {
            Debug.LogWarning("[SimulationProcessManager] Simulation is already running.");
            return false;
        }

        if (workflowManager != null &&
            workflowManager.currentState != RoomBuildWorkflowManager.WorkflowState.SimulationProcess)
        {
            Debug.LogWarning($"[SimulationProcessManager] Cannot start simulation from state:{workflowManager.currentState}");
            return false;
        }

        if (requireAtLeastOneFurniture &&
            (furniturePlacementManager == null || !furniturePlacementManager.HasAnyFurniture))
        {
            Debug.LogWarning("[SimulationProcessManager] Cannot start simulation. No furniture registered.");
            uiManager?.ShowNotification("No furniture is available for simulation.");
            return false;
        }

        if (validatePlacementBeforeStart && !ValidatePlacement())
        {
            return false;
        }

        runningCoroutine = StartCoroutine(StartSimulationRoutine());
        return true;
    }

    private IEnumerator StartSimulationRoutine()
    {
        SimulationStarted?.Invoke("Simulation request started.");
        moveModeController?.SetManipulationMode(FurnitureManipulationMode.None);

        string sessionId = ResolveSessionId();
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            FinishFailure("Cannot start simulation. Session id is missing.");
            yield break;
        }

        FurnitureServerApiClient resolvedApiClient = ResolveApiClient();
        if (resolvedApiClient == null)
        {
            FinishFailure("Cannot start simulation. Server API client is missing.");
            yield break;
        }

        ReportStatus("Uploading placed scene.");
        string placedSceneJson = BuildPlacedSceneJson(sessionId);
        bool placedSceneOk = false;
        FurnitureServerStatusResponse placedSceneResponse = null;
        string placedSceneError = string.Empty;

        yield return resolvedApiClient.PostPlacedScene(sessionId, placedSceneJson, (ok, response, error) =>
        {
            placedSceneOk = ok;
            placedSceneResponse = response;
            placedSceneError = error;
        });

        if (!placedSceneOk)
        {
            FinishFailure($"Placed scene upload failed. {placedSceneError}");
            yield break;
        }

        if (placedSceneResponse != null && placedSceneResponse.IsFailedStatus())
        {
            FinishFailure(string.IsNullOrWhiteSpace(placedSceneResponse.error)
                ? "Placed scene upload failed on server."
                : placedSceneResponse.error);
            yield break;
        }

        ReportStatus("Starting simulation job.");
        string simulationJson = BuildSimulationRequestJson();
        bool simulateOk = false;
        SimulationJobResponse simulateResponse = null;
        string simulateError = string.Empty;

        yield return resolvedApiClient.PostSimulate(sessionId, simulationJson, (ok, response, error) =>
        {
            simulateOk = ok;
            simulateResponse = response;
            simulateError = error;
        });

        if (!simulateOk)
        {
            FinishFailure($"Simulation start request failed. {simulateError}");
            yield break;
        }

        if (simulateResponse != null && simulateResponse.IsFailedStatus())
        {
            FinishFailure(string.IsNullOrWhiteSpace(simulateResponse.error)
                ? "Simulation failed on server."
                : simulateResponse.error);
            yield break;
        }

        if (simulateResponse != null && simulateResponse.IsCompletedStatus())
        {
            yield return FetchSimulationResultRoutine(resolvedApiClient, sessionId);
            yield break;
        }

        ReportStatus("Waiting for simulation job.");
        yield return PollSimulationJobRoutine(resolvedApiClient, sessionId);
    }

    private IEnumerator PollSimulationJobRoutine(FurnitureServerApiClient resolvedApiClient, string sessionId)
    {
        for (int attempt = 1; attempt <= maxJobPollAttempts; attempt++)
        {
            bool requestOk = false;
            SimulationJobResponse job = null;
            string requestError = string.Empty;

            yield return resolvedApiClient.GetSimulationJob(sessionId, (ok, response, error) =>
            {
                requestOk = ok;
                job = response;
                requestError = error;
            });

            if (!requestOk)
            {
                Debug.LogWarning($"[SimulationProcessManager] Simulation job polling failed. attempt:{attempt}, error:{requestError}");
            }
            else if (job != null)
            {
                ReportStatus(FormatSimulationJobStatus(job));

                if (job.IsFailedStatus())
                {
                    FinishFailure(string.IsNullOrWhiteSpace(job.error)
                        ? "Simulation failed on server."
                        : job.error);
                    yield break;
                }

                if (job.IsCompletedStatus())
                {
                    yield return FetchSimulationResultRoutine(resolvedApiClient, sessionId);
                    yield break;
                }
            }

            if (attempt < maxJobPollAttempts)
            {
                yield return new WaitForSeconds(jobPollIntervalSeconds);
            }
        }

        FinishFailure("Simulation job polling timed out.");
    }

    private IEnumerator FetchSimulationResultRoutine(FurnitureServerApiClient resolvedApiClient, string sessionId)
    {
        ReportStatus("Fetching simulation result.");

        bool resultOk = false;
        SimulationResultResponse result = null;
        string resultError = string.Empty;

        yield return resolvedApiClient.GetSimulationResult(sessionId, (ok, response, error) =>
        {
            resultOk = ok;
            result = response;
            resultError = error;
        });

        if (!resultOk || result == null)
        {
            FinishFailure($"Simulation result request failed. {resultError}");
            yield break;
        }

        if (result.IsFailedStatus())
        {
            FinishFailure(string.IsNullOrWhiteSpace(result.error)
                ? "Simulation failed on server."
                : result.error);
            yield break;
        }

        if (fetchSimulationPlaybackAfterResult)
        {
            ReportStatus("Fetching simulation playback.");

            bool playbackOk = false;
            SimulationPlaybackResponse playback = null;
            string playbackError = string.Empty;

            yield return resolvedApiClient.GetSimulationPlayback(sessionId, (ok, response, error) =>
            {
                playbackOk = ok;
                playback = response;
                playbackError = error;
            });

            if (playbackOk)
            {
                result.playback = playback;
            }
            else
            {
                result.playback_error = playbackError;
                Debug.LogWarning($"[SimulationProcessManager] Simulation playback request failed. {playbackError}");
            }
        }

        if (saveSimulationDebugFiles)
        {
            result.debug_folder_path = SaveSimulationDebugFiles(sessionId, result, result.playback);
            if (!string.IsNullOrWhiteSpace(result.debug_folder_path))
            {
                ReportStatus("Simulation debug files saved.");
            }
        }

        LastResult = result;
        LastCompletedResult = result;
        SimulationResultReceived?.Invoke(result);
        SimulationResultPanelController.ShowResultOnPanel(uiManager != null ? uiManager.panel_SimulationSuccess : null, result);
        FinishSuccess("Simulation result received.");
    }

    private string BuildPlacedSceneJson(string sessionId)
    {
        List<PlacedSceneObject> objects = new List<PlacedSceneObject>();

        if (furniturePlacementManager != null)
        {
            foreach (PlacedFurniture furniture in furniturePlacementManager.PlacedFurnitures)
            {
                if (furniture == null)
                {
                    continue;
                }

                Transform t = furniture.transform;
                objects.Add(new PlacedSceneObject
                {
                    id = furniture.FurnitureId,
                    label = furniture.Label,
                    position = ToArray(t.position),
                    rotation = ToArray(t.eulerAngles),
                    scale = ToArray(t.localScale),
                    is_fixed = furniture.IsFixed,
                    is_floor_contactless = furniture.IsFloorContactless
                });
            }
        }

        PlacedSceneRequest request = new PlacedSceneRequest
        {
            scan_session_id = sessionId,
            objects = objects.ToArray()
        };

        if (TryGetEntranceWorldPosition(out Vector3 entrancePosition))
        {
            request.entrance_position = ToArray(entrancePosition);
            request.entrance_radius = confirmRoomManager != null && confirmRoomManager.entranceRadiusMeters > 0f
                ? confirmRoomManager.entranceRadiusMeters
                : 1.5f;
        }
        else
        {
            Debug.LogWarning("[SimulationProcessManager] Placed scene has no entrance_position. ConfirmRoomManager entrance is missing or not assigned.");
        }

        return JsonUtility.ToJson(request);
    }

    private bool TryGetEntranceWorldPosition(out Vector3 worldPosition)
    {
        worldPosition = Vector3.zero;
        if (confirmRoomManager == null || !confirmRoomManager.hasEntrance)
        {
            return false;
        }

        Transform markerRoot = confirmRoomManager.entranceMarkerRoot;
        if (markerRoot != null && markerRoot.childCount > 0)
        {
            Transform marker = markerRoot.Find("ConfirmedRoom_EntrancePoint");
            if (marker == null) marker = markerRoot.GetChild(0);
            if (marker != null)
            {
                worldPosition = marker.position;
                return true;
            }
        }

        worldPosition = confirmRoomManager.entrancePointWorld;
        return worldPosition != Vector3.zero;
    }

    private string BuildSimulationRequestJson()
    {
        SimulationRequest request = new SimulationRequest
        {
            duration_sec = durationSec,
            fps = fps,
            enable_vlm = enableVlm,
            no_graphics = noGraphics
        };

        return JsonUtility.ToJson(request);
    }

    private bool ValidatePlacement()
    {
        if (furniturePlacementManager == null)
        {
            Debug.LogWarning("[SimulationProcessManager] Cannot validate placement. FurniturePlacementManager is missing.");
            return false;
        }

        List<PlacedFurniture> invalidFurnitures = furniturePlacementManager.EvaluatePlacementConditions();
        foreach (PlacedFurniture furniture in furniturePlacementManager.PlacedFurnitures)
        {
            if (furniture == null)
            {
                continue;
            }

            furniture.SetPlacementValidationFailed(invalidFurnitures.Contains(furniture));
        }

        moveModeController?.RefreshPlacementValidityVisuals();

        if (invalidFurnitures.Count == 0)
        {
            return true;
        }

        string message = $"Cannot start simulation. Invalid furniture count: {invalidFurnitures.Count}.";
        Debug.LogWarning($"[SimulationProcessManager] {message}");
        uiManager?.ShowNotification(message);
        return false;
    }

    private void ReportStatus(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            Debug.Log($"[SimulationProcessManager] {message}");
        }

        SimulationStatusChanged?.Invoke(message);
    }

    private string FormatSimulationJobStatus(SimulationJobResponse job)
    {
        if (job == null)
        {
            return "Simulation job status is empty.";
        }

        string status = string.IsNullOrWhiteSpace(job.status) ? "running" : job.status;
        if (job.progress > 0f)
        {
            return $"Simulation {status}. progress:{job.progress:0.##}";
        }

        return $"Simulation {status}.";
    }

    private string SaveSimulationDebugFiles(string sessionId, SimulationResultResponse result, SimulationPlaybackResponse playback)
    {
        string safeSessionId = SanitizePathPart(string.IsNullOrWhiteSpace(sessionId) ? "no_session" : sessionId);
        string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        string folder = Path.Combine(Application.persistentDataPath, simulationDebugFolderName, safeSessionId, stamp);

        try
        {
            Directory.CreateDirectory(folder);

            string resultJson = !string.IsNullOrWhiteSpace(result != null ? result.RawJson : null)
                ? result.RawJson
                : JsonUtility.ToJson(result, true);
            WriteText(folder, "simulation_result.json", resultJson);
            SaveRawCsvLikeText(folder, "simulation_result_raw.csv", resultJson);

            if (playback != null)
            {
                string playbackJson = !string.IsNullOrWhiteSpace(playback.RawJson)
                    ? playback.RawJson
                    : JsonUtility.ToJson(playback, true);
                WriteText(folder, "simulation_playback.json", playbackJson);
                SaveRawCsvLikeText(folder, "simulation_playback_raw.csv", playback.RawJson);

                if (playback.frames != null && playback.frames.Length > 0)
                {
                    WriteText(folder, "simulation_playback_frames.txt", string.Join(Environment.NewLine, playback.frames));
                }
            }

            if (result != null && result.vlm != null)
            {
                WriteText(folder, "vlm_result.json", JsonUtility.ToJson(result.vlm, true));
            }

            ExtractAndSaveCsvFields(folder, "result", resultJson);
            if (playback != null)
            {
                ExtractAndSaveCsvFields(folder, "playback", playback.RawJson);
            }

            WriteText(folder, "summary.txt", BuildDebugSummary(sessionId, result, playback));
            Debug.Log($"[SimulationProcessManager] Simulation debug files saved: {folder}");
            return folder;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SimulationProcessManager] Failed to save simulation debug files. {e.Message}");
            return string.Empty;
        }
    }

    private void ExtractAndSaveCsvFields(string folder, string prefix, string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return;
        }

        string[] candidateFields =
        {
            "csv",
            "csv_data",
            "csv_text",
            "simulation_csv",
            "playback_csv",
            "transform_csv",
            "result_csv",
            "frames_csv",
            "trajectory_csv"
        };

        for (int i = 0; i < candidateFields.Length; i++)
        {
            string field = candidateFields[i];
            if (TryExtractJsonStringField(rawJson, field, out string value) && !string.IsNullOrWhiteSpace(value))
            {
                WriteText(folder, $"{prefix}_{field}.csv", value);
            }
        }
    }

    private void SaveRawCsvLikeText(string folder, string fileName, string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return;
        }

        string trimmed = rawText.TrimStart();
        if (trimmed.StartsWith("{", StringComparison.Ordinal) || trimmed.StartsWith("[", StringComparison.Ordinal))
        {
            return;
        }

        if (rawText.Contains(",") || rawText.Contains("\n"))
        {
            WriteText(folder, fileName, rawText);
        }
    }

    private string BuildDebugSummary(string sessionId, SimulationResultResponse result, SimulationPlaybackResponse playback)
    {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine($"saved_utc: {DateTime.UtcNow:o}");
        builder.AppendLine($"session_id: {sessionId}");
        builder.AppendLine($"result_status: {(result != null ? result.status : string.Empty)}");
        builder.AppendLine($"risk_level: {(result != null ? result.risk_level : string.Empty)}");
        builder.AppendLine($"risk_score: {(result != null ? result.risk_score.ToString("0.###") : string.Empty)}");
        builder.AppendLine($"vlm_status: {(result != null && result.vlm != null ? result.vlm.status : string.Empty)}");
        builder.AppendLine($"vlm_error: {(result != null && result.vlm != null ? result.vlm.error : string.Empty)}");
        builder.AppendLine($"playback_status: {(playback != null ? playback.status : string.Empty)}");
        builder.AppendLine($"playback_frames: {(playback != null && playback.frames != null ? playback.frames.Length : 0)}");
        return builder.ToString();
    }

    private void WriteText(string folder, string fileName, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        File.WriteAllText(Path.Combine(folder, fileName), text, Encoding.UTF8);
    }

    private string SanitizePathPart(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "empty";
        }

        char[] invalid = Path.GetInvalidFileNameChars();
        StringBuilder builder = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            builder.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        }

        return builder.ToString();
    }

    private bool TryExtractJsonStringField(string json, string fieldName, out string value)
    {
        value = string.Empty;
        if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(fieldName))
        {
            return false;
        }

        string token = "\"" + fieldName + "\"";
        int tokenIndex = json.IndexOf(token, StringComparison.Ordinal);
        if (tokenIndex < 0)
        {
            return false;
        }

        int colonIndex = json.IndexOf(':', tokenIndex + token.Length);
        if (colonIndex < 0)
        {
            return false;
        }

        int valueStart = colonIndex + 1;
        while (valueStart < json.Length && char.IsWhiteSpace(json[valueStart]))
        {
            valueStart++;
        }

        if (valueStart >= json.Length || json[valueStart] != '"')
        {
            return false;
        }

        StringBuilder builder = new StringBuilder();
        bool escaped = false;
        for (int i = valueStart + 1; i < json.Length; i++)
        {
            char c = json[i];
            if (escaped)
            {
                AppendEscapedJsonChar(builder, json, ref i, c);
                escaped = false;
                continue;
            }

            if (c == '\\')
            {
                escaped = true;
                continue;
            }

            if (c == '"')
            {
                value = builder.ToString();
                return true;
            }

            builder.Append(c);
        }

        return false;
    }

    private void AppendEscapedJsonChar(StringBuilder builder, string json, ref int index, char escapedChar)
    {
        switch (escapedChar)
        {
            case '"': builder.Append('"'); break;
            case '\\': builder.Append('\\'); break;
            case '/': builder.Append('/'); break;
            case 'b': builder.Append('\b'); break;
            case 'f': builder.Append('\f'); break;
            case 'n': builder.Append('\n'); break;
            case 'r': builder.Append('\r'); break;
            case 't': builder.Append('\t'); break;
            case 'u':
                if (index + 4 < json.Length)
                {
                    string hex = json.Substring(index + 1, 4);
                    if (ushort.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out ushort code))
                    {
                        builder.Append((char)code);
                        index += 4;
                        break;
                    }
                }
                builder.Append("\\u");
                break;
            default:
                builder.Append(escapedChar);
                break;
        }
    }

    private void FinishSuccess(string message)
    {
        Debug.Log($"[SimulationProcessManager] {message}");
        SimulationCompleted?.Invoke(message);
        workflowManager?.RequestCommand(RoomBuildWorkflowManager.WorkflowCommand.SimulationSucceeded);
        runningCoroutine = null;
    }

    private void FinishFailure(string message)
    {
        Debug.LogWarning($"[SimulationProcessManager] {message}");
        SimulationFailed?.Invoke(message);
        runningCoroutine = null;
    }

    private string ResolveSessionId()
    {
        string sessionId = furnitureCaptureManager != null ? furnitureCaptureManager.CurrentScanSessionId : null;
        if (string.IsNullOrWhiteSpace(sessionId) && serverPlacementPipeline != null)
        {
            sessionId = serverPlacementPipeline.ActiveScanSessionId;
        }

        return sessionId;
    }

    private FurnitureServerApiClient ResolveApiClient()
    {
        if (apiClient != null)
        {
            return apiClient;
        }

        if (serverPlacementPipeline != null && serverPlacementPipeline.apiClient != null)
        {
            return serverPlacementPipeline.apiClient;
        }

        return FindFirst<FurnitureServerApiClient>();
    }

    private void EnsureReferences()
    {
        if (!autoFindReferences)
        {
            return;
        }

        if (furniturePlacementManager == null) furniturePlacementManager = FindFirst<FurniturePlacementManager>();
        if (moveModeController == null) moveModeController = FindFirst<FurnitureMoveModeController>();
        if (roomGeometryProvider == null) roomGeometryProvider = FindFirst<ConfirmedRoomGeometryProvider>();
        if (serverPlacementPipeline == null) serverPlacementPipeline = FindFirst<FurnitureServerPlacementPipeline>();
        if (furnitureCaptureManager == null) furnitureCaptureManager = FindFirst<FurnitureCaptureManager>();
        if (apiClient == null) apiClient = ResolveApiClient();
        if (workflowManager == null) workflowManager = FindFirst<RoomBuildWorkflowManager>();
        if (confirmRoomManager == null && workflowManager != null) confirmRoomManager = workflowManager.confirmRoomManager;
        if (confirmRoomManager == null) confirmRoomManager = FindFirst<ConfirmRoomManager>();
        if (uiManager == null) uiManager = FindFirst<UIManager>();
    }

    private static float[] ToArray(Vector3 value)
    {
        return new[] { value.x, value.y, value.z };
    }

    private static T FindFirst<T>() where T : UnityEngine.Object
    {
        T[] objects = FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        return objects != null && objects.Length > 0 ? objects[0] : null;
    }
}

// =========================================================================
// 사용자 정의 UI 연동용으로 재작성된 SimulationResultPanelController
// =========================================================================
public class SimulationResultPanelController : MonoBehaviour
{
    [Header("UI Component References")]
    public TMP_Text aiAdviceText;             // AIAdviceText 연결
    public TMP_Text riskResultText;           // RiskResultText 연결
    public TMP_Text roomLevelText;            // RoomLevelText 연결
    public TMP_Text fallHazardFurnituresList; // FallHazardFurnituresList 연결

    private SimulationResultResponse currentResult;

    private void OnEnable()
    {
        if (currentResult == null)
        {
            currentResult = SimulationProcessManager.LastCompletedResult;
        }

        if (currentResult != null)
        {
            SetResult(currentResult);
        }
    }

    public void SetResult(SimulationResultResponse result)
    {
        currentResult = result;

        if (result == null) return;

        // 1. AI 조언 업데이트
        if (aiAdviceText != null)
        {
            aiAdviceText.text = string.IsNullOrEmpty(result.advice)
                ? "시뮬레이션 분석 결과가 없습니다."
                : result.advice;
        }

        // 2. 위험 점수 업데이트 (소수점 버림)
        if (riskResultText != null)
        {
            riskResultText.text = $"위험지수 (S): {Mathf.RoundToInt(result.risk_score)}";
        }

        // 3. 레벨 텍스트 업데이트
        if (roomLevelText != null)
        {
            roomLevelText.text = result.risk_level;
        }

        // 4. 낙하 위험 물건 리스트 업데이트
        if (fallHazardFurnituresList != null)
        {
            string listString = "낙하 위험 물건:\n";
            if (result.fallen != null && result.fallen.Length > 0)
            {
                foreach (string item in result.fallen)
                {
                    listString += $"- {item}\n";
                }
            }
            else
            {
                listString += "- 없음\n";
            }
            fallHazardFurnituresList.text = listString;
        }
    }

    // Manager 스크립트에서 정적으로 호출하는 부분 (그대로 유지)
    public static void ShowResultOnPanel(GameObject panel, SimulationResultResponse result)
    {
        if (panel == null)
        {
            Debug.LogWarning("[SimulationResultPanelController] Simulation success panel is missing.");
            return;
        }

        SimulationResultPanelController controller = panel.GetComponent<SimulationResultPanelController>();
        if (controller == null)
        {
            controller = panel.AddComponent<SimulationResultPanelController>();
        }

        controller.SetResult(result);
    }
}

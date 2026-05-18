using System;
using System.Collections;
using System.Collections.Generic;
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

    [Header("Job Polling")]
    [Min(1)] public int maxJobPollAttempts = 120;
    [Min(0.1f)] public float jobPollIntervalSeconds = 2.0f;

    public event Action<string> SimulationStarted;
    public event Action<string> SimulationCompleted;
    public event Action<string> SimulationFailed;
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
                    scale = ToArray(t.localScale)
                });
            }
        }

        PlacedSceneRequest request = new PlacedSceneRequest
        {
            scan_session_id = sessionId,
            objects = objects.ToArray()
        };

        return JsonUtility.ToJson(request);
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
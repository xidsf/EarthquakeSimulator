using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// scan_session_id 기반 서버 API 호출 전용 클라이언트입니다.
/// 배치 단계에서는 새 세션을 만들지 않고 FurnitureCaptureManager.CurrentScanSessionId를 사용해야 합니다.
/// </summary>
public class FurnitureServerApiClient : MonoBehaviour
{
    [Header("Server")]
    [Tooltip("뒤에 / 를 붙이지 않습니다.")]
    public string baseUrl = "https://api.earquake.xyz";

    [Tooltip("detect-session 호출 시 mode query 값입니다.")]
    public string detectMode = "auto";

    [Tooltip("process-scan 호출 시 use_confirmed=1을 사용할지 여부입니다.")]
    public bool useConfirmedFurniture = true;

    [Header("Timeout")]
    [Min(1)] public int requestTimeoutSeconds = 60;

    [Header("Log")]
    public bool logRequests = true;
    public bool logResponseBody = false;

    // ── 기존 메서드 (하위 호환 유지) ─────────────────────────────────────────

    [Obsolete("Use POST /scan-session/{scan_session_id}/finish followed by GET /scan-job/{scan_session_id}.")]
    public IEnumerator PostDetectSession(string scanSessionId, Action<bool, FurnitureServerStatusResponse, string> onComplete)
    {
        string escapedId = UnityWebRequest.EscapeURL(scanSessionId);
        string escapedMode = UnityWebRequest.EscapeURL(string.IsNullOrWhiteSpace(detectMode) ? "auto" : detectMode);
        string url = BuildUrl($"/detect-session/{escapedId}?mode={escapedMode}");
        yield return SendJsonRequest<FurnitureServerStatusResponse>(url, "POST", onComplete);
    }

    [Obsolete("Manual GLB placement flow no longer calls /detections directly.")]
    public IEnumerator GetDetections(string scanSessionId, Action<bool, FurnitureServerDetectionResponse, string> onComplete)
    {
        string url = BuildUrl($"/detections/{UnityWebRequest.EscapeURL(scanSessionId)}");
        yield return SendJsonRequest<FurnitureServerDetectionResponse>(url, "GET", onComplete);
    }

    [Obsolete("Use POST /scan-session/{scan_session_id}/finish instead of /process-scan.")]
    public IEnumerator PostProcessScan(string scanSessionId, Action<bool, FurnitureServerStatusResponse, string> onComplete)
    {
        string escapedId = UnityWebRequest.EscapeURL(scanSessionId);
        string useConfirmed = useConfirmedFurniture ? "1" : "0";
        string url = BuildUrl($"/process-scan/{escapedId}?use_confirmed={useConfirmed}");
        yield return SendJsonRequest<FurnitureServerStatusResponse>(url, "POST", onComplete);
    }

    public IEnumerator GetResult(string scanSessionId, Action<bool, FurnitureServerResultResponse, string> onComplete)
    {
        string url = BuildUrl($"/result/{UnityWebRequest.EscapeURL(scanSessionId)}");
        yield return SendJsonRequest<FurnitureServerResultResponse>(url, "GET", onComplete);
    }

    public IEnumerator PostFinishScanSession(string scanSessionId, Action<bool, FurnitureServerStatusResponse, string> onComplete)
    {
        string url = BuildUrl($"/scan-session/{UnityWebRequest.EscapeURL(scanSessionId)}/finish");
        yield return SendJsonRequest<FurnitureServerStatusResponse>(url, "POST", onComplete);
    }

    public IEnumerator PostFinishScanSession(
        string scanSessionId,
        string jsonBody,
        Action<bool, FurnitureServerStatusResponse, string> onComplete)
    {
        string url = BuildUrl($"/scan-session/{UnityWebRequest.EscapeURL(scanSessionId)}/finish");
        yield return SendJsonRequest<FurnitureServerStatusResponse>(url, "POST", onComplete, jsonBody);
    }

    public IEnumerator PostPlacedScene(
        string sessionId,
        string jsonBody,
        Action<bool, FurnitureServerStatusResponse, string> onComplete)
    {
        string url = BuildUrl($"/placed-scene/{UnityWebRequest.EscapeURL(sessionId)}");
        yield return SendJsonRequest<FurnitureServerStatusResponse>(url, "POST", onComplete, jsonBody);
    }

    public IEnumerator PostSimulate(
        string sessionId,
        string jsonBody,
        Action<bool, SimulationJobResponse, string> onComplete)
    {
        string url = BuildUrl($"/simulate/{UnityWebRequest.EscapeURL(sessionId)}");
        yield return SendJsonRequest<SimulationJobResponse>(url, "POST", onComplete, jsonBody);
    }

    public IEnumerator GetSimulationJob(
        string sessionId,
        Action<bool, SimulationJobResponse, string> onComplete)
    {
        string url = BuildUrl($"/simulation-job/{UnityWebRequest.EscapeURL(sessionId)}");
        yield return SendJsonRequest<SimulationJobResponse>(url, "GET", onComplete);
    }

    public IEnumerator GetSimulationResult(
        string sessionId,
        Action<bool, SimulationResultResponse, string> onComplete)
    {
        string url = BuildUrl($"/simulation-result/{UnityWebRequest.EscapeURL(sessionId)}");
        yield return SendJsonRequest<SimulationResultResponse>(url, "GET", onComplete);
    }

    public IEnumerator GetSimulationPlayback(
        string sessionId,
        Action<bool, SimulationPlaybackResponse, string> onComplete)
    {
        string url = BuildUrl($"/simulation-playback/{UnityWebRequest.EscapeURL(sessionId)}");
        yield return SendJsonRequest<SimulationPlaybackResponse>(url, "GET", onComplete);
    }

    public IEnumerator GetScanJobStatus(string scanSessionId, Action<bool, FurnitureServerStatusResponse, string> onComplete)
    {
        string url = BuildUrl($"/scan-job/{UnityWebRequest.EscapeURL(scanSessionId)}");
        yield return SendJsonRequest<FurnitureServerStatusResponse>(url, "GET", onComplete);
    }

    /// <summary>
    /// POST /simulation-input/{session_id}
    /// 배치 완료 시 클라이언트가 직렬화한 가구 Transform + 방 형상 JSON을 서버로 전송합니다.
    /// 서버(Python)는 이 입력을 받아 헤드리스 시뮬레이션 Unity 실행에 사용합니다.
    /// </summary>
    public IEnumerator PostSimulationInput(
        string sessionId,
        string jsonBody,
        Action<bool, FurnitureServerStatusResponse, string> onComplete)
    {
        string url = BuildUrl($"/simulation-input/{UnityWebRequest.EscapeURL(sessionId)}");
        yield return SendJsonRequest<FurnitureServerStatusResponse>(url, "POST", onComplete, jsonBody);
    }

    // ── 신규 메서드 ──────────────────────────────────────────────────────────

    /// <summary>
    /// POST /run-pipeline/{session_id}
    /// room_to_unity_pipeline.py를 서버에서 백그라운드로 실행합니다.
    /// 즉시 { status: "processing" } 를 반환하며, 완료 여부는 GetResult polling으로 확인합니다.
    /// force=true 시 이미 완료된 세션도 재실행합니다 (?force=1).
    /// </summary>
    [Obsolete("Use POST /scan-session/{scan_session_id}/finish followed by GET /scan-job/{scan_session_id}.")]
    public IEnumerator PostRunPipeline(string scanSessionId, Action<bool, FurnitureServerStatusResponse, string> onComplete, bool force = false)
    {
        string escaped = UnityWebRequest.EscapeURL(scanSessionId);
        string url = BuildUrl($"/run-pipeline/{escaped}{(force ? "?force=1" : string.Empty)}");
        yield return SendJsonRequest<FurnitureServerStatusResponse>(url, "POST", onComplete);
    }

    /// <summary>
    /// GET /pipeline-status/{session_id}
    /// 파이프라인 실행 상태를 조회합니다 (queued / processing / done / failed / not_started).
    /// </summary>
    [Obsolete("Use GET /scan-job/{scan_session_id}.")]
    public IEnumerator GetPipelineStatus(string scanSessionId, Action<bool, FurnitureServerStatusResponse, string> onComplete)
    {
        string url = BuildUrl($"/pipeline-status/{UnityWebRequest.EscapeURL(scanSessionId)}");
        yield return SendJsonRequest<FurnitureServerStatusResponse>(url, "GET", onComplete);
    }

    private string BuildUrl(string path)
    {
        string root = string.IsNullOrWhiteSpace(baseUrl) ? "https://api.earquake.xyz" : baseUrl.TrimEnd('/');
        return root + path;
    }

    private IEnumerator SendJsonRequest<T>(string url, string method, Action<bool, T, string> onComplete, string jsonBody = null)
        where T : class, new()
    {
        if (logRequests)
        {
            Debug.Log($"[FurnitureServerApiClient] {method} {url}");
        }

        using (UnityWebRequest request = new UnityWebRequest(url, method))
        {
            request.timeout = requestTimeoutSeconds;
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Accept", "application/json");

            if (!string.IsNullOrWhiteSpace(jsonBody))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody));
                request.SetRequestHeader("Content-Type", "application/json");
            }

            yield return request.SendWebRequest();

            bool success = request.result == UnityWebRequest.Result.Success;
            string body = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;

            if (logResponseBody)
            {
                Debug.Log($"[FurnitureServerApiClient] Response {request.responseCode}: {body}");
            }

            if (!success)
            {
                string error = $"HTTP {(long)request.responseCode} / {request.error} / {body}";
                Debug.LogWarning($"[FurnitureServerApiClient] Request failed. {error}");
                onComplete?.Invoke(false, null, error);
                yield break;
            }

            if (string.IsNullOrWhiteSpace(body))
            {
                onComplete?.Invoke(true, new T(), string.Empty);
                yield break;
            }

            try
            {
                T parsed = JsonUtility.FromJson<T>(body);
                if (parsed is IFurnitureServerRawJsonResponse rawJsonResponse)
                {
                    rawJsonResponse.RawJson = body;
                }

                onComplete?.Invoke(true, parsed, string.Empty);
            }
            catch (Exception e)
            {
                if (typeof(IFurnitureServerRawJsonResponse).IsAssignableFrom(typeof(T)))
                {
                    T fallback = new T();
                    ((IFurnitureServerRawJsonResponse)fallback).RawJson = body;
                    Debug.LogWarning($"[FurnitureServerApiClient] JSON parse failed; returning raw response. {e.Message}");
                    onComplete?.Invoke(true, fallback, string.Empty);
                    yield break;
                }

                string error = $"JSON parse failed. {e.Message}. body:{body}";
                Debug.LogWarning($"[FurnitureServerApiClient] {error}");
                onComplete?.Invoke(false, null, error);
            }
        }
    }
}

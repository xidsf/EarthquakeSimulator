using System;
using System.Collections;
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

    public IEnumerator PostDetectSession(string scanSessionId, Action<bool, FurnitureServerStatusResponse, string> onComplete)
    {
        string url = BuildDetectSessionUrl(scanSessionId);
        yield return SendJsonRequest<FurnitureServerStatusResponse>(url, "POST", onComplete);
    }

    public IEnumerator GetDetections(string scanSessionId, Action<bool, FurnitureServerDetectionResponse, string> onComplete)
    {
        string url = BuildUrl($"/detections/{UnityWebRequest.EscapeURL(scanSessionId)}");
        yield return SendJsonRequest<FurnitureServerDetectionResponse>(url, "GET", onComplete);
    }

    public IEnumerator PostProcessScan(string scanSessionId, Action<bool, FurnitureServerStatusResponse, string> onComplete)
    {
        string url = BuildProcessScanUrl(scanSessionId);
        yield return SendJsonRequest<FurnitureServerStatusResponse>(url, "POST", onComplete);
    }

    public IEnumerator GetResult(string scanSessionId, Action<bool, FurnitureServerResultResponse, string> onComplete)
    {
        string url = BuildUrl($"/result/{UnityWebRequest.EscapeURL(scanSessionId)}");
        yield return SendJsonRequest<FurnitureServerResultResponse>(url, "GET", onComplete);
    }

    private string BuildDetectSessionUrl(string scanSessionId)
    {
        string escapedId = UnityWebRequest.EscapeURL(scanSessionId);
        string escapedMode = UnityWebRequest.EscapeURL(string.IsNullOrWhiteSpace(detectMode) ? "auto" : detectMode);
        return BuildUrl($"/detect-session/{escapedId}?mode={escapedMode}");
    }

    private string BuildProcessScanUrl(string scanSessionId)
    {
        string escapedId = UnityWebRequest.EscapeURL(scanSessionId);
        string useConfirmed = useConfirmedFurniture ? "1" : "0";
        return BuildUrl($"/process-scan/{escapedId}?use_confirmed={useConfirmed}");
    }

    private string BuildUrl(string path)
    {
        string root = string.IsNullOrWhiteSpace(baseUrl) ? "https://api.earquake.xyz" : baseUrl.TrimEnd('/');
        return root + path;
    }

    private IEnumerator SendJsonRequest<T>(string url, string method, Action<bool, T, string> onComplete)
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
                onComplete?.Invoke(true, parsed, string.Empty);
            }
            catch (Exception e)
            {
                string error = $"JSON parse failed. {e.Message}. body:{body}";
                Debug.LogWarning($"[FurnitureServerApiClient] {error}");
                onComplete?.Invoke(false, null, error);
            }
        }
    }
}

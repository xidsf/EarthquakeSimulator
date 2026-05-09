using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

public class FurnitureSessionClient : MonoBehaviour
{
    [Header("Session API")]
    public string startSessionUrl = "https://api.earquake.xyz/scan-session/start";
    public string scanFrameUrl = "https://api.earquake.xyz/scan-frame";
    public string processScanBaseUrl = "https://api.earquake.xyz/process-scan/";
    public string resultBaseUrl = "https://api.earquake.xyz/result/";

    public IEnumerator StartSession(Action<string> onStarted)
    {
        WWWForm form = new WWWForm();

        using (UnityWebRequest request = UnityWebRequest.Post(startSessionUrl, form))
        {
            request.timeout = 30;
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[FurnitureSession] StartSession failed: {request.error}");
                Debug.LogError(request.downloadHandler.text);
                yield break;
            }

            string json = request.downloadHandler.text;
            Debug.Log($"[FurnitureSession] StartSession response: {json}");

            ScanSessionStartResponse response =
                JsonUtility.FromJson<ScanSessionStartResponse>(json);

            if (response == null || string.IsNullOrEmpty(response.scan_session_id))
            {
                Debug.LogError("[FurnitureSession] scan_session_id is empty.");
                yield break;
            }

            onStarted?.Invoke(response.scan_session_id);
        }
    }

    public IEnumerator UploadFrame(
        string scanSessionId,
        string frameId,
        Texture2D image,
        string metadataJson,
        Action<ScanFrameUploadResponse> onUploaded)
    {
        byte[] jpgBytes = image.EncodeToJPG(85);

        WWWForm form = new WWWForm();
        form.AddBinaryData("image", jpgBytes, $"{frameId}.jpg", "image/jpeg");
        form.AddField("metadata", metadataJson);

        using (UnityWebRequest request = UnityWebRequest.Post(scanFrameUrl, form))
        {
            request.timeout = 60;
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[FurnitureSession] UploadFrame failed: {request.error}");
                Debug.LogError(request.downloadHandler.text);
                yield break;
            }

            string json = request.downloadHandler.text;
            Debug.Log($"[FurnitureSession] UploadFrame response: {json}");

            ScanFrameUploadResponse response =
                JsonUtility.FromJson<ScanFrameUploadResponse>(json);

            onUploaded?.Invoke(response);
        }
    }

    public IEnumerator ProcessScan(
        string scanSessionId,
        Action<ProcessScanResponse> onProcessed)
    {
        string url = processScanBaseUrl + scanSessionId;

        WWWForm form = new WWWForm();

        using (UnityWebRequest request = UnityWebRequest.Post(url, form))
        {
            request.timeout = 60;
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[FurnitureSession] ProcessScan failed: {request.error}");
                Debug.LogError(request.downloadHandler.text);
                yield break;
            }

            string json = request.downloadHandler.text;
            Debug.Log($"[FurnitureSession] ProcessScan response: {json}");

            ProcessScanResponse response =
                JsonUtility.FromJson<ProcessScanResponse>(json);

            onProcessed?.Invoke(response);
        }
    }

    public IEnumerator LoadSessionResult(
        string scanSessionId,
        Action<FurnitureResultResponse> onCompleted)
    {
        string url = resultBaseUrl + scanSessionId;

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            request.timeout = 60;
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[FurnitureSession] LoadSessionResult failed: {request.error}");
                Debug.LogError(request.downloadHandler.text);
                yield break;
            }

            string json = request.downloadHandler.text;
            Debug.Log($"[FurnitureSession] Result response: {json}");

            FurnitureResultResponse result =
                JsonUtility.FromJson<FurnitureResultResponse>(json);

            if (result == null || result.objects == null)
            {
                Debug.LogError("[FurnitureSession] Failed to parse result.");
                yield break;
            }

            onCompleted?.Invoke(result);
        }
    }
}
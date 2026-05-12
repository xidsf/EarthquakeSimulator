using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

public class FurnitureScanClient : MonoBehaviour
{

    string scanUrl = "https://api.earquake.xyz/scan";
    string resultBaseUrl = "https://api.earquake.xyz/result/";

    public IEnumerator SendScan(
        Texture2D image,
        string metadataJson,
        Action<FurnitureResultResponse> onCompleted)
    {
        byte[] jpgBytes = image.EncodeToJPG(80);

        WWWForm form = new WWWForm();
        form.AddBinaryData("image", jpgBytes, "capture.jpg", "image/jpeg");
        form.AddField("metadata", metadataJson);

        using (UnityWebRequest request = UnityWebRequest.Post(scanUrl, form))
        {
            request.timeout = 60;
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[FurnitureScan] /scan failed: {request.error}");
                Debug.LogError(request.downloadHandler.text);
                yield break;
            }

            string json = request.downloadHandler.text;
            Debug.Log($"[FurnitureScan] scan response: {json}");

            FurnitureScanResponse scanResponse =
                JsonUtility.FromJson<FurnitureScanResponse>(json);

            if (string.IsNullOrEmpty(scanResponse.job_id))
            {
                Debug.LogError("[FurnitureScan] job_id is empty.");
                yield break;
            }

            yield return StartCoroutine(PollResult(scanResponse.job_id, onCompleted));
        }
    }

    private IEnumerator PollResult(
        string jobId,
        Action<FurnitureResultResponse> onCompleted)
    {
        string url = resultBaseUrl + jobId;

        for (int i = 0; i < 20; i++)
        {
            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                request.timeout = 30;
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[FurnitureScan] /result failed: {request.error}");
                    Debug.LogError(request.downloadHandler.text);
                    yield break;
                }

                string json = request.downloadHandler.text;
                Debug.Log($"[FurnitureScan] result response: {json}");

                FurnitureResultResponse result =
                    JsonUtility.FromJson<FurnitureResultResponse>(json);

                if (result.status == "completed")
                {
                    onCompleted?.Invoke(result);
                    yield break;
                }
            }

            yield return new WaitForSeconds(1.0f);
        }

        Debug.LogError("[FurnitureScan] result polling timeout.");
    }

    [Obsolete("Use LoadSessionResult(scanSessionId). /result/latest must not be used because it can return another scan session.")]
    public IEnumerator LoadLatestResult(Action<FurnitureResultResponse> onCompleted)
    {
        Debug.LogError("[FurnitureScan] /result/latest is disabled. Use LoadSessionResult(scan_session_id).");
        yield break;
    }

    public IEnumerator LoadSessionResult(string scanSessionId, Action<FurnitureResultResponse> onCompleted)
    {
        if (string.IsNullOrWhiteSpace(scanSessionId))
        {
            Debug.LogError("[FurnitureScan] scanSessionId is empty.");
            yield break;
        }

        string url = resultBaseUrl + UnityWebRequest.EscapeURL(scanSessionId);

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            request.timeout = 60;

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[FurnitureScan] /result/{{scan_session_id}} failed: {request.error}");
                Debug.LogError(request.downloadHandler.text);
                yield break;
            }

            string json = request.downloadHandler.text;
            Debug.Log($"[FurnitureScan] Session result response: {json}");

            FurnitureResultResponse result =
                JsonUtility.FromJson<FurnitureResultResponse>(json);

            if (result == null || result.objects == null)
            {
                Debug.LogError("[FurnitureScan] Failed to parse session result.");
                yield break;
            }

            if (!string.IsNullOrWhiteSpace(result.scan_session_id) && result.scan_session_id != scanSessionId)
            {
                Debug.LogError($"[FurnitureScan] Wrong scan result received. expected:{scanSessionId}, actual:{result.scan_session_id}");
                yield break;
            }

            onCompleted?.Invoke(result);
        }
    }
}

using System.Collections;
using UnityEngine;

public class FurnitureSessionTester : MonoBehaviour
{
    public FurnitureSessionClient sessionClient;
    public FurnitureObjectFactory objectFactory;
    public Transform furnitureRoot;

    public void RunSessionTest()
    {
        ClearPreviousFurniture();
        StartCoroutine(RunSessionTestRoutine());
    }

    private IEnumerator RunSessionTestRoutine()
    {
        string scanSessionId = null;

        yield return StartCoroutine(sessionClient.StartSession(id =>
        {
            scanSessionId = id;
        }));

        if (string.IsNullOrEmpty(scanSessionId))
        {
            Debug.LogError("[FurnitureSessionTester] Failed to create scan session.");
            yield break;
        }

        Debug.Log($"[FurnitureSessionTester] Created session: {scanSessionId}");

        Texture2D frame1 = CreateTestTexture(640, 480, Color.gray);
        Texture2D frame2 = CreateTestTexture(640, 480, Color.white);

        string metadata1 = CreateFakeMetadataJson(scanSessionId, "frame_0001", 640, 480);
        string metadata2 = CreateFakeMetadataJson(scanSessionId, "frame_0002", 640, 480);

        yield return StartCoroutine(sessionClient.UploadFrame(
            scanSessionId,
            "frame_0001",
            frame1,
            metadata1,
            response =>
            {
                Debug.Log($"[FurnitureSessionTester] Uploaded {response.frame_id}");
            }
        ));

        yield return StartCoroutine(sessionClient.UploadFrame(
            scanSessionId,
            "frame_0002",
            frame2,
            metadata2,
            response =>
            {
                Debug.Log($"[FurnitureSessionTester] Uploaded {response.frame_id}");
            }
        ));

        yield return StartCoroutine(sessionClient.FinishScan(
            scanSessionId,
            response =>
            {
                Debug.Log($"[FurnitureSessionTester] Finish requested. Frame count: {response.frame_count}");
            }
        ));

        yield return StartCoroutine(sessionClient.PollScanJob(
            scanSessionId,
            response =>
            {
                Debug.Log($"[FurnitureSessionTester] Scan job completed. Object count: {response.object_count}");
            }
        ));

        yield return StartCoroutine(sessionClient.LoadSessionResult(
            scanSessionId,
            OnSessionResultLoaded
        ));
    }

    private void OnSessionResultLoaded(FurnitureResultResponse result)
    {
        Debug.Log($"[FurnitureSessionTester] Result loaded. Object count: {result.objects.Count}");

        foreach (FurnitureObjectResult obj in result.objects)
        {
            Debug.Log($"[FurnitureSessionTester] Creating object: {obj.label}");
            objectFactory.CreateFromResult(obj);
        }
    }

    private Texture2D CreateTestTexture(int width, int height, Color color)
    {
        Texture2D texture = new Texture2D(width, height, TextureFormat.RGB24, false);

        Color[] pixels = new Color[width * height];

        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = color;

        texture.SetPixels(pixels);
        texture.Apply();

        return texture;
    }

    private string CreateFakeMetadataJson(
        string scanSessionId,
        string frameId,
        int width,
        int height)
    {
        return $@"
        {{
            ""scan_session_id"": ""{scanSessionId}"",
            ""frame_id"": ""{frameId}"",
            ""image_width"": {width},
            ""image_height"": {height},
            ""camera_to_world"": [1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1],
            ""projection_matrix"": [1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1],
            ""floor"": {{
                ""normal"": [0,1,0],
                ""point"": [0,0,0]
            }},
            ""walls"": []
        }}";
    }

    private void ClearPreviousFurniture()
    {
        if (furnitureRoot == null)
            return;

        for (int i = furnitureRoot.childCount - 1; i >= 0; i--)
        {
            Destroy(furnitureRoot.GetChild(i).gameObject);
        }
    }
}

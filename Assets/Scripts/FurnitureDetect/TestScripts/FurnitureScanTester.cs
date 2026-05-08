using UnityEngine;

public class FurnitureScanTester : MonoBehaviour
{
    public FurnitureScanClient scanClient;
    public FurnitureObjectFactory objectFactory;

    public void RunTest()
    {
        Texture2D tex = new Texture2D(640, 480, TextureFormat.RGB24, false);

        Color[] pixels = new Color[640 * 480];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = Color.gray;

        tex.SetPixels(pixels);
        tex.Apply();

        string metadataJson = CreateFakeMetadataJson();

        StartCoroutine(scanClient.SendScan(tex, metadataJson, OnScanCompleted));


    }

    public void RunLatestMeshTest()
    {
        StartCoroutine(scanClient.LoadLatestResult(OnScanCompleted));
    }

    private string CreateFakeMetadataJson()
    {
        return @"
        {
            ""frame_id"": ""unity_test_frame"",
            ""image_width"": 640,
            ""image_height"": 480,
            ""camera_to_world"": [1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1],
            ""projection_matrix"": [1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1],
            ""floor"": {
                ""normal"": [0,1,0],
                ""point"": [0,0,0]
            },
            ""walls"": []
        }";
    }

    private void OnScanCompleted(FurnitureResultResponse result)
    {
        Debug.Log($"[FurnitureScanTester] Completed. Object count: {result.objects.Count}");

        foreach (var obj in result.objects)
        {
            Debug.Log($"Detected furniture: {obj.label}");
            objectFactory.CreateFromResult(obj);
        }
    }
}
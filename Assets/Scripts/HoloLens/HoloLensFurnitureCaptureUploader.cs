using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

#if WINDOWS_UWP || UNITY_WSA
using UnityEngine.Windows.WebCam;
#endif

public class HoloLensFurnitureCaptureUploader : MonoBehaviour
{
    [Header("Server")]
    [SerializeField]
    private string detectUrl = "https://api.earquake.xyz/detect";

    [Header("Capture")]
    [SerializeField]
    private bool useHighestResolution = false;

    [SerializeField]
    private int preferredWidth = 1280;

    [SerializeField]
    private int preferredHeight = 720;

    [SerializeField]
    [Range(1, 100)]
    private int jpgQuality = 90;

    [Tooltip("가구 인식용이므로 기본값은 0 권장")]
    [SerializeField]
    private bool includeHologramsInPhoto = false;

    [Header("Upload")]
    [SerializeField]
    private int requestTimeoutSeconds = 30;

    [SerializeField]
    private string scanId = "room_scan_001";

    [SerializeField]
    private string sourceName = "hololens2_furniture_capture";

    [Header("Optional Room Context")]
    [TextArea(3, 10)]
    [SerializeField]
    private string roomContextJson = "";

    [Header("Debug")]
    [SerializeField]
    private bool isBusy = false;

    [SerializeField]
    private string lastStatus = "";

#if WINDOWS_UWP || UNITY_WSA
    private PhotoCapture photoCaptureObject;
    private Resolution cameraResolution;
#endif

    private int frameIndex = 0;

    // MRTK PressableButton에 이 메서드를 연결하면 됨
    public void CaptureAndUploadOneFrame()
    {
        if (isBusy)
        {
            Debug.LogWarning("[FurnitureCapture] 이미 촬영/업로드 중입니다.");
            return;
        }

#if WINDOWS_UWP || UNITY_WSA
        StartPhotoCapture();
#else
        Debug.LogWarning(
            "[FurnitureCapture] PhotoCapture는 UWP/HoloLens 빌드에서 동작합니다. " +
            "Editor에서는 기존 HTTPTest.cs의 UploadInspectorTextures()로 서버 연결만 테스트하세요."
        );
#endif
    }

#if WINDOWS_UWP || UNITY_WSA
    private void StartPhotoCapture()
    {
        isBusy = true;
        lastStatus = "Creating PhotoCapture";
        Debug.Log("[FurnitureCapture] PhotoCapture 생성 시작");

        PhotoCapture.CreateAsync(false, OnPhotoCaptureCreated);
    }

    private void OnPhotoCaptureCreated(PhotoCapture captureObject)
    {
        if (captureObject == null)
        {
            Fail("PhotoCapture 생성 실패");
            return;
        }

        photoCaptureObject = captureObject;
        cameraResolution = SelectCameraResolution();

        CameraParameters cameraParameters = new CameraParameters
        {
            hologramOpacity = includeHologramsInPhoto ? 1.0f : 0.0f,
            cameraResolutionWidth = cameraResolution.width,
            cameraResolutionHeight = cameraResolution.height,
            pixelFormat = CapturePixelFormat.BGRA32
        };

        lastStatus = $"Starting photo mode {cameraResolution.width}x{cameraResolution.height}";
        Debug.Log($"[FurnitureCapture] Photo mode 시작: {cameraResolution.width}x{cameraResolution.height}");

        photoCaptureObject.StartPhotoModeAsync(cameraParameters, OnPhotoModeStarted);
    }

    private Resolution SelectCameraResolution()
    {
        List<Resolution> resolutions = PhotoCapture.SupportedResolutions.ToList();

        if (resolutions == null || resolutions.Count == 0)
        {
            Debug.LogWarning("[FurnitureCapture] SupportedResolutions가 비어있습니다. 기본 Resolution 사용");
            return new Resolution
            {
                width = preferredWidth,
                height = preferredHeight,
                refreshRateRatio = new RefreshRate
                {
                    numerator = 30,
                    denominator = 1
                }
            };
        }

        if (useHighestResolution)
        {
            return resolutions
                .OrderByDescending(r => r.width * r.height)
                .First();
        }

        return resolutions
            .OrderBy(r =>
                Mathf.Abs(r.width - preferredWidth) +
                Mathf.Abs(r.height - preferredHeight)
            )
            .First();
    }

    private void OnPhotoModeStarted(PhotoCapture.PhotoCaptureResult result)
    {
        if (!result.success)
        {
            CleanupPhotoCapture();
            Fail("Photo mode 시작 실패");
            return;
        }

        lastStatus = "Taking photo";
        Debug.Log("[FurnitureCapture] 사진 촬영 시작");

        photoCaptureObject.TakePhotoAsync(OnCapturedPhotoToMemory);
    }

    private void OnCapturedPhotoToMemory(
        PhotoCapture.PhotoCaptureResult result,
        PhotoCaptureFrame photoCaptureFrame)
    {
        if (!result.success || photoCaptureFrame == null)
        {
            CleanupPhotoCapture();
            Fail("사진 촬영 실패");
            return;
        }

        Matrix4x4 cameraToWorld = Matrix4x4.identity;
        Matrix4x4 projection = Matrix4x4.identity;

        bool hasCameraToWorld = photoCaptureFrame.TryGetCameraToWorldMatrix(out cameraToWorld);
        bool hasProjection = photoCaptureFrame.TryGetProjectionMatrix(out projection);

        byte[] jpgBytes;

        try
        {
            jpgBytes = ConvertPhotoFrameToJpg(photoCaptureFrame);
        }
        catch (Exception e)
        {
            CleanupPhotoCapture();
            Fail($"사진 JPG 변환 실패: {e.Message}");
            return;
        }

        string clientFrameId = Guid.NewGuid().ToString();
        string fileName = $"{clientFrameId}.jpg";

        CaptureMetadata metadata = BuildMetadata(
            clientFrameId,
            cameraResolution.width,
            cameraResolution.height,
            cameraToWorld,
            projection,
            hasCameraToWorld,
            hasProjection
        );

        string metadataJson = JsonUtility.ToJson(metadata);

        photoCaptureObject.StopPhotoModeAsync(_ =>
        {
            CleanupPhotoCapture();
            StartCoroutine(UploadCapturedImageCoroutine(jpgBytes, fileName, metadataJson));
        });
    }

    private byte[] ConvertPhotoFrameToJpg(PhotoCaptureFrame photoCaptureFrame)
    {
        List<byte> imageBuffer = new List<byte>();
        photoCaptureFrame.CopyRawImageDataIntoBuffer(imageBuffer);

        Texture2D texture = new Texture2D(
            cameraResolution.width,
            cameraResolution.height,
            TextureFormat.BGRA32,
            false
        );

        texture.LoadRawTextureData(imageBuffer.ToArray());
        texture.Apply();

        byte[] jpgBytes = texture.EncodeToJPG(jpgQuality);

        Destroy(texture);

        return jpgBytes;
    }

    private void CleanupPhotoCapture()
    {
        if (photoCaptureObject != null)
        {
            photoCaptureObject.Dispose();
            photoCaptureObject = null;
        }
    }
#endif

    private CaptureMetadata BuildMetadata(
        string clientFrameId,
        int imageWidth,
        int imageHeight,
        Matrix4x4 photoCameraToWorld,
        Matrix4x4 photoProjection,
        bool hasPhotoCameraToWorld,
        bool hasPhotoProjection)
    {
        Transform unityCameraTransform = Camera.main != null ? Camera.main.transform : null;

        Matrix4x4 unityCameraToWorld = Matrix4x4.identity;

        if (unityCameraTransform != null)
        {
            unityCameraToWorld = unityCameraTransform.localToWorldMatrix;
        }

        CaptureMetadata metadata = new CaptureMetadata
        {
            scan_id = scanId,
            client_frame_id = clientFrameId,
            frame_index = frameIndex,
            source = sourceName,

            image_width = imageWidth,
            image_height = imageHeight,

            unity_time = Time.time,
            unix_time_ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),

            has_photo_camera_to_world = hasPhotoCameraToWorld,
            has_photo_projection = hasPhotoProjection,

            photo_camera_to_world = MatrixToDto(photoCameraToWorld),
            photo_projection = MatrixToDto(photoProjection),
            unity_camera_to_world = MatrixToDto(unityCameraToWorld),

            room_context_json = roomContextJson
        };

        frameIndex++;

        return metadata;
    }

    private IEnumerator UploadCapturedImageCoroutine(
        byte[] imageBytes,
        string fileName,
        string metadataJson)
    {
        if (imageBytes == null || imageBytes.Length == 0)
        {
            Fail("업로드할 이미지 바이트가 비어있습니다.");
            yield break;
        }

        lastStatus = "Uploading image";
        Debug.Log($"[FurnitureCapture] 업로드 시작: {fileName}, bytes: {imageBytes.Length}");

        List<IMultipartFormSection> formData = new List<IMultipartFormSection>
        {
            new MultipartFormFileSection(
                "image",
                imageBytes,
                fileName,
                "image/jpeg"
            ),

            new MultipartFormDataSection("metadata_json", metadataJson),
            new MultipartFormDataSection("source", sourceName),
            new MultipartFormDataSection("scan_id", scanId),
            new MultipartFormDataSection("client_frame_index", frameIndex.ToString() ),
        };

        using (UnityWebRequest request = UnityWebRequest.Post(detectUrl, formData))
        {
            request.timeout = requestTimeoutSeconds;

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Fail(
                    "업로드 실패\n" +
                    $"responseCode: {request.responseCode}\n" +
                    $"error: {request.error}\n" +
                    $"body: {request.downloadHandler.text}"
                );
                yield break;
            }

            string responseText = request.downloadHandler.text;

            lastStatus = "Upload success";
            Debug.Log($"[FurnitureCapture] 업로드 성공: {responseText}");

            try
            {
                FurnitureDetectResponse response =
                    JsonUtility.FromJson<FurnitureDetectResponse>(responseText);

                if (response != null && response.objects != null)
                {
                    foreach (FurnitureDetectedObject obj in response.objects)
                    {
                        Debug.Log(
                            "[FurnitureCapture] Detected " +
                            $"id: {obj.id}, label: {obj.label}, confidence: {obj.confidence}"
                        );
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[FurnitureCapture] 응답 JSON 파싱 실패: {e.Message}");
            }
        }

        isBusy = false;
    }

    private void Fail(string message)
    {
        lastStatus = message;
        isBusy = false;
        Debug.LogError($"[FurnitureCapture] {message}");
    }

    private Matrix4x4Dto MatrixToDto(Matrix4x4 matrix)
    {
        Matrix4x4Dto dto = new Matrix4x4Dto();
        dto.values = new float[16];

        int index = 0;

        for (int row = 0; row < 4; row++)
        {
            for (int col = 0; col < 4; col++)
            {
                dto.values[index] = matrix[row, col];
                index++;
            }
        }

        return dto;
    }
}

[Serializable]
public class CaptureMetadata
{
    public string scan_id;
    public string client_frame_id;
    public int frame_index;
    public string source;

    public int image_width;
    public int image_height;

    public float unity_time;
    public long unix_time_ms;

    public bool has_photo_camera_to_world;
    public bool has_photo_projection;

    public Matrix4x4Dto photo_camera_to_world;
    public Matrix4x4Dto photo_projection;
    public Matrix4x4Dto unity_camera_to_world;

    public string room_context_json;
}

[Serializable]
public class Matrix4x4Dto
{
    public float[] values;
}

[Serializable]
public class FurnitureDetectResponse
{
    public string frame_id;
    public FurnitureDetectedObject[] objects;
}

[Serializable]
public class FurnitureDetectedObject
{
    public string id;
    public string label;
    public float confidence;
    public int[] bbox;
    public string mesh_url;
}
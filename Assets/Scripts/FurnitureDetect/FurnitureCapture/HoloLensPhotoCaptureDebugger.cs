using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

#if UNITY_WSA && !UNITY_EDITOR
using UnityEngine.Windows.WebCam;
#endif

public class HoloLensPhotoCaptureDebugger : MonoBehaviour
{
    [Header("Debug Save")]
    [SerializeField] private string outputFolderName = "FurnitureCaptureDebug";
    [SerializeField, Range(1, 100)] private int jpegQuality = 90;

    [Header("Room Info")]
    [SerializeField] private RoomGeometrySnapshotProvider roomSnapshotProvider;

    private bool isCapturing = false;

#if UNITY_WSA && !UNITY_EDITOR
    private PhotoCapture photoCaptureObject;
    private Resolution selectedResolution;
#endif

    public void CapturePhotoForDebug()
    {
        if (isCapturing)
        {
            Debug.LogWarning("[CaptureDebugger] 이미 촬영 중입니다.");
            return;
        }

        isCapturing = true;

#if UNITY_WSA && !UNITY_EDITOR
        Debug.Log("[CaptureDebugger] HoloLens2 PhotoCapture 경로 실행: 실제 RGB/PV 카메라 촬영");
        StartHoloLensPhotoCapture();
#else
        Debug.LogError(
            "[CaptureDebugger] 현재 실행 환경에서는 실제 HoloLens2 RGB/PV 카메라 촬영을 수행하지 않습니다.\n" +
            "Unity Editor / Holographic Remoting / Standalone에서는 GameView 캡처를 하지 않도록 막아두었습니다.\n" +
            "실제 환경 사진은 HoloLens2 UWP 빌드에서 CapturePhotoForDebug()를 실행해야 얻을 수 있습니다."
        );
        isCapturing = false;
#endif
    }

    public void SetRoomObjects(
        Transform root,
        Transform floor,
        Transform ceiling,
        IList<Transform> walls)
    {
        if (roomSnapshotProvider == null)
        {
            Debug.LogWarning("[CaptureDebugger] roomSnapshotProvider가 연결되어 있지 않습니다.");
            return;
        }

        roomSnapshotProvider.SetRoomObjects(root, floor, ceiling, walls);
    }

    public void SetRoomObjects(
        Transform floor,
        Transform ceiling,
        IList<Transform> walls)
    {
        SetRoomObjects(null, floor, ceiling, walls);
    }

    public void SetRoomGameObjects(
        GameObject root,
        GameObject floor,
        GameObject ceiling,
        IList<GameObject> walls)
    {
        if (roomSnapshotProvider == null)
        {
            Debug.LogWarning("[CaptureDebugger] roomSnapshotProvider가 연결되어 있지 않습니다.");
            return;
        }

        roomSnapshotProvider.SetRoomGameObjects(root, floor, ceiling, walls);
    }

#if UNITY_WSA && !UNITY_EDITOR

    private void StartHoloLensPhotoCapture()
    {
        List<Resolution> resolutions = PhotoCapture.SupportedResolutions
            .OrderByDescending(resolution => resolution.width * resolution.height)
            .ToList();

        if (resolutions.Count == 0)
        {
            FinishWithError("지원되는 카메라 해상도가 없습니다.");
            return;
        }

        selectedResolution = resolutions[0];

        PhotoCapture.CreateAsync(false, captureObject =>
        {
            if (captureObject == null)
            {
                FinishWithError("PhotoCapture 객체 생성 실패");
                return;
            }

            photoCaptureObject = captureObject;

            CameraParameters cameraParameters = new CameraParameters
            {
                hologramOpacity = 0.0f,
                cameraResolutionWidth = selectedResolution.width,
                cameraResolutionHeight = selectedResolution.height,
                pixelFormat = CapturePixelFormat.BGRA32
            };

            photoCaptureObject.StartPhotoModeAsync(cameraParameters, OnPhotoModeStarted);
        });
    }

    private void OnPhotoModeStarted(PhotoCapture.PhotoCaptureResult result)
    {
        if (!result.success)
        {
            CleanupPhotoCapture();
            FinishWithError("PhotoMode 시작 실패");
            return;
        }

        photoCaptureObject.TakePhotoAsync(OnCapturedPhotoToMemory);
    }

    private void OnCapturedPhotoToMemory(PhotoCapture.PhotoCaptureResult result, PhotoCaptureFrame frame)
    {
        if (!result.success || frame == null)
        {
            StopPhotoModeThen(() => FinishWithError("사진 메모리 캡처 실패"));
            return;
        }

        Matrix4x4 cameraToWorld = Matrix4x4.identity;
        Matrix4x4 projection = Matrix4x4.identity;

        bool hasCameraToWorld = frame.TryGetCameraToWorldMatrix(out cameraToWorld);
        bool hasProjection = frame.TryGetProjectionMatrix(out projection);

        Texture2D texture = new Texture2D(
            selectedResolution.width,
            selectedResolution.height,
            TextureFormat.BGRA32,
            false
        );

        frame.UploadImageDataToTexture(texture);
        byte[] jpgBytes = texture.EncodeToJPG(jpegQuality);
        Destroy(texture);

        string captureId = CreateCaptureId();

        FurnitureCaptureMetadata metadata = BuildMetadata(
            captureId,
            captureId + ".jpg",
            selectedResolution.width,
            selectedResolution.height,
            hasCameraToWorld,
            cameraToWorld,
            hasProjection,
            projection,
            "HoloLens2_PhotoCapture"
        );

        StopPhotoModeThen(() =>
        {
            SaveCaptureDebugFiles(captureId, jpgBytes, metadata);
            isCapturing = false;
        });
    }

    private void StopPhotoModeThen(Action afterStop)
    {
        if (photoCaptureObject == null)
        {
            afterStop?.Invoke();
            return;
        }

        photoCaptureObject.StopPhotoModeAsync(result =>
        {
            CleanupPhotoCapture();
            afterStop?.Invoke();
        });
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

    private FurnitureCaptureMetadata BuildMetadata(
        string captureId,
        string imageFileName,
        int imageWidth,
        int imageHeight,
        bool hasCameraToWorld,
        Matrix4x4 cameraToWorld,
        bool hasProjection,
        Matrix4x4 projection,
        string source)
    {
        return new FurnitureCaptureMetadata
        {
            capture_id = captureId,
            timestamp_utc = DateTime.UtcNow.ToString("o"),
            source = source,

            image_file_name = imageFileName,
            image_width = imageWidth,
            image_height = imageHeight,

            has_camera_to_world = hasCameraToWorld,
            has_projection = hasProjection,

            camera_to_world = FurnitureCaptureSerialization.MatrixToRowMajorArray(cameraToWorld),
            projection = FurnitureCaptureSerialization.MatrixToRowMajorArray(projection),

            approx_intrinsics = hasProjection
                ? FurnitureCaptureSerialization.BuildApproxIntrinsicsFromProjection(
                    projection,
                    imageWidth,
                    imageHeight
                )
                : null,

            room = roomSnapshotProvider != null
                ? roomSnapshotProvider.BuildSnapshot()
                : null
        };
    }

    private void SaveCaptureDebugFiles(
        string captureId,
        byte[] jpgBytes,
        FurnitureCaptureMetadata metadata)
    {
        if (jpgBytes == null || jpgBytes.Length == 0)
        {
            Debug.LogError("[CaptureDebugger] 저장할 JPG 바이트가 비어있습니다.");
            return;
        }

        string rootFolder = Path.Combine(Application.persistentDataPath, outputFolderName);
        string captureFolder = Path.Combine(rootFolder, captureId);

        Directory.CreateDirectory(captureFolder);

        string imagePath = Path.Combine(captureFolder, captureId + ".jpg");
        string metadataPath = Path.Combine(captureFolder, captureId + ".json");

        File.WriteAllBytes(imagePath, jpgBytes);

        string metadataJson = JsonUtility.ToJson(metadata, true);
        File.WriteAllText(metadataPath, metadataJson);

        Debug.Log(
            "[CaptureDebugger] 촬영 디버그 파일 저장 완료\n" +
            $"persistentDataPath: {Application.persistentDataPath}\n" +
            $"captureId: {captureId}\n" +
            $"imagePath: {imagePath}\n" +
            $"metadataPath: {metadataPath}"
        );
    }

    private string CreateCaptureId()
    {
        return "capture_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmssfff");
    }

    private void FinishWithError(string message)
    {
        Debug.LogError("[CaptureDebugger] " + message);
        isCapturing = false;
    }
}

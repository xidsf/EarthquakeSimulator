using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

#if UNITY_WSA && !UNITY_EDITOR
using UnityEngine.Windows.WebCam;
#endif

/// <summary>
/// HoloLens2 실제 RGB/PV 카메라 사진 촬영, 디버그 저장, 서버 전송을 담당하는 단일 컴포넌트입니다.
///
/// 책임 범위:
/// - HoloLens2 PhotoCapture로 실제 환경 사진 촬영
/// - JPG + metadata JSON을 Application.persistentDataPath에 저장
/// - 저장된 JPG + metadata JSON을 서버로 Multipart 전송
/// - 외부에서 전달받은 floor / ceiling / walls 정보를 metadata에 포함
///
/// 책임 범위가 아닌 것:
/// - 방 생성 로직 자체
/// - 서버의 SAM3 추론 로직
/// - 서버 응답을 이용한 실제 가구 Prefab 배치 로직
///
/// 중요:
/// - 이 스크립트는 이름 기반 GameObject.Find / prefix search를 사용하지 않습니다.
/// - 방 생성 완료 시점에 SetRoomObjects(...)로 실제 생성된 Floor/Ceiling/Wall Transform들을 직접 넘겨주세요.
/// </summary>
public class HoloLensFurnitureCaptureAndUpload : MonoBehaviour
{
    [Header("Server")]
    [SerializeField] private string detectUrl = "https://api.earquake.xyz/detect";
    [SerializeField] private int requestTimeoutSeconds = 20;

    [Header("Debug Save")]
    [SerializeField] private string outputFolderName = "FurnitureCaptureDebug";
    [SerializeField, Range(1, 100)] private int jpegQuality = 90;

    [Header("Room Objects - Explicit References")]
    [Tooltip("선택 사항입니다. Confirmed Room 전체 Root를 알고 있다면 넣어주세요. 이름 기반 자동 탐색에는 사용하지 않고 metadata 기록용으로만 사용합니다.")]
    [SerializeField] private Transform roomRoot;

    [Tooltip("동적으로 생성된 ConfirmedRoom_Floor 오브젝트의 Transform을 방 생성 완료 시점에 직접 넣어주세요.")]
    [SerializeField] private Transform floorObject;

    [Tooltip("동적으로 생성된 ConfirmedRoom_Ceiling 오브젝트의 Transform을 방 생성 완료 시점에 직접 넣어주세요.")]
    [SerializeField] private Transform ceilingObject;

    [Tooltip("동적으로 생성된 ConfirmedRoom_Wall 오브젝트들의 Transform을 방 생성 완료 시점에 직접 넣어주세요.")]
    [SerializeField] private List<Transform> wallObjects = new List<Transform>();

    [Header("Room Metadata Option")]
    [Tooltip("서버에서 2D mask/bbox를 월드 위치로 되돌릴 때 사용할 수 있도록 Mesh vertex world 좌표를 포함합니다.")]
    [SerializeField] private bool includeMeshVertices = true;

    [Tooltip("triangle index까지 포함하면 JSON 크기가 커질 수 있습니다. 처음에는 꺼두는 것을 권장합니다.")]
    [SerializeField] private bool includeMeshTriangles = false;

    [Header("Capture Option")]
    [Tooltip("true이면 촬영 후 저장이 끝나자마자 서버로 전송합니다. false이면 저장만 합니다.")]
    [SerializeField] private bool uploadImmediatelyAfterCapture = false;

    private bool isCapturing = false;
    private bool isUploading = false;

#if UNITY_WSA && !UNITY_EDITOR
    private PhotoCapture photoCaptureObject;
    private Resolution selectedResolution;
#endif

    // ----------------------------------------------------------------------
    // Button entry points
    // ----------------------------------------------------------------------

    /// <summary>
    /// 촬영 후 JPG + JSON만 저장합니다.
    /// UI 촬영 버튼에는 우선 이 함수를 연결하는 것을 권장합니다.
    /// </summary>
    public void CapturePhotoForDebug()
    {
        uploadImmediatelyAfterCapture = false;
        StartCapture();
    }

    /// <summary>
    /// 촬영 후 JPG + JSON을 저장하고, 저장된 파일을 즉시 서버로 전송합니다.
    /// 촬영/저장 테스트가 끝난 뒤 이 함수를 버튼에 연결하세요.
    /// </summary>
    public void CapturePhotoAndUpload()
    {
        uploadImmediatelyAfterCapture = true;
        StartCapture();
    }

    /// <summary>
    /// 가장 최근에 저장된 capture 패키지 하나를 서버로 전송합니다.
    /// </summary>
    public void UploadLatestSavedCapture()
    {
        if (isUploading)
        {
            Debug.LogWarning("[FurnitureCapture] 이미 업로드 중입니다.");
            return;
        }

        string debugRoot = GetDebugRootFolderPath();

        if (!Directory.Exists(debugRoot))
        {
            Debug.LogError($"[FurnitureCapture] 디버그 폴더가 없습니다: {debugRoot}");
            return;
        }

        DirectoryInfo rootInfo = new DirectoryInfo(debugRoot);
        DirectoryInfo[] captureFolders = rootInfo.GetDirectories();

        if (captureFolders.Length == 0)
        {
            Debug.LogWarning($"[FurnitureCapture] 업로드할 캡처 폴더가 없습니다: {debugRoot}");
            return;
        }

        Array.Sort(captureFolders, (a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));

        for (int i = 0; i < captureFolders.Length; i++)
        {
            if (TryFindCapturePackage(captureFolders[i].FullName, out CapturePackage package))
            {
                StartCoroutine(UploadCapturePackageCoroutine(package, 0, 1));
                return;
            }
        }

        Debug.LogError($"[FurnitureCapture] JPG/JSON 쌍을 가진 캡처 폴더를 찾지 못했습니다: {debugRoot}");
    }

    /// <summary>
    /// 저장된 모든 capture 패키지를 서버로 전송합니다.
    /// </summary>
    public void UploadAllSavedCaptures()
    {
        if (isUploading)
        {
            Debug.LogWarning("[FurnitureCapture] 이미 업로드 중입니다.");
            return;
        }

        StartCoroutine(UploadAllSavedCapturesCoroutine());
    }

    // ----------------------------------------------------------------------
    // Room object reference setup
    // ----------------------------------------------------------------------

    /// <summary>
    /// 방 생성 완료 시점에 생성된 방 오브젝트들을 직접 넘겨주세요.
    /// 이름 기반 탐색을 하지 않으므로 이 함수 호출이 가장 안전합니다.
    /// </summary>
    public void SetRoomObjects(
        Transform root,
        Transform floor,
        Transform ceiling,
        IList<Transform> walls)
    {
        roomRoot = root;
        floorObject = floor;
        ceilingObject = ceiling;
        wallObjects = walls != null
            ? walls.Where(wall => wall != null).ToList()
            : new List<Transform>();

        Debug.Log(
            "[FurnitureCapture] Room objects set\n" +
            $"root: {(roomRoot != null ? roomRoot.name : "null")}\n" +
            $"floor: {(floorObject != null ? floorObject.name : "null")}\n" +
            $"ceiling: {(ceilingObject != null ? ceilingObject.name : "null")}\n" +
            $"walls: {wallObjects.Count}"
        );
    }

    /// <summary>
    /// Root가 필요 없거나 아직 모를 때 사용할 수 있는 간단 버전입니다.
    /// </summary>
    public void SetRoomObjects(
        Transform floor,
        Transform ceiling,
        IList<Transform> walls)
    {
        SetRoomObjects(null, floor, ceiling, walls);
    }

    /// <summary>
    /// 방 생성 코드가 GameObject를 관리하고 있을 때 쓰기 위한 편의 함수입니다.
    /// </summary>
    public void SetRoomGameObjects(
        GameObject root,
        GameObject floor,
        GameObject ceiling,
        IList<GameObject> walls)
    {
        List<Transform> wallTransforms = new List<Transform>();

        if (walls != null)
        {
            for (int i = 0; i < walls.Count; i++)
            {
                if (walls[i] != null)
                    wallTransforms.Add(walls[i].transform);
            }
        }

        SetRoomObjects(
            root != null ? root.transform : null,
            floor != null ? floor.transform : null,
            ceiling != null ? ceiling.transform : null,
            wallTransforms
        );
    }

    public void SetRoomRoot(Transform root)
    {
        roomRoot = root;
        Debug.Log($"[FurnitureCapture] Room root set: {(roomRoot != null ? roomRoot.name : "null")}");
    }

    public void SetFloorObject(Transform floor)
    {
        floorObject = floor;
        Debug.Log($"[FurnitureCapture] Floor object set: {(floorObject != null ? floorObject.name : "null")}");
    }

    public void SetCeilingObject(Transform ceiling)
    {
        ceilingObject = ceiling;
        Debug.Log($"[FurnitureCapture] Ceiling object set: {(ceilingObject != null ? ceilingObject.name : "null")}");
    }

    public void SetWallObjects(IList<Transform> walls)
    {
        wallObjects = walls != null
            ? walls.Where(wall => wall != null).ToList()
            : new List<Transform>();

        Debug.Log($"[FurnitureCapture] Wall objects set: {wallObjects.Count}");
    }

    public void AddWallObject(Transform wall)
    {
        if (wall == null)
        {
            Debug.LogWarning("[FurnitureCapture] 추가하려는 wall이 null입니다.");
            return;
        }

        if (wallObjects == null)
            wallObjects = new List<Transform>();

        if (!wallObjects.Contains(wall))
            wallObjects.Add(wall);
    }

    public void ClearRoomObjects()
    {
        roomRoot = null;
        floorObject = null;
        ceilingObject = null;
        wallObjects = new List<Transform>();
        Debug.Log("[FurnitureCapture] Room object references cleared.");
    }

    // ----------------------------------------------------------------------
    // Capture
    // ----------------------------------------------------------------------

    private void StartCapture()
    {
        if (isCapturing)
        {
            Debug.LogWarning("[FurnitureCapture] 이미 촬영 중입니다.");
            return;
        }

        isCapturing = true;

#if UNITY_WSA && !UNITY_EDITOR
        Debug.Log("[FurnitureCapture] HoloLens2 PhotoCapture 경로 실행: 실제 RGB/PV 카메라 촬영");
        StartHoloLensPhotoCapture();
#else
        Debug.LogError(
            "[FurnitureCapture] 현재 실행 환경에서는 실제 HoloLens2 RGB/PV 카메라 촬영을 수행하지 않습니다.\n" +
            "Unity Editor / Holographic Remoting / Standalone에서는 GameView 캡처를 하지 않도록 막아두었습니다.\n" +
            "실제 환경 사진은 HoloLens2 UWP 빌드에서 CapturePhotoForDebug() 또는 CapturePhotoAndUpload()를 실행해야 얻을 수 있습니다."
        );
        isCapturing = false;
#endif
    }

#if UNITY_WSA && !UNITY_EDITOR

    private void StartHoloLensPhotoCapture()
    {
        List<Resolution> resolutions = PhotoCapture.SupportedResolutions
            .OrderByDescending(resolution => resolution.width * resolution.height)
            .ToList();

        if (resolutions.Count == 0)
        {
            FinishCaptureWithError("지원되는 카메라 해상도가 없습니다.");
            return;
        }

        selectedResolution = resolutions[0];

        PhotoCapture.CreateAsync(false, captureObject =>
        {
            if (captureObject == null)
            {
                FinishCaptureWithError("PhotoCapture 객체 생성 실패");
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
            FinishCaptureWithError("PhotoMode 시작 실패");
            return;
        }

        // Unity PhotoCapture 메모리 캡처 API입니다.
        // CapturePhotoToMemoryAsync가 아니라 TakePhotoAsync를 사용해야 합니다.
        photoCaptureObject.TakePhotoAsync(OnCapturedPhotoToMemory);
    }

    private void OnCapturedPhotoToMemory(PhotoCapture.PhotoCaptureResult result, PhotoCaptureFrame frame)
    {
        if (!result.success || frame == null)
        {
            StopPhotoModeThen(() => FinishCaptureWithError("사진 메모리 캡처 실패"));
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
        string imageFileName = captureId + ".jpg";

        CaptureMetadata metadata = BuildCaptureMetadata(
            captureId,
            imageFileName,
            selectedResolution.width,
            selectedResolution.height,
            hasCameraToWorld,
            cameraToWorld,
            hasProjection,
            projection
        );

        StopPhotoModeThen(() =>
        {
            CapturePackage package = SaveCaptureDebugFiles(captureId, jpgBytes, metadata);
            isCapturing = false;

            if (uploadImmediatelyAfterCapture && package != null)
            {
                StartCoroutine(UploadCapturePackageCoroutine(package, 0, 1));
            }
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

    private void FinishCaptureWithError(string message)
    {
        Debug.LogError($"[FurnitureCapture] {message}");
        isCapturing = false;
    }

    // ----------------------------------------------------------------------
    // Save
    // ----------------------------------------------------------------------

    private CapturePackage SaveCaptureDebugFiles(
        string captureId,
        byte[] jpgBytes,
        CaptureMetadata metadata)
    {
        if (jpgBytes == null || jpgBytes.Length == 0)
        {
            Debug.LogError("[FurnitureCapture] 저장할 JPG 바이트가 비어있습니다.");
            return null;
        }

        string rootFolder = GetDebugRootFolderPath();
        string captureFolder = Path.Combine(rootFolder, captureId);

        Directory.CreateDirectory(captureFolder);

        string imagePath = Path.Combine(captureFolder, captureId + ".jpg");
        string metadataPath = Path.Combine(captureFolder, captureId + ".json");

        File.WriteAllBytes(imagePath, jpgBytes);

        string metadataJson = JsonUtility.ToJson(metadata, true);
        File.WriteAllText(metadataPath, metadataJson);

        Debug.Log(
            "[FurnitureCapture] 촬영 디버그 파일 저장 완료\n" +
            $"captureId: {captureId}\n" +
            $"imagePath: {imagePath}\n" +
            $"metadataPath: {metadataPath}"
        );

        return new CapturePackage
        {
            captureId = captureId,
            folderPath = captureFolder,
            imagePath = imagePath,
            metadataPath = metadataPath
        };
    }

    private string GetDebugRootFolderPath()
    {
        return Path.Combine(Application.persistentDataPath, outputFolderName);
    }

    private string CreateCaptureId()
    {
        return "capture_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmssfff");
    }

    // ----------------------------------------------------------------------
    // Upload
    // ----------------------------------------------------------------------

    private IEnumerator UploadAllSavedCapturesCoroutine()
    {
        isUploading = true;

        string debugRoot = GetDebugRootFolderPath();

        if (!Directory.Exists(debugRoot))
        {
            Debug.LogError($"[FurnitureCapture] 디버그 폴더가 없습니다: {debugRoot}");
            isUploading = false;
            yield break;
        }

        DirectoryInfo rootInfo = new DirectoryInfo(debugRoot);
        DirectoryInfo[] captureFolders = rootInfo.GetDirectories();
        Array.Sort(captureFolders, (a, b) => a.Name.CompareTo(b.Name));

        List<CapturePackage> packages = new List<CapturePackage>();

        for (int i = 0; i < captureFolders.Length; i++)
        {
            if (TryFindCapturePackage(captureFolders[i].FullName, out CapturePackage package))
            {
                packages.Add(package);
            }
        }

        if (packages.Count == 0)
        {
            Debug.LogWarning($"[FurnitureCapture] 업로드할 JPG/JSON 캡처 패키지가 없습니다: {debugRoot}");
            isUploading = false;
            yield break;
        }

        Debug.Log($"[FurnitureCapture] 전체 캡처 업로드 시작: {packages.Count}개");

        for (int i = 0; i < packages.Count; i++)
        {
            yield return UploadCapturePackageCoroutine(packages[i], i, packages.Count, keepUploadingFlag: true);
        }

        Debug.Log("[FurnitureCapture] 전체 캡처 업로드 완료");
        isUploading = false;
    }

    private IEnumerator UploadCapturePackageCoroutine(
        CapturePackage package,
        int index,
        int totalCount,
        bool keepUploadingFlag = false)
    {
        if (package == null)
        {
            Debug.LogError("[FurnitureCapture] 업로드할 CapturePackage가 null입니다.");
            yield break;
        }

        if (!keepUploadingFlag)
        {
            if (isUploading)
            {
                Debug.LogWarning("[FurnitureCapture] 이미 업로드 중입니다.");
                yield break;
            }

            isUploading = true;
        }

        if (!File.Exists(package.imagePath))
        {
            Debug.LogError($"[FurnitureCapture] 이미지 파일이 없습니다: {package.imagePath}");
            if (!keepUploadingFlag) isUploading = false;
            yield break;
        }

        if (!File.Exists(package.metadataPath))
        {
            Debug.LogError($"[FurnitureCapture] 메타데이터 파일이 없습니다: {package.metadataPath}");
            if (!keepUploadingFlag) isUploading = false;
            yield break;
        }

        byte[] imageBytes;
        string metadataJson;

        try
        {
            imageBytes = File.ReadAllBytes(package.imagePath);
            metadataJson = File.ReadAllText(package.metadataPath);
        }
        catch (Exception e)
        {
            Debug.LogError($"[FurnitureCapture] 업로드 파일 읽기 실패\n{e.Message}");
            if (!keepUploadingFlag) isUploading = false;
            yield break;
        }

        string imageFileName = Path.GetFileName(package.imagePath);

        List<IMultipartFormSection> formData = new List<IMultipartFormSection>();

        // Flask 서버의 request.files["image"]와 맞춥니다.
        formData.Add(new MultipartFormFileSection(
            "image",
            imageBytes,
            imageFileName,
            GetMimeType(imageFileName)
        ));

        // Flask 서버에서는 request.form["metadata_json"]으로 받을 수 있습니다.
        formData.Add(new MultipartFormDataSection("metadata_json", metadataJson));
        formData.Add(new MultipartFormDataSection("capture_id", package.captureId));
        formData.Add(new MultipartFormDataSection("source", "hololens2_furniture_capture"));
        formData.Add(new MultipartFormDataSection("client_frame_index", index.ToString()));
        formData.Add(new MultipartFormDataSection("client_total_count", totalCount.ToString()));

        using (UnityWebRequest request = UnityWebRequest.Post(detectUrl, formData))
        {
            request.timeout = requestTimeoutSeconds;

            Debug.Log(
                $"[FurnitureCapture] [{index + 1}/{totalCount}] 업로드 시작\n" +
                $"url: {detectUrl}\n" +
                $"image: {package.imagePath}\n" +
                $"metadata: {package.metadataPath}"
            );

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError(
                    $"[FurnitureCapture] [{index + 1}/{totalCount}] 업로드 실패\n" +
                    $"responseCode: {request.responseCode}\n" +
                    $"error: {request.error}\n" +
                    $"body: {request.downloadHandler.text}"
                );

                if (!keepUploadingFlag) isUploading = false;
                yield break;
            }

            string responseText = request.downloadHandler.text;

            Debug.Log(
                $"[FurnitureCapture] [{index + 1}/{totalCount}] 업로드 성공\n" +
                $"responseCode: {request.responseCode}\n" +
                $"response: {responseText}"
            );

            TryLogDetectResponse(responseText);
        }

        if (!keepUploadingFlag)
        {
            isUploading = false;
        }
    }

    private bool TryFindCapturePackage(string folderPath, out CapturePackage package)
    {
        package = null;

        if (!Directory.Exists(folderPath))
            return false;

        string[] jpgFiles = Directory.GetFiles(folderPath, "*.jpg", SearchOption.TopDirectoryOnly);
        string[] jsonFiles = Directory.GetFiles(folderPath, "*.json", SearchOption.TopDirectoryOnly);

        if (jpgFiles.Length == 0 || jsonFiles.Length == 0)
            return false;

        Array.Sort(jpgFiles);
        Array.Sort(jsonFiles);

        string imagePath = jpgFiles[0];
        string imageNameNoExt = Path.GetFileNameWithoutExtension(imagePath);
        string expectedJsonPath = Path.Combine(folderPath, imageNameNoExt + ".json");
        string metadataPath = File.Exists(expectedJsonPath) ? expectedJsonPath : jsonFiles[0];

        package = new CapturePackage
        {
            captureId = imageNameNoExt,
            folderPath = folderPath,
            imagePath = imagePath,
            metadataPath = metadataPath
        };

        return true;
    }

    private string GetMimeType(string fileName)
    {
        string ext = Path.GetExtension(fileName).ToLowerInvariant();

        switch (ext)
        {
            case ".png":
                return "image/png";

            case ".jpg":
            case ".jpeg":
            default:
                return "image/jpeg";
        }
    }

    private void TryLogDetectResponse(string responseText)
    {
        try
        {
            UploadResponseDto response = JsonUtility.FromJson<UploadResponseDto>(responseText);

            if (response == null || response.objects == null)
                return;

            for (int i = 0; i < response.objects.Length; i++)
            {
                UploadDetectedObjectDto obj = response.objects[i];

                Debug.Log(
                    "[FurnitureCapture] Detected Object - " +
                    $"id: {obj.id}, " +
                    $"label: {obj.label}, " +
                    $"confidence: {obj.confidence}"
                );
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[FurnitureCapture] 응답 JSON 파싱 실패: {e.Message}");
        }
    }

    // ----------------------------------------------------------------------
    // Metadata
    // ----------------------------------------------------------------------

    private CaptureMetadata BuildCaptureMetadata(
        string captureId,
        string imageFileName,
        int imageWidth,
        int imageHeight,
        bool hasCameraToWorld,
        Matrix4x4 cameraToWorld,
        bool hasProjection,
        Matrix4x4 projection)
    {
        CaptureMetadata metadata = new CaptureMetadata
        {
            capture_id = captureId,
            timestamp_utc = DateTime.UtcNow.ToString("o"),
            source = "HoloLens2_PhotoCapture",
            image_file_name = imageFileName,
            image_width = imageWidth,
            image_height = imageHeight,
            has_camera_to_world = hasCameraToWorld,
            has_projection = hasProjection,
            camera_to_world = MatrixToRowMajorArray(cameraToWorld),
            projection = MatrixToRowMajorArray(projection),
            approx_intrinsics = hasProjection
                ? BuildApproxIntrinsicsFromProjection(projection, imageWidth, imageHeight)
                : null,
            room = BuildRoomSnapshot()
        };

        return metadata;
    }

    private RoomSnapshot BuildRoomSnapshot()
    {
        RoomSnapshot snapshot = new RoomSnapshot
        {
            room_id = roomRoot != null ? roomRoot.name : "explicit_room_objects",
            timestamp_utc = DateTime.UtcNow.ToString("o"),
            coordinate_space = "Unity world space",
            root_object_name = roomRoot != null ? roomRoot.name : null,
            root_path = roomRoot != null ? GetTransformPath(roomRoot) : null,
            has_floor = floorObject != null,
            has_ceiling = ceilingObject != null,
            assigned_wall_count = wallObjects != null ? wallObjects.Count(wall => wall != null) : 0
        };

        int floorCount = 0;
        int ceilingCount = 0;
        int wallCount = 0;

        if (floorObject != null)
        {
            snapshot.surfaces.Add(BuildSurfaceSnapshot("floor", floorObject, 0));
            floorCount = 1;
        }
        else
        {
            Debug.LogWarning(
                "[FurnitureCapture] floorObject가 설정되지 않았습니다. " +
                "방 생성 완료 시점에 SetRoomObjects(...) 또는 SetFloorObject(...)를 호출하세요."
            );
        }

        if (ceilingObject != null)
        {
            snapshot.surfaces.Add(BuildSurfaceSnapshot("ceiling", ceilingObject, 0));
            ceilingCount = 1;
        }
        else
        {
            Debug.LogWarning(
                "[FurnitureCapture] ceilingObject가 설정되지 않았습니다. " +
                "방 생성 완료 시점에 SetRoomObjects(...) 또는 SetCeilingObject(...)를 호출하세요."
            );
        }

        if (wallObjects != null)
        {
            for (int i = 0; i < wallObjects.Count; i++)
            {
                Transform wall = wallObjects[i];

                if (wall == null)
                {
                    Debug.LogWarning($"[FurnitureCapture] wallObjects[{i}]가 null입니다. 해당 wall은 metadata에서 제외합니다.");
                    continue;
                }

                snapshot.surfaces.Add(BuildSurfaceSnapshot("wall", wall, wallCount));
                wallCount++;
            }
        }

        if (wallCount == 0)
        {
            Debug.LogWarning(
                "[FurnitureCapture] 설정된 wallObject가 없습니다. " +
                "방 생성 완료 시점에 SetRoomObjects(...) 또는 SetWallObjects(...)를 호출하세요."
            );
        }

        Debug.Log(
            "[FurnitureCapture] Room metadata 생성 완료\n" +
            $"root: {(roomRoot != null ? roomRoot.name : "null")}\n" +
            $"floor: {floorCount}, ceiling: {ceilingCount}, wall: {wallCount}"
        );

        return snapshot;
    }

    private RoomSurfaceSnapshot BuildSurfaceSnapshot(string surfaceType, Transform target, int index)
    {
        RoomSurfaceSnapshot surface = new RoomSurfaceSnapshot
        {
            surface_type = surfaceType,
            index = index,
            object_name = target.name,
            object_path = GetTransformPath(target),
            position_world = SerializableVector3.From(target.position),
            rotation_world = SerializableQuaternion.From(target.rotation),
            lossy_scale = SerializableVector3.From(target.lossyScale),
            local_to_world = MatrixToRowMajorArray(target.localToWorldMatrix)
        };

        Renderer renderer = target.GetComponent<Renderer>();
        if (renderer != null)
        {
            surface.has_renderer_bounds = true;
            surface.bounds_center_world = SerializableVector3.From(renderer.bounds.center);
            surface.bounds_size_world = SerializableVector3.From(renderer.bounds.size);
        }

        BoxCollider boxCollider = target.GetComponent<BoxCollider>();
        if (boxCollider != null)
        {
            surface.has_box_collider = true;
            surface.box_collider_center_local = SerializableVector3.From(boxCollider.center);
            surface.box_collider_size_local = SerializableVector3.From(boxCollider.size);
        }

        MeshCollider meshCollider = target.GetComponent<MeshCollider>();
        if (meshCollider != null)
        {
            surface.has_mesh_collider = true;
            surface.mesh_collider_convex = meshCollider.convex;
        }

        Mesh mesh = null;

        MeshFilter meshFilter = target.GetComponent<MeshFilter>();
        if (meshFilter != null && meshFilter.sharedMesh != null)
        {
            mesh = meshFilter.sharedMesh;
        }
        else if (meshCollider != null && meshCollider.sharedMesh != null)
        {
            mesh = meshCollider.sharedMesh;
        }

        if (mesh != null)
        {
            surface.has_mesh = true;
            surface.mesh_vertex_count = mesh.vertexCount;
            surface.mesh_triangle_count = mesh.triangles != null ? mesh.triangles.Length / 3 : 0;

            if (includeMeshVertices)
            {
                Vector3[] vertices = mesh.vertices;
                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 worldVertex = target.TransformPoint(vertices[i]);
                    surface.mesh_vertices_world.Add(SerializableVector3.From(worldVertex));
                }
            }

            if (includeMeshTriangles)
            {
                surface.mesh_triangles = mesh.triangles;
            }
        }

        return surface;
    }

    private string GetTransformPath(Transform target)
    {
        if (target == null)
            return string.Empty;

        List<string> names = new List<string>();
        Transform current = target;

        while (current != null)
        {
            names.Add(current.name);
            current = current.parent;
        }

        names.Reverse();
        return string.Join("/", names.ToArray());
    }

    private float[] MatrixToRowMajorArray(Matrix4x4 m)
    {
        return new float[]
        {
            m.m00, m.m01, m.m02, m.m03,
            m.m10, m.m11, m.m12, m.m13,
            m.m20, m.m21, m.m22, m.m23,
            m.m30, m.m31, m.m32, m.m33
        };
    }

    private ApproxCameraIntrinsics BuildApproxIntrinsicsFromProjection(
        Matrix4x4 projection,
        int imageWidth,
        int imageHeight)
    {
        return new ApproxCameraIntrinsics
        {
            fx = projection.m00 * imageWidth * 0.5f,
            fy = projection.m11 * imageHeight * 0.5f,
            cx = (1.0f - projection.m02) * imageWidth * 0.5f,
            cy = (1.0f + projection.m12) * imageHeight * 0.5f
        };
    }

    // ----------------------------------------------------------------------
    // DTO classes - 전부 내부 클래스로 두어 다른 테스트 코드의 DTO와 이름 충돌을 피합니다.
    // ----------------------------------------------------------------------

    [Serializable]
    private class CapturePackage
    {
        public string captureId;
        public string folderPath;
        public string imagePath;
        public string metadataPath;
    }

    [Serializable]
    private class CaptureMetadata
    {
        public string capture_id;
        public string timestamp_utc;
        public string source;

        public string image_file_name;
        public int image_width;
        public int image_height;

        public bool has_camera_to_world;
        public bool has_projection;

        public float[] camera_to_world;
        public float[] projection;

        public ApproxCameraIntrinsics approx_intrinsics;

        public string matrix_layout = "row-major: m00,m01,m02,m03,m10,...,m33";
        public string coordinate_space = "Unity world space";

        public RoomSnapshot room;
    }

    [Serializable]
    private class ApproxCameraIntrinsics
    {
        public float fx;
        public float fy;
        public float cx;
        public float cy;

        public string note =
            "Approximate intrinsics derived from Unity projection matrix. " +
            "For actual placement, use projection + camera_to_world based ray reconstruction.";
    }

    [Serializable]
    private class RoomSnapshot
    {
        public string room_id;
        public string timestamp_utc;
        public string coordinate_space;
        public string root_object_name;
        public string root_path;
        public bool has_floor;
        public bool has_ceiling;
        public int assigned_wall_count;
        public List<RoomSurfaceSnapshot> surfaces = new List<RoomSurfaceSnapshot>();
    }

    [Serializable]
    private class RoomSurfaceSnapshot
    {
        public string surface_type;
        public int index;
        public string object_name;
        public string object_path;

        public SerializableVector3 position_world;
        public SerializableQuaternion rotation_world;
        public SerializableVector3 lossy_scale;

        public float[] local_to_world;

        public bool has_renderer_bounds;
        public SerializableVector3 bounds_center_world;
        public SerializableVector3 bounds_size_world;

        public bool has_box_collider;
        public SerializableVector3 box_collider_center_local;
        public SerializableVector3 box_collider_size_local;

        public bool has_mesh_collider;
        public bool mesh_collider_convex;

        public bool has_mesh;
        public int mesh_vertex_count;
        public int mesh_triangle_count;

        public List<SerializableVector3> mesh_vertices_world = new List<SerializableVector3>();
        public int[] mesh_triangles;
    }

    [Serializable]
    private struct SerializableVector3
    {
        public float x;
        public float y;
        public float z;

        public SerializableVector3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public static SerializableVector3 From(Vector3 v)
        {
            return new SerializableVector3(v.x, v.y, v.z);
        }
    }

    [Serializable]
    private struct SerializableQuaternion
    {
        public float x;
        public float y;
        public float z;
        public float w;

        public SerializableQuaternion(float x, float y, float z, float w)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            this.w = w;
        }

        public static SerializableQuaternion From(Quaternion q)
        {
            return new SerializableQuaternion(q.x, q.y, q.z, q.w);
        }
    }

    [Serializable]
    private class UploadResponseDto
    {
        public string frame_id;
        public UploadDetectedObjectDto[] objects;
    }

    [Serializable]
    private class UploadDetectedObjectDto
    {
        public string id;
        public string label;
        public float confidence;
        public int[] bbox;
        public string mask_url;
        public string mesh_url;
    }
}

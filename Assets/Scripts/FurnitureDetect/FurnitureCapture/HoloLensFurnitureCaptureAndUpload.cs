using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

#if UNITY_WSA && !UNITY_EDITOR
using UnityEngine.Windows.WebCam;
#endif

/// <summary>
/// HoloLens2 실제 RGB/PV 카메라 사진 촬영, 디버그 저장, scan session 기반 서버 전송을 담당하는 컴포넌트입니다.
///
/// 서버 흐름:
/// 1. StartScanSession() -> POST /scan-session/start
/// 2. CapturePhotoAndUpload() -> POST /scan-frame, multipart: image, metadata
/// 3. StartDetectionForCurrentSession() -> POST /detect-session/{scan_session_id}?mode=auto
/// 4. GetDetectionsForCurrentSession() -> GET /detections/{scan_session_id}
/// 5. ProcessScanForCurrentSession() -> POST /process-scan/{scan_session_id}?use_confirmed=1
/// 6. GetResultForCurrentSession() -> GET /result/{scan_session_id}
/// </summary>
public class HoloLensFurnitureCaptureAndUpload : MonoBehaviour
{
    [Header("Server Endpoints")]
    [SerializeField] private string serverBaseUrl = "https://api.earquake.xyz";
    [SerializeField] private string startScanSessionPath = "/scan-session/start";
    [SerializeField] private string uploadScanFramePath = "/scan-frame";
    [SerializeField] private string detectSessionPathFormat = "/detect-session/{0}?mode=auto";
    [SerializeField] private string detectionsPathFormat = "/detections/{0}";
    [SerializeField] private string processScanPathFormat = "/process-scan/{0}?use_confirmed=1";
    [SerializeField] private string finishScanSessionPathFormat = "/scan-session/{0}/finish";
    [SerializeField] private string scanJobPathFormat = "/scan-job/{0}";
    [SerializeField] private string resultPathFormat = "/result/{0}";
    [SerializeField] private int requestTimeoutSeconds = 30;
    [SerializeField] private int longRequestTimeoutSeconds = 300;

    [Header("Scan Session State")]
    [SerializeField] private string currentScanSessionId = string.Empty;
    [SerializeField] private string currentScanSessionStatus = string.Empty;
    [SerializeField] private int nextFrameNumber = 1;
    [SerializeField] private bool currentScanSessionCreatedByServer = false;

    [Tooltip("로컬 저장만 하는 테스트에서 scan_session_id가 없으면 local_scan_... 값을 만들어 metadata에 넣습니다. 서버 업로드에는 사용할 수 없습니다.")]
    [SerializeField] private bool autoCreateLocalSessionForSaveOnly = true;

    [Header("Debug Save")]
    [SerializeField] private string outputFolderName = "FurnitureCaptureDebug";
    [SerializeField, Range(1, 100)] private int jpegQuality = 90;

    [Header("Room Objects - Explicit References")]
    [SerializeField] private Transform roomRoot;
    [SerializeField] private Transform floorObject;
    [SerializeField] private Transform ceilingObject;
    [SerializeField] private List<Transform> wallObjects = new List<Transform>();

    [Header("Room Metadata Option")]
    [SerializeField] private bool includeMeshVertices = true;
    [SerializeField] private bool includeMeshTriangles = false;

    [Header("Depth Samples Option")]
    [Tooltip("/scan-frame metadata 안에 픽셀 -> Unity world raycast sample을 포함합니다.")]
    [SerializeField] private bool includeDepthSamples = true;

    [Tooltip("가로 depth sample 개수입니다. 16이면 16x9 grid 기준 144개 sample을 생성합니다.")]
    [SerializeField, Range(1, 64)] private int depthSampleGridX = 16;

    [Tooltip("세로 depth sample 개수입니다. 9이면 16x9 grid 기준 144개 sample을 생성합니다.")]
    [SerializeField, Range(1, 64)] private int depthSampleGridY = 9;

    [Tooltip("Raycast 최대 거리입니다. 단위는 Unity meter입니다.")]
    [SerializeField] private float depthRaycastMaxDistance = 8.0f;

    [Tooltip("PhotoCaptureFrame.TryGetProjectionMatrix(near, far, out projection)에 사용할 near clip입니다. 너무 작으면 ray 복원 오차가 커질 수 있어 0.05m를 기본값으로 둡니다.")]
    [SerializeField] private float depthProjectionNearClip = 0.05f;
    private float DepthProjectionNearClip => Mathf.Max(0.01f, depthProjectionNearClip);

    [Tooltip("Depth sample raycast 대상 Layer입니다. 가능하면 Spatial Mesh Collider 전용 Layer만 지정하세요.")]
    [SerializeField] private LayerMask depthRaycastLayerMask = Physics.DefaultRaycastLayers;

    [Tooltip("true이면 Raycast miss도 metadata에 hit=false로 포함합니다. 서버가 bbox 안 hit 분포를 판단하기 쉽도록 기본 true를 권장합니다.")]
    [SerializeField] private bool includeMissedDepthSamples = true;

    [Header("Capture Option")]
    [Tooltip("true이면 촬영 후 저장이 끝나자마자 /scan-frame으로 전송합니다. false이면 저장만 합니다.")]
    [SerializeField] private bool uploadImmediatelyAfterCapture = false;

    [Header("Room Object Auto Binding")]
    [SerializeField] private bool autoBindRoomObjectsBeforeCapture = true;
    [SerializeField] private bool autoFindConfirmRoomManager = true;
    [SerializeField] private ConfirmRoomManager confirmRoomManager;
    [SerializeField] private RoomGeometrySnapshotProvider roomSnapshotProvider;

    [Header("Editor / Remote Test Capture")]
    [Tooltip("Unity Editor / Holographic Remoting / Standalone 테스트에서 GameView 스크린샷을 JPG로 저장/업로드합니다. 실제 HoloLens PV 카메라 사진이 아닙니다.")]
    [SerializeField] private bool enableEditorOrRemoteTestCapture = true;
    [SerializeField] private Texture2D testImageOverride;
    [SerializeField] private int fallbackTestImageWidth = 1280;
    [SerializeField] private int fallbackTestImageHeight = 720;

    private bool isCapturing = false;
    private bool isUploading = false;
    private bool isStartingScanSession = false;
    private bool isAutoStartingSessionAndCapturing = false;
    private bool isServerRequestRunning = false;

#if UNITY_WSA && !UNITY_EDITOR
    private PhotoCapture photoCaptureObject;
    private Resolution selectedResolution;
#endif

    public string CurrentScanSessionId => currentScanSessionId;
    public string CurrentScanSessionStatus => currentScanSessionStatus;
    public int NextFrameNumber => nextFrameNumber;
    public bool HasActiveScanSession => !string.IsNullOrEmpty(currentScanSessionId);
    public bool HasActiveServerScanSession => HasActiveScanSession && currentScanSessionCreatedByServer;

    // ----------------------------------------------------------------------
    // Server session entry points
    // ----------------------------------------------------------------------

    /// <summary>
    /// 촬영 세션 시작 버튼에 연결합니다.
    /// POST /scan-session/start를 호출하고, 이후 모든 frame 업로드에 같은 scan_session_id를 사용합니다.
    /// </summary>
    public void StartScanSession()
    {
        if (isStartingScanSession)
        {
            Debug.LogWarning("[FurnitureCapture] scan session 시작 요청이 이미 진행 중입니다.");
            return;
        }

        StartCoroutine(StartScanSessionCoroutine());
    }

    public void ClearScanSession()
    {
        currentScanSessionId = string.Empty;
        currentScanSessionStatus = string.Empty;
        currentScanSessionCreatedByServer = false;
        nextFrameNumber = 1;
        Debug.Log("[FurnitureCapture] scan session 상태를 초기화했습니다.");
    }

    [Obsolete("Use FinishScanForCurrentSession. The current server flow does not call /detect-session directly.")]
    public void StartDetectionForCurrentSession()
    {
        if (!EnsureServerScanSession("가구 후보 탐지"))
            return;

        string path = string.Format(detectSessionPathFormat, UnityWebRequest.EscapeURL(currentScanSessionId));
        StartCoroutine(PostEmptyCoroutine(BuildUrl(path), "detect-session", longRequestTimeoutSeconds));
    }

    [Obsolete("Use GetScanJobForCurrentSession. The current server flow does not call /detections directly.")]
    public void GetDetectionsForCurrentSession()
    {
        if (!EnsureScanSession("가구 후보 조회"))
            return;

        string path = string.Format(detectionsPathFormat, UnityWebRequest.EscapeURL(currentScanSessionId));
        StartCoroutine(GetCoroutine(BuildUrl(path), "detections", requestTimeoutSeconds));
    }

    [Obsolete("Use FinishScanForCurrentSession. The current server flow does not call /process-scan.")]
    public void ProcessScanForCurrentSession()
    {
        if (!EnsureServerScanSession("Unity 배치 결과 생성"))
            return;

        string path = string.Format(processScanPathFormat, UnityWebRequest.EscapeURL(currentScanSessionId));
        StartCoroutine(PostEmptyCoroutine(BuildUrl(path), "process-scan", longRequestTimeoutSeconds));
    }

    public void GetResultForCurrentSession()
    {
        if (!EnsureScanSession("Unity 배치 결과 조회"))
            return;

        string path = string.Format(resultPathFormat, UnityWebRequest.EscapeURL(currentScanSessionId));
        StartCoroutine(GetCoroutine(BuildUrl(path), "result", requestTimeoutSeconds));
    }

    public void FinishScanForCurrentSession()
    {
        if (!EnsureServerScanSession("GLB generation start"))
            return;

        string path = string.Format(finishScanSessionPathFormat, UnityWebRequest.EscapeURL(currentScanSessionId));
        StartCoroutine(PostEmptyCoroutine(BuildUrl(path), "finish-scan-session", longRequestTimeoutSeconds));
    }

    public void GetScanJobForCurrentSession()
    {
        if (!EnsureScanSession("GLB generation status"))
            return;

        string path = string.Format(scanJobPathFormat, UnityWebRequest.EscapeURL(currentScanSessionId));
        StartCoroutine(GetCoroutine(BuildUrl(path), "scan-job", requestTimeoutSeconds));
    }

    private IEnumerator StartScanSessionCoroutine()
    {
        isStartingScanSession = true;

        string url = BuildUrl(startScanSessionPath);
        byte[] bodyRaw = Encoding.UTF8.GetBytes("{}");

        using (UnityWebRequest request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
        {
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = requestTimeoutSeconds;

            Debug.Log($"[FurnitureCapture] scan session 시작 요청: {url}");
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError(
                    "[FurnitureCapture] scan session 시작 실패\n" +
                    $"responseCode: {request.responseCode}\n" +
                    $"error: {request.error}\n" +
                    $"body: {request.downloadHandler.text}"
                );
                isStartingScanSession = false;
                yield break;
            }

            string responseText = request.downloadHandler.text;
            StartScanSessionResponse response = null;

            try
            {
                response = JsonUtility.FromJson<StartScanSessionResponse>(responseText);
            }
            catch (Exception e)
            {
                Debug.LogError($"[FurnitureCapture] scan session 응답 파싱 실패: {e.Message}\nbody: {responseText}");
                isStartingScanSession = false;
                yield break;
            }

            if (response == null || string.IsNullOrEmpty(response.scan_session_id))
            {
                Debug.LogError($"[FurnitureCapture] scan_session_id가 없는 응답입니다. body: {responseText}");
                isStartingScanSession = false;
                yield break;
            }

            currentScanSessionId = response.scan_session_id;
            currentScanSessionStatus = response.status;
            currentScanSessionCreatedByServer = true;
            nextFrameNumber = 1;

            Debug.Log(
                "[FurnitureCapture] scan session 시작 완료\n" +
                $"scan_session_id: {currentScanSessionId}\n" +
                $"status: {currentScanSessionStatus}"
            );
        }

        isStartingScanSession = false;
    }

    // ----------------------------------------------------------------------
    // Button entry points
    // ----------------------------------------------------------------------

    /// <summary>
    /// 촬영 후 JPG + JSON만 저장합니다. 서버 업로드는 하지 않습니다.
    /// </summary>
    public void CapturePhotoForDebug()
    {
        uploadImmediatelyAfterCapture = false;
        StartCapture();
    }

    /// <summary>
    /// 촬영 후 JPG + JSON을 저장하고, /scan-frame으로 즉시 업로드합니다.
    /// 이 함수 호출 전 StartScanSession()이 성공해 있어야 합니다.
    /// 디버그용으로 세션 시작과 촬영을 분리해서 테스트할 때 사용합니다.
    /// </summary>
    public void CapturePhotoAndUpload()
    {
        if (!EnsureServerScanSession("사진 업로드"))
            return;

        uploadImmediatelyAfterCapture = true;
        StartCapture();
    }

    /// <summary>
    /// 사용자용 사진 촬영 버튼에 연결하기 위한 함수입니다.
    /// 서버 scan_session_id가 없으면 먼저 /scan-session/start를 호출하고, 성공한 뒤 첫 frame을 촬영/업로드합니다.
    /// 이미 서버 scan_session_id가 있으면 새 session을 만들지 않고 다음 frame만 촬영/업로드합니다.
    /// </summary>
    public void CapturePhotoAndUploadWithAutoSession()
    {
        if (isAutoStartingSessionAndCapturing)
        {
            Debug.LogWarning("[FurnitureCapture] scan session 자동 시작 후 촬영 요청이 이미 진행 중입니다.");
            return;
        }

        if (HasActiveServerScanSession)
        {
            CapturePhotoAndUpload();
            return;
        }

        StartCoroutine(CapturePhotoAndUploadWithAutoSessionCoroutine());
    }

    private IEnumerator CapturePhotoAndUploadWithAutoSessionCoroutine()
    {
        isAutoStartingSessionAndCapturing = true;

        if (isStartingScanSession)
        {
            Debug.Log("[FurnitureCapture] 기존 scan session 시작 요청 완료를 기다린 뒤 촬영합니다.");
            while (isStartingScanSession)
                yield return null;
        }

        if (!HasActiveServerScanSession)
        {
            yield return StartScanSessionCoroutine();
        }

        if (HasActiveServerScanSession)
        {
            CapturePhotoAndUpload();
        }
        else
        {
            Debug.LogError("[FurnitureCapture] scan session 자동 생성에 실패하여 사진 촬영 + 업로드를 중단했습니다.");
        }

        isAutoStartingSessionAndCapturing = false;
    }

    public void UploadLatestSavedCapture()
    {
        if (isUploading)
        {
            Debug.LogWarning("[FurnitureCapture] 이미 업로드 중입니다.");
            return;
        }

        List<CapturePackage> packages = FindSavedCapturePackages();
        if (packages.Count == 0)
        {
            Debug.LogWarning($"[FurnitureCapture] 업로드할 JPG/JSON 캡처 패키지가 없습니다: {GetDebugRootFolderPath()}");
            return;
        }

        packages.Sort((a, b) => b.lastWriteTimeUtc.CompareTo(a.lastWriteTimeUtc));
        StartCoroutine(UploadCapturePackageCoroutine(packages[0], 0, 1));
    }

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

    public bool SetRoomObjectsFromConfirmRoomManager(ConfirmRoomManager manager)
    {
        if (manager == null)
        {
            Debug.LogWarning("[FurnitureCapture] ConfirmRoomManager가 null입니다.");
            return false;
        }

        confirmRoomManager = manager;
        roomSnapshotProvider?.SetConfirmRoomManager(manager);

        if (manager.ConfirmedRoomFloorObject == null || manager.ConfirmedRoomCeilingObject == null)
        {
            Debug.LogWarning("[FurnitureCapture] Confirmed room floor/ceiling이 아직 생성되지 않았습니다.");
            return false;
        }

        List<Transform> wallTransforms = new List<Transform>();
        IReadOnlyList<GameObject> sourceWalls = manager.ConfirmedRoomWallObjects;

        if (sourceWalls != null)
        {
            for (int i = 0; i < sourceWalls.Count; i++)
            {
                if (sourceWalls[i] != null)
                    wallTransforms.Add(sourceWalls[i].transform);
            }
        }

        if (wallTransforms.Count == 0)
        {
            Debug.LogWarning("[FurnitureCapture] Confirmed room wall 목록이 비어 있습니다.");
            return false;
        }

        SetRoomObjects(
            manager.confirmedRoomRoot,
            manager.ConfirmedRoomFloorObject.transform,
            manager.ConfirmedRoomCeilingObject.transform,
            wallTransforms
        );

        return true;
    }

    private bool TryAutoBindRoomObjectsFromConfirmRoom()
    {
        if (!autoBindRoomObjectsBeforeCapture)
            return HasCompleteRoomObjects();

        if (HasCompleteRoomObjects())
        {
            if (roomSnapshotProvider != null && confirmRoomManager != null)
                roomSnapshotProvider.SetConfirmRoomManager(confirmRoomManager);

            if (roomSnapshotProvider != null)
                roomSnapshotProvider.SetRoomObjects(roomRoot, floorObject, ceilingObject, wallObjects);

            return true;
        }

        if (confirmRoomManager == null && autoFindConfirmRoomManager)
        {
            ConfirmRoomManager[] managers = FindObjectsByType<ConfirmRoomManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (managers != null && managers.Length > 0)
                confirmRoomManager = managers[0];
        }

        if (confirmRoomManager != null && SetRoomObjectsFromConfirmRoomManager(confirmRoomManager))
            return true;

        Debug.LogWarning("[FurnitureCapture] Confirmed room objects 자동 등록에 실패했습니다. metadata의 floor/ceiling/wall이 비어 있을 수 있습니다.");
        return false;
    }

    private bool HasCompleteRoomObjects()
    {
        return floorObject != null &&
               ceilingObject != null &&
               wallObjects != null &&
               wallObjects.Any(wall => wall != null);
    }

    public void SetRoomObjects(Transform root, Transform floor, Transform ceiling, IList<Transform> walls)
    {
        roomRoot = root;
        floorObject = floor;
        ceilingObject = ceiling;
        wallObjects = walls != null ? walls.Where(wall => wall != null).ToList() : new List<Transform>();

        if (roomSnapshotProvider != null)
            roomSnapshotProvider.SetRoomObjects(roomRoot, floorObject, ceilingObject, wallObjects);

        Debug.Log(
            "[FurnitureCapture] Room objects set\n" +
            $"root: {(roomRoot != null ? roomRoot.name : "null")}\n" +
            $"floor: {(floorObject != null ? floorObject.name : "null")}\n" +
            $"ceiling: {(ceilingObject != null ? ceilingObject.name : "null")}\n" +
            $"walls: {wallObjects.Count}"
        );
    }

    public void SetRoomObjects(Transform floor, Transform ceiling, IList<Transform> walls)
    {
        SetRoomObjects(null, floor, ceiling, walls);
    }

    public void SetRoomGameObjects(GameObject root, GameObject floor, GameObject ceiling, IList<GameObject> walls)
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
    }

    public void SetFloorObject(Transform floor)
    {
        floorObject = floor;
    }

    public void SetCeilingObject(Transform ceiling)
    {
        ceilingObject = ceiling;
    }

    public void SetWallObjects(IList<Transform> walls)
    {
        wallObjects = walls != null ? walls.Where(wall => wall != null).ToList() : new List<Transform>();
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
        confirmRoomManager = null;
        roomSnapshotProvider?.ClearRoomObjects();
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

        if (uploadImmediatelyAfterCapture && !EnsureServerScanSession("사진 업로드"))
            return;

        if (!uploadImmediatelyAfterCapture && !HasActiveScanSession && autoCreateLocalSessionForSaveOnly)
            StartLocalDebugScanSession();

        isCapturing = true;
        TryAutoBindRoomObjectsFromConfirmRoom();

#if UNITY_WSA && !UNITY_EDITOR
        Debug.Log("[FurnitureCapture] HoloLens2 PhotoCapture 경로 실행: 실제 RGB/PV 카메라 촬영");
        StartHoloLensPhotoCapture();
#else
        if (enableEditorOrRemoteTestCapture)
        {
            Debug.LogWarning(
                "[FurnitureCapture] Editor/Remote 테스트 캡처 경로 실행. " +
                "실제 HoloLens PV 카메라 사진이 아니라 GameView/테스트 이미지로 저장 및 업로드를 검증합니다."
            );
            StartCoroutine(CaptureEditorOrRemoteTestCoroutine());
        }
        else
        {
            FinishCaptureWithError(
                "현재 실행 환경에서는 실제 HoloLens2 RGB/PV 카메라 촬영을 수행하지 않습니다. " +
                "테스트 전송이 필요하면 enableEditorOrRemoteTestCapture를 켜세요."
            );
        }
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

        // 중요:
        // TryGetProjectionMatrix(out projection)으로 받은 raw HoloLens projection은
        // Unity Physics ray 복원에 필요한 near/far clip이 인코딩되어 있지 않아
        // 빌드 환경에서 pixel -> world ray가 엉뚱한 방향으로 나갈 수 있습니다.
        // depth sample용 raycast에는 near/far를 명시한 projection을 사용합니다.
        float depthNearClip = DepthProjectionNearClip;
        float depthFarClip = Mathf.Max(depthNearClip + 0.01f, depthRaycastMaxDistance);
        bool hasProjection = frame.TryGetProjectionMatrix(depthNearClip, depthFarClip, out projection);

        Texture2D texture = new Texture2D(
            selectedResolution.width,
            selectedResolution.height,
            TextureFormat.BGRA32,
            false
        );

        frame.UploadImageDataToTexture(texture);
        byte[] jpgBytes = texture.EncodeToJPG(jpegQuality);
        Destroy(texture);

        SaveFrameAndMaybeUpload(
            jpgBytes,
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

#if !UNITY_WSA || UNITY_EDITOR
    private IEnumerator CaptureEditorOrRemoteTestCoroutine()
    {
        yield return new WaitForEndOfFrame();

        Texture2D texture = null;

        if (testImageOverride != null)
        {
            texture = DuplicateTexture(testImageOverride);
        }
        else
        {
            try
            {
                texture = ScreenCapture.CaptureScreenshotAsTexture();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[FurnitureCapture] GameView 캡처 실패. fallback 이미지 생성: {e.Message}");
                texture = CreateFallbackTestTexture();
            }
        }

        if (texture == null)
            texture = CreateFallbackTestTexture();

        byte[] jpgBytes = texture.EncodeToJPG(jpegQuality);
        int imageWidth = texture.width;
        int imageHeight = texture.height;
        Destroy(texture);

        Matrix4x4 cameraToWorld = Matrix4x4.identity;
        Matrix4x4 projection = Matrix4x4.identity;
        bool hasCameraToWorld = false;
        bool hasProjection = false;

        if (Camera.main != null)
        {
            cameraToWorld = Camera.main.cameraToWorldMatrix;
            projection = Camera.main.projectionMatrix;
            hasCameraToWorld = true;
            hasProjection = true;
        }

        SaveFrameAndMaybeUpload(
            jpgBytes,
            imageWidth,
            imageHeight,
            hasCameraToWorld,
            cameraToWorld,
            hasProjection,
            projection,
            "UnityEditorOrRemote_TestCapture"
        );

        isCapturing = false;
    }

    private Texture2D DuplicateTexture(Texture2D source)
    {
        RenderTexture temporary = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.Default, RenderTextureReadWrite.Linear);
        Graphics.Blit(source, temporary);

        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = temporary;

        Texture2D readable = new Texture2D(source.width, source.height, TextureFormat.RGB24, false);
        readable.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
        readable.Apply();

        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(temporary);
        return readable;
    }

    private Texture2D CreateFallbackTestTexture()
    {
        int width = Mathf.Max(16, fallbackTestImageWidth);
        int height = Mathf.Max(16, fallbackTestImageHeight);
        Texture2D texture = new Texture2D(width, height, TextureFormat.RGB24, false);
        Color32[] pixels = new Color32[width * height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                byte value = (byte)(((x / 32) + (y / 32)) % 2 == 0 ? 96 : 160);
                pixels[y * width + x] = new Color32(value, value, value, 255);
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();
        return texture;
    }
#endif

    private void SaveFrameAndMaybeUpload(
        byte[] jpgBytes,
        int imageWidth,
        int imageHeight,
        bool hasCameraToWorld,
        Matrix4x4 cameraToWorld,
        bool hasProjection,
        Matrix4x4 projection,
        string source)
    {
        string frameId = ReserveNextFrameId();
        string imageFileName = frameId + ".jpg";
        string captureId = BuildCaptureId(currentScanSessionId, frameId);

        CaptureMetadata metadata = BuildCaptureMetadata(
            captureId,
            frameId,
            imageFileName,
            imageWidth,
            imageHeight,
            hasCameraToWorld,
            cameraToWorld,
            hasProjection,
            projection,
            source
        );

        CapturePackage package = SaveCaptureDebugFiles(captureId, jpgBytes, metadata);

        if (uploadImmediatelyAfterCapture && package != null)
            StartCoroutine(UploadCapturePackageCoroutine(package, 0, 1));
    }

    private void FinishCaptureWithError(string message)
    {
        Debug.LogError($"[FurnitureCapture] {message}");
        isCapturing = false;
    }

    // ----------------------------------------------------------------------
    // Save
    // ----------------------------------------------------------------------

    private CapturePackage SaveCaptureDebugFiles(string captureId, byte[] jpgBytes, CaptureMetadata metadata)
    {
        if (jpgBytes == null || jpgBytes.Length == 0)
        {
            Debug.LogError("[FurnitureCapture] 저장할 JPG 바이트가 비어있습니다.");
            return null;
        }

        string sessionFolderName = SanitizePathPart(string.IsNullOrEmpty(metadata.scan_session_id) ? "no_session" : metadata.scan_session_id);
        string frameFolderName = SanitizePathPart(string.IsNullOrEmpty(metadata.frame_id) ? captureId : metadata.frame_id);

        string rootFolder = GetDebugRootFolderPath();
        string captureFolder = Path.Combine(rootFolder, sessionFolderName, frameFolderName);

        Directory.CreateDirectory(captureFolder);

        string imagePath = Path.Combine(captureFolder, frameFolderName + ".jpg");
        string metadataPath = Path.Combine(captureFolder, frameFolderName + ".json");

        File.WriteAllBytes(imagePath, jpgBytes);

        string metadataJson = JsonUtility.ToJson(metadata, true);
        File.WriteAllText(metadataPath, metadataJson);

        Debug.Log(
            "[FurnitureCapture] 촬영 디버그 파일 저장 완료\n" +
            $"scan_session_id: {metadata.scan_session_id}\n" +
            $"frame_id: {metadata.frame_id}\n" +
            $"imagePath: {imagePath}\n" +
            $"metadataPath: {metadataPath}"
        );

        return new CapturePackage
        {
            captureId = captureId,
            scanSessionId = metadata.scan_session_id,
            frameId = metadata.frame_id,
            folderPath = captureFolder,
            imagePath = imagePath,
            metadataPath = metadataPath,
            lastWriteTimeUtc = Directory.GetLastWriteTimeUtc(captureFolder)
        };
    }

    private string GetDebugRootFolderPath()
    {
        return Path.Combine(Application.persistentDataPath, outputFolderName);
    }

    private string ReserveNextFrameId()
    {
        int frameNumber = Mathf.Max(1, nextFrameNumber);
        nextFrameNumber = frameNumber + 1;
        return "frame_" + frameNumber.ToString("0000");
    }

    private string BuildCaptureId(string scanSessionId, string frameId)
    {
        string session = string.IsNullOrEmpty(scanSessionId) ? "no_session" : scanSessionId;
        return session + "_" + frameId;
    }

    private void StartLocalDebugScanSession()
    {
        currentScanSessionId = "local_scan_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmssfff");
        currentScanSessionStatus = "local_save_only";
        currentScanSessionCreatedByServer = false;
        nextFrameNumber = 1;

        Debug.LogWarning(
            "[FurnitureCapture] 서버 scan session 없이 로컬 저장용 session을 생성했습니다.\n" +
            $"scan_session_id: {currentScanSessionId}\n" +
            "이 session은 /scan-frame 업로드용으로 사용할 수 없습니다. 서버 업로드 전에는 StartScanSession()을 호출하세요."
        );
    }

    private string SanitizePathPart(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "empty";

        char[] invalidChars = Path.GetInvalidFileNameChars();
        string sanitized = value;
        for (int i = 0; i < invalidChars.Length; i++)
            sanitized = sanitized.Replace(invalidChars[i], '_');

        return sanitized;
    }

    // ----------------------------------------------------------------------
    // Upload
    // ----------------------------------------------------------------------

    private IEnumerator UploadAllSavedCapturesCoroutine()
    {
        isUploading = true;

        List<CapturePackage> packages = FindSavedCapturePackages();

        if (packages.Count == 0)
        {
            Debug.LogWarning($"[FurnitureCapture] 업로드할 JPG/JSON 캡처 패키지가 없습니다: {GetDebugRootFolderPath()}");
            isUploading = false;
            yield break;
        }

        packages.Sort((a, b) =>
        {
            int sessionCompare = string.Compare(a.scanSessionId, b.scanSessionId, StringComparison.Ordinal);
            if (sessionCompare != 0)
                return sessionCompare;

            return string.Compare(a.frameId, b.frameId, StringComparison.Ordinal);
        });

        Debug.Log($"[FurnitureCapture] 전체 캡처 업로드 시작: {packages.Count}개");

        for (int i = 0; i < packages.Count; i++)
            yield return UploadCapturePackageCoroutine(packages[i], i, packages.Count, keepUploadingFlag: true);

        Debug.Log("[FurnitureCapture] 전체 캡처 업로드 완료");
        isUploading = false;
    }

    private IEnumerator UploadCapturePackageCoroutine(CapturePackage package, int index, int totalCount, bool keepUploadingFlag = false)
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
        CaptureMetadata metadata;

        try
        {
            imageBytes = File.ReadAllBytes(package.imagePath);
            metadataJson = File.ReadAllText(package.metadataPath);
            metadata = JsonUtility.FromJson<CaptureMetadata>(metadataJson);
        }
        catch (Exception e)
        {
            Debug.LogError($"[FurnitureCapture] 업로드 파일 읽기 실패\n{e.Message}");
            if (!keepUploadingFlag) isUploading = false;
            yield break;
        }

        if (metadata == null || string.IsNullOrEmpty(metadata.scan_session_id) || string.IsNullOrEmpty(metadata.frame_id))
        {
            Debug.LogError(
                "[FurnitureCapture] metadata에 scan_session_id 또는 frame_id가 없습니다. " +
                "새 서버 API에서는 metadata 필드 안에 두 값이 반드시 필요합니다."
            );
            if (!keepUploadingFlag) isUploading = false;
            yield break;
        }

        if (metadata.scan_session_id.StartsWith("local_scan_", StringComparison.Ordinal))
        {
            Debug.LogError(
                "[FurnitureCapture] local_scan_ 세션은 서버에 생성된 scan_session_id가 아니므로 /scan-frame 업로드를 중단합니다. " +
                "StartScanSession()으로 서버 세션을 먼저 생성하세요."
            );
            if (!keepUploadingFlag) isUploading = false;
            yield break;
        }

        string imageFileName = Path.GetFileName(package.imagePath);
        List<IMultipartFormSection> formData = new List<IMultipartFormSection>
        {
            new MultipartFormFileSection("image", imageBytes, imageFileName, GetMimeType(imageFileName)),
            new MultipartFormDataSection("metadata", metadataJson)
        };

        string url = BuildUrl(uploadScanFramePath);

        using (UnityWebRequest request = UnityWebRequest.Post(url, formData))
        {
            request.timeout = requestTimeoutSeconds;

            Debug.Log(
                $"[FurnitureCapture] [{index + 1}/{totalCount}] scan-frame 업로드 시작\n" +
                $"url: {url}\n" +
                $"scan_session_id: {metadata.scan_session_id}\n" +
                $"frame_id: {metadata.frame_id}\n" +
                $"image: {package.imagePath}\n" +
                $"metadata: {package.metadataPath}"
            );

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError(
                    $"[FurnitureCapture] [{index + 1}/{totalCount}] scan-frame 업로드 실패\n" +
                    $"responseCode: {request.responseCode}\n" +
                    $"error: {request.error}\n" +
                    $"body: {request.downloadHandler.text}"
                );

                if (!keepUploadingFlag) isUploading = false;
                yield break;
            }

            Debug.Log(
                $"[FurnitureCapture] [{index + 1}/{totalCount}] scan-frame 업로드 성공\n" +
                $"responseCode: {request.responseCode}\n" +
                $"response: {request.downloadHandler.text}"
            );
        }

        if (!keepUploadingFlag)
            isUploading = false;
    }

    private List<CapturePackage> FindSavedCapturePackages()
    {
        List<CapturePackage> packages = new List<CapturePackage>();
        string debugRoot = GetDebugRootFolderPath();

        if (!Directory.Exists(debugRoot))
            return packages;

        List<string> candidateFolders = new List<string>();
        candidateFolders.Add(debugRoot);
        candidateFolders.AddRange(Directory.GetDirectories(debugRoot, "*", SearchOption.AllDirectories));

        HashSet<string> usedFolders = new HashSet<string>();
        for (int i = 0; i < candidateFolders.Count; i++)
        {
            string folder = candidateFolders[i];
            if (usedFolders.Contains(folder))
                continue;

            if (TryFindCapturePackage(folder, out CapturePackage package))
            {
                packages.Add(package);
                usedFolders.Add(folder);
            }
        }

        return packages;
    }

    private bool TryFindCapturePackage(string folderPath, out CapturePackage package)
    {
        package = null;

        if (!Directory.Exists(folderPath))
            return false;

        string[] jpgFiles = Directory.GetFiles(folderPath, "*.jpg", SearchOption.TopDirectoryOnly);
        string[] jpegFiles = Directory.GetFiles(folderPath, "*.jpeg", SearchOption.TopDirectoryOnly);
        string[] jsonFiles = Directory.GetFiles(folderPath, "*.json", SearchOption.TopDirectoryOnly);

        List<string> imageFiles = new List<string>();
        imageFiles.AddRange(jpgFiles);
        imageFiles.AddRange(jpegFiles);

        if (imageFiles.Count == 0 || jsonFiles.Length == 0)
            return false;

        imageFiles.Sort(StringComparer.Ordinal);
        Array.Sort(jsonFiles, StringComparer.Ordinal);

        string imagePath = imageFiles[0];
        string imageNameNoExt = Path.GetFileNameWithoutExtension(imagePath);
        string expectedJsonPath = Path.Combine(folderPath, imageNameNoExt + ".json");
        string metadataPath = File.Exists(expectedJsonPath) ? expectedJsonPath : jsonFiles[0];

        string metadataJson = File.ReadAllText(metadataPath);
        CaptureMetadata metadata = null;

        try
        {
            metadata = JsonUtility.FromJson<CaptureMetadata>(metadataJson);
        }
        catch
        {
            metadata = null;
        }

        string scanSessionId = metadata != null ? metadata.scan_session_id : string.Empty;
        string frameId = metadata != null && !string.IsNullOrEmpty(metadata.frame_id) ? metadata.frame_id : imageNameNoExt;

        package = new CapturePackage
        {
            captureId = imageNameNoExt,
            scanSessionId = scanSessionId,
            frameId = frameId,
            folderPath = folderPath,
            imagePath = imagePath,
            metadataPath = metadataPath,
            lastWriteTimeUtc = Directory.GetLastWriteTimeUtc(folderPath)
        };

        return true;
    }

    private string GetMimeType(string fileName)
    {
        string ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext == ".png" ? "image/png" : "image/jpeg";
    }

    // ----------------------------------------------------------------------
    // Generic server requests
    // ----------------------------------------------------------------------

    private IEnumerator PostEmptyCoroutine(string url, string label, int timeoutSeconds)
    {
        if (isServerRequestRunning)
        {
            Debug.LogWarning("[FurnitureCapture] 다른 서버 요청이 이미 진행 중입니다.");
            yield break;
        }

        isServerRequestRunning = true;
        byte[] bodyRaw = Encoding.UTF8.GetBytes("{}");

        using (UnityWebRequest request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
        {
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = timeoutSeconds;

            Debug.Log($"[FurnitureCapture] {label} POST 요청: {url}");
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError(
                    $"[FurnitureCapture] {label} POST 실패\n" +
                    $"responseCode: {request.responseCode}\n" +
                    $"error: {request.error}\n" +
                    $"body: {request.downloadHandler.text}"
                );
            }
            else
            {
                Debug.Log(
                    $"[FurnitureCapture] {label} POST 성공\n" +
                    $"responseCode: {request.responseCode}\n" +
                    $"response: {request.downloadHandler.text}"
                );
            }
        }

        isServerRequestRunning = false;
    }

    private IEnumerator GetCoroutine(string url, string label, int timeoutSeconds)
    {
        if (isServerRequestRunning)
        {
            Debug.LogWarning("[FurnitureCapture] 다른 서버 요청이 이미 진행 중입니다.");
            yield break;
        }

        isServerRequestRunning = true;

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            request.timeout = timeoutSeconds;

            Debug.Log($"[FurnitureCapture] {label} GET 요청: {url}");
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError(
                    $"[FurnitureCapture] {label} GET 실패\n" +
                    $"responseCode: {request.responseCode}\n" +
                    $"error: {request.error}\n" +
                    $"body: {request.downloadHandler.text}"
                );
            }
            else
            {
                Debug.Log(
                    $"[FurnitureCapture] {label} GET 성공\n" +
                    $"responseCode: {request.responseCode}\n" +
                    $"response: {request.downloadHandler.text}"
                );
            }
        }

        isServerRequestRunning = false;
    }

    private string BuildUrl(string path)
    {
        string baseUrl = string.IsNullOrEmpty(serverBaseUrl) ? "https://api.earquake.xyz" : serverBaseUrl.TrimEnd('/');
        string cleanPath = string.IsNullOrEmpty(path) ? string.Empty : path.TrimStart('/');
        return baseUrl + "/" + cleanPath;
    }

    private bool EnsureScanSession(string actionName)
    {
        if (HasActiveScanSession)
            return true;

        Debug.LogError($"[FurnitureCapture] {actionName}을 수행하려면 먼저 StartScanSession()으로 scan_session_id를 받아야 합니다.");
        return false;
    }

    private bool EnsureServerScanSession(string actionName)
    {
        if (HasActiveServerScanSession)
            return true;

        Debug.LogError(
            $"[FurnitureCapture] {actionName}을 수행하려면 먼저 StartScanSession()으로 서버 scan_session_id를 받아야 합니다. " +
            $"현재 scan_session_id: {(string.IsNullOrEmpty(currentScanSessionId) ? "null" : currentScanSessionId)}, " +
            $"created_by_server: {currentScanSessionCreatedByServer}"
        );
        return false;
    }

    // ----------------------------------------------------------------------
    // Metadata
    // ----------------------------------------------------------------------

    private CaptureMetadata BuildCaptureMetadata(
        string captureId,
        string frameId,
        string imageFileName,
        int imageWidth,
        int imageHeight,
        bool hasCameraToWorld,
        Matrix4x4 cameraToWorld,
        bool hasProjection,
        Matrix4x4 projection,
        string source = "HoloLens2_PhotoCapture")
    {
        List<DepthSample> depthSamples = BuildDepthSamples(
            imageWidth,
            imageHeight,
            hasCameraToWorld,
            cameraToWorld,
            hasProjection,
            projection
        );

        int depthHitCount = 0;
        for (int i = 0; i < depthSamples.Count; i++)
        {
            if (depthSamples[i] != null && depthSamples[i].hit)
                depthHitCount++;
        }

        CaptureMetadata metadata = new CaptureMetadata
        {
            scan_session_id = currentScanSessionId,
            frame_id = frameId,
            capture_id = captureId,
            timestamp_utc = DateTime.UtcNow.ToString("o"),
            source = source,
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
            depth_sample_source = includeDepthSamples ? "Unity Physics.Raycast from PhotoCapture near/far projection + camera_to_world" : "disabled",
            depth_sample_grid = includeDepthSamples
                ? new DepthSampleGrid { x = Mathf.Max(1, depthSampleGridX), y = Mathf.Max(1, depthSampleGridY) }
                : null,
            depth_sample_count = depthSamples.Count,
            depth_sample_hit_count = depthHitCount,
            depth_samples = depthSamples,
            room = BuildRoomSnapshot()
        };

        return metadata;
    }

    private List<DepthSample> BuildDepthSamples(
        int imageWidth,
        int imageHeight,
        bool hasCameraToWorld,
        Matrix4x4 cameraToWorld,
        bool hasProjection,
        Matrix4x4 projection)
    {
        List<DepthSample> samples = new List<DepthSample>();

        if (!includeDepthSamples)
            return samples;

        if (imageWidth <= 0 || imageHeight <= 0)
        {
            Debug.LogWarning("[FurnitureCapture] depth_samples 생성 실패: image size가 유효하지 않습니다.");
            return samples;
        }

        if (!hasCameraToWorld || !hasProjection)
        {
            Debug.LogWarning(
                "[FurnitureCapture] depth_samples 생성 실패: camera_to_world 또는 projection matrix가 없습니다. " +
                "PhotoCaptureFrame matrix가 있는 실제 HoloLens 촬영에서 depth_samples가 생성됩니다."
            );
            return samples;
        }

        int gridX = Mathf.Max(1, depthSampleGridX);
        int gridY = Mathf.Max(1, depthSampleGridY);
        float maxDistance = Mathf.Max(0.01f, depthRaycastMaxDistance);
        Matrix4x4 inverseProjection = projection.inverse;

        int hitCount = 0;

        for (int y = 0; y < gridY; y++)
        {
            for (int x = 0; x < gridX; x++)
            {
                int pixelX = Mathf.Clamp(Mathf.RoundToInt(((x + 0.5f) / gridX) * imageWidth), 0, imageWidth - 1);
                int pixelY = Mathf.Clamp(Mathf.RoundToInt(((y + 0.5f) / gridY) * imageHeight), 0, imageHeight - 1);

                Ray ray = BuildWorldRayFromPhotoPixel(
                    pixelX,
                    pixelY,
                    imageWidth,
                    imageHeight,
                    cameraToWorld,
                    inverseProjection
                );

                bool hit = Physics.Raycast(
                    ray,
                    out RaycastHit hitInfo,
                    maxDistance,
                    depthRaycastLayerMask,
                    QueryTriggerInteraction.Ignore
                );

                if (hit)
                    hitCount++;

                if (!hit && !includeMissedDepthSamples)
                    continue;

                DepthSample sample = new DepthSample
                {
                    pixel = new int[] { pixelX, pixelY },
                    hit = hit,
                    world = hit
                        ? new float[] { hitInfo.point.x, hitInfo.point.y, hitInfo.point.z }
                        : null,
                    normal = hit
                        ? new float[] { hitInfo.normal.x, hitInfo.normal.y, hitInfo.normal.z }
                        : null,
                    distance = hit ? hitInfo.distance : -1.0f
                };

                samples.Add(sample);
            }
        }

        Debug.Log(
            "[FurnitureCapture] depth_samples 생성 완료\n" +
            $"grid: {gridX}x{gridY}, samples: {samples.Count}, hit: {hitCount}, maxDistance: {maxDistance}"
        );

        return samples;
    }

    private Ray BuildWorldRayFromPhotoPixel(
        int pixelX,
        int pixelY,
        int imageWidth,
        int imageHeight,
        Matrix4x4 cameraToWorld,
        Matrix4x4 inverseProjection)
    {
        float ndcX = (((pixelX + 0.5f) / imageWidth) * 2.0f) - 1.0f;
        float ndcY = 1.0f - (((pixelY + 0.5f) / imageHeight) * 2.0f);

        // PhotoCaptureFrame.TryGetProjectionMatrix(near, far, out projection)으로 받은
        // Unity projection은 D3D 스타일 clip z [0, 1] 기준으로 near/far plane을 복원할 수 있습니다.
        // 기존처럼 clip z 하나만 inverse projection 하면 raw projection/convention 차이로
        // HoloLens 빌드에서 ray가 반대 방향 또는 엉뚱한 방향으로 나갈 수 있으므로,
        // near/far 두 점을 unproject한 뒤 방향을 계산합니다.
        Vector4 nearClip = new Vector4(ndcX, ndcY, 0.0f, 1.0f);
        Vector4 farClip = new Vector4(ndcX, ndcY, 1.0f, 1.0f);

        Vector4 nearCamera = inverseProjection * nearClip;
        Vector4 farCamera = inverseProjection * farClip;

        if (Mathf.Abs(nearCamera.w) > Mathf.Epsilon)
            nearCamera /= nearCamera.w;

        if (Mathf.Abs(farCamera.w) > Mathf.Epsilon)
            farCamera /= farCamera.w;

        Vector3 nearWorld = cameraToWorld.MultiplyPoint3x4(
            new Vector3(nearCamera.x, nearCamera.y, nearCamera.z)
        );

        Vector3 farWorld = cameraToWorld.MultiplyPoint3x4(
            new Vector3(farCamera.x, farCamera.y, farCamera.z)
        );

        Vector3 worldDirection = farWorld - nearWorld;

        if (worldDirection.sqrMagnitude < 0.000001f)
        {
            worldDirection = cameraToWorld.MultiplyVector(Vector3.forward);
        }

        Vector3 worldOrigin = cameraToWorld.MultiplyPoint3x4(Vector3.zero);
        return new Ray(worldOrigin, worldDirection.normalized);
    }

    private RoomSnapshot BuildRoomSnapshot()
    {
        TryAutoBindRoomObjectsFromConfirmRoom();

        RoomSnapshot snapshot = new RoomSnapshot
        {
            room_id = roomRoot != null ? roomRoot.name : "explicit_room_objects",
            timestamp_utc = DateTime.UtcNow.ToString("o"),
            coordinate_space = "Unity world space",
            root_object_name = roomRoot != null ? roomRoot.name : null,
            root_path = roomRoot != null ? GetTransformPath(roomRoot) : null,
            has_floor = floorObject != null,
            has_ceiling = ceilingObject != null,
            assigned_wall_count = wallObjects != null ? wallObjects.Count(wall => wall != null) : 0,
            entrance = BuildEntranceSnapshot()
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
            Debug.LogWarning("[FurnitureCapture] floorObject가 설정되지 않았습니다.");
        }

        if (ceilingObject != null)
        {
            snapshot.surfaces.Add(BuildSurfaceSnapshot("ceiling", ceilingObject, 0));
            ceilingCount = 1;
        }
        else
        {
            Debug.LogWarning("[FurnitureCapture] ceilingObject가 설정되지 않았습니다.");
        }

        if (wallObjects != null)
        {
            for (int i = 0; i < wallObjects.Count; i++)
            {
                Transform wall = wallObjects[i];
                if (wall == null)
                    continue;

                snapshot.surfaces.Add(BuildSurfaceSnapshot("wall", wall, wallCount));
                wallCount++;
            }
        }

        if (wallCount == 0)
            Debug.LogWarning("[FurnitureCapture] 설정된 wallObject가 없습니다.");

        Debug.Log(
            "[FurnitureCapture] Room metadata 생성 완료\n" +
            $"root: {(roomRoot != null ? roomRoot.name : "null")}\n" +
            $"floor: {floorCount}, ceiling: {ceilingCount}, wall: {wallCount}"
        );

        return snapshot;
    }

    private RoomEntranceSnapshot BuildEntranceSnapshot()
    {
        if (confirmRoomManager != null && confirmRoomManager.hasEntrance)
        {
            return new RoomEntranceSnapshot
            {
                has_entrance = true,
                center_world = SerializableVector3.From(confirmRoomManager.entrancePointWorld),
                radius_meters = confirmRoomManager.entranceRadiusMeters,
                shape = "circle_xz",
                coordinate_space = "Unity world space",
                purpose = "entrance_clearance_area",
                note = "Server should reconstruct a 1.5m radius entrance clearance area from this center point."
            };
        }

        float radius = confirmRoomManager != null ? confirmRoomManager.entranceRadiusMeters : 1.5f;
        return new RoomEntranceSnapshot
        {
            has_entrance = false,
            radius_meters = radius,
            shape = "circle_xz",
            coordinate_space = "Unity world space",
            purpose = "entrance_clearance_area",
            note = "Entrance point was not assigned on the client."
        };
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
            mesh = meshFilter.sharedMesh;
        else if (meshCollider != null && meshCollider.sharedMesh != null)
            mesh = meshCollider.sharedMesh;

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
                surface.mesh_triangles = mesh.triangles;
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

    private ApproxCameraIntrinsics BuildApproxIntrinsicsFromProjection(Matrix4x4 projection, int imageWidth, int imageHeight)
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
    // DTO classes - 내부 클래스로 두어 다른 테스트 코드 DTO와 이름 충돌을 피합니다.
    // ----------------------------------------------------------------------

    [Serializable]
    private class StartScanSessionResponse
    {
        public string scan_session_id;
        public string status;
    }

    [Serializable]
    private class CapturePackage
    {
        public string captureId;
        public string scanSessionId;
        public string frameId;
        public string folderPath;
        public string imagePath;
        public string metadataPath;
        public DateTime lastWriteTimeUtc;
    }

    [Serializable]
    private class CaptureMetadata
    {
        public string scan_session_id;
        public string frame_id;

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

        public string depth_sample_source;
        public DepthSampleGrid depth_sample_grid;
        public int depth_sample_count;
        public int depth_sample_hit_count;
        public List<DepthSample> depth_samples = new List<DepthSample>();

        public RoomSnapshot room;
    }

    [Serializable]
    private class DepthSampleGrid
    {
        public int x;
        public int y;
    }

    [Serializable]
    private class DepthSample
    {
        public int[] pixel;
        public bool hit;
        public float[] world;
        public float[] normal;
        public float distance;
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
        public RoomEntranceSnapshot entrance;
        public List<RoomSurfaceSnapshot> surfaces = new List<RoomSurfaceSnapshot>();
    }

    [Serializable]
    private class RoomEntranceSnapshot
    {
        public bool has_entrance;
        public SerializableVector3 center_world;
        public float radius_meters;
        public string shape;
        public string coordinate_space;
        public string purpose;
        public string note;
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
}

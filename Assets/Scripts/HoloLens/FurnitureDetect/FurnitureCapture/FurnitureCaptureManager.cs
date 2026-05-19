using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ConfirmedRoomRoot에서 생성된 Floor/Ceiling/Wall 참조를 사진 촬영/업로드 계열 컴포넌트에 전달하고,
/// scan session 기반 서버 요청을 UI 패널에서 간단히 호출할 수 있도록 중계합니다.
/// </summary>
public class FurnitureCaptureManager : MonoBehaviour
{
    [Header("References")]
    public ConfirmRoomManager confirmRoomManager;
    public HoloLensFurnitureCaptureAndUpload furnitureCapture;
    public HoloLensPhotoCaptureDebugger photoCaptureDebugger;
    public RoomGeometrySnapshotProvider roomSnapshotProvider;

    [Tooltip("참조가 비어 있으면 Scene에서 자동 탐색합니다.")]
    public bool autoFindReferences = true;

    [Header("Binding Option")]
    [Tooltip("RoomCapture로 넘어갈 때 ConfirmedRoomRoot의 Floor/Ceiling/Wall을 capture 컴포넌트들에 자동 등록합니다.")]
    public bool bindConfirmedRoomObjectsOnRoomConfirmed = true;

    [Tooltip("등록 전 capture 컴포넌트의 기존 room object 참조를 지웁니다.")]
    public bool clearExistingRoomObjectsBeforeBind = true;

    [Header("Debug")]
    public bool lastBindSucceeded;
    public string lastBindStatus;
    public int lastBoundWallCount;

    public string CurrentScanSessionId => furnitureCapture != null ? furnitureCapture.CurrentScanSessionId : string.Empty;
    public string CurrentScanSessionStatus => furnitureCapture != null ? furnitureCapture.CurrentScanSessionStatus : string.Empty;
    public int NextFrameNumber => furnitureCapture != null ? furnitureCapture.NextFrameNumber : 1;
    public bool HasActiveScanSession => furnitureCapture != null && furnitureCapture.HasActiveScanSession;
    public bool HasActiveServerScanSession => furnitureCapture != null && furnitureCapture.HasActiveServerScanSession;

    private void Awake()
    {
        EnsureReferences();
    }

    public void BindFromWorkflow(RoomBuildWorkflowManager workflow)
    {
        if (workflow == null)
            return;

        if (confirmRoomManager == null)
            confirmRoomManager = workflow.confirmRoomManager;

        EnsureReferences();
    }

    public bool BindConfirmedRoomObjectsFromConfirmRoom()
    {
        EnsureReferences();

        lastBindSucceeded = false;
        lastBoundWallCount = 0;

        if (confirmRoomManager == null)
        {
            lastBindStatus = "ConfirmRoomManager가 연결되어 있지 않습니다.";
            Debug.LogWarning($"[FurnitureCaptureManager] {lastBindStatus}");
            return false;
        }

        if (confirmRoomManager.confirmedRoomRoot == null)
        {
            lastBindStatus = "ConfirmedRoomRoot가 없습니다.";
            Debug.LogWarning($"[FurnitureCaptureManager] {lastBindStatus}");
            return false;
        }

        GameObject floor = confirmRoomManager.ConfirmedRoomFloorObject;
        GameObject ceiling = confirmRoomManager.ConfirmedRoomCeilingObject;
        IReadOnlyList<GameObject> sourceWalls = confirmRoomManager.ConfirmedRoomWallObjects;

        if (floor == null)
        {
            lastBindStatus = "ConfirmedRoom_Floor가 없습니다. ConfirmRoomManager.BuildConfirmedRoomVisualization() 이후 등록해야 합니다.";
            Debug.LogWarning($"[FurnitureCaptureManager] {lastBindStatus}");
            return false;
        }

        if (ceiling == null)
        {
            lastBindStatus = "ConfirmedRoom_Ceiling이 없습니다. ConfirmRoomManager.BuildConfirmedRoomVisualization() 이후 등록해야 합니다.";
            Debug.LogWarning($"[FurnitureCaptureManager] {lastBindStatus}");
            return false;
        }

        List<Transform> wallTransforms = new List<Transform>();

        if (sourceWalls != null)
        {
            for (int i = 0; i < sourceWalls.Count; i++)
            {
                GameObject wall = sourceWalls[i];
                if (wall != null)
                    wallTransforms.Add(wall.transform);
            }
        }

        if (wallTransforms.Count == 0)
        {
            lastBindStatus = "ConfirmedRoom_Wall 목록이 비어 있습니다.";
            Debug.LogWarning($"[FurnitureCaptureManager] {lastBindStatus}");
            return false;
        }

        if (clearExistingRoomObjectsBeforeBind)
        {
            furnitureCapture?.ClearRoomObjects();
            roomSnapshotProvider?.ClearRoomObjects();
        }

        Transform root = confirmRoomManager.confirmedRoomRoot;

        if (furnitureCapture != null)
            furnitureCapture.SetRoomObjectsFromConfirmRoomManager(confirmRoomManager);

        if (photoCaptureDebugger != null)
            photoCaptureDebugger.SetRoomObjectsFromConfirmRoomManager(confirmRoomManager);

        if (roomSnapshotProvider != null)
        {
            roomSnapshotProvider.SetConfirmRoomManager(confirmRoomManager);
            roomSnapshotProvider.SetRoomObjects(root, floor.transform, ceiling.transform, wallTransforms);
        }

        lastBindSucceeded = true;
        lastBoundWallCount = wallTransforms.Count;
        lastBindStatus = $"Confirmed room objects bound. floor:1, ceiling:1, walls:{lastBoundWallCount}";
        Debug.Log($"[FurnitureCaptureManager] {lastBindStatus}");
        return true;
    }

    public void ClearRoomObjectBindings()
    {
        furnitureCapture?.ClearRoomObjects();
        roomSnapshotProvider?.ClearRoomObjects();

        lastBindSucceeded = false;
        lastBoundWallCount = 0;
        lastBindStatus = "Room object bindings cleared.";
        Debug.Log($"[FurnitureCaptureManager] {lastBindStatus}");
    }

    // ----------------------------------------------------------------------
    // Scan session / server flow
    // ----------------------------------------------------------------------

    public void StartScanSession()
    {
        EnsureReferences();
        furnitureCapture?.StartScanSession();
    }

    public void ClearScanSession()
    {
        EnsureReferences();
        furnitureCapture?.ClearScanSession();
    }

    public void StartDetectionForCurrentSession()
    {
        EnsureReferences();
        Debug.LogWarning("[FurnitureCaptureManager] StartDetectionForCurrentSession is deprecated. Use FinishScanForCurrentSession instead.");
        furnitureCapture?.FinishScanForCurrentSession();
    }

    public void GetDetectionsForCurrentSession()
    {
        EnsureReferences();
        Debug.LogWarning("[FurnitureCaptureManager] GetDetectionsForCurrentSession is deprecated. Use GetScanJobForCurrentSession instead.");
        furnitureCapture?.GetScanJobForCurrentSession();
    }

    public void ProcessScanForCurrentSession()
    {
        EnsureReferences();
        Debug.LogWarning("[FurnitureCaptureManager] ProcessScanForCurrentSession is deprecated. Use FinishScanForCurrentSession instead.");
        furnitureCapture?.FinishScanForCurrentSession();
    }

    public void FinishScanForCurrentSession()
    {
        EnsureReferences();
        furnitureCapture?.FinishScanForCurrentSession();
    }

    public void GetScanJobForCurrentSession()
    {
        EnsureReferences();
        furnitureCapture?.GetScanJobForCurrentSession();
    }

    public void GetResultForCurrentSession()
    {
        EnsureReferences();
        furnitureCapture?.GetResultForCurrentSession();
    }

    // ----------------------------------------------------------------------
    // Capture / upload
    // ----------------------------------------------------------------------

    public void CapturePhotoForDebug()
    {
        EnsureReferences();
        BindConfirmedRoomObjectsFromConfirmRoom();
        furnitureCapture?.CapturePhotoForDebug();
    }

    public void CapturePhotoAndUpload()
    {
        EnsureReferences();
        BindConfirmedRoomObjectsFromConfirmRoom();
        furnitureCapture?.CapturePhotoAndUpload();
    }

    public void CapturePhotoAndUploadWithAutoSession()
    {
        EnsureReferences();
        BindConfirmedRoomObjectsFromConfirmRoom();
        furnitureCapture?.CapturePhotoAndUploadWithAutoSession();
    }

    public void UploadLatestSavedCapture()
    {
        EnsureReferences();
        furnitureCapture?.UploadLatestSavedCapture();
    }

    public void UploadAllSavedCaptures()
    {
        EnsureReferences();
        furnitureCapture?.UploadAllSavedCaptures();
    }

    private void EnsureReferences()
    {
        if (!autoFindReferences)
            return;

        if (confirmRoomManager == null)
            confirmRoomManager = FindFirstObjectByTypeIncludingInactive<ConfirmRoomManager>();

        if (furnitureCapture == null)
            furnitureCapture = FindFirstObjectByTypeIncludingInactive<HoloLensFurnitureCaptureAndUpload>();

        if (photoCaptureDebugger == null)
            photoCaptureDebugger = FindFirstObjectByTypeIncludingInactive<HoloLensPhotoCaptureDebugger>();

        if (roomSnapshotProvider == null)
            roomSnapshotProvider = FindFirstObjectByTypeIncludingInactive<RoomGeometrySnapshotProvider>();
    }

    private static T FindFirstObjectByTypeIncludingInactive<T>() where T : Object
    {
        T[] objects = Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        return objects != null && objects.Length > 0 ? objects[0] : null;
    }
}

using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;
using TMPro;

public enum RoomScanState { Idle, AutoScanning, Frozen }

public class RoomBuilder : MonoBehaviour
{
    //  MRTK XR Rig  (AR 매니저를 여기서 획득)
    [Header("MRTK XR Rig")]
    [Tooltip("씬의 MRTK XR Rig. 비워두면 씬 전체에서 자동 탐색")]
    public GameObject mrtkXRRig;

    private ARPlaneManager   planeManager;
    private ARAnchorManager  anchorManager;
    private ARRaycastManager raycastManager;

    [Header("방 구조 설정")]
    public GameObject roomFrameObject;
    public GameObject furnitureObject;
    [Tooltip("천장 높이 초기값 (자동 감지 시 덮어씀)")]
    public float ceilingHeight = 2.5f;
    public float wallThickness = 0.1f;

    [Header("인식 시각화 머티리얼 (비워두면 자동 생성)")]
    [Tooltip("스캔 중 바닥 오버레이 색상")]
    public Material floorScanMaterial;
    [Tooltip("스캔 중 벽 오버레이 색상")]
    public Material wallScanMaterial;
    [Tooltip("스캔 중 천장 오버레이 색상")]
    public Material ceilingScanMaterial;
    [Tooltip("FreezeRoom 후 바닥에 적용할 최종 머티리얼")]
    public Material floorFinalMaterial;

    [Header("평면 시각화")]
    [Tooltip("바닥 메쉬 오버레이 표시 여부")]
    public bool showFloorVisualization    = true;
    [Tooltip("벽 메쉬 오버레이 표시 여부\n" +
             "HoloLens 2에서 흰 화면이 나타나면 비활성화")]
    public bool showWallVisualization     = true;
    [Tooltip("천장 메쉬 오버레이 표시 여부")]
    public bool showCeilingVisualization  = true;

    [Header("자동 인식 설정")]
    [Tooltip("카메라 Y 대비 이 값(m) 이하를 바닥 후보로 판단")]
    public float floorBelowCameraThreshold = -0.5f;
    [Tooltip("바닥 기준 이 범위 내 수평면 = 가구 (무시)")]
    public float furnitureMinHeight = 0.25f;
    public float furnitureMaxHeight = 1.30f;

    // ─────────────────────────────────────────────
    //  디버그 UI  (비워두면 런타임 생성)
    // ─────────────────────────────────────────────
    [Header("디버그 UI (연결 필요)")]
    public TextMeshProUGUI debugStatusText;
    public TextMeshProUGUI debugPlanesText;
    public Button          freezeButton;

    [Header("UI 매니저")]
    public RoomBuildUIManager uiManager;

    // ─────────────────────────────────────────────
    //  공개 상태
    // ─────────────────────────────────────────────
    public RoomScanState ScanState     { get; private set; } = RoomScanState.Idle;
    public bool          IsRoomFrozen  => ScanState == RoomScanState.Frozen;
    public float         currentFloorHeight => currentFloorY;

    // ─────────────────────────────────────────────
    //  내부 상태
    // ─────────────────────────────────────────────
    // 자동 인식 결과
    private ARPlane bestFloorPlane;
    private ARPlane prevBestFloorPlane; // 단일 바닥 표시: 교체 시 이전 시각화 제거용
    private ARPlane bestCeilingPlane;
    private readonly List<ARPlane> detectedWalls = new List<ARPlane>();

    // 평면별 시각화 GO  (TrackableId → GameObject)
    private readonly Dictionary<TrackableId, GameObject> planeVisuals
        = new Dictionary<TrackableId, GameObject>();


    // 런타임 생성 스캔 머티리얼
    private Material _floorScanMat;
    private Material _wallScanMat;
    private Material _ceilingScanMat;

    // 방 지오메트리
    private readonly List<Vector3> cornerPoints = new List<Vector3>();
    private bool  isRoomFinished   = false;
    private float currentFloorY    = 0f;
    private bool  isFloorDetected  = false;
    private float currentCeilingY  = 0f;
    private bool  isCeilingDetected = false;


    private static readonly List<ARRaycastHit> arHits = new List<ARRaycastHit>();

    // ─────────────────────────────────────────────
    //  초기화
    // ─────────────────────────────────────────────
    void Awake()
    {
        // AR 매니저: mrtkXRRig에서 획득 → 없으면 씬 전체 탐색
        GameObject rig = mrtkXRRig;
        if (rig == null)
        {
            // "MRTK XR Rig" 이름으로 탐색
            rig = GameObject.Find("MRTK XR Rig");
        }

        if (rig != null)
        {
            planeManager   = rig.GetComponentInChildren<ARPlaneManager>(true);
            anchorManager  = rig.GetComponentInChildren<ARAnchorManager>(true);
            raycastManager = rig.GetComponentInChildren<ARRaycastManager>(true);
        }

        // 위에서 못 찾은 경우 씬 전체 폴백
        if (planeManager   == null) planeManager   = FindAnyObjectByType<ARPlaneManager>();
        if (raycastManager == null) raycastManager = FindAnyObjectByType<ARRaycastManager>();
        if (anchorManager  == null) anchorManager  = FindAnyObjectByType<ARAnchorManager>();

        if (roomFrameObject == null)
        {
            roomFrameObject = new GameObject("RoomFrame");
            roomFrameObject.transform.position = Vector3.zero;
        }

        // 스캔 머티리얼 준비
        _floorScanMat   = floorScanMaterial   != null ? floorScanMaterial   : MakeScanMat(new Color(0.20f, 0.65f, 1.00f, 0.40f));
        _wallScanMat    = wallScanMaterial     != null ? wallScanMaterial    : MakeScanMat(new Color(0.85f, 0.85f, 0.85f, 0.35f));
        _ceilingScanMat = ceilingScanMaterial  != null ? ceilingScanMaterial : MakeScanMat(new Color(1.00f, 0.88f, 0.20f, 0.32f));
    }

    void Start()
    {
        EnsureSceneLighting();
        StartAutoScan();
    }

    void Update()
    {
        if (ScanState != RoomScanState.AutoScanning) return;

        ClassifyPlanes();
        RefreshDebugUI();
    }

    public void StartAutoScan()
    {
        if (isRoomFinished) return;
        ScanState = RoomScanState.AutoScanning;

        if (planeManager != null)
        {
            planeManager.enabled = true;
            planeManager.requestedDetectionMode =
                PlaneDetectionMode.Horizontal | PlaneDetectionMode.Vertical;

            // ARFoundation 6.0: trackablesChanged 이벤트로 평면 추가/갱신/삭제 감지
            planeManager.trackablesChanged.AddListener(OnPlanesChanged);
        }

        UpdateDebugStatus("방을 둘러보세요...");
    }

    private void ClassifyPlanes()
    {
        if (planeManager == null) return;

        float cameraY  = Camera.main != null ? Camera.main.transform.position.y : 0f;
        float lowestY  = float.MaxValue;
        float highestY = float.MinValue;
        ARPlane newFloor = null, newCeiling = null;
        detectedWalls.Clear();

        foreach (var plane in planeManager.trackables)
        {
            if (!plane.gameObject.activeSelf) continue;
            GetPlaneType(plane, cameraY,
                out bool isFloor, out bool isCeiling, out bool isWall, out bool isFurniture);

            if (isFurniture) continue;

            float py = plane.transform.position.y;
            if      (isFloor   && py < lowestY)  { lowestY  = py; newFloor   = plane; }
            else if (isCeiling && py > highestY)  { highestY = py; newCeiling = plane; }
            else if (isWall)                       detectedWalls.Add(plane);
        }

        if (newFloor != null)
        {
            // 베스트 바닥 변경 시 이전 바닥 시각화 제거 (동시에 여러 바닥 표시 방지)
            if (prevBestFloorPlane != null
                && newFloor.trackableId != prevBestFloorPlane.trackableId)
            {
                RemovePlaneVisual(prevBestFloorPlane.trackableId);
            }
            prevBestFloorPlane = newFloor;
            bestFloorPlane     = newFloor;
            currentFloorY      = newFloor.transform.position.y;
            isFloorDetected    = true;
        }
        if (newCeiling != null) { bestCeilingPlane = newCeiling; currentCeilingY = newCeiling.transform.position.y; isCeilingDetected = true; }
    }

    // ─────────────────────────────────────────────
    //  평면 타입 판별 (공용)
    // ─────────────────────────────────────────────
    private void GetPlaneType(ARPlane plane, float cameraY,
        out bool isFloor, out bool isCeiling, out bool isWall, out bool isFurniture)
    {
        PlaneClassifications cls = plane.classifications;
        PlaneAlignment        ali = plane.alignment;
        float                 py  = plane.transform.position.y;

        bool hasNoCls      = cls == PlaneClassifications.None;
        bool hasFloorCls   = (cls & PlaneClassifications.Floor)    != 0;
        bool hasCeilingCls = (cls & PlaneClassifications.Ceiling)  != 0;
        bool hasWallCls    = (cls & PlaneClassifications.WallFace) != 0;
        bool hasFurnCls    = (cls & PlaneClassifications.Table) != 0 ||
                             (cls & PlaneClassifications.Seat)  != 0;

        // 경사 천장: 분류 없는 평면 중 노멀이 아래를 향하고(y < -0.2) 카메라보다 높은 위치
        // (예: 다락방 지붕 등 HorizontalDown이 아닌 경우 포함)
        bool isSlantedCeiling = hasNoCls
            && py > cameraY + 0.3f
            && plane.normal.y < -0.2f;

        isFloor   = hasFloorCls   || (hasNoCls && ali == PlaneAlignment.HorizontalUp   && py < cameraY + floorBelowCameraThreshold);
        isCeiling = hasCeilingCls || (hasNoCls && ali == PlaneAlignment.HorizontalDown) || isSlantedCeiling;
        isWall    = hasWallCls    || (hasNoCls && (ali == PlaneAlignment.Vertical || ali == PlaneAlignment.NotAxisAligned));

        isFurniture = hasFurnCls
            || (!isFloor && !isCeiling && !isWall
                && ali == PlaneAlignment.HorizontalUp
                && isFloorDetected
                && py > currentFloorY + furnitureMinHeight
                && py < currentFloorY + furnitureMaxHeight);
    }

    // ─────────────────────────────────────────────
    //  평면 추가 / 갱신 / 삭제 이벤트
    // ─────────────────────────────────────────────
    private void OnPlanesChanged(ARTrackablesChangedEventArgs<ARPlane> args)
    {
        float cameraY = Camera.main != null ? Camera.main.transform.position.y : 0f;

        foreach (var plane in args.added)
        {
            DisableDefaultPlaneMesh(plane);
            UpdatePlaneVisual(plane, cameraY);
        }
        foreach (var plane in args.updated)
        {
            UpdatePlaneVisual(plane, cameraY);
        }
        // ARFoundation 6.0: removed는 KeyValuePair<TrackableId, ARPlane> 컬렉션
        foreach (var kv in args.removed)
        {
            RemovePlaneVisual(kv.Key);
        }
    }

    // ─────────────────────────────────────────────
    //  기본 ARPlane 메쉬 시각화 비활성화
    //  (울퉁불퉁한 기본 시각화 → 우리의 평평한 폴리곤으로 교체)
    // ─────────────────────────────────────────────
    private void DisableDefaultPlaneMesh(ARPlane plane)
    {
        // ARPlaneMeshVisualizer: 기본 범프 메쉬 생성기
        var vis = plane.GetComponent<ARPlaneMeshVisualizer>();
        if (vis != null) vis.enabled = false;

        // ARPlane GO 자체의 MeshRenderer 비활성화
        // (MeshFilter는 Behaviour가 아니므로 enabled 없음 → 건드리지 않음)
        var mr = plane.GetComponent<MeshRenderer>();
        if (mr != null) mr.enabled = false;
    }

    // ─────────────────────────────────────────────
    //  평면 시각화 GO 생성/갱신 (평평한 폴리곤 메쉬)
    // ─────────────────────────────────────────────
    private void UpdatePlaneVisual(ARPlane plane, float cameraY)
    {
        GetPlaneType(plane, cameraY,
            out bool isFloor, out bool isCeiling, out bool isWall, out bool isFurniture);

        if (isFurniture) { RemovePlaneVisual(plane.trackableId); return; }
        if (!isFloor && !isCeiling && !isWall) return;

        // 바닥은 최적 1개만 표시 (비최적 바닥 시각화 차단)
        if (isFloor && bestFloorPlane != null
            && plane.trackableId != bestFloorPlane.trackableId)
        {
            RemovePlaneVisual(plane.trackableId);
            return;
        }

        // 타입별 시각화 토글: 비활성화된 타입은 기존 GO 제거 후 종료
        bool showThis = (isFloor    &&  showFloorVisualization)
                     || (isCeiling  &&  showCeilingVisualization)
                     || (isWall     &&  showWallVisualization);

        if (!showThis)
        {
            RemovePlaneVisual(plane.trackableId);
            return;
        }

        Material mat = isFloor ? _floorScanMat
                     : isCeiling ? _ceilingScanMat
                     : _wallScanMat;

        // GO 생성 또는 기존 GO 재사용
        if (!planeVisuals.TryGetValue(plane.trackableId, out GameObject visGO))
        {
            visGO = new GameObject($"PlaneVis_{plane.trackableId}");
            visGO.AddComponent<MeshFilter>();
            var mr = visGO.AddComponent<MeshRenderer>();

            // 생성 직후 기본 흰색 재질 노출 방지: 머티리얼 먼저 설정
            mr.material = mat;

            // 손가락/포인터 Ray가 시각화 메쉬에 막히지 않도록 Ignore Raycast 레이어 설정
            visGO.layer = 2;

            planeVisuals[plane.trackableId] = visGO;
        }
        else
        {
            var mr = visGO.GetComponent<MeshRenderer>();
            if (mr != null) mr.material = mat;
        }

        // 평평한 폴리곤 메쉬 생성
        var mfComp = visGO.GetComponent<MeshFilter>();
        Mesh mesh = BuildFlatPlaneMesh(plane, isFloor, isCeiling);
        if (mesh != null) mfComp.mesh = mesh;
    }

    // 감지 해제된 평면의 시각화 GO 제거
    private void RemovePlaneVisual(TrackableId id)
    {
        if (planeVisuals.TryGetValue(id, out GameObject go))
        {
            if (go != null) Destroy(go);
            planeVisuals.Remove(id);
        }
    }

    // ─────────────────────────────────────────────
    //  평평한 폴리곤 메쉬 생성
    //  바닥/천장: 경계를 단일 Y값으로 평탄화 → 완전히 평평
    //  벽: 경계를 ARPlane 로컬 좌표로 사용 (이미 평면)
    // ─────────────────────────────────────────────
    private Mesh BuildFlatPlaneMesh(ARPlane plane, bool isFloor, bool isCeiling)
    {
        var    boundary = plane.boundary;
        int    n        = boundary.Length;
        if (n < 3) return null;

        // GO 위치를 평면 중심으로 설정
        Vector3 center = plane.transform.position;
        if (isFloor   && isFloorDetected)   center.y = currentFloorY;
        if (isCeiling && isCeilingDetected) center.y = currentCeilingY;
        plane.GetComponent<MeshFilter>(); // (unused, GO parenting)

        // 시각화 GO 위치
        if (planeVisuals.TryGetValue(plane.trackableId, out GameObject visGO))
            visGO.transform.position = center;

        // 노멀 방향 결정 (Lit 셰이더 조명 반영을 위해 명시적 설정)
        Vector3 frontNormal, backNormal;
        if      (isFloor)   { frontNormal = Vector3.up;         backNormal = Vector3.down; }
        else if (isCeiling) { frontNormal = Vector3.down;       backNormal = Vector3.up;   }
        else                { frontNormal = plane.transform.up; backNormal = -frontNormal; } // 벽: ARPlane 법선

        // 앞면(0..n-1) / 뒷면(n..2n-1) 버텍스 분리 → 노멀이 서로 다름
        Vector3[] verts   = new Vector3[n * 2];
        Vector3[] normals = new Vector3[n * 2];

        for (int i = 0; i < n; i++)
        {
            Vector3 worldPt = plane.transform.TransformPoint(
                new Vector3(boundary[i].x, 0f, boundary[i].y));

            // 바닥/천장: Y를 단일값으로 고정 (평탄화 핵심)
            if      (isFloor   && isFloorDetected)   worldPt.y = currentFloorY;
            else if (isCeiling && isCeilingDetected) worldPt.y = currentCeilingY;
            // 벽: 그대로 (ARFoundation 벽 평면은 이미 수직 평면)

            Vector3 localPt  = worldPt - center;
            verts[i]         = localPt;
            verts[i + n]     = localPt;
            normals[i]       = frontNormal;
            normals[i + n]   = backNormal;
        }

        // 앞면 + 뒷면(와인딩 반전) 삼각형 생성
        List<int> tris = new List<int>((n - 2) * 6);
        for (int i = 1; i < n - 1; i++)
        {
            tris.Add(0);     tris.Add(i);         tris.Add(i + 1);     // 앞면
            tris.Add(n);     tris.Add(n + i + 1); tris.Add(n + i);     // 뒷면
        }

        Mesh mesh = new Mesh { name = "FlatPlaneMesh" };
        mesh.vertices  = verts;
        mesh.normals   = normals;
        mesh.triangles = tris.ToArray();
        mesh.RecalculateBounds();
        return mesh;
    }

    // ─────────────────────────────────────────────
    //  방 고정  (완성 버튼 연결)
    // ─────────────────────────────────────────────
    public void FreezeRoom()
    {
        if (ScanState == RoomScanState.Frozen) return;

        if (!isFloorDetected)
        {
            UpdateDebugStatus("[오류] 바닥이 감지되지 않았습니다");
            if (uiManager != null) uiManager.ShowAlert("바닥이 감지되지 않았습니다.");
            return;
        }

        ScanState = RoomScanState.Frozen;

        // 스캔 이벤트 해제 + 평면 고정
        if (planeManager != null)
        {
            planeManager.trackablesChanged.RemoveListener(OnPlanesChanged);
            planeManager.enabled = false;
        }

        // 천장 높이 계산
        if (isCeilingDetected && currentCeilingY > currentFloorY + 1.0f)
            ceilingHeight = currentCeilingY - currentFloorY;

        // 바닥 평면 경계 → 코너 포인트
        cornerPoints.Clear();
        if (bestFloorPlane != null)
            ExtractCornerPoints(bestFloorPlane);

        // 코너를 인근 감지 벽 평면에 스냅 → 창문/갭 부근 코너 정밀도 향상
        SnapCornersToWalls();

        bool built;
        if (cornerPoints.Count >= 3)
        {
            BuildRoomGeometry();
            built = true;
            UpdateDebugStatus($"완료  코너 {cornerPoints.Count}개 | 높이 {ceilingHeight:F2}m");
        }
        else
        {
            built = TryBuildRoomFromWalls();
            UpdateDebugStatus(built
                ? $"완료 (벽 기반)  높이 {ceilingHeight:F2}m"
                : "[경고] 인식 불충분 - 더 둘러보세요");

            if (!built)
            {
                ScanState = RoomScanState.AutoScanning;
                if (planeManager != null)
                {
                    planeManager.enabled = true;
                    planeManager.trackablesChanged.AddListener(OnPlanesChanged);
                }
                if (freezeButton != null) freezeButton.interactable = true;
                return;
            }
        }

        // 방 완성 후 스캔 오버레이 전체 제거 → 최종 지오메트리로 대체
        // (창문/외벽 방향으로 오버레이가 뚫고 나가는 현상 방지)
        if (built) ClearScanVisuals();

        if (freezeButton != null) freezeButton.interactable = false;
        if (uiManager    != null) uiManager.ShowAlert("방 인식 완료!");
    }

    // 스캔 머티리얼 → 최종 머티리얼 교체
    private void SwapToFinalMaterials()
    {
        Material finalMat = floorFinalMaterial != null ? floorFinalMaterial : _floorScanMat;
        foreach (var kv in planeVisuals)
        {
            if (kv.Value == null) continue;
            var mr = kv.Value.GetComponent<MeshRenderer>();
            if (mr != null && mr.material == _floorScanMat)
                mr.material = finalMat;
        }
    }

    // ─────────────────────────────────────────────
    //  코너 포인트 추출 / 폴백
    // ─────────────────────────────────────────────
    private void ExtractCornerPoints(ARPlane floor)
    {
        foreach (Vector2 b in floor.boundary)
        {
            Vector3 worldPt = floor.transform.TransformPoint(new Vector3(b.x, 0f, b.y));
            worldPt.y = currentFloorY;
            cornerPoints.Add(worldPt);
        }
    }

    // ─────────────────────────────────────────────
    //  코너 포인트를 인근 감지 벽 평면에 스냅
    //  창문처럼 벽이 미감지된 방향도 양옆 코너가 인접 벽에 맞춰지면
    //  BuildRoomGeometry()가 해당 엣지에 자동으로 합성 벽을 생성함
    // ─────────────────────────────────────────────
    private void SnapCornersToWalls()
    {
        if (detectedWalls.Count == 0 || cornerPoints.Count < 3) return;

        const float snapThreshold = 0.4f; // 0.4m 이내 벽에만 스냅

        for (int i = 0; i < cornerPoints.Count; i++)
        {
            Vector3 cp      = cornerPoints[i];
            float   minDist = snapThreshold;
            Vector3 best    = cp;

            foreach (var wall in detectedWalls)
            {
                Vector3 wallNorm = wall.transform.up;         // ARPlane 법선
                Vector3 wallPos  = wall.transform.position;
                wallPos.y        = currentFloorY;

                // 벽 평면까지 수직 거리
                float dist = Mathf.Abs(Vector3.Dot(cp - wallPos, wallNorm));
                if (dist < minDist)
                {
                    minDist = dist;
                    // 벽 평면에 수직 투영
                    best   = cp - wallNorm * Vector3.Dot(cp - wallPos, wallNorm);
                    best.y = currentFloorY;
                }
            }
            cornerPoints[i] = best;
        }
    }

    // ─────────────────────────────────────────────
    //  스캔 오버레이 전체 제거 (방 완성 후 호출)
    //  최종 지오메트리(FinalFloor, Wall_X)로 대체되므로
    //  오버레이가 창문/외벽을 뚫고 나가는 현상 방지
    // ─────────────────────────────────────────────
    private void ClearScanVisuals()
    {
        foreach (var kv in new List<KeyValuePair<TrackableId, GameObject>>(planeVisuals))
        {
            if (kv.Value != null) Destroy(kv.Value);
        }
        planeVisuals.Clear();
    }

    private bool TryBuildRoomFromWalls()
    {
        if (detectedWalls.Count < 2) return false;

        float minX = float.MaxValue, maxX = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;

        foreach (var wall in detectedWalls)
        {
            Vector3 p = wall.transform.position;
            float hw = wall.size.x * 0.5f, hd = wall.size.y * 0.5f;
            minX = Mathf.Min(minX, p.x - hw); maxX = Mathf.Max(maxX, p.x + hw);
            minZ = Mathf.Min(minZ, p.z - hd); maxZ = Mathf.Max(maxZ, p.z + hd);
        }

        float y = currentFloorY;
        cornerPoints.Clear();
        cornerPoints.Add(new Vector3(minX, y, minZ));
        cornerPoints.Add(new Vector3(maxX, y, minZ));
        cornerPoints.Add(new Vector3(maxX, y, maxZ));
        cornerPoints.Add(new Vector3(minX, y, maxZ));

        BuildRoomGeometry();
        return true;
    }

    // ─────────────────────────────────────────────
    //  방 지오메트리 생성 (벽 + 바닥 메쉬)
    // ─────────────────────────────────────────────
    private void BuildRoomGeometry()
    {
        if (isRoomFinished || cornerPoints.Count < 3) return;
        isRoomFinished = true;

        for (int i = 0; i < cornerPoints.Count; i++)
            CreateWallSegment(cornerPoints[i], cornerPoints[(i + 1) % cornerPoints.Count], i);

        CreateFinalFloorMesh();

        Debug.Log($"[RoomBuilder] 완료: 코너 {cornerPoints.Count}개, 높이 {ceilingHeight:F2}m");
    }

    // 외부 호환용
    public void FinishRoom()
    {
        if (cornerPoints.Count >= 3) BuildRoomGeometry();
    }

    // ─────────────────────────────────────────────
    //  벽 세그먼트 생성
    // ─────────────────────────────────────────────
    private void CreateWallSegment(Vector3 start, Vector3 end, int index)
    {
        // wallPrefab 없이도 동작: 기본 Cube로 생성
        Vector3    center   = (start + end) * 0.5f;
        center.y            = currentFloorY + ceilingHeight * 0.5f;
        float      length   = Vector3.Distance(start, end);
        Quaternion rotation = Quaternion.LookRotation((end - start).normalized);

        GameObject wall;
        if (TryGetWallPrefab(out GameObject prefab))
        {
            wall = Instantiate(prefab, center, rotation, roomFrameObject.transform);
        }
        else
        {
            wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.SetParent(roomFrameObject.transform);
            wall.transform.SetPositionAndRotation(center, rotation);
        }
        wall.name = "Wall_" + index;
        wall.transform.localScale = new Vector3(wallThickness, ceilingHeight, length);

        // 사용자 직접 조정을 위한 BoxCollider 보장 (Cube는 자동 포함)
        // Inspector에서 MRTK3 ObjectManipulator 컴포넌트 추가 시 Far/Near 인터랙션으로 조정 가능
        if (wall.GetComponent<BoxCollider>() == null)
            wall.AddComponent<BoxCollider>();
    }

    [SerializeField] private GameObject wallPrefab;
    private bool TryGetWallPrefab(out GameObject prefab) { prefab = wallPrefab; return wallPrefab != null; }

    // ─────────────────────────────────────────────
    //  최종 바닥 메쉬 생성  (Ear Clipping 삼각분할)
    // ─────────────────────────────────────────────
    private void CreateFinalFloorMesh()
    {
        if (cornerPoints.Count < 3) return;

        GameObject floorObj = new GameObject("FinalFloor");
        floorObj.transform.SetParent(roomFrameObject.transform);

        Vector3 center = Vector3.zero;
        foreach (var p in cornerPoints) center += p;
        center /= cornerPoints.Count;
        floorObj.transform.position = center;

        MeshFilter   mf = floorObj.AddComponent<MeshFilter>();
        MeshRenderer mr = floorObj.AddComponent<MeshRenderer>();
        mr.material = floorFinalMaterial != null ? floorFinalMaterial : _floorScanMat;

        int       n       = cornerPoints.Count;
        List<int> earTris = EarClipTriangulate(cornerPoints);
        Mesh      mesh    = new Mesh { name = "FinalFloorMesh" };

        Vector3[] verts   = new Vector3[n * 2];
        Vector3[] normals = new Vector3[n * 2];
        Vector2[] uvs     = new Vector2[n * 2];

        for (int i = 0; i < n; i++)
        {
            Vector3 lp = cornerPoints[i] - center;
            lp.y = 0f; // 완전 평탄화
            verts[i]       = verts[i + n] = lp;
            normals[i]     = Vector3.up;
            normals[i + n] = Vector3.down;
            uvs[i]         = uvs[i + n] = new Vector2(cornerPoints[i].x, cornerPoints[i].z);
        }

        List<int> tris = new List<int>();
        for (int i = 0; i < earTris.Count; i += 3)
        {
            tris.Add(earTris[i]); tris.Add(earTris[i + 1]); tris.Add(earTris[i + 2]);
            tris.Add(earTris[i] + n); tris.Add(earTris[i + 2] + n); tris.Add(earTris[i + 1] + n);
        }

        mesh.vertices  = verts;
        mesh.normals   = normals;
        mesh.uv        = uvs;
        mesh.triangles = tris.ToArray();
        mesh.RecalculateBounds();
        mf.mesh = mesh;

        // Compound Convex Collider
        const float th = 0.05f;
        for (int i = 0; i < earTris.Count; i += 3)
        {
            Vector3 a = cornerPoints[earTris[i]]     - center; a.y = 0f;
            Vector3 b = cornerPoints[earTris[i + 1]] - center; b.y = 0f;
            Vector3 c = cornerPoints[earTris[i + 2]] - center; c.y = 0f;
            Vector3 d = Vector3.down * th;

            Mesh pm = new Mesh { name = $"FloorCol_{i / 3}" };
            pm.vertices  = new[] { a, b, c, a + d, b + d, c + d };
            pm.triangles = new[] { 0,1,2, 3,5,4, 0,3,1, 1,3,4, 1,4,2, 2,4,5, 2,5,0, 0,5,3 };
            pm.RecalculateNormals(); pm.RecalculateBounds();

            var col = new GameObject($"FloorCol_{i / 3}");
            col.transform.SetParent(floorObj.transform, false);
            var mc = col.AddComponent<MeshCollider>();
            mc.sharedMesh = pm; mc.convex = true;
        }
    }

    // ─────────────────────────────────────────────
    //  Ear Clipping 삼각분할
    // ─────────────────────────────────────────────
    private List<int> EarClipTriangulate(List<Vector3> poly)
    {
        var tris = new List<int>();
        int n = poly.Count;
        if (n < 3) return tris;

        float area = 0f;
        for (int i = 0; i < n; i++)
        {
            Vector3 a = poly[i], b = poly[(i + 1) % n];
            area += a.x * b.z - b.x * a.z;
        }
        if (Mathf.Approximately(area, 0f)) return tris;

        bool      isCCW = area > 0f;
        var       idx   = new List<int>(n);
        for (int i = 0; i < n; i++) idx.Add(i);

        int count = n, iter = 0, maxIter = n * n, cur = 0;
        while (count > 2 && iter++ < maxIter)
        {
            int ip = ((cur - 1) % count + count) % count;
            int ic = cur % count;
            int in_ = (cur + 1) % count;
            int pi = idx[ip], ci = idx[ic], ni = idx[in_];

            Vector2 A = new Vector2(poly[pi].x, poly[pi].z);
            Vector2 B = new Vector2(poly[ci].x, poly[ci].z);
            Vector2 C = new Vector2(poly[ni].x, poly[ni].z);

            float cross   = C2D(A, B, C);
            bool  convex  = isCCW ? cross > 0f : cross < 0f;

            if (convex)
            {
                bool ear = true;
                for (int j = 0; j < count && ear; j++)
                {
                    int t = idx[j];
                    if (t == pi || t == ci || t == ni) continue;
                    if (PinT(new Vector2(poly[t].x, poly[t].z), A, B, C)) ear = false;
                }
                if (ear)
                {
                    if (isCCW) { tris.Add(pi); tris.Add(ni); tris.Add(ci); }
                    else       { tris.Add(pi); tris.Add(ci); tris.Add(ni); }
                    idx.RemoveAt(ic); count--;
                    if (count <= 2) break;
                    cur = 0; continue;
                }
            }
            if (++cur >= count) cur = 0;
        }
        return tris;
    }

    private float C2D(Vector2 A, Vector2 B, Vector2 C)
        => (B.x - A.x) * (C.y - A.y) - (B.y - A.y) * (C.x - A.x);

    private bool PinT(Vector2 P, Vector2 A, Vector2 B, Vector2 C)
    {
        float d1 = C2D(A, B, P), d2 = C2D(B, C, P), d3 = C2D(C, A, P);
        return !((d1 < 0 || d2 < 0 || d3 < 0) && (d1 > 0 || d2 > 0 || d3 > 0));
    }

    // ─────────────────────────────────────────────
    //  UI 업데이트
    // ─────────────────────────────────────────────
    private void UpdateDebugStatus(string msg)
    {
        if (debugStatusText != null) debugStatusText.text = msg;
    }

    private void RefreshDebugUI()
    {
        if (debugPlanesText == null) return;
        debugPlanesText.text =
            $"바닥: {(isFloorDetected   ? $"감지됨 (Y={currentFloorY:F2}m)" : "미감지")}\n" +
            $"천장: {(isCeilingDetected ? $"감지됨 (H={(currentCeilingY - currentFloorY):F1}m)" : "미감지")}\n" +
            $"벽: {detectedWalls.Count}개";
    }

    // ─────────────────────────────────────────────
    //  씬 조명 보장 (Directional Light 없으면 자동 생성)
    // ─────────────────────────────────────────────
    private void EnsureSceneLighting()
    {
        // 기존 Directional Light 탐색
        foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
            if (l.type == LightType.Directional) return;

        // 없으면 임의 Directional Light 생성
        GameObject lightGO = new GameObject("RoomScan_DirectionalLight");
        Light light = lightGO.AddComponent<Light>();
        light.type      = LightType.Directional;
        light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        light.intensity = 0.9f;
        light.color     = Color.white;

        // Ambient: 그림자 면이 너무 어둡지 않도록 Flat 0.4 설정
        RenderSettings.ambientMode  = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.4f, 0.4f, 0.4f);

        Debug.Log("[RoomBuilder] Directional Light 자동 생성");
    }

    // ─────────────────────────────────────────────
    //  반투명 스캔 머티리얼 생성
    //  URP Lit → Standard → Sprites/Default 순서로 시도
    // ─────────────────────────────────────────────
    private static Material MakeScanMat(Color color)
    {
        // ── URP Lit (투명, 조명 반영) ─────────────────
        Shader sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh != null)
        {
            var m = new Material(sh);
            m.SetColor("_BaseColor", color);

            // Surface = Transparent
            m.SetFloat("_Surface",    1f);
            m.SetFloat("_Blend",      0f); // Alpha blend
            m.SetFloat("_Smoothness", 0.15f); // 낮은 광택
            m.SetFloat("_Metallic",   0f);

            // 블렌딩
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite",   0);

            // 양면 렌더링
            m.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);

            // 키워드
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHATEST_ON");

            // 렌더 타입 / 큐
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            // 섀도우 캐스팅 제거
            m.SetShaderPassEnabled("ShadowCaster", false);

            return m;
        }

        // ── Standard (Built-in RP 폴백) ────────────────
        sh = Shader.Find("Standard");
        if (sh != null)
        {
            var m = new Material(sh);
            m.color = color;
            m.SetFloat("_Mode", 3f); // Transparent
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite",   0);
            m.SetInt("_Cull",     (int)UnityEngine.Rendering.CullMode.Off);
            m.EnableKeyword("_ALPHABLEND_ON");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return m;
        }

        // ── Sprites/Default (최후 폴백) ────────────────
        sh = Shader.Find("Sprites/Default");
        if (sh != null)
        {
            var fallback = new Material(sh);
            fallback.color = color;
            return fallback;
        }

        // ── 절대 폴백: 내장 투명 셰이더 ─────────────────
        // 빌드에 어떤 셰이더도 포함되지 않은 극단적 상황
        Debug.LogWarning("[RoomBuilder] 투명 셰이더를 찾지 못했습니다. " +
                         "Inspector에서 스캔 머티리얼을 직접 할당해주세요.");
        return new Material(Shader.Find("Hidden/InternalErrorShader"));
    }
}

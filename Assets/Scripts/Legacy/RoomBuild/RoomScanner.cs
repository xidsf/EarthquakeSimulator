using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using System;
using System.Collections.Generic;

/// <summary>
/// AR 평면 인식 및 분류, 시각화를 담당하는 클래스
/// </summary>
public class RoomScanner : MonoBehaviour
{
    // ─── 평면별 누적 통계 (프레임 투표 기반 신뢰도 계산) ─────────────────────
    private class PlaneStats
    {
        public int total;        // 총 관찰 프레임 수
        public int ceilingHits;  // 천장으로 분류된 프레임 수
        public int wallHits;     // 벽으로 분류된 프레임 수
        public float ceilYSum;   // 천장 분류 시 Y 합산
        public float ceilAreaSum;// 천장 분류 시 면적 합산

        public void Record(bool isCeiling, bool isWall, float y, float area)
        {
            total++;
            if (isCeiling) { ceilingHits++; ceilYSum += y; ceilAreaSum += area; }
            if (isWall)    wallHits++;
        }

        /// <summary>천장 신뢰도 점수: 안정성² × 평균면적 × 법선정렬도</summary>
        public float CeilingScore(float alignDot, int minFrames = 4)
        {
            if (total < minFrames || ceilingHits == 0) return 0f;
            float stability = (float)ceilingHits / total;
            float avgArea   = ceilAreaSum / ceilingHits;
            return stability * stability * avgArea * alignDot;
        }

        /// <summary>벽 신뢰도: 충분히 관찰 전에는 1로 가정</summary>
        public float WallReliability => total >= 8 ? (float)wallHits / total : 1f;
    }

    [Header("MRTK XR Rig")]
    public GameObject mrtkXRRig;

    [Header("인식 설정")]
    [Tooltip("카메라 Y 대비 이 값(m) 이하를 바닥 후보로 판단")]
    public float floorBelowCameraThreshold = -0.5f;
    public float furnitureMinHeight = 0.25f;
    public float furnitureMaxHeight = 1.30f;
    [Tooltip("벽으로 인식하기 위한 최소 면적 (m²)")]
    public float minWallArea = 0.3f;
    [Tooltip("벽이 바닥-천장 높이의 최소 몇 %를 차지해야 '진짜 벽'으로 인정할지 (0.0~1.0)")]
    public float wallHeightMinRatio = 0.6f;
    [Tooltip("천장이 기울어져 있는지 여부 (꺼져있으면 수평면만 천장으로 인식)")]
    public bool slantedCeilingEnabled = false;

    [Header("천장 인식 필터")]
    [Tooltip("카메라 기준 최소 천장 인식 높이 (m)")]
    public float ceilingMinAboveCamera = 0.3f;
    [Tooltip("바닥 기준 최대 천장 인식 높이 (m)")]
    public float maxCeilingHeightFromFloor = 3.5f;
    [Tooltip("천장 인식 최소 면적 (m²)")]
    public float minCeilingArea = 0.5f;
    [Tooltip("벽 법선의 수직 성분이 이 값 초과이면 수평면 오탐으로 간주하여 제외")]
    public float wallNormalVerticalThreshold = 0.35f;
    [Tooltip("충분한 관찰 후 벽 분류 비율이 이 값 미만이면 불안정 평면으로 제외")]
    public float wallStabilityMinRatio = 0.50f;

    [Header("시각화 머티리얼")]
    public Material floorScanMaterial;
    public Material wallScanMaterial;
    public Material ceilingScanMaterial;

    [Header("시각화 옵션")]
    public bool showFloorVisualization = true;
    public bool showWallVisualization = true;
    public bool showCeilingVisualization = true;

    // AR 매니저
    private ARPlaneManager planeManager;
    private ARAnchorManager anchorManager;
    private ARRaycastManager raycastManager;

    // 내부 상태
    private ARPlane bestFloorPlane;
    private ARPlane prevBestFloorPlane;
    private ARPlane bestCeilingPlane;
    private readonly List<ARPlane> detectedWalls = new List<ARPlane>();
    private readonly Dictionary<TrackableId, GameObject> planeVisuals = new Dictionary<TrackableId, GameObject>();

    // 평면별 누적 통계 (TrackableId → PlaneStats)
    private readonly Dictionary<TrackableId, PlaneStats> _statsMap = new Dictionary<TrackableId, PlaneStats>();

    // 천장 Y 히스토그램용 관찰 버퍼 (y값, 가중치)
    private readonly List<(float y, float weight)> _ceilingObs = new List<(float, float)>();
    private const int CeilingObsMax   = 80;   // 버퍼 최대 크기
    private const float CeilingBinSize = 0.12f; // 히스토그램 버킷 크기 (m)

    private Material _floorScanMat;
    private Material _wallScanMat;
    private Material _ceilingScanMat;

    private float currentFloorY = 0f;
    private bool isFloorDetected = false;
    private float currentCeilingY = 0f;
    private bool isCeilingDetected = false;

    // 이벤트 (RoomBuildManager에서 구독)
    public event Action<ARPlane, float> OnFloorUpdated;
    public event Action<ARPlane, float> OnCeilingUpdated;
    public event Action<List<ARPlane>> OnWallsUpdated;
    public event Action<string> OnStatusChanged;
    /// <summary>평면 변화(추가/갱신) 시 매번 발생 - 미리보기 실시간 갱신용</summary>
    public event Action OnPlaneDataChanged;

    public ARPlane BestFloorPlane => bestFloorPlane;
    public List<ARPlane> DetectedWalls => detectedWalls;
    public float CurrentFloorY => currentFloorY;
    public float CurrentCeilingY => currentCeilingY;
    public bool IsFloorDetected => isFloorDetected;
    public bool IsCeilingDetected => isCeilingDetected;

    void Awake()
    {
        InitializeARManagers();
        PrepareMaterials();
    }

    private void InitializeARManagers()
    {
        GameObject rig = mrtkXRRig != null ? mrtkXRRig : GameObject.Find("MRTK XR Rig");

        if (rig != null)
        {
            planeManager = rig.GetComponentInChildren<ARPlaneManager>(true);
            anchorManager = rig.GetComponentInChildren<ARAnchorManager>(true);
            raycastManager = rig.GetComponentInChildren<ARRaycastManager>(true);
        }

        if (planeManager == null) planeManager = FindAnyObjectByType<ARPlaneManager>();
        if (raycastManager == null) raycastManager = FindAnyObjectByType<ARRaycastManager>();
        if (anchorManager == null) anchorManager = FindAnyObjectByType<ARAnchorManager>();
    }

    private void PrepareMaterials()
    {
        _floorScanMat = floorScanMaterial != null ? floorScanMaterial : MakeScanMat(new Color(0.20f, 0.65f, 1.00f, 0.40f));
        _wallScanMat = wallScanMaterial != null ? wallScanMaterial : MakeScanMat(new Color(0.85f, 0.85f, 0.85f, 0.35f));
        _ceilingScanMat = ceilingScanMaterial != null ? ceilingScanMaterial : MakeScanMat(new Color(1.00f, 0.88f, 0.20f, 0.32f));
    }

    public void StartScanning()
    {
        if (planeManager != null)
        {
            planeManager.enabled = true;
            planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal | PlaneDetectionMode.Vertical;
            planeManager.trackablesChanged.AddListener(OnPlanesChanged);
        }
        OnStatusChanged?.Invoke("방을 둘러보세요...");
    }

    public void StopScanning()
    {
        if (planeManager != null)
        {
            planeManager.trackablesChanged.RemoveListener(OnPlanesChanged);
            planeManager.enabled = false;
        }
    }

    void Update()
    {
        if (planeManager != null && planeManager.enabled)
        {
            ClassifyPlanes();
        }
    }

    private void ClassifyPlanes()
    {
        float cameraY = Camera.main != null ? Camera.main.transform.position.y : 0f;
        float lowestFloorY = float.MaxValue;
        ARPlane newFloor   = null;

        float bestCeilingScore = 0f;
        ARPlane newCeiling = null;

        bool wallsChanged = false;
        
        // 기존: detectedWalls.Clear() - 현재: 누적 관리를 위해 유지하고 개별 업데이트
        // 대신 유효하지 않은 평면(Removed)은 OnPlanesChanged에서 처리됨

        foreach (var plane in planeManager.trackables)
        {
            // 추적 상태가 정지된 것은 제외하되, 단순히 비활성화된 것(시야 밖)은 누적을 위해 포함 가능
            if (plane.trackingState == TrackingState.None) continue;
            
            GetPlaneType(plane, cameraY, out bool isFloor, out bool isCeiling, out bool isWall, out bool isFurniture);
            if (isFurniture) continue;

            float py   = plane.transform.position.y;
            float area = plane.size.x * plane.size.y;

            if (!_statsMap.TryGetValue(plane.trackableId, out PlaneStats stats))
            {
                stats = new PlaneStats();
                _statsMap[plane.trackableId] = stats;
            }
            stats.Record(isCeiling, isWall, py, area);

            if (isFloor && py < lowestFloorY)
            {
                lowestFloorY = py;
                newFloor = plane;
            }
            else if (isCeiling)
            {
                if (py < cameraY + ceilingMinAboveCamera) continue;
                if (isFloorDetected && py > currentFloorY + maxCeilingHeightFromFloor) continue;
                if (area < minCeilingArea) continue;

                float alignDot = Mathf.Abs(plane.normal.y);
                float score = stats.CeilingScore(alignDot);
                if (score > bestCeilingScore)
                {
                    bestCeilingScore = score;
                    newCeiling = plane;
                }
                RecordCeilingObs(py, area * alignDot);
            }
            else if (isWall)
            {
                if (Mathf.Abs(plane.normal.y) > wallNormalVerticalThreshold) continue;
                if (stats.WallReliability < wallStabilityMinRatio) continue;
                if (area < minWallArea) continue;

                // [추가] 바닥-천장을 잇는 '진짜 벽'인지 검증
                if (isFloorDetected)
                {
                    // ARPlane의 center.y와 size.y를 이용해 수직 범위 계산
                    float wallMinY = plane.center.y - plane.size.y * 0.5f;
                    float wallHeight = plane.size.y;

                    // 기준이 될 방 높이 (천장 미감지 시 기본 2.5m 가정)
                    float roomHeight = isCeilingDetected ? (currentCeilingY - currentFloorY) : 2.5f;

                    // 조건 A: 벽의 아래쪽이 바닥 근처(0.3m 이내)에 있는가
                    bool startsNearFloor = (wallMinY - currentFloorY) < 0.3f;
                    
                    // 조건 B: 벽의 높이가 방 전체 높이의 일정 비율 이상인가
                    bool spansEnoughHeight = (wallHeight / roomHeight) >= wallHeightMinRatio;

                    if (!startsNearFloor || !spansEnoughHeight) continue;
                }

                if (!detectedWalls.Contains(plane))
                {
                    detectedWalls.Add(plane);
                    wallsChanged = true;
                }
            }
        }

        if (newFloor != null)
        {
            if (prevBestFloorPlane != null && newFloor.trackableId != prevBestFloorPlane.trackableId)
                RemovePlaneVisual(prevBestFloorPlane.trackableId);
            prevBestFloorPlane = newFloor;
            bestFloorPlane     = newFloor;
            currentFloorY      = newFloor.transform.position.y;
            isFloorDetected    = true;
            OnFloorUpdated?.Invoke(bestFloorPlane, currentFloorY);
        }

        if (newCeiling != null)
        {
            bestCeilingPlane = newCeiling;
            currentCeilingY  = EstimateCeilingY(newCeiling.transform.position.y);
            isCeilingDetected = true;
            OnCeilingUpdated?.Invoke(bestCeilingPlane, currentCeilingY);
        }

        if (wallsChanged) OnWallsUpdated?.Invoke(detectedWalls);

        if (newFloor != null || newCeiling != null || wallsChanged)
            OnPlaneDataChanged?.Invoke();
    }

    // ─── 천장 Y 히스토그램 ───────────────────────────────────────────────────

    private void RecordCeilingObs(float y, float weight)
    {
        _ceilingObs.Add((y, weight));
        if (_ceilingObs.Count > CeilingObsMax)
            _ceilingObs.RemoveAt(0);
    }

    /// <summary>
    /// 버퍼에 누적된 천장 Y 관찰을 히스토그램으로 집계하고,
    /// 가장 가중치가 높은 버킷의 가중 평균 Y를 반환한다.
    /// 데이터가 부족하면 fallback 값을 그대로 반환.
    /// </summary>
    private float EstimateCeilingY(float fallback)
    {
        if (_ceilingObs.Count < 5) return fallback;

        // 버킷 인덱스 → (가중치 합, Y×가중치 합)
        var bins = new Dictionary<int, (float wSum, float ywSum)>();
        foreach (var (y, w) in _ceilingObs)
        {
            int b = Mathf.FloorToInt(y / CeilingBinSize);
            bins.TryGetValue(b, out var cur);
            bins[b] = (cur.wSum + w, cur.ywSum + y * w);
        }

        float bestW = 0f, bestY = fallback;
        foreach (var kv in bins)
        {
            if (kv.Value.wSum > bestW)
            {
                bestW = kv.Value.wSum;
                bestY = kv.Value.ywSum / kv.Value.wSum; // 가중 평균
            }
        }
        return bestY;
    }

    private void GetPlaneType(ARPlane plane, float cameraY, out bool isFloor, out bool isCeiling, out bool isWall, out bool isFurniture)
    {
        PlaneClassifications cls = plane.classifications;
        PlaneAlignment ali = plane.alignment;
        float py = plane.transform.position.y;

        bool hasNoCls = cls == PlaneClassifications.None;
        bool hasFloorCls = (cls & PlaneClassifications.Floor) != 0;
        bool hasCeilingCls = (cls & PlaneClassifications.Ceiling) != 0;
        bool hasWallCls = (cls & PlaneClassifications.WallFace) != 0;
        bool hasFurnCls = (cls & PlaneClassifications.Table) != 0 || (cls & PlaneClassifications.Seat) != 0;

        bool isSlantedCeiling = hasNoCls && py > cameraY + 0.3f && plane.normal.y < -0.2f;

        isFloor = hasFloorCls || (hasNoCls && ali == PlaneAlignment.HorizontalUp && py < cameraY + floorBelowCameraThreshold);
        
        // slantedCeilingEnabled가 꺼져있으면 수평면(ali == HorizontalDown)만 천장으로 인정
        if (slantedCeilingEnabled)
            isCeiling = hasCeilingCls || (hasNoCls && ali == PlaneAlignment.HorizontalDown) || isSlantedCeiling;
        else
            isCeiling = hasCeilingCls || (hasNoCls && ali == PlaneAlignment.HorizontalDown);

        isWall = hasWallCls || (hasNoCls && (ali == PlaneAlignment.Vertical || ali == PlaneAlignment.NotAxisAligned));

        isFurniture = hasFurnCls || (!isFloor && !isCeiling && !isWall && ali == PlaneAlignment.HorizontalUp && isFloorDetected && py > currentFloorY + furnitureMinHeight && py < currentFloorY + furnitureMaxHeight);
    }

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
        foreach (var kv in args.removed)
        {
            RemovePlaneVisual(kv.Key);
            _statsMap.Remove(kv.Key); // 제거된 평면 통계 정리

            // detectedWalls에서도 제거 (MissingReferenceException 방지)
            detectedWalls.RemoveAll(w => w == null || w.trackableId == kv.Key);
        }
    }

    private void DisableDefaultPlaneMesh(ARPlane plane)
    {
        var vis = plane.GetComponent<ARPlaneMeshVisualizer>();
        if (vis != null) vis.enabled = false;
        var mr = plane.GetComponent<MeshRenderer>();
        if (mr != null) mr.enabled = false;
    }

    private void UpdatePlaneVisual(ARPlane plane, float cameraY)
    {
        GetPlaneType(plane, cameraY, out bool isFloor, out bool isCeiling, out bool isWall, out bool isFurniture);

        if (isFurniture) { RemovePlaneVisual(plane.trackableId); return; }
        if (!isFloor && !isCeiling && !isWall) return;

        if (isFloor && bestFloorPlane != null && plane.trackableId != bestFloorPlane.trackableId)
        {
            RemovePlaneVisual(plane.trackableId);
            return;
        }

        bool showThis = (isFloor && showFloorVisualization) || (isCeiling && showCeilingVisualization) || (isWall && showWallVisualization);

        if (!showThis)
        {
            RemovePlaneVisual(plane.trackableId);
            return;
        }

        Material mat = isFloor ? _floorScanMat : isCeiling ? _ceilingScanMat : _wallScanMat;

        if (!planeVisuals.TryGetValue(plane.trackableId, out GameObject visGO))
        {
            visGO = new GameObject($"PlaneVis_{plane.trackableId}");
            visGO.AddComponent<MeshFilter>();
            var mr = visGO.AddComponent<MeshRenderer>();
            mr.material = mat;
            visGO.layer = 2; // Ignore Raycast
            planeVisuals[plane.trackableId] = visGO;
        }
        else
        {
            var mr = visGO.GetComponent<MeshRenderer>();
            if (mr != null) mr.material = mat;
        }

        var mfComp = visGO.GetComponent<MeshFilter>();
        Mesh mesh = BuildFlatPlaneMesh(plane, isFloor, isCeiling);
        if (mesh != null) mfComp.mesh = mesh;
    }

    private void RemovePlaneVisual(TrackableId id)
    {
        if (planeVisuals.TryGetValue(id, out GameObject go))
        {
            if (go != null) Destroy(go);
            planeVisuals.Remove(id);
        }
    }

    private Mesh BuildFlatPlaneMesh(ARPlane plane, bool isFloor, bool isCeiling)
    {
        var boundary = plane.boundary;
        int n = boundary.Length;
        if (n < 3) return null;

        Vector3 center = plane.transform.position;
        if (isFloor && isFloorDetected) center.y = currentFloorY;
        if (isCeiling && isCeilingDetected) center.y = currentCeilingY;

        if (planeVisuals.TryGetValue(plane.trackableId, out GameObject visGO))
            visGO.transform.position = center;

        Vector3 frontNormal, backNormal;
        if (isFloor) { frontNormal = Vector3.up; backNormal = Vector3.down; }
        else if (isCeiling) { frontNormal = Vector3.down; backNormal = Vector3.up; }
        else { frontNormal = plane.transform.up; backNormal = -frontNormal; }

        Vector3[] verts = new Vector3[n * 2];
        Vector3[] normals = new Vector3[n * 2];

        for (int i = 0; i < n; i++)
        {
            Vector3 worldPt = plane.transform.TransformPoint(new Vector3(boundary[i].x, 0f, boundary[i].y));
            if (isFloor && isFloorDetected) worldPt.y = currentFloorY;
            else if (isCeiling && isCeilingDetected) worldPt.y = currentCeilingY;

            Vector3 localPt = worldPt - center;
            verts[i] = localPt;
            verts[i + n] = localPt;
            normals[i] = frontNormal;
            normals[i + n] = backNormal;
        }

        List<int> tris = new List<int>((n - 2) * 6);
        for (int i = 1; i < n - 1; i++)
        {
            tris.Add(0); tris.Add(i); tris.Add(i + 1);
            tris.Add(n); tris.Add(n + i + 1); tris.Add(n + i);
        }

        Mesh mesh = new Mesh { name = "FlatPlaneMesh" };
        mesh.vertices = verts;
        mesh.normals = normals;
        mesh.triangles = tris.ToArray();
        mesh.RecalculateBounds();
        return mesh;
    }

    public void ClearScanVisuals()
    {
        foreach (var kv in new List<KeyValuePair<TrackableId, GameObject>>(planeVisuals))
        {
            if (kv.Value != null) Destroy(kv.Value);
        }
        planeVisuals.Clear();
        _statsMap.Clear();
        _ceilingObs.Clear();
    }

    /// <summary>
    /// 현재 showFloor/Wall/CeilingVisualization 값에 맞게 모든 평면 시각화를 즉시 갱신.
    /// Inspector에서 토글 변경 시 RoomBuildManager가 호출.
    /// </summary>
    public void RefreshAllVisuals()
    {
        if (planeManager == null) return;
        float cameraY = Camera.main != null ? Camera.main.transform.position.y : 0f;
        foreach (var plane in planeManager.trackables)
        {
            if (plane.gameObject.activeSelf)
                UpdatePlaneVisual(plane, cameraY);
            else
                RemovePlaneVisual(plane.trackableId);
        }
    }

    private static Material MakeScanMat(Color color)
    {
        Shader sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh != null)
        {
            var m = new Material(sh);
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_Smoothness", 0.15f);
            m.SetFloat("_Metallic", 0f);
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHATEST_ON");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            m.SetShaderPassEnabled("ShadowCaster", false);
            return m;
        }
        sh = Shader.Find("Standard");
        if (sh != null)
        {
            var m = new Material(sh);
            m.color = color;
            m.SetFloat("_Mode", 3f);
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            m.EnableKeyword("_ALPHABLEND_ON");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return m;
        }
        return new Material(Shader.Find("Sprites/Default")) { color = color };
    }
}

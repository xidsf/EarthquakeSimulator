using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Collections.Generic;
using UnityEngine.XR.ARFoundation;

/// <summary>
/// 방 구축 전체 프로세스를 총괄하는 매니저 클래스
/// RoomScanner와 연동하여 상태 제어 및 지오메트리 생성을 담당
/// </summary>
public class RoomBuildManager : MonoBehaviour
{
    [Header("Components")]
    [SerializeField] private RoomScanner roomScanner;
    [SerializeField] private RoomBuildUIManager uiManager;

    [Header("Room Objects")]
    public GameObject roomFrameObject;
    public float wallThickness = 0.1f;
    public Material floorFinalMaterial;
    public GameObject wallPrefab;

    [Header("Debug UI")]
    public TextMeshProUGUI debugStatusText;
    public TextMeshProUGUI debugPlanesText;
    public Button freezeButton;

    [Header("Preview Materials (미설정 시 자동 생성)")]
    public Material previewFloorMaterial;
    public Material previewWallMaterial;
    public Material previewCeilingMaterial;

    [Header("디버그 시각화")]
    public bool showRoomPreview = true;

    // 상태 관리
    public RoomScanState ScanState { get; private set; } = RoomScanState.Idle;

    private List<Vector3> cornerPoints = new List<Vector3>();
    private bool isRoomFinished = false;
    private float ceilingHeight = 2.5f;

    // 미리보기 서브 오브젝트
    private GameObject _previewRoot;
    private GameObject _previewFloorObj;
    private GameObject _previewWallsObj;
    private GameObject _previewCeilingObj;

    private Material _cachedFloorMat;
    private Material _cachedWallMat;
    private Material _cachedCeilingMat;

    private float _previewTimer = 0f;
    private const float PreviewInterval = 0.15f;

    void Awake()
    {
        if (roomScanner == null) roomScanner = GetComponent<RoomScanner>();

        if (roomScanner != null)
        {
            roomScanner.OnFloorUpdated   += (plane, height) => RefreshDebugUI();
            roomScanner.OnCeilingUpdated += (plane, height) => RefreshDebugUI();
            roomScanner.OnWallsUpdated   += (walls)         => RefreshDebugUI();
            roomScanner.OnStatusChanged  += UpdateDebugStatus;
            roomScanner.OnPlaneDataChanged += () => { _previewTimer = PreviewInterval; };
        }
    }

    void Start()
    {
        if (roomFrameObject == null)
        {
            roomFrameObject = new GameObject("RoomFrame");
            roomFrameObject.transform.position = Vector3.zero;
        }

        InitPreviewMaterials();
        InitPreviewObjects();
        StartAutoScan();
    }

    void Update()
    {
        if (ScanState != RoomScanState.AutoScanning) return;

        if (showRoomPreview)
        {
            _previewTimer += Time.deltaTime;
            if (_previewTimer >= PreviewInterval)
            {
                _previewTimer = 0f;
                UpdateRoomPreview();
            }
        }
    }

    private void InitPreviewMaterials()
    {
        _cachedFloorMat = previewFloorMaterial != null ? previewFloorMaterial : MakePreviewMat(new Color(0.20f, 0.65f, 1.00f, 0.35f));
        _cachedWallMat = previewWallMaterial != null ? previewWallMaterial : MakePreviewMat(new Color(0.85f, 0.85f, 0.85f, 0.30f));
        _cachedCeilingMat = previewCeilingMaterial != null ? previewCeilingMaterial : MakePreviewMat(new Color(1.00f, 0.88f, 0.20f, 0.28f));
    }

    private void InitPreviewObjects()
    {
        _previewRoot = new GameObject("PreviewRoom");
        _previewRoot.transform.SetParent(transform);

        _previewFloorObj   = CreatePreviewChild("PreviewFloor",   _cachedFloorMat);
        _previewWallsObj   = CreatePreviewChild("PreviewWalls",   _cachedWallMat);
        _previewCeilingObj = CreatePreviewChild("PreviewCeiling", _cachedCeilingMat);

        _previewFloorObj.transform.SetParent(_previewRoot.transform);
        _previewWallsObj.transform.SetParent(_previewRoot.transform);
        _previewCeilingObj.transform.SetParent(_previewRoot.transform);

        SetPreviewActive(false, false, false);
    }

    private static GameObject CreatePreviewChild(string name, Material mat)
    {
        var go = new GameObject(name);
        go.AddComponent<MeshFilter>();
        go.AddComponent<MeshRenderer>().material = mat;
        go.layer = 2;
        return go;
    }

    public void StartAutoScan()
    {
        if (isRoomFinished) return;
        ScanState = RoomScanState.AutoScanning;
        if (roomScanner != null) roomScanner.StartScanning();
    }

    public void FreezeRoom()
    {
        if (ScanState == RoomScanState.Frozen) return;
        if (roomScanner == null || !roomScanner.IsFloorDetected)
        {
            UpdateDebugStatus("[오류] 바닥이 감지되지 않았습니다");
            return;
        }

        ScanState = RoomScanState.Frozen;
        roomScanner.StopScanning();
        SetPreviewActive(false, false, false);

        ceilingHeight = roomScanner.CurrentCeilingY - roomScanner.CurrentFloorY;
        if (ceilingHeight < 1.0f) ceilingHeight = 2.5f;

        cornerPoints.Clear();
        cornerPoints.AddRange(BuildBestFloorPolygon(roomScanner.CurrentFloorY));

        if (cornerPoints.Count >= 3)
        {
            BuildRoomGeometry();
            UpdateDebugStatus($"완료 코너 {cornerPoints.Count}개 | 높이 {ceilingHeight:F2}m");
        }
        else
        {
            UpdateDebugStatus("[경고] 인식 불충분");
            ScanState = RoomScanState.AutoScanning;
            roomScanner.StartScanning();
        }
    }

    private void UpdateRoomPreview()
    {
        if (roomScanner == null || !roomScanner.IsFloorDetected)
        {
            SetPreviewActive(false, false, false);
            return;
        }

        List<Vector3> previewCorners = BuildBestFloorPolygon(roomScanner.CurrentFloorY);
        if (previewCorners.Count < 3)
        {
            SetPreviewActive(false, false, false);
            return;
        }

        float floorY = roomScanner.CurrentFloorY;
        float h = roomScanner.CurrentCeilingY - floorY;
        if (h < 1.0f) h = 2.5f;

        UpdateFlatMesh(_previewFloorObj, previewCorners, floorY, true);
        _previewFloorObj.SetActive(true);

        UpdateWallsMesh(_previewWallsObj, previewCorners, floorY, h);
        _previewWallsObj.SetActive(true);

        if (roomScanner.IsCeilingDetected)
        {
            UpdateFlatMesh(_previewCeilingObj, previewCorners, roomScanner.CurrentCeilingY, false);
            _previewCeilingObj.SetActive(true);
        }
    }

    private List<Vector3> BuildBestFloorPolygon(float floorY)
    {
        var walls = roomScanner.DetectedWalls;
        if (walls.Count >= 3)
        {
            List<ARPlane> merged = MergeCoplanarWalls(walls);
            var tagged = ComputeTaggedCornersFromWalls(merged, floorY);
            if (tagged.Count >= 3)
            {
                List<Vector3> ordered = OrderCornersByWallWalk(tagged);
                if (ordered.Count >= 3) return ordered;
            }
        }

        var floorPts = new List<Vector3>();
        ExtractCornerPoints(roomScanner.BestFloorPlane, floorPts);
        return floorPts.Count >= 3 ? floorPts : BuildFallbackAABB(walls, floorY);
    }

    private void ExtractCornerPoints(ARPlane floor, List<Vector3> outPoints)
    {
        if (floor == null) return;
        foreach (Vector2 b in floor.boundary)
        {
            Vector3 worldPt = floor.transform.TransformPoint(new Vector3(b.x, 0f, b.y));
            worldPt.y = roomScanner.CurrentFloorY;
            outPoints.Add(worldPt);
        }
    }

    private List<ARPlane> MergeCoplanarWalls(List<ARPlane> walls)
    {
        var groups = new List<(ARPlane rep, Vector3 norm)>();
        foreach (var w in walls)
        {
            if (w == null) continue;
            Vector3 n = w.transform.up; n.y = 0f; n.Normalize();
            bool added = false;
            foreach (var g in groups)
            {
                if (Mathf.Abs(Vector3.Dot(n, g.norm)) > 0.98f)
                {
                    float d = Mathf.Abs(Vector3.Dot(w.transform.position - g.rep.transform.position, g.norm));
                    if (d < 0.4f) { added = true; break; }
                }
            }
            if (!added) groups.Add((w, n));
        }
        var res = new List<ARPlane>();
        foreach (var g in groups) res.Add(g.rep);
        return res;
    }

    private List<(Vector3 pos, int wA, int wB)> ComputeTaggedCornersFromWalls(List<ARPlane> walls, float floorY)
    {
        var res = new List<(Vector3 pos, int wA, int wB)>();
        for (int i = 0; i < walls.Count; i++)
        {
            for (int j = i + 1; j < walls.Count; j++)
            {
                if (TryIntersectXZ(walls[i], walls[j], floorY, out Vector3 pt))
                {
                    if (IsNear(pt, walls[i], 4f) && IsNear(pt, walls[j], 4f))
                        res.Add((pt, i, j));
                }
            }
        }
        return res;
    }

    private bool IsNear(Vector3 pt, ARPlane w, float margin)
    {
        float proj = Vector3.Dot(pt - w.transform.position, w.transform.right);
        return Mathf.Abs(proj) <= (w.size.x * 0.5f + margin);
    }

    private bool TryIntersectXZ(ARPlane a, ARPlane b, float y, out Vector3 pt)
    {
        pt = new Vector3(0, y, 0);
        Vector3 nA = a.transform.up; nA.y = 0; nA.Normalize();
        Vector3 nB = b.transform.up; nB.y = 0; nB.Normalize();
        float det = nA.x * nB.z - nB.x * nA.z;
        if (Mathf.Abs(det) < 0.001f) return false;
        float cA = Vector3.Dot(nA, a.transform.position);
        float cB = Vector3.Dot(nB, b.transform.position);
        pt.x = (cA * nB.z - cB * nA.z) / det;
        pt.z = (nA.x * cB - nB.x * cA) / det;
        return true;
    }

    private List<Vector3> OrderCornersByWallWalk(List<(Vector3 pos, int wA, int wB)> corners)
    {
        if (corners.Count < 3) return new List<Vector3>();
        List<Vector3> best = new List<Vector3>();
        for (int i = 0; i < Mathf.Min(corners.Count, 3); i++)
        {
            var p = Walk(corners, i, corners[i].wA);
            if (p.Count > best.Count) best = p;
        }
        return best;
    }

    private List<Vector3> Walk(List<(Vector3 pos, int wA, int wB)> corners, int start, int wall)
    {
        int n = corners.Count;
        var visited = new bool[n];
        var res = new List<Vector3>();
        int cur = start; int curW = wall;
        for (int i = 0; i < n; i++)
        {
            visited[cur] = true; res.Add(corners[cur].pos);
            int nextW = (corners[cur].wA == curW) ? corners[cur].wB : corners[cur].wA;
            int next = -1; float minD = float.MaxValue;
            for (int j = 0; j < n; j++)
            {
                if (visited[j]) continue;
                if (corners[j].wA == nextW || corners[j].wB == nextW)
                {
                    float d = Vector3.Distance(corners[cur].pos, corners[j].pos);
                    if (d < minD) { minD = d; next = j; }
                }
            }
            if (next == -1) break;
            cur = next; curW = nextW;
        }
        return res;
    }

    private List<Vector3> BuildFallbackAABB(List<ARPlane> walls, float floorY)
    {
        if (walls.Count == 0) return new List<Vector3>();
        float minX = 100, maxX = -100, minZ = 100, maxZ = -100;
        foreach (var w in walls)
        {
            minX = Mathf.Min(minX, w.transform.position.x - 1); maxX = Mathf.Max(maxX, w.transform.position.x + 1);
            minZ = Mathf.Min(minZ, w.transform.position.z - 1); maxZ = Mathf.Max(maxZ, w.transform.position.z + 1);
        }
        return new List<Vector3> { new Vector3(minX, floorY, minZ), new Vector3(maxX, floorY, minZ), new Vector3(maxX, floorY, maxZ), new Vector3(minX, floorY, maxZ) };
    }

    private void BuildRoomGeometry()
    {
        if (isRoomFinished) return; isRoomFinished = true;
        for (int i = 0; i < cornerPoints.Count; i++)
            CreateWallSegment(cornerPoints[i], cornerPoints[(i + 1) % cornerPoints.Count], i);
        CreateFinalFloorMesh();
    }

    private void CreateWallSegment(Vector3 s, Vector3 e, int idx)
    {
        Vector3 c = (s + e) * 0.5f; c.y = roomScanner.CurrentFloorY + ceilingHeight * 0.5f;
        Quaternion r = Quaternion.LookRotation((e - s).normalized);
        GameObject w = wallPrefab ? Instantiate(wallPrefab, c, r, roomFrameObject.transform) : GameObject.CreatePrimitive(PrimitiveType.Cube);
        w.transform.SetParent(roomFrameObject.transform); w.transform.SetPositionAndRotation(c, r);
        w.transform.localScale = new Vector3(wallThickness, ceilingHeight, Vector3.Distance(s, e));
    }

    private void CreateFinalFloorMesh()
    {
        GameObject f = new GameObject("FinalFloor"); f.transform.SetParent(roomFrameObject.transform);
        f.transform.position = ComputeCenter(cornerPoints);
        f.AddComponent<MeshFilter>().mesh = BuildMesh(cornerPoints, roomScanner.CurrentFloorY, true);
        f.AddComponent<MeshRenderer>().material = floorFinalMaterial;
    }

    private Mesh BuildMesh(List<Vector3> pts, float y, bool up)
    {
        Mesh m = new Mesh(); int n = pts.Count; Vector3[] v = new Vector3[n];
        for (int i = 0; i < n; i++) v[i] = pts[i] - ComputeCenter(pts);
        m.vertices = v; m.triangles = EarClipTriangulate(pts).ToArray();
        m.RecalculateNormals(); return m;
    }

    private void UpdateFlatMesh(GameObject t, List<Vector3> c, float y, bool up)
    {
        t.GetComponent<MeshFilter>().sharedMesh = BuildMesh(c, y, up);
        t.transform.position = ComputeCenter(c); t.transform.position = new Vector3(t.transform.position.x, y, t.transform.position.z);
    }

    private void UpdateWallsMesh(GameObject t, List<Vector3> corners, float floorY, float h)
    {
        int n = corners.Count; Vector3[] v = new Vector3[n * 4]; int[] tris = new int[n * 6];
        Vector3 center = ComputeCenter(corners);
        for (int i = 0; i < n; i++)
        {
            int next = (i + 1) % n;
            v[i * 4] = corners[i] - center; v[i * 4 + 1] = corners[next] - center;
            v[i * 4 + 2] = v[i * 4 + 1] + Vector3.up * h; v[i * 4 + 3] = v[i * 4] + Vector3.up * h;
            int ti = i * 6; tris[ti] = i * 4; tris[ti + 1] = i * 4 + 2; tris[ti + 2] = i * 4 + 1;
            tris[ti + 3] = i * 4; tris[ti + 4] = i * 4 + 3; tris[ti + 5] = i * 4 + 2;
        }
        Mesh m = new Mesh(); m.vertices = v; m.triangles = tris; m.RecalculateNormals();
        t.GetComponent<MeshFilter>().sharedMesh = m; t.transform.position = center;
    }

    private static Vector3 ComputeCenter(List<Vector3> p) { Vector3 c = Vector3.zero; foreach (var x in p) c += x; return c / p.Count; }
    private void SetPreviewActive(bool f, bool w, bool c) { if(_previewFloorObj) _previewFloorObj.SetActive(f); if(_previewWallsObj) _previewWallsObj.SetActive(w); if(_previewCeilingObj) _previewCeilingObj.SetActive(c); }

    private List<int> EarClipTriangulate(List<Vector3> poly)
    {
        var tris = new List<int>(); int n = poly.Count; if (n < 3) return tris;
        var idx = new List<int>(); for (int i = 0; i < n; i++) idx.Add(i);
        int cur = 0; while (idx.Count > 2)
        {
            int p = idx[(cur + idx.Count - 1) % idx.Count], c = idx[cur], next = idx[(cur + 1) % idx.Count];
            Vector2 A = new Vector2(poly[p].x, poly[p].z), B = new Vector2(poly[c].x, poly[c].z), C = new Vector2(poly[next].x, poly[next].z);
            if ((B.x - A.x) * (C.y - A.y) - (B.y - A.y) * (C.x - A.x) > 0)
            {
                tris.Add(idx[(cur + idx.Count - 1) % idx.Count]); tris.Add(idx[cur]); tris.Add(idx[(cur + 1) % idx.Count]); idx.RemoveAt(cur); cur = 0;
            }
            else if (++cur >= idx.Count) break;
        }
        return tris;
    }

    private void UpdateDebugStatus(string m) { if (debugStatusText) debugStatusText.text = m; }
    private void RefreshDebugUI() { if (debugPlanesText && roomScanner != null) debugPlanesText.text = $"바닥: {(roomScanner.IsFloorDetected ? "OK" : "NO")}\n벽: {roomScanner.DetectedWalls.Count}개"; }
    private static Material MakePreviewMat(Color c) { var m = new Material(Shader.Find("Universal Render Pipeline/Lit")); m.color = c; return m; }
}

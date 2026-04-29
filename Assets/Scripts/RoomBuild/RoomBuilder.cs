using UnityEngine;
using UnityEngine.XR.ARFoundation;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.UI;
using TMPro;

[RequireComponent(typeof(LineRenderer))]
public class RoomBuilder : MonoBehaviour
{
    [Header("매니저 및 설정")]
    public ARPlaneManager planeManager;
    public ARAnchorManager anchorManager;
    public ARRaycastManager raycastManager;
    public GameObject wallPrefab;
    public GameObject roomContainer;
    public Material floorMaterial;
    public float ceilingHeight = 2.5f;
    public float wallThickness = 0.1f;

    [Header("시각적 피드백 & 프리뷰")]
    public GameObject cornerMarkerPrefab;
    public Material previewMeshMaterial;
    private LineRenderer lineRenderer;
    private GameObject previewMeshObj;
    private MeshFilter previewMeshFilter;

    [Header("UI 요소")]
    public RoomBuildUIManager uiManager;
    public Slider ceilingHeightSlider;
    public TextMeshProUGUI ceilingHeightText;
    public Toggle SnappingToggle;
    public Toggle MagneticSnapToggle;

    [Header("자동 보정 설정")]
    public bool enableSnapping = true;
    public float snapAngleThreshold = 15f;
    public bool enableMagneticSnap = false;
    public float magneticSnapRadius = 0.2f;

    [Header("자동 완성 설정")]
    [Tooltip("AutoComplete에서 사각형 대신 L자형 등을 만들 때 사용할 형태")]
    public RoomShape autoCompleteShape = RoomShape.Rectangle;

    public enum RoomShape
    {
        Rectangle,
        LShape,
        TShape,
        UShape,
        Custom
    }

    // AR Raycast 결과 버퍼
    private static readonly List<ARRaycastHit> arHits = new List<ARRaycastHit>();

    [Header("방 구조 오브젝트")]
    public GameObject roomFrameObject;
    public GameObject furnitureObject;

    // HoloLens 2: 시선 커서 (선택적 할당)
    [Header("HoloLens 2 Gaze 커서 (선택)")]
    public Transform gazeCursor;

    // 상태 관리
    private List<Vector3> cornerPoints = new List<Vector3>();
    private List<GameObject> cornerMarkers = new List<GameObject>();
    private bool isRoomFinished = false;
    private float currentFloorY = 0f;
    private bool isFloorDetected = false;
    private float currentCeilingY = 0f;
    private bool isCeilingDetected = false;
    private bool isManualMode = false;

    public bool IsScanning { get { return !isRoomFinished; } }
    public float currentFloorHeight { get { return currentFloorY; } }

    private void OnCeilingHeightChanged(float value)
    {
        ceilingHeight = value * 0.1f;
        UpdateCeilingHeightText(value * 0.1f);
        if (isRoomFinished) UpdateWallHeights();
    }

    private void UpdateWallHeights()
    {
        if (roomFrameObject == null) return;
        for (int i = 0; i < roomFrameObject.transform.childCount; i++)
        {
            Transform child = roomFrameObject.transform.GetChild(i);
            if (child.name.StartsWith("Wall_"))
            {
                Vector3 newScale = child.localScale;
                newScale.y = ceilingHeight;
                child.localScale = newScale;

                Vector3 newPos = child.position;
                newPos.y = currentFloorY + (ceilingHeight / 2f);
                child.position = newPos;
            }
        }
    }

    private void UpdateCeilingHeightText(float value)
    {
        if (ceilingHeightText != null)
            ceilingHeightText.text = $"{value:F2}m";
    }

    void Awake()
    {
        lineRenderer = GetComponent<LineRenderer>();
        lineRenderer.startWidth = 0.05f;
        lineRenderer.endWidth = 0.05f;
        lineRenderer.positionCount = 0;
        lineRenderer.loop = false;

        // MRTK XR Rig 교체 후 Inspector 참조가 끊어진 경우 씬 전체에서 자동 탐색
        if (planeManager == null)
            planeManager = FindAnyObjectByType<ARPlaneManager>();
        if (raycastManager == null)
            raycastManager = FindAnyObjectByType<ARRaycastManager>();
        if (anchorManager == null)
            anchorManager = FindAnyObjectByType<ARAnchorManager>();

        if (roomContainer == null)
        {
            roomContainer = new GameObject("RoomContainer");
            roomContainer.transform.position = Vector3.zero;
        }

        previewMeshObj = new GameObject("PreviewMesh");
        previewMeshFilter = previewMeshObj.AddComponent<MeshFilter>();
        MeshRenderer mr = previewMeshObj.AddComponent<MeshRenderer>();
        if (previewMeshMaterial != null) mr.material = previewMeshMaterial;

        if (ceilingHeightSlider != null)
        {
            ceilingHeightSlider.value = ceilingHeight * 0.1f;
            UpdateCeilingHeightText(ceilingHeight);
            ceilingHeightSlider.onValueChanged.AddListener(OnCeilingHeightChanged);
        }

        if (SnappingToggle != null) SnappingToggle.onValueChanged.AddListener((v) => { enableSnapping = v; });
        if (MagneticSnapToggle != null) MagneticSnapToggle.onValueChanged.AddListener((v) => { enableMagneticSnap = v; });
    }

    void Update()
    {
        if (isRoomFinished) return;

        UpdateFloorDetection();

        if (isFloorDetected && cornerPoints.Count >= 1)
        {
            // HoloLens 2: 시선 기반 히트 위치 계산
            Vector3 gazeHit = GetGazeHitPosition(out bool success);
            if (success)
            {
                gazeHit = ApplySnappingLogic(gazeHit);
                UpdateRealtimePreview(gazeHit);

                // 시선 커서 위치 갱신 (할당된 경우)
                if (gazeCursor != null)
                    gazeCursor.position = gazeHit + Vector3.up * 0.01f;
            }
        }
    }

    // ─────────────────────────────────────────────
    //  바닥/천장 감지
    //  HoloLens 2: ARRaycastManager.Raycast(Ray, ...) 오버로드 사용
    // ─────────────────────────────────────────────
    private void UpdateFloorDetection()
    {
        float planeFloorY = float.MaxValue;
        float ceilingY = float.MinValue;
        bool foundFloor = false, foundCeiling = false;

        if (planeManager != null)
        {
            foreach (var plane in planeManager.trackables)
            {
                if (plane.alignment == UnityEngine.XR.ARSubsystems.PlaneAlignment.HorizontalUp)
                {
                    if (plane.transform.position.y < planeFloorY) { planeFloorY = plane.transform.position.y; foundFloor = true; }
                }
                else if (plane.alignment == UnityEngine.XR.ARSubsystems.PlaneAlignment.HorizontalDown)
                {
                    if (plane.transform.position.y > ceilingY) { ceilingY = plane.transform.position.y; foundCeiling = true; }
                }
            }
        }

        if (foundCeiling) { currentCeilingY = ceilingY; isCeilingDetected = true; }

        if (Camera.main != null && raycastManager != null)
        {
            // HoloLens 2: 시선 방향 Ray 사용 (스크린 픽셀 좌표 미사용)
            Ray gazeRay = GetGazeRay();
            if (raycastManager.Raycast(gazeRay, arHits,
                    UnityEngine.XR.ARSubsystems.TrackableType.PlaneWithinPolygon |
                    UnityEngine.XR.ARSubsystems.TrackableType.PlaneWithinInfinity))
            {
                float detectedY = arHits[0].pose.position.y;
                bool isHighSurface = foundFloor && detectedY > planeFloorY + 0.15f;
                if (!isHighSurface)
                {
                    currentFloorY = foundFloor ? Mathf.Min(detectedY, planeFloorY) : detectedY;
                    isFloorDetected = true;
                    return;
                }
            }
        }

        if (foundFloor)
        {
            currentFloorY = planeFloorY;
            isFloorDetected = true;
        }
    }

    // HoloLens 2 시선(Gaze) Ray: Camera.main.transform.forward 기반
    private Ray GetGazeRay()
    {
        if (Camera.main != null)
            return new Ray(Camera.main.transform.position, Camera.main.transform.forward);
        return new Ray(Vector3.zero, Vector3.forward);
    }

    // 시선 Ray로 바닥 히트 위치 계산 (기존 GetCrosshairPosition 대체)
    private Vector3 GetGazeHitPosition(out bool success)
    {
        success = false;
        if (!isFloorDetected) return Vector3.zero;

        Ray ray = GetGazeRay();

        if (ray.direction.y < 0)
        {
            // HoloLens 2: Ray 오버로드로 ARRaycast
            if (raycastManager != null &&
                raycastManager.Raycast(ray, arHits, UnityEngine.XR.ARSubsystems.TrackableType.PlaneWithinPolygon))
            {
                Vector3 hitPos = arHits[0].pose.position;
                if (!isFloorDetected || hitPos.y <= currentFloorY + 0.15f)
                {
                    currentFloorY = hitPos.y;
                    success = true;
                    return hitPos;
                }
            }

            // AR 감지 실패 시 수학적 바닥 평면과 교차
            Plane mathFloor = new Plane(Vector3.up, new Vector3(0, currentFloorY, 0));
            if (mathFloor.Raycast(ray, out float enterDist))
            {
                success = true;
                return ray.GetPoint(enterDist);
            }
        }
        else
        {
            // 시선이 위를 향할 때: 천장 평면 투영 후 바닥 Y로 내림
            float targetCeilingY = isCeilingDetected ? currentCeilingY : (currentFloorY + ceilingHeight);
            Plane mathCeiling = new Plane(Vector3.down, new Vector3(0, targetCeilingY, 0));

            if (mathCeiling.Raycast(ray, out float enterDist))
            {
                Vector3 ceilingHit = ray.GetPoint(enterDist);
                success = true;
                return new Vector3(ceilingHit.x, currentFloorY, ceilingHit.z);
            }
        }

        return Vector3.zero;
    }

    // ─────────────────────────────────────────────
    //  실시간 프리뷰
    // ─────────────────────────────────────────────
    private void UpdateRealtimePreview(Vector3 currentAimPos)
    {
        lineRenderer.positionCount = cornerPoints.Count + 1;
        lineRenderer.loop = false;
        for (int i = 0; i < cornerPoints.Count; i++)
            lineRenderer.SetPosition(i, cornerPoints[i]);
        lineRenderer.SetPosition(cornerPoints.Count, currentAimPos);

        if (cornerPoints.Count >= 2)
        {
            if (previewMeshObj != null) previewMeshObj.SetActive(true);
            List<Vector3> previewVerts = new List<Vector3>(cornerPoints);
            previewVerts.Add(currentAimPos);
            UpdatePreviewMesh(previewVerts);
        }
        else
        {
            if (previewMeshObj != null) previewMeshObj.SetActive(false);
        }
    }

    // ─────────────────────────────────────────────
    //  점 추가 / 실행취소
    //  HoloLens 2: UI 버튼 → MRTK 에어탭으로 호출 (추가 처리 불필요)
    // ─────────────────────────────────────────────
    public void AddCornerPoint()
    {
        if (isRoomFinished || !isFloorDetected)
        {
            uiManager.ShowAlert("포인트 추가 실패");
            return;
        }
        StartCoroutine(AddCornerPointRoutine());
    }

    private IEnumerator AddCornerPointRoutine()
    {
        if (!isManualMode)
        {
            isManualMode = true;
            lineRenderer.positionCount = 0;
            if (previewMeshObj != null) previewMeshObj.SetActive(false);
            yield return null;
        }

        Vector3 hitPos = GetGazeHitPosition(out bool hitSuccess);

        if (hitSuccess)
        {
            hitPos = ApplySnappingLogic(hitPos);
            cornerPoints.Add(hitPos);

            if (cornerMarkerPrefab != null)
            {
                GameObject marker = Instantiate(cornerMarkerPrefab, hitPos, Quaternion.identity);
                marker.transform.SetParent(roomFrameObject.transform, true);
                cornerMarkers.Add(marker);
            }

            uiManager.ShowAlert("포인트 추가 완료");
        }
        else
        {
            uiManager.ShowAlert("포인트 추가 실패");
        }
    }

    private Vector3 ApplySnappingLogic(Vector3 targetPos)
    {
        Vector3 finalPos = targetPos;

        if (enableSnapping && cornerPoints.Count >= 2)
        {
            Vector3 lastPoint = cornerPoints[cornerPoints.Count - 1];
            Vector3 prevPoint = cornerPoints[cornerPoints.Count - 2];

            Vector3 prevDir = (lastPoint - prevPoint);
            prevDir.y = 0;
            prevDir.Normalize();

            Vector3 currentVector = finalPos - lastPoint;
            currentVector.y = 0;
            float currentDist = currentVector.magnitude;

            if (currentDist > 0)
            {
                Vector3 currentDir = currentVector.normalized;
                float angle = Vector3.Angle(prevDir, currentDir);

                Vector3 snappedDir = Vector3.zero;
                bool shouldSnap = false;

                if (angle <= snapAngleThreshold) { snappedDir = prevDir; shouldSnap = true; }
                else if (angle >= 180f - snapAngleThreshold) { snappedDir = -prevDir; shouldSnap = true; }
                else if (Mathf.Abs(angle - 90f) <= snapAngleThreshold)
                {
                    Vector3 right = new Vector3(prevDir.z, 0, -prevDir.x);
                    Vector3 left  = new Vector3(-prevDir.z, 0, prevDir.x);
                    snappedDir = Vector3.Dot(currentDir, right) > 0 ? right : left;
                    shouldSnap = true;
                }

                if (shouldSnap)
                {
                    float projectedLength = Vector3.Dot(currentVector, snappedDir);
                    if (projectedLength > 0.1f)
                    {
                        finalPos = lastPoint + (snappedDir * projectedLength);
                        finalPos.y = currentFloorY;
                    }
                }
            }
        }

        return finalPos;
    }

    public void UndoLastPoint()
    {
        if (isRoomFinished || cornerPoints.Count == 0) return;

        cornerPoints.RemoveAt(cornerPoints.Count - 1);

        GameObject lastMarker = cornerMarkers[cornerMarkers.Count - 1];
        cornerMarkers.RemoveAt(cornerMarkers.Count - 1);
        Destroy(lastMarker);

        UpdateLineRenderer();
        if (cornerPoints.Count == 1)
        {
            if (previewMeshObj != null) previewMeshObj.SetActive(false);
        }
    }

    // ─────────────────────────────────────────────
    //  자동 완성
    // ─────────────────────────────────────────────
    public void AutoCompleteRoom()
    {
        if (cornerPoints.Count < 2)
        {
            Debug.LogWarning("자동 완성은 최소 2개의 점이 필요합니다.");
            return;
        }
        switch (autoCompleteShape)
        {
            case RoomShape.Rectangle: AutoCompleteRectangle(); break;
            case RoomShape.LShape:    AutoCompleteLShape();    break;
            case RoomShape.TShape:    AutoCompleteTShape();    break;
            case RoomShape.UShape:    AutoCompleteUShape();    break;
            case RoomShape.Custom:    Debug.Log("Custom: 점을 수동으로 찍어주세요."); break;
        }
    }

    public void AutoCompleteRectangle()
    {
        if (cornerPoints.Count != 2)
        {
            Debug.LogWarning("사각형 자동 완성은 점이 정확히 2개일 때만 가능합니다.");
            return;
        }

        Vector3 p0 = cornerPoints[0];
        Vector3 p1 = cornerPoints[1];
        Vector3 dir  = (p1 - p0).normalized;
        Vector3 perp = Vector3.Cross(dir, Vector3.up).normalized;
        float depth  = EstimateRoomDepth(p0, perp);

        cornerPoints.Add(p1 + perp * depth);
        cornerPoints.Add(p0 + perp * depth);
        FinishRoom();
    }

    private void AutoCompleteLShape()
    {
        if (cornerPoints.Count != 2) return;

        Vector3 p0 = cornerPoints[0];
        Vector3 p1 = cornerPoints[1];
        Vector3 dir  = (p1 - p0).normalized;
        Vector3 perp = Vector3.Cross(dir, Vector3.up).normalized;
        float width = Vector3.Distance(p0, p1);
        float depth = EstimateRoomDepth(p0, perp);
        if (Mathf.Abs(depth) < 1.5f) depth = Mathf.Sign(depth) * 3.0f;

        float cutD = 0.6f, cutW = 0.5f;
        Vector3 p2 = p1 + perp * (depth * cutD);
        Vector3 p3 = p1 + perp * (depth * cutD) - dir * (width * cutW);
        Vector3 p4 = p3 + perp * (depth * (1f - cutD));
        Vector3 p5 = p0 + perp * depth;
        p2.y = p3.y = p4.y = p5.y = currentFloorY;

        cornerPoints.Clear();
        cornerPoints.AddRange(new Vector3[] { p0, p1, p2, p3, p4, p5 });
        FinishRoom();
    }

    private void AutoCompleteTShape()
    {
        if (cornerPoints.Count != 2) return;

        Vector3 p0 = cornerPoints[0];
        Vector3 p1 = cornerPoints[1];
        Vector3 dir  = (p1 - p0).normalized;
        Vector3 perp = Vector3.Cross(dir, Vector3.up).normalized;
        float width = Vector3.Distance(p0, p1);
        float depth = EstimateRoomDepth(p0, perp);
        if (Mathf.Abs(depth) < 1.5f) depth = Mathf.Sign(depth) * 4.0f;

        float barD = 0.35f, stemW = 0.4f, sideM = (1f - stemW) / 2f;
        Vector3 v1 = p1 + perp * (depth * barD);
        Vector3 v2 = p0 + dir * (width * (1f - sideM)) + perp * (depth * barD);
        Vector3 v3 = p0 + dir * (width * (1f - sideM)) + perp * depth;
        Vector3 v4 = p0 + dir * (width * sideM) + perp * depth;
        Vector3 v5 = p0 + dir * (width * sideM) + perp * (depth * barD);
        Vector3 v6 = p0 + perp * (depth * barD);
        v1.y = v2.y = v3.y = v4.y = v5.y = v6.y = currentFloorY;

        cornerPoints.Clear();
        cornerPoints.AddRange(new Vector3[] { p0, p1, v1, v2, v3, v4, v5, v6 });
        FinishRoom();
    }

    private void AutoCompleteUShape()
    {
        if (cornerPoints.Count != 2) return;

        Vector3 p0 = cornerPoints[0];
        Vector3 p1 = cornerPoints[1];
        Vector3 dir  = (p1 - p0).normalized;
        Vector3 perp = Vector3.Cross(dir, Vector3.up).normalized;
        float width = Vector3.Distance(p0, p1);
        float depth = EstimateRoomDepth(p0, perp);
        if (Mathf.Abs(depth) < 1.5f) depth = Mathf.Sign(depth) * 4.0f;

        float armW = 0.25f, notchD = 0.55f;
        Vector3 v0 = p0;
        Vector3 v1 = p1;
        Vector3 v2 = p1 + perp * depth;
        Vector3 v3 = p0 + dir * (width * (1f - armW)) + perp * depth;
        Vector3 v4 = p0 + dir * (width * (1f - armW)) + perp * (depth * notchD);
        Vector3 v5 = p0 + dir * (width * armW) + perp * (depth * notchD);
        Vector3 v6 = p0 + dir * (width * armW) + perp * depth;
        Vector3 v7 = p0 + perp * depth;
        v0.y = v1.y = v2.y = v3.y = v4.y = v5.y = v6.y = v7.y = currentFloorY;

        cornerPoints.Clear();
        cornerPoints.AddRange(new Vector3[] { v0, v1, v2, v3, v4, v5, v6, v7 });
        FinishRoom();
    }

    private float EstimateRoomDepth(Vector3 origin, Vector3 direction)
    {
        float maxDepth = 2.0f, maxDot = 0f;
        if (planeManager == null) return maxDepth;

        foreach (var plane in planeManager.trackables)
        {
            if (plane.alignment != UnityEngine.XR.ARSubsystems.PlaneAlignment.HorizontalUp) continue;
            foreach (Vector2 bound in plane.boundary)
            {
                Vector3 worldBound = plane.transform.TransformPoint(new Vector3(bound.x, 0, bound.y));
                worldBound.y = currentFloorY;
                float dot = Vector3.Dot(worldBound - origin, direction);
                if (Mathf.Abs(dot) > maxDot) { maxDot = Mathf.Abs(dot); maxDepth = dot; }
            }
        }
        return maxDepth;
    }

    // ─────────────────────────────────────────────
    //  방 완성
    // ─────────────────────────────────────────────
    private void UpdateLineRenderer()
    {
        lineRenderer.positionCount = cornerPoints.Count;
        lineRenderer.SetPositions(cornerPoints.ToArray());
    }

    public void FinishRoom()
    {
        if (cornerPoints.Count < 3) { Debug.Log("최소 3개의 점이 필요합니다."); return; }
        if (isRoomFinished)         { Debug.Log("이미 방이 완성되었습니다.");    return; }

        isRoomFinished = true;
        lineRenderer.loop = true;

        for (int i = 0; i < cornerPoints.Count; i++)
            CreateWallSegment(cornerPoints[i], cornerPoints[(i + 1) % cornerPoints.Count], i);

        CreateCustomFloorMesh();

        lineRenderer.enabled = false;
        if (previewMeshObj != null) previewMeshObj.SetActive(false);
        foreach (var marker in cornerMarkers) marker.SetActive(false);

        Debug.Log("방 및 바닥 생성 완료!");
    }

    // ─────────────────────────────────────────────
    //  바닥 메쉬 생성 (Ear Clipping)
    // ─────────────────────────────────────────────
    private void CreateCustomFloorMesh()
    {
        if (cornerPoints.Count < 3) return;

        GameObject floorObj = new GameObject("CustomFloor");
        floorObj.layer = LayerMask.NameToLayer("Floor");
        floorObj.transform.SetParent(roomFrameObject.transform);

        Vector3 centerPos = Vector3.zero;
        foreach (var p in cornerPoints) centerPos += p;
        centerPos /= cornerPoints.Count;
        floorObj.transform.position = centerPos;

        MeshFilter   mf  = floorObj.AddComponent<MeshFilter>();
        MeshRenderer mr  = floorObj.AddComponent<MeshRenderer>();
        if (floorMaterial != null) mr.material = floorMaterial;

        List<int> earTris = EarClipTriangulate(cornerPoints);
        int n = cornerPoints.Count;
        Mesh mesh = new Mesh { name = "CustomFloorMesh" };

        Vector3[] verts   = new Vector3[n * 2];
        Vector3[] normals = new Vector3[n * 2];
        Vector2[] uvs     = new Vector2[n * 2];

        for (int i = 0; i < n; i++)
        {
            Vector3 lp = cornerPoints[i] - centerPos;
            verts[i] = verts[i + n] = lp;
            normals[i] = Vector3.up; normals[i + n] = Vector3.down;
            uvs[i] = uvs[i + n] = new Vector2(cornerPoints[i].x, cornerPoints[i].z);
        }

        List<int> tris = new List<int>();
        for (int i = 0; i < earTris.Count; i += 3)
        {
            tris.Add(earTris[i]); tris.Add(earTris[i+1]); tris.Add(earTris[i+2]);
            tris.Add(earTris[i]+n); tris.Add(earTris[i+2]+n); tris.Add(earTris[i+1]+n);
        }

        mesh.vertices = verts; mesh.normals = normals; mesh.uv = uvs;
        mesh.triangles = tris.ToArray();
        mesh.RecalculateBounds();
        mf.mesh = mesh;

        // Compound Convex Collider
        float thickness = 0.05f;
        for (int i = 0; i < earTris.Count; i += 3)
        {
            Vector3 a = cornerPoints[earTris[i]]   - centerPos;
            Vector3 b = cornerPoints[earTris[i+1]] - centerPos;
            Vector3 c = cornerPoints[earTris[i+2]] - centerPos;
            Vector3 d = Vector3.down * thickness;

            Mesh pm = new Mesh { name = $"FloorCollider_{i/3}" };
            pm.vertices  = new Vector3[] { a, b, c, a+d, b+d, c+d };
            pm.triangles = new int[] { 0,1,2, 3,5,4, 0,3,1,1,3,4, 1,4,2,2,4,5, 2,5,0,0,5,3 };
            pm.RecalculateNormals(); pm.RecalculateBounds();

            GameObject cc = new GameObject($"FloorCollider_{i/3}");
            cc.transform.SetParent(floorObj.transform, false);
            var mc = cc.AddComponent<MeshCollider>();
            mc.sharedMesh = pm; mc.convex = true;
        }
    }

    // ─────────────────────────────────────────────
    //  Ear Clipping 삼각분할
    // ─────────────────────────────────────────────
    private List<int> EarClipTriangulate(List<Vector3> polygon)
    {
        List<int> triangles = new List<int>();
        int n = polygon.Count;
        if (n < 3) return triangles;

        float signedArea = 0f;
        for (int i = 0; i < n; i++)
        {
            Vector3 cur  = polygon[i];
            Vector3 next = polygon[(i + 1) % n];
            signedArea += (cur.x * next.z - next.x * cur.z);
        }
        if (Mathf.Approximately(signedArea, 0f)) { Debug.LogWarning("면적이 0입니다."); return triangles; }

        bool isInputCCW = signedArea > 0f;
        List<int> indices = new List<int>();
        for (int i = 0; i < n; i++) indices.Add(i);

        int count = n, maxIter = n * n, iter = 0, idx = 0;

        while (count > 2 && iter < maxIter)
        {
            iter++;
            int iPrev = ((idx - 1) % count + count) % count;
            int iCurr = idx % count;
            int iNext = (idx + 1) % count;

            int pi = indices[iPrev], ci = indices[iCurr], ni = indices[iNext];

            Vector2 A = new Vector2(polygon[pi].x, polygon[pi].z);
            Vector2 B = new Vector2(polygon[ci].x, polygon[ci].z);
            Vector2 C = new Vector2(polygon[ni].x, polygon[ni].z);

            float cross   = Cross2D(A, B, C);
            bool isConvex = isInputCCW ? (cross > 0f) : (cross < 0f);

            if (isConvex)
            {
                bool isEar = true;
                for (int j = 0; j < count; j++)
                {
                    int t = indices[j];
                    if (t == pi || t == ci || t == ni) continue;
                    if (IsPointInTriangle(new Vector2(polygon[t].x, polygon[t].z), A, B, C)) { isEar = false; break; }
                }

                if (isEar)
                {
                    if (isInputCCW) { triangles.Add(pi); triangles.Add(ni); triangles.Add(ci); }
                    else            { triangles.Add(pi); triangles.Add(ci); triangles.Add(ni); }

                    indices.RemoveAt(iCurr);
                    count--;
                    if (count <= 2) break;
                    idx = 0;
                    continue;
                }
            }

            idx++;
            if (idx >= count) idx = 0;
        }
        return triangles;
    }

    private float Cross2D(Vector2 A, Vector2 B, Vector2 C)
        => (B.x - A.x) * (C.y - A.y) - (B.y - A.y) * (C.x - A.x);

    private bool IsPointInTriangle(Vector2 P, Vector2 A, Vector2 B, Vector2 C)
    {
        float d1 = Cross2D(A, B, P), d2 = Cross2D(B, C, P), d3 = Cross2D(C, A, P);
        return !((d1 < 0 || d2 < 0 || d3 < 0) && (d1 > 0 || d2 > 0 || d3 > 0));
    }

    // ─────────────────────────────────────────────
    //  벽 생성
    // ─────────────────────────────────────────────
    private void CreateWallSegment(Vector3 start, Vector3 end, int index)
    {
        Vector3 center = (start + end) / 2f;
        center.y += ceilingHeight / 2f;

        float      length    = Vector3.Distance(start, end);
        Quaternion rotation  = Quaternion.LookRotation((end - start).normalized);
        GameObject wall      = Instantiate(wallPrefab, center, rotation, roomFrameObject.transform);
        wall.name            = "Wall_" + index;
        wall.transform.localScale = new Vector3(wallThickness, ceilingHeight, length);
    }

    // ─────────────────────────────────────────────
    //  프리뷰 메쉬
    // ─────────────────────────────────────────────
    private void UpdatePreviewMesh(List<Vector3> vertices)
    {
        if (vertices == null || vertices.Count < 3) return;

        Vector3 centerPos = Vector3.zero;
        foreach (var v in vertices) centerPos += v;
        centerPos /= vertices.Count;
        previewMeshObj.transform.position = centerPos;

        List<int> earTris = EarClipTriangulate(vertices);
        int n = vertices.Count;
        Mesh mesh = new Mesh { name = "PreviewFloorMesh" };

        Vector3[] verts   = new Vector3[n * 2];
        Vector3[] normals = new Vector3[n * 2];
        for (int i = 0; i < n; i++)
        {
            verts[i] = verts[i + n] = vertices[i] - centerPos;
            normals[i] = Vector3.up; normals[i + n] = Vector3.down;
        }

        List<int> tris = new List<int>();
        for (int i = 0; i < earTris.Count; i += 3)
        {
            tris.Add(earTris[i]); tris.Add(earTris[i+1]); tris.Add(earTris[i+2]);
            tris.Add(earTris[i]+n); tris.Add(earTris[i+2]+n); tris.Add(earTris[i+1]+n);
        }

        mesh.vertices = verts; mesh.normals = normals;
        mesh.triangles = tris.ToArray();
        mesh.RecalculateBounds();
        previewMeshFilter.mesh = mesh;
    }
}

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
    public float ceilingHeight = 24f;
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

    // 지원하는 방 형태 열거형
    public enum RoomShape
    {
        Rectangle,  // 기본 사각형
        LShape,     // L자형
        TShape,     // T자형
        UShape,     // U자형
        Custom      // 사용자가 직접 모든 점을 찍음
    }

    // AR Raycast 결과 버퍼
    private static readonly List<ARRaycastHit> arHits = new List<ARRaycastHit>();

    [Header("방 구조 오브젝트")]
    public GameObject roomFrameObject;
    public GameObject furnitureObject;

    // 상태 ��리
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
            ceilingHeightSlider.value = ceilingHeight / 0.1f;
            UpdateCeilingHeightText(ceilingHeight);
            ceilingHeightSlider.onValueChanged.AddListener(OnCeilingHeightChanged);
        }

        if (SnappingToggle != null) SnappingToggle.onValueChanged.AddListener((value) => { enableSnapping = value; });
        if (MagneticSnapToggle != null) MagneticSnapToggle.onValueChanged.AddListener((value) => { enableMagneticSnap = value; });
    }

    void Update()
    {
        if (isRoomFinished) return;

        UpdateFloorDetection();

        if (isFloorDetected)
        {
            if (cornerPoints.Count >= 1)
            {
                Vector3 crosshairPos = GetCrosshairPosition(out bool success);
                if (success)
                {
                    crosshairPos = ApplySnappingLogic(crosshairPos);
                    UpdateRealtimePreview(crosshairPos);
                }
            }
        }
    }

    // ─────────────────────────────────────────────
    //  바닥/천장 감지 (기존 로직 유지)
    // ─────────────────────────────────────────────
    private void UpdateFloorDetection()
    {
        float planeFloorY = float.MaxValue;
        float ceilingY = float.MinValue;
        bool foundFloor = false, foundCeiling = false;

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

        if (foundCeiling) { currentCeilingY = ceilingY; isCeilingDetected = true; }

        if (Camera.main != null && raycastManager != null)
        {
            Vector2 sc = new Vector2(Screen.width / 2f, Screen.height / 2f);
            if (raycastManager.Raycast(sc, arHits,
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

    private Vector3 GetCrosshairPosition(out bool success)
    {
        success = false;
        if (!isFloorDetected) return Vector3.zero;

        Vector2 screenCenter = new Vector2(Screen.width / 2f, Screen.height / 2f);
        Ray ray = Camera.main.ScreenPointToRay(screenCenter);

        if (ray.direction.y < 0)
        {
            if (raycastManager != null &&
                raycastManager.Raycast(screenCenter, arHits, UnityEngine.XR.ARSubsystems.TrackableType.PlaneWithinPolygon))
            {
                Vector3 hitPos = arHits[0].pose.position;
                if (!isFloorDetected || hitPos.y <= currentFloorY + 0.15f)
                {
                    currentFloorY = hitPos.y;
                    success = true;
                    return hitPos;
                }
            }

            Plane mathFloor = new Plane(Vector3.up, new Vector3(0, currentFloorY, 0));
            if (mathFloor.Raycast(ray, out float enterDistance))
            {
                success = true;
                return ray.GetPoint(enterDistance);
            }
        }
        else
        {
            float targetCeilingY = isCeilingDetected ? currentCeilingY : (currentFloorY + ceilingHeight);
            Plane mathCeiling = new Plane(Vector3.down, new Vector3(0, targetCeilingY, 0));

            if (mathCeiling.Raycast(ray, out float enterDistance))
            {
                Vector3 ceilingHit = ray.GetPoint(enterDistance);
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
        {
            lineRenderer.SetPosition(i, cornerPoints[i]);
        }
        lineRenderer.SetPosition(cornerPoints.Count, currentAimPos);

        if (cornerPoints.Count >= 2)
        {
            if (previewMeshObj != null) previewMeshObj.SetActive(true);
            List<Vector3> previewVertices = new List<Vector3>(cornerPoints);
            previewVertices.Add(currentAimPos);
            UpdatePreviewMesh(previewVertices);
        }
        else
        {
            if (previewMeshObj != null) previewMeshObj.SetActive(false);
        }
    }

    // ─────────────────────────────────────────────
    //  점 추가 / 실행취소
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

        Vector3 hitPos = GetCrosshairPosition(out bool hitSuccess);

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
                    Vector3 left = new Vector3(-prevDir.z, 0, prevDir.x);
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
    //  자동 완성: 다양한 방 형태 지원
    // ─────────────────────────────────────────────

    /// <summary>
    /// 2개의 점(벽 한 면)으로 다양한 형태의 방을 자동 완성합니다.
    /// autoCompleteShape 설정에 따라 사각형, L자형, T자형, U자형을 생성합니다.
    /// </summary>
    public void AutoCompleteRoom()
    {
        if (cornerPoints.Count < 2)
        {
            Debug.LogWarning("자동 완성은 최소 2개의 점이 필요합니다.");
            return;
        }

        switch (autoCompleteShape)
        {
            case RoomShape.Rectangle:
                AutoCompleteRectangle();
                break;
            case RoomShape.LShape:
                AutoCompleteLShape();
                break;
            case RoomShape.TShape:
                AutoCompleteTShape();
                break;
            case RoomShape.UShape:
                AutoCompleteUShape();
                break;
            case RoomShape.Custom:
                Debug.Log("Custom 모드에서는 모든 점을 수동으로 찍어주세요.");
                break;
        }
    }

    /// <summary>
    /// 기존 사각형 자동 완성 (2점 → 4점)
    /// </summary>
    public void AutoCompleteRectangle()
    {
        if (cornerPoints.Count != 2)
        {
            Debug.LogWarning("사각형 자동 완성은 점이 정확히 2개일 때만 가능합니다.");
            return;
        }

        Vector3 p0 = cornerPoints[0];
        Vector3 p1 = cornerPoints[1];

        Vector3 dir = (p1 - p0).normalized;
        Vector3 perpendicular = Vector3.Cross(dir, Vector3.up).normalized;

        float depth = EstimateRoomDepth(p0, perpendicular);

        Vector3 p2 = p1 + (perpendicular * depth);
        Vector3 p3 = p0 + (perpendicular * depth);

        cornerPoints.Add(p2);
        cornerPoints.Add(p3);

        FinishRoom();
    }

    /// <summary>
    /// L자형 방 자동 완성 (2점 → 6점)
    /// 
    ///  p0 ──── p1
    ///  |        |
    ///  |   p4 ─ p3
    ///  |   |
    ///  p5 ─┘  (= p4에서 아래로)
    ///  
    /// 전체 깊이의 60% 지점에서 폭의 50%만큼 잘라낸 형태
    /// </summary>
    private void AutoCompleteLShape()
    {
        if (cornerPoints.Count != 2)
        {
            Debug.LogWarning("L자형 자동 완성은 점이 정확히 2개일 때만 가능합니다.");
            return;
        }

        Vector3 p0 = cornerPoints[0];
        Vector3 p1 = cornerPoints[1];

        Vector3 dir = (p1 - p0).normalized;
        Vector3 perp = Vector3.Cross(dir, Vector3.up).normalized;

        float width = Vector3.Distance(p0, p1);
        float depth = EstimateRoomDepth(p0, perp);
        if (Mathf.Abs(depth) < 1.5f) depth = Mathf.Sign(depth) * 3.0f;

        // L자형 비율: 깊이 60% 지점에서 폭 50% 잘라냄
        float cutDepthRatio = 0.6f;
        float cutWidthRatio = 0.5f;

        Vector3 p2 = p1 + perp * (depth * cutDepthRatio);
        Vector3 p3 = p1 + perp * (depth * cutDepthRatio) - dir * (width * cutWidthRatio);
        Vector3 p4 = p3 + perp * (depth * (1f - cutDepthRatio));
        Vector3 p5 = p0 + perp * depth;

        // 모든 점의 Y를 바닥 높이로 고정
        p2.y = p3.y = p4.y = p5.y = currentFloorY;

        cornerPoints.Clear();
        cornerPoints.AddRange(new Vector3[] { p0, p1, p2, p3, p4, p5 });

        FinishRoom();
    }

    /// <summary>
    /// T자형 방 자동 완성 (2점 → 8점)
    ///
    ///  p0 ─────────── p1
    ///  |               |
    ///  p7  p2 ─── p3  (양쪽에서 안으로 들어옴)
    ///      |       |
    ///      p5 ─── p4
    ///      
    /// 상단 가로 바 + 중앙 세로 줄기
    /// </summary>
    private void AutoCompleteTShape()
    {
        if (cornerPoints.Count != 2)
        {
            Debug.LogWarning("T자형 자동 완성은 점이 정확히 2개일 때만 가능합니다.");
            return;
        }

        Vector3 p0 = cornerPoints[0];
        Vector3 p1 = cornerPoints[1];

        Vector3 dir = (p1 - p0).normalized;
        Vector3 perp = Vector3.Cross(dir, Vector3.up).normalized;

        float width = Vector3.Distance(p0, p1);
        float depth = EstimateRoomDepth(p0, perp);
        if (Mathf.Abs(depth) < 1.5f) depth = Mathf.Sign(depth) * 4.0f;

        // T자형: 상단 바 깊이 40%, 줄기 폭 40% (양쪽 30%씩 잘라냄)
        float barDepthRatio = 0.35f;
        float stemWidthRatio = 0.4f;
        float sideMargin = (1f - stemWidthRatio) / 2f;

        Vector3 barBottom_right = p1 + perp * (depth * barDepthRatio);
        Vector3 stem_topRight = p0 + dir * (width * (1f - sideMargin)) + perp * (depth * barDepthRatio);
        Vector3 stem_bottomRight = p0 + dir * (width * (1f - sideMargin)) + perp * depth;
        Vector3 stem_bottomLeft = p0 + dir * (width * sideMargin) + perp * depth;
        Vector3 stem_topLeft = p0 + dir * (width * sideMargin) + perp * (depth * barDepthRatio);
        Vector3 barBottom_left = p0 + perp * (depth * barDepthRatio);

        // Y 고정
        barBottom_right.y = stem_topRight.y = stem_bottomRight.y =
        stem_bottomLeft.y = stem_topLeft.y = barBottom_left.y = currentFloorY;

        cornerPoints.Clear();
        cornerPoints.AddRange(new Vector3[] {
            p0, p1,
            barBottom_right, stem_topRight,
            stem_bottomRight, stem_bottomLeft,
            stem_topLeft, barBottom_left
        });

        FinishRoom();
    }

    /// <summary>
    /// U자형 방 자동 완성 (2점 → 8점)
    ///
    ///  p0 ── p7    p2 ── p1
    ///  |      |    |      |
    ///  |      p6 ─ p3     |
    ///  |                  |
    ///  p5 ────────── p4
    ///  
    /// 안쪽 중앙에 사각형 홈이 파인 형태
    /// </summary>
    private void AutoCompleteUShape()
    {
        if (cornerPoints.Count != 2)
        {
            Debug.LogWarning("U자형 자동 완성은 점이 정확히 2개일 때만 가능합니다.");
            return;
        }

        Vector3 p0 = cornerPoints[0];
        Vector3 p1 = cornerPoints[1];

        Vector3 dir = (p1 - p0).normalized;
        Vector3 perp = Vector3.Cross(dir, Vector3.up).normalized;

        float width = Vector3.Distance(p0, p1);
        float depth = EstimateRoomDepth(p0, perp);
        if (Mathf.Abs(depth) < 1.5f) depth = Mathf.Sign(depth) * 4.0f;

        // U자형: 양쪽 벽 폭 25%, 홈 깊이 55%
        float armWidthRatio = 0.25f;
        float notchDepthRatio = 0.55f;

        Vector3 pt1 = p1;  // 오른쪽 상단
        Vector3 pt2 = p1 + perp * (depth * notchDepthRatio);  // 오른쪽 팔 안쪽 상단
        Vector3 pt3 = p0 + dir * (width * (1f - armWidthRatio)) + perp * (depth * notchDepthRatio);
        Vector3 pt4 = p0 + dir * (width * armWidthRatio) + perp * (depth * notchDepthRatio);
        Vector3 pt5 = p0 + perp * (depth * notchDepthRatio);  // 왼쪽 팔 안쪽 상단
        Vector3 pt6 = p0 + dir * (width * armWidthRatio) + perp * depth;
        Vector3 pt7 = p0 + dir * (width * (1f - armWidthRatio)) + perp * depth;
        Vector3 pt8 = p1 + perp * depth;

        // 올바른 U자 순서로 정렬
        // 외곽을 시계방향(또는 반시계)으로 한바퀴 도는 순서
        Vector3 v0 = p0;
        Vector3 v1 = pt1;
        Vector3 v2 = pt8;                  // 오른쪽 하단
        Vector3 v3 = pt7;                  // 오른쪽 팔 안쪽 하단
        Vector3 v4 = pt3;                  // 홈 오른쪽 상단
        Vector3 v5 = pt4;                  // 홈 왼쪽 상단
        Vector3 v6 = pt6;                  // 왼쪽 팔 안쪽 하단
        Vector3 v7 = p0 + perp * depth;    // 왼쪽 하단

        v0.y = v1.y = v2.y = v3.y = v4.y = v5.y = v6.y = v7.y = currentFloorY;

        cornerPoints.Clear();
        cornerPoints.AddRange(new Vector3[] { v0, v1, v2, v3, v4, v5, v6, v7 });

        FinishRoom();
    }

    /// <summary>
    /// AR 바닥 평면 데이터를 분석하여 방의 깊이를 추정합니다.
    /// </summary>
    private float EstimateRoomDepth(Vector3 origin, Vector3 direction)
    {
        float maxDepth = 2.0f;
        float maxDot = 0f;

        foreach (var plane in planeManager.trackables)
        {
            if (plane.alignment != UnityEngine.XR.ARSubsystems.PlaneAlignment.HorizontalUp) continue;

            foreach (Vector2 bound in plane.boundary)
            {
                Vector3 worldBound = plane.transform.TransformPoint(new Vector3(bound.x, 0, bound.y));
                worldBound.y = currentFloorY;

                float dot = Vector3.Dot(worldBound - origin, direction);
                if (Mathf.Abs(dot) > maxDot)
                {
                    maxDot = Mathf.Abs(dot);
                    maxDepth = dot;
                }
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
        if (cornerPoints.Count < 3)
        {
            Debug.Log("방을 만들려면 최소 3개의 점이 필요합니다.");
            return;
        }

        if (isRoomFinished)
        {
            Debug.Log("이미 방이 완성되었습니다.");
            return;
        }

        isRoomFinished = true;
        lineRenderer.loop = true;

        // 벽 생성 (N각형의 모든 변에 대해)
        for (int i = 0; i < cornerPoints.Count; i++)
        {
            Vector3 p1 = cornerPoints[i];
            Vector3 p2 = cornerPoints[(i + 1) % cornerPoints.Count];
            CreateWallSegment(p1, p2, i);
        }

        // 바닥 생성 (Ear Clipping으로 오목 다각형도 지원)
        CreateCustomFloorMesh();

        lineRenderer.enabled = false;
        if (previewMeshObj != null) previewMeshObj.SetActive(false);
        foreach (var marker in cornerMarkers) marker.SetActive(false);

        Debug.Log("방 및 바닥 생성 완료!");
    }

    // ─────────────────────────────────────────────
    //  바닥 메쉬 생성 (Ear Clipping 삼각분할)
    // ─────────────────────────────────────────────

    /// <summary>
    /// Ear Clipping 알고리즘으로 임의의 단순 다각형(볼록/오목 모두)을 삼각분할합니다.
    /// 기존 Fan Triangulation은 볼록 다각형에서만 정확했으나,
    /// Ear Clipping은 L자형, T자형, U자형 등 오목 다각형에서도 올바르게 작동합니다.
    /// </summary>
    private void CreateCustomFloorMesh()
    {
        if (cornerPoints.Count < 3) return;

        GameObject floorObj = new GameObject("CustomFloor");
        floorObj.layer = LayerMask.NameToLayer("Floor");
        floorObj.transform.SetParent(roomFrameObject.transform);

        Vector3 centerPos = Vector3.zero;
        for (int i = 0; i < cornerPoints.Count; i++)
            centerPos += cornerPoints[i];
        centerPos /= cornerPoints.Count;
        floorObj.transform.position = centerPos;

        MeshFilter meshFilter = floorObj.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = floorObj.AddComponent<MeshRenderer>();
        if (floorMaterial != null)
            meshRenderer.material = floorMaterial;

        // ── Ear Clipping 삼각분할 ──
        List<int> earTriangles = EarClipTriangulate(cornerPoints);

        int vertCount = cornerPoints.Count;
        Mesh floorMesh = new Mesh();
        floorMesh.name = "CustomFloorMesh";

        // 양면 렌더링용 버텍스 (정면 + 뒷면)
        Vector3[] vertices = new Vector3[vertCount * 2];
        Vector3[] normals = new Vector3[vertCount * 2];
        Vector2[] uvs = new Vector2[vertCount * 2];

        for (int i = 0; i < vertCount; i++)
        {
            Vector3 localPos = cornerPoints[i] - centerPos;

            vertices[i] = localPos;
            normals[i] = Vector3.up;
            uvs[i] = new Vector2(cornerPoints[i].x, cornerPoints[i].z);

            vertices[i + vertCount] = localPos;
            normals[i + vertCount] = Vector3.down;
            uvs[i + vertCount] = uvs[i];
        }

        // Ear Clipping 결과를 양면으로 확장
        // EarClipTriangulate는 CW 순서로 출력 → Unity에서 법선 Vector3.up (정면)
        List<int> allTriangles = new List<int>();
        for (int i = 0; i < earTriangles.Count; i += 3)
        {
            // 정면 (위에서 볼 때, CW → 법선 up)
            allTriangles.Add(earTriangles[i]);
            allTriangles.Add(earTriangles[i + 1]);
            allTriangles.Add(earTriangles[i + 2]);

            // 뒷면 (아래에서 볼 때, 순서 반전 → 법선 down)
            allTriangles.Add(earTriangles[i] + vertCount);
            allTriangles.Add(earTriangles[i + 2] + vertCount);
            allTriangles.Add(earTriangles[i + 1] + vertCount);
        }

        floorMesh.vertices = vertices;
        floorMesh.normals = normals;
        floorMesh.uv = uvs;
        floorMesh.triangles = allTriangles.ToArray();
        floorMesh.RecalculateBounds();

        meshFilter.mesh = floorMesh;

        // ── Compound Convex Collider ──
        // 오목 MeshCollider는 동적 Rigidbody와 함께 사용할 수 없으므로,
        // Ear Clipping 결과의 각 삼각형을 삼각 프리즘(바닥~약간의 두께)으로 만들어
        // 개별 convex MeshCollider로 붙입니다.
        // Unity는 자식 오브젝트의 Collider들을 Compound Collider로 자동 합쳐줍니다.
        float floorThickness = 0.05f;
        for (int i = 0; i < earTriangles.Count; i += 3)
        {
            Vector3 a = cornerPoints[earTriangles[i]] - centerPos;
            Vector3 b = cornerPoints[earTriangles[i + 1]] - centerPos;
            Vector3 c = cornerPoints[earTriangles[i + 2]] - centerPos;

            // 삼각 프리즘: 상단 3개 + 하단 3개 = 6개 꼭짓점
            Vector3 downOffset = Vector3.down * floorThickness;
            Vector3[] prismVerts = new Vector3[]
            {
                a, b, c,                               // 상단 면
                a + downOffset, b + downOffset, c + downOffset  // 하단 면
            };

            // 프리즘의 5개 면 (상단, 하단, 측면 3개)을 삼각형으로 구성
            int[] prismTris = new int[]
            {
                // 상단 (CW)
                0, 1, 2,
                // 하단 (반전)
                3, 5, 4,
                // 측면 1
                0, 3, 1, 1, 3, 4,
                // 측면 2
                1, 4, 2, 2, 4, 5,
                // 측면 3
                2, 5, 0, 0, 5, 3
            };

            Mesh prismMesh = new Mesh();
            prismMesh.name = $"FloorCollider_{i / 3}";
            prismMesh.vertices = prismVerts;
            prismMesh.triangles = prismTris;
            prismMesh.RecalculateNormals();
            prismMesh.RecalculateBounds();

            GameObject colliderChild = new GameObject($"FloorCollider_{i / 3}");
            colliderChild.transform.SetParent(floorObj.transform, false);
            colliderChild.transform.localPosition = Vector3.zero;

            MeshCollider mc = colliderChild.AddComponent<MeshCollider>();
            mc.sharedMesh = prismMesh;
            mc.convex = true;
        }
    }

    // ─────────────────────────────────────────────
    //  Ear Clipping 삼각분할 알고리즘
    // ─────────────────────────────────────────────

    /// <summary>
    /// 2D(XZ 평면) Ear Clipping 삼각분할.
    /// 입력: 3D 점 리스트 (Y는 무시하고 XZ만 사용)
    /// 출력: 삼각형 인덱스 리스트 (원본 cornerPoints 기준 인덱스)
    /// </summary>
    /// <summary>
    /// 2D(XZ 평면) Ear Clipping 삼각분할.
    /// 
    /// Unity는 좌수(left-handed) 좌표계를 사용하므로, 위에서 내려다볼 때
    /// 삼각형 인덱스가 시계방향(CW)이면 법선이 Vector3.up을 향합니다.
    /// 
    /// 이 함수는 다각형의 winding을 판별한 뒤, 최종 출력 삼각형이
    /// 항상 CW(= Unity에서 법선 up)가 되도록 보장합니다.
    /// </summary>
    private List<int> EarClipTriangulate(List<Vector3> polygon)
    {
        List<int> triangles = new List<int>();
        int n = polygon.Count;
        if (n < 3) return triangles;

        // 1. Shoelace formula로 다각형 감김 방향 판별 (XZ 평면)
        float signedArea = 0f;
        for (int i = 0; i < n; i++)
        {
            Vector3 current = polygon[i];
            Vector3 next = polygon[(i + 1) % n];
            signedArea += (current.x * next.z - next.x * current.z);
        }

        if (Mathf.Approximately(signedArea, 0f))
        {
            Debug.LogWarning("다각형의 면적이 0입니다. 점들이 일직선 위에 있습니다.");
            return triangles;
        }

        // signedArea > 0 → XZ 평면에서 CCW (2D 수학 기준)
        // signedArea < 0 → XZ 평면에서 CW  (2D 수학 기준)
        // Unity 좌수 좌표계에서 위에서 봤을 때:
        //   CW 삼각형 → 법선 up (우리가 원하는 방향)
        // 따라서 Ear Clipping은 입력 순서 그대로 처리하되,
        // "볼록" 판별 기준만 winding에 맞추면 됩니다.
        bool isInputCCW = signedArea > 0f;

        // 2. 인덱스 리스트 (항상 원본 순서)
        List<int> indices = new List<int>();
        for (int i = 0; i < n; i++)
            indices.Add(i);

        int count = indices.Count;
        int maxIterations = count * count;
        int iteration = 0;
        int i_idx = 0;

        while (count > 2 && iteration < maxIterations)
        {
            iteration++;

            int idxPrev = ((i_idx - 1) % count + count) % count;
            int idxCurr = i_idx % count;
            int idxNext = (i_idx + 1) % count;

            int prevIdx = indices[idxPrev];
            int currIdx = indices[idxCurr];
            int nextIdx = indices[idxNext];

            Vector2 A = new Vector2(polygon[prevIdx].x, polygon[prevIdx].z);
            Vector2 B = new Vector2(polygon[currIdx].x, polygon[currIdx].z);
            Vector2 C = new Vector2(polygon[nextIdx].x, polygon[nextIdx].z);

            // 볼록 판별: cross의 부호가 winding에 따라 달라짐
            // CCW 입력 → cross > 0 이 볼록
            // CW 입력  → cross < 0 이 볼록
            float cross = Cross2D(A, B, C);
            bool isConvex = isInputCCW ? (cross > 0f) : (cross < 0f);

            if (isConvex)
            {
                // "귀"인지 확인: 삼각형 ABC 안에 다른 꼭짓점이 없어야 함
                bool isEar = true;
                for (int j = 0; j < count; j++)
                {
                    int testIdx = indices[j];
                    if (testIdx == prevIdx || testIdx == currIdx || testIdx == nextIdx) continue;

                    Vector2 P = new Vector2(polygon[testIdx].x, polygon[testIdx].z);
                    if (IsPointInTriangle(P, A, B, C))
                    {
                        isEar = false;
                        break;
                    }
                }

                if (isEar)
                {
                    // 삼각형 추가
                    // CCW 입력이면 그대로 넣으면 CCW 삼각형 → Unity에서 법선 down
                    // → CW로 뒤집어야 법선 up이 됨
                    // CW 입력이면 그대로 넣으면 CW 삼각형 → Unity에서 법선 up (정상)
                    if (isInputCCW)
                    {
                        // 순서 뒤집기: prev, next, curr (CW로 변환)
                        triangles.Add(prevIdx);
                        triangles.Add(nextIdx);
                        triangles.Add(currIdx);
                    }
                    else
                    {
                        // 이미 CW → 그대로
                        triangles.Add(prevIdx);
                        triangles.Add(currIdx);
                        triangles.Add(nextIdx);
                    }

                    // 현재 꼭짓점 제거
                    indices.RemoveAt(idxCurr);
                    count--;

                    if (count <= 2) break;
                    i_idx = 0;
                    continue;
                }
            }

            i_idx++;
            if (i_idx >= count) i_idx = 0;
        }

        return triangles;
    }

    /// <summary>
    /// 2D 외적 (A→B × A→C).
    /// </summary>
    private float Cross2D(Vector2 A, Vector2 B, Vector2 C)
    {
        return (B.x - A.x) * (C.y - A.y) - (B.y - A.y) * (C.x - A.x);
    }

    /// <summary>
    /// 점 P가 삼각형 ABC 내부에 있는지 판별 (부호 일관성 방식).
    /// 삼각형의 winding에 관계없이 작동합니다.
    /// </summary>
    private bool IsPointInTriangle(Vector2 P, Vector2 A, Vector2 B, Vector2 C)
    {
        float d1 = Cross2D(A, B, P);
        float d2 = Cross2D(B, C, P);
        float d3 = Cross2D(C, A, P);

        bool hasNeg = (d1 < 0) || (d2 < 0) || (d3 < 0);
        bool hasPos = (d1 > 0) || (d2 > 0) || (d3 > 0);

        return !(hasNeg && hasPos);
    }

    // ─────────────────────────────────────────────
    //  벽 생성
    // ─────────────────────────────────────────────
    private void CreateWallSegment(Vector3 start, Vector3 end, int index)
    {
        Vector3 center = (start + end) / 2f;
        center.y += (ceilingHeight / 2f);

        float length = Vector3.Distance(start, end);

        Vector3 direction = (end - start).normalized;
        Quaternion rotation = Quaternion.LookRotation(direction);

        GameObject wall = Instantiate(wallPrefab, center, rotation, roomFrameObject.transform);
        wall.name = "Wall_" + index;
        wall.transform.localScale = new Vector3(wallThickness, ceilingHeight, length);
    }

    // ─────────────────────────────────────────────
    //  프리뷰 메쉬 (Ear Clipping 적용)
    // ─────────────────────────────────────────────
    private void UpdatePreviewMesh(List<Vector3> vertices)
    {
        if (vertices == null || vertices.Count < 3) return;

        Vector3 centerPos = Vector3.zero;
        for (int i = 0; i < vertices.Count; i++)
            centerPos += vertices[i];
        centerPos /= vertices.Count;
        previewMeshObj.transform.position = centerPos;

        // Ear Clipping으로 프리뷰도 정확하게 삼각분할
        List<int> earTriangles = EarClipTriangulate(vertices);

        int vertCount = vertices.Count;
        Mesh mesh = new Mesh();
        mesh.name = "PreviewFloorMesh";

        Vector3[] verts = new Vector3[vertCount * 2];
        Vector3[] normals = new Vector3[vertCount * 2];

        for (int i = 0; i < vertCount; i++)
        {
            Vector3 localPos = vertices[i] - centerPos;
            verts[i] = localPos;
            verts[i + vertCount] = localPos;
            normals[i] = Vector3.up;
            normals[i + vertCount] = Vector3.down;
        }

        List<int> allTriangles = new List<int>();
        for (int i = 0; i < earTriangles.Count; i += 3)
        {
            allTriangles.Add(earTriangles[i]);
            allTriangles.Add(earTriangles[i + 1]);
            allTriangles.Add(earTriangles[i + 2]);

            allTriangles.Add(earTriangles[i] + vertCount);
            allTriangles.Add(earTriangles[i + 2] + vertCount);
            allTriangles.Add(earTriangles[i + 1] + vertCount);
        }

        mesh.vertices = verts;
        mesh.normals = normals;
        mesh.triangles = allTriangles.ToArray();
        mesh.RecalculateBounds();
        previewMeshFilter.mesh = mesh;
    }
}
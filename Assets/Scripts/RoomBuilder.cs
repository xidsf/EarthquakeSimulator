using UnityEngine;
using UnityEngine.XR.ARFoundation;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine.UI;

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
    public float ceilingHeight = 2.4f;
    public float wallThickness = 0.1f;

    [Header("시각적 피드백 & 프리뷰")]
    public GameObject cornerMarkerPrefab;
    public Material previewMeshMaterial; // 반투명한 프리뷰용 재질 (새로 추가됨)
    private LineRenderer lineRenderer;
    private GameObject previewMeshObj;
    private MeshFilter previewMeshFilter;

    [Header("UI 요소")]
    public RectTransform PointAlertUIPanel;
    public Toggle SnappingToggle;
    public Toggle MagneticSnapToggle;
    private TextMeshProUGUI uiText;
    private Vector2 uiOriginPos;
    private int uiDisplayMovementDistance = 100;
    private Coroutine uiCoroutine;

    [Header("자동 보정 설정")]
    public bool enableSnapping = true;
    public float snapAngleThreshold = 15f;
    public bool enableMagneticSnap = false; // AR 경계 기반 스냅은 신뢰할 수 없어 비활성화
    public float magneticSnapRadius = 0.2f;

    // AR Raycast 결과 버퍼 (매 프레임 GC 할당 방지)
    private static readonly List<ARRaycastHit> arHits = new List<ARRaycastHit>();

    // 상태 관리
    private List<Vector3> cornerPoints = new List<Vector3>();
    private List<GameObject> cornerMarkers = new List<GameObject>();
    private bool isRoomFinished = false;
    private float currentFloorY = 0f;
    private bool isFloorDetected = false;
    private float currentCeilingY = 0f;
    private bool isCeilingDetected = false;
    private bool isManualMode = false; // 사용자가 직접 점을 찍기 시작했는지 여부
    private GameObject anchoredObbObj;
    private MeshFilter anchoredObbFilter;

    public bool IsScanning { get { return !isRoomFinished;  }  }
    public float currentFloorHeight { get { return currentFloorY; } }

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

        uiOriginPos = PointAlertUIPanel.anchoredPosition;
        uiText = PointAlertUIPanel.GetComponentInChildren<TextMeshProUGUI>();
        if (uiText == null) Debug.LogWarning("UI 패널에 TextMeshProUGUI가 없습니다.");

        // 실시간 프리뷰 메쉬 초기화
        previewMeshObj = new GameObject("PreviewMesh");
        previewMeshFilter = previewMeshObj.AddComponent<MeshFilter>();
        MeshRenderer mr = previewMeshObj.AddComponent<MeshRenderer>();
        if (previewMeshMaterial != null) mr.material = previewMeshMaterial;

        // 1점 앵커링용 전용 프리뷰 메쉬 초기화
        anchoredObbObj = new GameObject("AnchoredOBBMesh");
        anchoredObbFilter = anchoredObbObj.AddComponent<MeshFilter>();
        MeshRenderer anchoredMr = anchoredObbObj.AddComponent<MeshRenderer>();
        if (previewMeshMaterial != null) anchoredMr.material = previewMeshMaterial;
        anchoredObbObj.SetActive(false);

        if(SnappingToggle != null) SnappingToggle.onValueChanged.AddListener((value) => { enableSnapping = value; });
        if (MagneticSnapToggle != null) MagneticSnapToggle.onValueChanged.AddListener((value) => { enableMagneticSnap = value; });
    }

    void Update()
    {
        if (isRoomFinished) return;

        UpdateFloorDetection();

        if (isFloorDetected)
        {
            if (!isManualMode && cornerPoints.Count == 0)
            {
                UpdateOBBPreview();
            }
            else if (cornerPoints.Count >= 1)
            {
                Vector3 crosshairPos = GetCrosshairPosition(out bool success);
                if (success)
                {
                    crosshairPos = ApplySnappingLogic(crosshairPos);
                    UpdateRealtimePreview(crosshairPos);

                    // [추가됨] 점이 정확히 1개일 때만 앵커링된 OBB를 보여줌
                    if (cornerPoints.Count == 1)
                    {
                        DrawAnchoredOBB(cornerPoints[0]);
                    }
                    else
                    {
                        // 점이 2개 이상이 되면 앵커링 OBB를 숨김
                        if (anchoredObbObj != null) anchoredObbObj.SetActive(false);
                    }
                }
            }
        }
    }

    // 0점(전체 프리뷰) 및 1점(앵커링 프리뷰) 처리 로직
    private void UpdateOBBPreview()
    {
        // 1점 이상이면 OBB 계산 자체를 멈춥니다.
        if (cornerPoints.Count >= 1) return;

        Vector3 bestForward = Vector3.forward;
        float maxArea = 0f;
        ARPlane mainPlane = null;

        foreach (var plane in planeManager.trackables)
        {
            if (plane.alignment == UnityEngine.XR.ARSubsystems.PlaneAlignment.HorizontalUp)
            {
                float area = plane.extents.x * plane.extents.y;
                if (area > maxArea) { maxArea = area; mainPlane = plane; }
            }
        }

        if (mainPlane == null) return;

        // --- Pass 1: 경계 엣지 가중 평균으로 벽 방향 추출 ---
        // plane.transform.forward/right는 AR 세션 시작 시 디바이스 방향에 고정되므로 사용 불가.
        // 대신 boundary 폴리곤 엣지의 실제 방향을 길이 가중 평균하여 벽 방향을 추정한다.
        // (1) 가장 긴 엣지를 fold 기준으로 삼아 180° 대칭 문제를 해결
        // (2) 모든 엣지를 같은 반공간으로 fold한 뒤 길이 가중 평균 → 지배적 벽 방향
        float maxEdgeLen = 0f;
        Vector3 refDir = Vector3.zero;
        foreach (var plane in planeManager.trackables)
        {
            if (plane.alignment != UnityEngine.XR.ARSubsystems.PlaneAlignment.HorizontalUp) continue;
            if (Mathf.Abs(plane.transform.position.y - currentFloorY) > 0.5f) continue;
            var boundary = plane.boundary;
            int cnt = boundary.Length;
            for (int i = 0; i < cnt; i++)
            {
                Vector3 wA = plane.transform.TransformPoint(new Vector3(boundary[i].x, 0, boundary[i].y));
                Vector3 wB = plane.transform.TransformPoint(new Vector3(boundary[(i + 1) % cnt].x, 0, boundary[(i + 1) % cnt].y));
                Vector3 edge = wB - wA; edge.y = 0f;
                float len = edge.magnitude;
                if (len > maxEdgeLen) { maxEdgeLen = len; refDir = edge.normalized; }
            }
        }
        if (maxEdgeLen > 0.1f)
        {
            Vector3 weightedDir = Vector3.zero;
            foreach (var plane in planeManager.trackables)
            {
                if (plane.alignment != UnityEngine.XR.ARSubsystems.PlaneAlignment.HorizontalUp) continue;
                if (Mathf.Abs(plane.transform.position.y - currentFloorY) > 0.5f) continue;
                var boundary = plane.boundary;
                int cnt = boundary.Length;
                for (int i = 0; i < cnt; i++)
                {
                    Vector3 wA = plane.transform.TransformPoint(new Vector3(boundary[i].x, 0, boundary[i].y));
                    Vector3 wB = plane.transform.TransformPoint(new Vector3(boundary[(i + 1) % cnt].x, 0, boundary[(i + 1) % cnt].y));
                    Vector3 edge = wB - wA; edge.y = 0f;
                    float len = edge.magnitude;
                    if (len < 0.05f) continue;
                    Vector3 d = edge.normalized;
                    if (Vector3.Dot(d, refDir) < 0f) d = -d; // fold to same half-space
                    weightedDir += d * len;
                }
            }
            if (weightedDir.sqrMagnitude > 0.001f)
                bestForward = weightedDir.normalized;
        }

        // --- Pass 2: bestForward OBB 기준으로 모든 바닥 경계점 범위 계산 ---
        Quaternion obbRot = Quaternion.LookRotation(bestForward);
        float minX = float.MaxValue, maxX = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;
        bool anyPoint = false;
        foreach (var plane in planeManager.trackables)
        {
            if (plane.alignment != UnityEngine.XR.ARSubsystems.PlaneAlignment.HorizontalUp) continue;
            if (Mathf.Abs(plane.transform.position.y - currentFloorY) > 0.5f) continue;
            foreach (Vector2 pt in plane.boundary)
            {
                Vector3 wPt = plane.transform.TransformPoint(new Vector3(pt.x, 0, pt.y));
                Vector3 localPt = Quaternion.Inverse(obbRot) * wPt;
                if (localPt.x < minX) minX = localPt.x;
                if (localPt.x > maxX) maxX = localPt.x;
                if (localPt.z < minZ) minZ = localPt.z;
                if (localPt.z > maxZ) maxZ = localPt.z;
                anyPoint = true;
            }
        }
        if (!anyPoint) return;

        Vector3[] corners = new Vector3[4];
        corners[0] = obbRot * new Vector3(minX, 0, maxZ);
        corners[1] = obbRot * new Vector3(maxX, 0, maxZ);
        corners[2] = obbRot * new Vector3(maxX, 0, minZ);
        corners[3] = obbRot * new Vector3(minX, 0, minZ);

        for (int i = 0; i < 4; i++) corners[i].y = currentFloorY;

        lineRenderer.positionCount = 0;

        if (previewMeshObj != null) previewMeshObj.SetActive(true);
        UpdatePreviewMesh(new List<Vector3>(corners));
    }

    // 바닥과 천장을 동시에 추적하는 로직
    private void UpdateFloorDetection()
    {
        // 항상 모든 평면 목록을 순회하여 최솟값(바닥) 및 최댓값(천장) 파악
        // → ARRaycast가 침대 등 높은 면을 가리켜도 실제 바닥 평면 Y를 놓치지 않기 위함
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

        // ARRaycast: 카메라 정중앙 방향의 정밀한 Y 반환
        // 단, 이미 알려진 바닥 평면보다 현저히 높은 면(침대 등, +15cm 초과)에 히트하면 무시
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
                    // ARRaycast 결과와 평면 목록 최솟값 중 낮은 값 채택
                    currentFloorY = foundFloor ? Mathf.Min(detectedY, planeFloorY) : detectedY;
                    isFloorDetected = true;
                    return;
                }
            }
        }

        // ARRaycast 미성공 또는 높은 면 히트: 평면 목록 최솟값으로 폴백
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

        // 카메라가 아래를 볼 때 (바닥)
        if (ray.direction.y < 0)
        {
            // 1순위: ARRaycastManager로 실제 감지된 AR 평면에 투영 (수학 평면보다 정확)
            if (raycastManager != null &&
                raycastManager.Raycast(screenCenter, arHits, UnityEngine.XR.ARSubsystems.TrackableType.PlaneWithinPolygon))
            {
                Vector3 hitPos = arHits[0].pose.position;
                // 현재 바닥보다 현저히 높은 면(침대·가구 등, +15cm 초과)에 히트 시 무시하고 수학 평면 폴백
                if (!isFloorDetected || hitPos.y <= currentFloorY + 0.15f)
                {
                    currentFloorY = hitPos.y; // ARRaycast 결과로 바닥 Y 동적 보정 (Camera Y Offset 문제 해결)
                    success = true;
                    return hitPos;
                }
            }

            // 2순위: AR 평면 미감지 시 수학 평면 폴백
            Plane mathFloor = new Plane(Vector3.up, new Vector3(0, currentFloorY, 0));
            if (mathFloor.Raycast(ray, out float enterDistance))
            {
                success = true;
                return ray.GetPoint(enterDistance);
            }
        }
        // 카메라가 위를 볼 때 (천장) — 수학 평면으로 투영 후 바닥으로 끌어내림
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

    // 실시간 프리뷰 (고무줄 라인 & 면적)
    private void UpdateRealtimePreview(Vector3 currentAimPos)
    {
        // 1. 점과 십자선을 잇는 선 그리기
        lineRenderer.positionCount = cornerPoints.Count + 1;
        lineRenderer.loop = false; // 진행 중이므로 루프 해제
        for (int i = 0; i < cornerPoints.Count; i++)
        {
            lineRenderer.SetPosition(i, cornerPoints[i]);
        }
        lineRenderer.SetPosition(cornerPoints.Count, currentAimPos);

        // 2. 면적 칠하기 (점이 2개 이상 모여서 '면'을 이룰 수 있을 때만 활성화)
        if (cornerPoints.Count >= 2)
        {
            if (previewMeshObj != null) previewMeshObj.SetActive(true);
            List<Vector3> previewVertices = new List<Vector3>(cornerPoints);
            previewVertices.Add(currentAimPos);
            UpdatePreviewMesh(previewVertices);
        }
        else
        {
            // 점이 1개일 때는 면적을 숨겨 시야를 넓게 확보함
            if (previewMeshObj != null) previewMeshObj.SetActive(false);
        }
    }

    // 점 추가 및 UI
    public void AddCornerPoint()
    {
        if (isRoomFinished || !isFloorDetected)
        {
            ShowUI(false);
            return;
        }

        // 코루틴을 호출하여 시간차를 두고 인식 시작
        StartCoroutine(AddCornerPointRoutine());
    }

    private IEnumerator AddCornerPointRoutine()
    {
        // 1. 첫 점을 찍는 순간이라면 즉시 OBB 연산과 렌더링을 차단
        if (!isManualMode)
        {
            isManualMode = true; // Update문에서 OBB가 다시 그려지는 것을 막음
            lineRenderer.positionCount = 0;
            if (previewMeshObj != null) previewMeshObj.SetActive(false);

            // 2. 한 프레임(약 0.016초) 대기하여 유니티가 OBB 데이터를 화면과 메모리에서 완전히 지우게 함
            yield return null;
        }

        // 3. 간섭이 완전히 사라진 깨끗한 상태에서 십자선 위치 계산 및 보정 진행
        Vector3 hitPos = GetCrosshairPosition(out bool hitSuccess);

        if (hitSuccess)
        {
            hitPos = ApplySnappingLogic(hitPos);

            cornerPoints.Add(hitPos);

            if (cornerMarkerPrefab != null)
            {
                GameObject marker = Instantiate(cornerMarkerPrefab, hitPos, Quaternion.identity);
                marker.transform.SetParent(roomContainer.transform, true);
                cornerMarkers.Add(marker);
            }

            ShowUI(true);
        }
        else
        {
            ShowUI(false);
        }
    }

    private Vector3 ApplySnappingLogic(Vector3 targetPos)
    {
        Vector3 finalPos = targetPos;

        // 직각/평행 보정: 사용자가 직접 찍은 이전 점 기준으로만 적용 (신뢰 가능)
        // Magnetic Snap(AR 경계 기반)은 AR 평면 경계가 실제 벽 모서리와 다르므로 제거됨
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

    // 2점 기반 자동 완성 (Auto Complete Rectangle)
    public void AutoCompleteRectangle()
    {
        if (cornerPoints.Count != 2)
        {
            Debug.LogWarning("자동 완성은 점이 정확히 2개 찍혀 있을 때만 가능합니다.");
            return;
        }

        Vector3 p0 = cornerPoints[0];
        Vector3 p1 = cornerPoints[1];

        // 1. 찍어둔 벽의 방향과 직각(90도) 방향 계산
        Vector3 dir = (p1 - p0).normalized;
        Vector3 perpendicular = Vector3.Cross(dir, Vector3.up).normalized;

        // 2. AR 바닥 데이터를 뒤져서 "이 방이 직각 방향으로 얼마나 깊은가?"를 자동 추정
        float maxDepth = 2.0f; // 기본 깊이 2m
        float maxDot = 0f;

        foreach (var plane in planeManager.trackables)
        {
            if (plane.alignment != UnityEngine.XR.ARSubsystems.PlaneAlignment.HorizontalUp) continue;

            foreach (Vector2 bound in plane.boundary)
            {
                Vector3 worldBound = plane.transform.TransformPoint(new Vector3(bound.x, 0, bound.y));
                worldBound.y = currentFloorY;

                // 직각 방향으로 얼마나 멀리 뻗어있는지(투영) 계산
                float dot = Vector3.Dot(worldBound - p0, perpendicular);
                if (Mathf.Abs(dot) > maxDot)
                {
                    maxDot = Mathf.Abs(dot);
                    maxDepth = dot; // 부호(방향) 유지
                }
            }
        }

        // 3. 남은 2개의 점(p2, p3)을 자동 계산하여 리스트에 강제 삽입
        Vector3 p2 = p1 + (perpendicular * maxDepth);
        Vector3 p3 = p0 + (perpendicular * maxDepth);

        cornerPoints.Add(p2);
        cornerPoints.Add(p3);

        // 4. 바로 방 완성!
        FinishRoom();
    }

    // 바닥과 천장을 동시에 추적하는 로직 (안정성 강화)
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

        if(isRoomFinished)
        {
            Debug.Log("이미 방이 완성되었습니다.");
            return;
        }

        isRoomFinished = true;
        lineRenderer.loop = true;

        for (int i = 0; i < cornerPoints.Count; i++)
        {
            Vector3 p1 = cornerPoints[i];
            Vector3 p2 = cornerPoints[(i + 1) % cornerPoints.Count];
            CreateWallSegment(p1, p2, i);
        }

        CreateCustomFloorMesh();

        // 방 완성이 끝나면 안내선과 프리뷰 메쉬off
        lineRenderer.enabled = false;
        if (previewMeshObj != null) previewMeshObj.SetActive(false);

        foreach (var marker in cornerMarkers) marker.SetActive(false);

        Debug.Log("방 및 바닥 생성 완료!");
    }

    // 사용자가 찍은 점들을 바탕으로 다각형 바닥을 생성하는 함수
    private void CreateCustomFloorMesh()
    {
        // 1. 바닥 오브젝트 생성
        GameObject floorObj = new GameObject("CustomFloor");
        floorObj.layer = LayerMask.NameToLayer("Floor");
        floorObj.transform.SetParent(roomContainer.transform);

        // [핵심 수정] 바닥 점들의 실제 중심점(Center) 계산 후 오브젝트 이동
        Vector3 centerPos = Vector3.zero;
        for (int i = 0; i < cornerPoints.Count; i++)
        {
            centerPos += cornerPoints[i];
        }
        centerPos /= cornerPoints.Count;
        floorObj.transform.position = centerPos; // 허공(0,0,0)에서 바닥 중심으로 이동!

        MeshFilter meshFilter = floorObj.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = floorObj.AddComponent<MeshRenderer>();

        if (floorMaterial != null)
        {
            meshRenderer.material = floorMaterial;
        }

        // 2. Mesh 데이터 준비
        Mesh floorMesh = new Mesh();
        Vector3[] vertices = new Vector3[cornerPoints.Count];
        Vector2[] uvs = new Vector2[cornerPoints.Count];

        for (int i = 0; i < cornerPoints.Count; i++)
        {
            // [핵심 수정] 월드 좌표에서 중심점을 빼서 순수 '로컬 좌표'로 변환
            vertices[i] = cornerPoints[i] - centerPos;

            // UV 설정 (바닥 텍스처가 깨지지 않게 기존 월드 X, Z 좌표를 UV로 사용)
            uvs[i] = new Vector2(cornerPoints[i].x, cornerPoints[i].z);
        }

        // 3. 다각형 삼각분할 (Triangulation)
        List<int> triangles = new List<int>();
        for (int i = 1; i < vertices.Length - 1; i++)
        {
            triangles.Add(0); triangles.Add(i); triangles.Add(i + 1);
            triangles.Add(0); triangles.Add(i + 1); triangles.Add(i); // 양면
        }

        // 4. Mesh에 데이터 적용
        floorMesh.vertices = vertices;
        floorMesh.triangles = triangles.ToArray();
        floorMesh.uv = uvs;
        floorMesh.RecalculateNormals();

        meshFilter.mesh = floorMesh;

        // 지진 시뮬레이션 중 가구가 바닥 위에 서 있도록 Collider 추가
        MeshCollider collider = floorObj.AddComponent<MeshCollider>();
        collider.sharedMesh = floorMesh;
    }

    private void CreateWallSegment(Vector3 start, Vector3 end, int index)
    {
        Vector3 center = (start + end) / 2f;
        center.y += (ceilingHeight / 2f); // 벽 높이의 절반만큼 올림

        float length = Vector3.Distance(start, end);

        Vector3 direction = (end - start).normalized;
        Quaternion rotation = Quaternion.LookRotation(direction);

        GameObject wall = Instantiate(wallPrefab, center, rotation, roomContainer.transform);
        wall.name = "Wall_" + index;

        // Z축이 Forward(길이) 방향이 되도록 설정
        wall.transform.localScale = new Vector3(wallThickness, ceilingHeight, length);
    }

    public void UndoLastPoint()
    {
        if (isRoomFinished || cornerPoints.Count == 0) return;

        cornerPoints.RemoveAt(cornerPoints.Count - 1);

        GameObject lastMarker = cornerMarkers[cornerMarkers.Count - 1];
        cornerMarkers.RemoveAt(cornerMarkers.Count - 1);
        Destroy(lastMarker);

        UpdateLineRenderer();
        if (cornerPoints.Count == 0)
        {
            isManualMode = false; 
            if (anchoredObbObj != null) anchoredObbObj.SetActive(false); // 1점 앵커링 OBB 숨기기
        }
        else if (cornerPoints.Count == 1)
        {
            if (previewMeshObj != null) previewMeshObj.SetActive(false); // 다각형 메쉬 숨기기
        }
    }

    private void UpdatePreviewMesh(List<Vector3> vertices)
    {
        if (vertices == null || vertices.Count < 3) return;

        // [핵심 수정] 프리뷰의 중심점 계산 후 이동
        Vector3 centerPos = Vector3.zero;
        for (int i = 0; i < vertices.Count; i++)
        {
            centerPos += vertices[i];
        }
        centerPos /= vertices.Count;
        previewMeshObj.transform.position = centerPos;

        Mesh mesh = new Mesh();
        Vector3[] verts = new Vector3[vertices.Count];
        List<int> triangles = new List<int>();

        for (int i = 0; i < vertices.Count; i++)
        {
            // 월드 좌표 -> 로컬 좌표 변환
            verts[i] = vertices[i] - centerPos;
        }

        for (int i = 1; i < verts.Length - 1; i++)
        {
            triangles.Add(0); triangles.Add(i); triangles.Add(i + 1);
            triangles.Add(0); triangles.Add(i + 1); triangles.Add(i); // 양면
        }

        mesh.vertices = verts;
        mesh.triangles = triangles.ToArray();
        mesh.RecalculateNormals();
        previewMeshFilter.mesh = mesh;
    }

    // 1점 앵커링: 첫 점을 기준으로 예상 방 모양(OBB)을 끌어다 보여줌
    private void DrawAnchoredOBB(Vector3 anchorPoint)
    {
        Vector3 bestForward = Vector3.forward;
        float maxArea = 0f;
        ARPlane mainPlane = null;

        // 1. 기준 평면 및 방향 계산
        foreach (var plane in planeManager.trackables)
        {
            if (plane.alignment == UnityEngine.XR.ARSubsystems.PlaneAlignment.HorizontalUp)
            {
                float area = plane.extents.x * plane.extents.y;
                if (area > maxArea) { maxArea = area; mainPlane = plane; }
            }
        }

        if (mainPlane == null)
        {
            if (anchoredObbObj != null) anchoredObbObj.SetActive(false);
            return;
        }

        // --- Pass 1: 경계 엣지 가중 평균으로 벽 방향 추출 (UpdateOBBPreview와 동일) ---
        float maxEdgeLen = 0f;
        Vector3 refDir = Vector3.zero;
        foreach (var plane in planeManager.trackables)
        {
            if (plane.alignment != UnityEngine.XR.ARSubsystems.PlaneAlignment.HorizontalUp) continue;
            if (Mathf.Abs(plane.transform.position.y - currentFloorY) > 0.5f) continue;
            var boundary = plane.boundary;
            int cnt = boundary.Length;
            for (int i = 0; i < cnt; i++)
            {
                Vector3 wA = plane.transform.TransformPoint(new Vector3(boundary[i].x, 0, boundary[i].y));
                Vector3 wB = plane.transform.TransformPoint(new Vector3(boundary[(i + 1) % cnt].x, 0, boundary[(i + 1) % cnt].y));
                Vector3 edge = wB - wA; edge.y = 0f;
                float len = edge.magnitude;
                if (len > maxEdgeLen) { maxEdgeLen = len; refDir = edge.normalized; }
            }
        }
        if (maxEdgeLen > 0.1f)
        {
            Vector3 weightedDir = Vector3.zero;
            foreach (var plane in planeManager.trackables)
            {
                if (plane.alignment != UnityEngine.XR.ARSubsystems.PlaneAlignment.HorizontalUp) continue;
                if (Mathf.Abs(plane.transform.position.y - currentFloorY) > 0.5f) continue;
                var boundary = plane.boundary;
                int cnt = boundary.Length;
                for (int i = 0; i < cnt; i++)
                {
                    Vector3 wA = plane.transform.TransformPoint(new Vector3(boundary[i].x, 0, boundary[i].y));
                    Vector3 wB = plane.transform.TransformPoint(new Vector3(boundary[(i + 1) % cnt].x, 0, boundary[(i + 1) % cnt].y));
                    Vector3 edge = wB - wA; edge.y = 0f;
                    float len = edge.magnitude;
                    if (len < 0.05f) continue;
                    Vector3 d = edge.normalized;
                    if (Vector3.Dot(d, refDir) < 0f) d = -d;
                    weightedDir += d * len;
                }
            }
            if (weightedDir.sqrMagnitude > 0.001f)
                bestForward = weightedDir.normalized;
        }

        // 2. 전체 방(OBB) 4개 모서리 도출
        Quaternion obbRot = Quaternion.LookRotation(bestForward);
        float minX = float.MaxValue, maxX = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;
        foreach (var plane in planeManager.trackables)
        {
            if (plane.alignment != UnityEngine.XR.ARSubsystems.PlaneAlignment.HorizontalUp) continue;
            if (Mathf.Abs(plane.transform.position.y - currentFloorY) > 0.5f) continue;
            foreach (Vector2 pt in plane.boundary)
            {
                Vector3 wPt = plane.transform.TransformPoint(new Vector3(pt.x, 0, pt.y));
                Vector3 localPt = Quaternion.Inverse(obbRot) * wPt;
                if (localPt.x < minX) minX = localPt.x;
                if (localPt.x > maxX) maxX = localPt.x;
                if (localPt.z < minZ) minZ = localPt.z;
                if (localPt.z > maxZ) maxZ = localPt.z;
            }
        }

        Vector3[] corners = new Vector3[4];
        corners[0] = obbRot * new Vector3(minX, 0, maxZ);
        corners[1] = obbRot * new Vector3(maxX, 0, maxZ);
        corners[2] = obbRot * new Vector3(maxX, 0, minZ);
        corners[3] = obbRot * new Vector3(minX, 0, minZ);

        for (int i = 0; i < 4; i++) corners[i].y = currentFloorY;

        // 3. [핵심 로직] 내가 찍은 1번 점(앵커)에 OBB를 자석처럼 착! 붙이기
        int closestIdx = 0;
        float minDist = float.MaxValue;
        for (int i = 0; i < 4; i++)
        {
            float dist = Vector3.Distance(corners[i], anchorPoint);
            if (dist < minDist) { minDist = dist; closestIdx = i; }
        }

        // 가장 가까운 모서리가 앵커 포인트와 일치하도록 OBB 전체를 밀어줌(Offset)
        Vector3 offset = anchorPoint - corners[closestIdx];
        for (int i = 0; i < 4; i++) corners[i] += offset;

        // 4. 면(Mesh) 렌더링
        if (anchoredObbObj != null)
        {
            anchoredObbObj.SetActive(true);

            // [핵심 수정] 앵커링 OBB의 중심점 계산 후 이동
            Vector3 centerPos = Vector3.zero;
            for (int i = 0; i < 4; i++)
            {
                centerPos += corners[i];
            }
            centerPos /= 4f;
            anchoredObbObj.transform.position = centerPos;

            // 월드 좌표 -> 로컬 좌표 변환
            Vector3[] localCorners = new Vector3[4];
            for (int i = 0; i < 4; i++)
            {
                localCorners[i] = corners[i] - centerPos;
            }

            Mesh mesh = new Mesh();
            List<int> triangles = new List<int>();
            for (int i = 1; i < 3; i++)
            {
                triangles.Add(0); triangles.Add(i); triangles.Add(i + 1);
                triangles.Add(0); triangles.Add(i + 1); triangles.Add(i); // 양면
            }
            mesh.vertices = localCorners;
            mesh.triangles = triangles.ToArray();
            mesh.RecalculateNormals();
            anchoredObbFilter.mesh = mesh;
        }
    }

    private void ShowUI(bool isSuccess)
    {
        if (uiCoroutine != null) StopCoroutine(uiCoroutine);
        if (isSuccess)
        {
            uiText.text = "Point Added";
        }
        else
        {
            uiText.text = "Failed to Add Point";
        }
        uiCoroutine = StartCoroutine(PointUICoroutine());
    }

    private IEnumerator PointUICoroutine()
    {
        PointAlertUIPanel.anchoredPosition = uiOriginPos;

        Vector2 targetPos = uiOriginPos - new Vector2(0, uiDisplayMovementDistance);

        while (Vector2.Distance(PointAlertUIPanel.anchoredPosition, targetPos) > 0.1f)
        {
            PointAlertUIPanel.anchoredPosition = Vector2.Lerp(PointAlertUIPanel.anchoredPosition, targetPos, Time.deltaTime * 5f);
            yield return null;
        }

        PointAlertUIPanel.anchoredPosition = targetPos;

        yield return new WaitForSecondsRealtime(0.5f);

        while (Vector2.Distance(PointAlertUIPanel.anchoredPosition, uiOriginPos) > 0.1f)
        {
            PointAlertUIPanel.anchoredPosition = Vector2.Lerp(PointAlertUIPanel.anchoredPosition, uiOriginPos, Time.deltaTime * 5f);
            yield return null;
        }

        PointAlertUIPanel.anchoredPosition = uiOriginPos;
    }
}
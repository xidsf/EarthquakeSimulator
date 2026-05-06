using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.MixedReality.SceneUnderstanding;
using UnityEngine;
using UnityEngine.Events;

using NumVector3 = System.Numerics.Vector3;
using NumMatrix4x4 = System.Numerics.Matrix4x4;
using UVector2 = UnityEngine.Vector2;
using UVector3 = UnityEngine.Vector3;
using UMatrix4x4 = UnityEngine.Matrix4x4;

#if MSFT_OPENXR
using Microsoft.MixedReality.OpenXR;
#endif

public partial class SceneUnderstandingRoomScanner : MonoBehaviour
{
    [Header("Scan")]
    public bool autoUpdate = true;
    public bool lockRoom = false;
    public float updateIntervalSeconds = 2.0f;
    public float queryRadiusMeters = 8.0f;
    public SceneMeshLevelOfDetail meshLevelOfDetail = SceneMeshLevelOfDetail.Medium;

    [Header("Room Reconstruction")]
    public float minWallHeightMeters = 1.2f;
    public float minWallLengthMeters = 0.5f;
    public float maxCloseGapMeters = 0.45f;
    public bool createClosureEdges = true;
    public bool showClosureEdges = true;

    [Header("Visualization")]
    public Transform sceneRoot;
    public Material wallMaterial;
    public Material floorMaterial;
    public Material ceilingMaterial;
    public Material closureEdgeMaterial;
    public bool showRawSceneMeshes = true;
    public bool showDetectedWallSegments = true;

    [Header("Runtime Debug")]
    public bool sceneRootAligned;
    public string sceneRootAlignStatus;
    public float lastComputeTimeSeconds;
    public float lastCameraY;
    public float lastFloorWorldY;
    public float lastEyeToFloor;

    [Header("Events")]
    public UnityEvent onSceneUpdated;
    public UnityEvent onRoomLocked;
    public UnityEvent onRoomUnlocked;

    public Scene currentScene { get; private set; }
    public RoomBuildResult currentRoom { get; private set; }

    private bool isRunning;
    private bool isComputing;
    private Scene previousScene;
    private GameObject rawRoot;
    private GameObject roomRoot;

    public Transform RawMeshRootTransform => rawRoot != null ? rawRoot.transform : null;

    private readonly List<ExtractedSceneMesh> extractedSceneMeshes = new List<ExtractedSceneMesh>();

    private class ExtractedSceneMesh
    {
        public SceneObjectKind kind;
        public MeshFilter meshFilter;
    }

    private async void Start()
    {
        if (sceneRoot == null)
        {
            sceneRoot = new GameObject("SceneUnderstandingRoot").transform;
        }

        await InitializeSceneUnderstanding();

        if (autoUpdate)
        {
            StartScanning();
        }
    }

    public void StartScanning()
    {
        if (isRunning)
        {
            return;
        }

        isRunning = true;
        StartCoroutine(UpdateLoop());
    }

    public void StopScanning()
    {
        isRunning = false;
    }

    public void LockRoom()
    {
        lockRoom = true;
        onRoomLocked?.Invoke();
    }

    public void UnlockRoom()
    {
        lockRoom = false;
        onRoomUnlocked?.Invoke();
    }

    public async void ForceUpdate()
    {
        await ComputeAndDisplayScene();
    }

    private IEnumerator UpdateLoop()
    {
        while (isRunning)
        {
            if (!lockRoom && !isComputing)
            {
                _ = ComputeAndDisplayScene();
            }

            yield return new WaitForSeconds(updateIntervalSeconds);
        }
    }

    private async Task InitializeSceneUnderstanding()
    {
        if (!SceneObserver.IsSupported())
        {
            Debug.LogError("[SU] Scene Understanding is not supported on this device/runtime.");
            return;
        }

        SceneObserverAccessStatus accessStatus = await SceneObserver.RequestAccessAsync();
        Debug.Log($"[SU] Access status: {accessStatus}");
    }

    private async Task ComputeAndDisplayScene()
    {
        if (isComputing)
        {
            return;
        }

        isComputing = true;
        float startTime = Time.realtimeSinceStartup;

        try
        {
            SceneQuerySettings settings = new SceneQuerySettings
            {
                EnableSceneObjectQuads = true,
                EnableSceneObjectMeshes = true,
                EnableWorldMesh = true,
                EnableOnlyObservedSceneObjects = false,
                RequestedMeshLevelOfDetail = meshLevelOfDetail
            };

            Scene newScene;

            if (previousScene != null)
            {
                newScene = await SceneObserver.ComputeAsync(settings, queryRadiusMeters, previousScene);
            }
            else
            {
                newScene = await SceneObserver.ComputeAsync(settings, queryRadiusMeters);
            }

            lastComputeTimeSeconds = Time.realtimeSinceStartup - startTime;

            if (newScene == null)
            {
                Debug.LogWarning("[SU] Computed scene is null.");
                return;
            }

            previousScene = newScene;
            currentScene = newScene;

            if (!TryAlignSceneRoot(newScene))
            {
                Debug.LogWarning("[SU] Failed to align scene objects.");
            }

            ClearChildren();

            // 중요:
            // raw mesh는 항상 생성한다.
            // 그래야 실제 렌더링된 MeshFilter 기준으로 floor / ceiling / wall segment를 계산할 수 있다.
            DisplayRawSceneObjects(newScene);
            SetRawMeshRenderersVisible(showRawSceneMeshes);

            currentRoom = BuildRoomFromExtractedMeshes();
            DisplayRoomBuildResult(currentRoom);

            UpdateCameraFloorDebugValues();
            LogSceneSummary(newScene, currentRoom);

            onSceneUpdated?.Invoke();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[SU] Compute failed: {ex}");
        }
        finally
        {
            isComputing = false;
        }
    }

    private bool TryAlignSceneRoot(Scene scene)
    {
        sceneRootAligned = false;
        sceneRootAlignStatus = "not tried";

        if (sceneRoot == null)
        {
            sceneRootAlignStatus = "sceneRoot is null";
            Debug.LogWarning("[SU] Scene root is null.");
            return false;
        }

#if MSFT_OPENXR
        if (scene == null)
        {
            sceneRootAlignStatus = "scene is null";
            Debug.LogWarning("[SU] Scene is null.");
            return false;
        }

        SpatialGraphNode node = SpatialGraphNode.FromStaticNodeId(scene.OriginSpatialGraphNodeId);

        if (node == null)
        {
            sceneRootAlignStatus = "SpatialGraphNode is null";
            Debug.LogWarning("[SU] Failed to create SpatialGraphNode from OriginSpatialGraphNodeId.");
            return false;
        }

        if (node.TryLocate(FrameTime.OnUpdate, out Pose pose))
        {
            sceneRoot.SetPositionAndRotation(pose.position, pose.rotation);

            sceneRootAligned = true;
            sceneRootAlignStatus =
                $"aligned pos:{pose.position.x:F2},{pose.position.y:F2},{pose.position.z:F2}";

            Debug.Log($"[SU] Scene root aligned. {sceneRootAlignStatus}");
            return true;
        }

        sceneRootAlignStatus = "TryLocate failed";
        Debug.LogWarning("[SU] SpatialGraphNode.TryLocate failed.");
        return false;
#else
        sceneRootAlignStatus = "MSFT_OPENXR not defined";
        Debug.LogWarning("[SU] MSFT_OPENXR is not defined. Scene root cannot be aligned through OpenXR SpatialGraphNode.");
        return false;
#endif
    }

    private void ClearChildren()
    {
        if (rawRoot != null)
        {
            Destroy(rawRoot);
        }

        if (roomRoot != null)
        {
            Destroy(roomRoot);
        }

        extractedSceneMeshes.Clear();

        rawRoot = new GameObject("Raw Scene Understanding Objects");
        rawRoot.transform.SetParent(sceneRoot, false);

        roomRoot = new GameObject("Reconstructed Room");
        roomRoot.transform.SetParent(sceneRoot, false);
    }

    private void DisplayRawSceneObjects(Scene scene)
    {
        foreach (SceneObject sceneObject in scene.SceneObjects)
        {
            bool exportableKind =
                sceneObject.Kind == SceneObjectKind.Wall ||
                sceneObject.Kind == SceneObjectKind.Floor ||
                sceneObject.Kind == SceneObjectKind.Ceiling ||
                sceneObject.Kind == SceneObjectKind.World ||
                sceneObject.Kind == SceneObjectKind.Platform ||
                sceneObject.Kind == SceneObjectKind.Unknown;

            if (!exportableKind)
            {
                continue;
            }

            GameObject objectRoot = new GameObject(sceneObject.Kind.ToString());
            objectRoot.transform.SetParent(rawRoot.transform, false);

            UMatrix4x4 objectMatrix =
                ConvertRightHandedMatrix4x4ToLeftHanded(sceneObject.GetLocationAsMatrix());

            SetUnityTransformFromMatrix4x4(objectRoot.transform, objectMatrix, true);

            Material mat = GetMaterialForKind(sceneObject.Kind);

            foreach (SceneMesh sceneMesh in sceneObject.Meshes)
            {
                Mesh unityMesh = GenerateUnityMesh(sceneMesh);

                if (unityMesh == null)
                {
                    continue;
                }

                GameObject meshObject = new GameObject(sceneObject.Kind + "_Mesh");
                meshObject.transform.SetParent(objectRoot.transform, false);

                MeshFilter meshFilter = meshObject.AddComponent<MeshFilter>();
                MeshRenderer meshRenderer = meshObject.AddComponent<MeshRenderer>();

                meshFilter.sharedMesh = unityMesh;
                meshRenderer.sharedMaterial = mat;
                meshRenderer.enabled = showRawSceneMeshes;

                extractedSceneMeshes.Add(new ExtractedSceneMesh
                {
                    kind = sceneObject.Kind,
                    meshFilter = meshFilter
                });
            }
        }
    }

    private void SetRawMeshRenderersVisible(bool visible)
    {
        if (rawRoot == null)
        {
            return;
        }

        MeshRenderer[] renderers = rawRoot.GetComponentsInChildren<MeshRenderer>(true);

        foreach (MeshRenderer renderer in renderers)
        {
            renderer.enabled = visible;
        }
    }

    private RoomBuildResult BuildRoomFromExtractedMeshes()
    {
        RoomBuildResult result = new RoomBuildResult();

        List<WallSegment> rawSegments = new List<WallSegment>();
        List<float> floorYValues = new List<float>();
        List<float> ceilingYValues = new List<float>();

        foreach (ExtractedSceneMesh extracted in extractedSceneMeshes)
        {
            List<UVector3> points = GetWorldVerticesFromMeshFilter(extracted.meshFilter);

            if (points.Count == 0)
            {
                continue;
            }

            if (extracted.kind == SceneObjectKind.Floor)
            {
                floorYValues.AddRange(points.Select(p => p.y));
            }
            else if (extracted.kind == SceneObjectKind.Ceiling)
            {
                ceilingYValues.AddRange(points.Select(p => p.y));
            }
            else if (extracted.kind == SceneObjectKind.Wall)
            {
                WallSegment? segment = TryExtractWallSegmentFromWorldPoints(points);

                if (segment.HasValue)
                {
                    WallSegment s = segment.Value;

                    if (s.length >= minWallLengthMeters && s.height >= minWallHeightMeters)
                    {
                        rawSegments.Add(s);
                    }
                }
            }
        }

        result.floorY = floorYValues.Count > 0 ? Median(floorYValues) : float.NaN;
        result.ceilingY = ceilingYValues.Count > 0 ? Median(ceilingYValues) : float.NaN;

        if (float.IsNaN(result.floorY) || float.IsNaN(result.ceilingY))
        {
            Debug.LogError("[Room] Failed to calculate floor or ceiling Y from rendered raw mesh vertices.");
            return result;
        }

        if (result.ceilingY <= result.floorY)
        {
            Debug.LogError(
                $"[Room] Invalid floor/ceiling order. " +
                $"floorY:{result.floorY:F2}, ceilingY:{result.ceilingY:F2}. " +
                $"This indicates a coordinate extraction or scene object classification issue."
            );
        }

        result.detectedWalls = MergeSimilarWallSegments(rawSegments);

        if (createClosureEdges)
        {
            result.closureEdges = BuildClosureEdges(
                result.detectedWalls,
                maxCloseGapMeters,
                result.floorY,
                result.ceilingY
            );
        }

        result.isClosedEnough = IsClosedEnough(
            result.detectedWalls,
            result.closureEdges,
            maxCloseGapMeters
        );

        return result;
    }

    // 이전 SimpleDebugUI 파일과의 호환성을 위해 유지.
    // 새 계산은 BuildRoomFromExtractedMeshes()가 기준이다.
    private RoomBuildResult BuildRoomFromScene(Scene scene)
    {
        if (scene == null)
        {
            return new RoomBuildResult();
        }

        if (extractedSceneMeshes.Count == 0)
        {
            DisplayRawSceneObjects(scene);
            SetRawMeshRenderersVisible(showRawSceneMeshes);
        }

        return BuildRoomFromExtractedMeshes();
    }

    private List<UVector3> GetWorldVerticesFromMeshFilter(MeshFilter meshFilter)
    {
        List<UVector3> points = new List<UVector3>();

        if (meshFilter == null || meshFilter.sharedMesh == null)
        {
            return points;
        }

        UVector3[] vertices = meshFilter.sharedMesh.vertices;
        Transform meshTransform = meshFilter.transform;

        for (int i = 0; i < vertices.Length; i++)
        {
            points.Add(meshTransform.TransformPoint(vertices[i]));
        }

        return points;
    }

    private WallSegment? TryExtractWallSegmentFromWorldPoints(List<UVector3> points)
    {
        if (points == null || points.Count < 4)
        {
            return null;
        }

        float minY = points.Min(p => p.y);
        float maxY = points.Max(p => p.y);
        float height = maxY - minY;

        List<UVector2> xz = points
            .Select(p => new UVector2(p.x, p.z))
            .ToList();

        UVector2 center = Average(xz);

        float xx = 0.0f;
        float xzCov = 0.0f;
        float zz = 0.0f;

        foreach (UVector2 p in xz)
        {
            UVector2 d = p - center;
            xx += d.x * d.x;
            xzCov += d.x * d.y;
            zz += d.y * d.y;
        }

        float angle = 0.5f * Mathf.Atan2(2.0f * xzCov, xx - zz);
        UVector2 dir = new UVector2(Mathf.Cos(angle), Mathf.Sin(angle)).normalized;

        float minT = float.MaxValue;
        float maxT = float.MinValue;

        foreach (UVector2 p in xz)
        {
            float t = UVector2.Dot(p - center, dir);
            minT = Mathf.Min(minT, t);
            maxT = Mathf.Max(maxT, t);
        }

        UVector2 a = center + dir * minT;
        UVector2 b = center + dir * maxT;

        return new WallSegment
        {
            start = a,
            end = b,
            center = center,
            bottomY = minY,
            topY = maxY,
            height = height,
            isClosure = false
        };
    }

    private List<WallSegment> MergeSimilarWallSegments(List<WallSegment> source)
    {
        List<WallSegment> result = new List<WallSegment>();

        foreach (WallSegment s in source)
        {
            bool duplicate = false;

            foreach (WallSegment existing in result)
            {
                if (UVector2.Distance(s.center, existing.center) < 0.25f &&
                    UVector2.Angle(s.direction, existing.direction) < 8.0f)
                {
                    duplicate = true;
                    break;
                }
            }

            if (!duplicate)
            {
                result.Add(s);
            }
        }

        return result;
    }

    private List<WallSegment> BuildClosureEdges(
        List<WallSegment> walls,
        float maxGap,
        float floorY,
        float ceilingY)
    {
        List<WallSegment> closures = new List<WallSegment>();

        if (walls.Count < 2)
        {
            return closures;
        }

        List<UVector2> endpoints = new List<UVector2>();

        foreach (WallSegment wall in walls)
        {
            endpoints.Add(wall.start);
            endpoints.Add(wall.end);
        }

        bool[] used = new bool[endpoints.Count];

        for (int i = 0; i < endpoints.Count; i++)
        {
            if (used[i])
            {
                continue;
            }

            int nearest = -1;
            float nearestDistance = float.MaxValue;

            for (int j = i + 1; j < endpoints.Count; j++)
            {
                if (used[j])
                {
                    continue;
                }

                float d = UVector2.Distance(endpoints[i], endpoints[j]);

                if (d < nearestDistance)
                {
                    nearestDistance = d;
                    nearest = j;
                }
            }

            if (nearest >= 0 && nearestDistance > 0.05f && nearestDistance <= maxGap)
            {
                used[i] = true;
                used[nearest] = true;

                closures.Add(new WallSegment
                {
                    start = endpoints[i],
                    end = endpoints[nearest],
                    center = (endpoints[i] + endpoints[nearest]) * 0.5f,
                    bottomY = floorY,
                    topY = ceilingY,
                    height = Mathf.Max(0.1f, ceilingY - floorY),
                    isClosure = true
                });
            }
        }

        return closures;
    }

    private bool IsClosedEnough(List<WallSegment> walls, List<WallSegment> closures, float maxGap)
    {
        if (walls.Count < 3)
        {
            return false;
        }

        List<UVector2> endpoints = new List<UVector2>();

        foreach (WallSegment wall in walls)
        {
            endpoints.Add(wall.start);
            endpoints.Add(wall.end);
        }

        foreach (WallSegment closure in closures)
        {
            endpoints.Add(closure.start);
            endpoints.Add(closure.end);
        }

        int openCount = 0;

        for (int i = 0; i < endpoints.Count; i++)
        {
            float nearest = float.MaxValue;

            for (int j = 0; j < endpoints.Count; j++)
            {
                if (i == j)
                {
                    continue;
                }

                nearest = Mathf.Min(nearest, UVector2.Distance(endpoints[i], endpoints[j]));
            }

            if (nearest > maxGap)
            {
                openCount++;
            }
        }

        return openCount <= 2;
    }

    private void DisplayRoomBuildResult(RoomBuildResult result)
    {
        if (result == null)
        {
            return;
        }

        if (showDetectedWallSegments)
        {
            foreach (WallSegment wall in result.detectedWalls)
            {
                CreateWallSegmentObject(wall, result.floorY, result.ceilingY, wallMaterial, "DetectedWall");
            }
        }

        if (showClosureEdges)
        {
            foreach (WallSegment edge in result.closureEdges)
            {
                CreateWallSegmentObject(edge, result.floorY, result.ceilingY, closureEdgeMaterial, "ClosureEdge");
            }
        }
    }

    private void CreateWallSegmentObject(
        WallSegment segment,
        float floorY,
        float ceilingY,
        Material material,
        string name)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;

        UVector2 mid2 = (segment.start + segment.end) * 0.5f;
        float length = UVector2.Distance(segment.start, segment.end);
        float height = Mathf.Max(0.1f, ceilingY - floorY);

        UVector2 dir2 = (segment.end - segment.start).normalized;
        float yaw = Mathf.Atan2(dir2.x, dir2.y) * Mathf.Rad2Deg;

        go.transform.position = new UVector3(
            mid2.x,
            floorY + height * 0.5f,
            mid2.y
        );

        go.transform.rotation = Quaternion.Euler(0.0f, yaw, 0.0f);
        go.transform.localScale = new UVector3(0.04f, height, length);

        if (roomRoot != null)
        {
            go.transform.SetParent(roomRoot.transform, true);
        }

        MeshRenderer renderer = go.GetComponent<MeshRenderer>();

        if (renderer != null && material != null)
        {
            renderer.sharedMaterial = material;
        }

        if (segment.isClosure)
        {
            Collider col = go.GetComponent<Collider>();

            if (col != null)
            {
                Destroy(col);
            }
        }
    }

    private Mesh GenerateUnityMesh(SceneMesh sceneMesh)
    {
        if (sceneMesh == null)
        {
            return null;
        }

        int triangleIndexCount = (int)sceneMesh.TriangleIndexCount;
        int vertexCount = (int)sceneMesh.VertexCount;

        uint[] indices = new uint[triangleIndexCount];
        sceneMesh.GetTriangleIndices(indices);

        NumVector3[] positions = new NumVector3[vertexCount];
        sceneMesh.GetVertexPositions(positions);

        UVector3[] unityVertices = new UVector3[positions.Length];
        int[] unityIndices = new int[indices.Length];

        for (int i = 0; i < positions.Length; i++)
        {
            unityVertices[i] = new UVector3(
                positions[i].X,
                positions[i].Y,
                -positions[i].Z
            );
        }

        // 이전 방식:
        // triangle winding을 뒤집지 않는다.
        for (int i = 0; i < indices.Length; i++)
        {
            unityIndices[i] = (int)indices[i];
        }

        Mesh mesh = new Mesh();

        if (unityVertices.Length > 65535)
        {
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        }

        mesh.vertices = unityVertices;
        mesh.triangles = unityIndices;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        return mesh;
    }

    private Material GetMaterialForKind(SceneObjectKind kind)
    {
        switch (kind)
        {
            case SceneObjectKind.Wall:
                return wallMaterial;

            case SceneObjectKind.Floor:
                return floorMaterial;

            case SceneObjectKind.Ceiling:
                return ceilingMaterial;

            default:
                return wallMaterial;
        }
    }

    private void UpdateCameraFloorDebugValues()
    {
        Camera mainCamera = Camera.main;

        if (mainCamera != null)
        {
            lastCameraY = mainCamera.transform.position.y;
        }

        if (sceneRoot != null)
        {
            lastSceneRootY = sceneRoot.position.y;
        }

        if (currentRoom != null &&
            !float.IsNaN(currentRoom.floorY) &&
            !float.IsNaN(currentRoom.ceilingY))
        {
            lastFloorWorldY = currentRoom.floorY;
            lastCeilingWorldY = currentRoom.ceilingY;

            lastEyeToFloor = lastCameraY - lastFloorWorldY;
            lastEyeToCeiling = lastCeilingWorldY - lastCameraY;

            if (sceneRoot != null)
            {
                Vector3 floorWorldPoint = new Vector3(0.0f, currentRoom.floorY, 0.0f);
                Vector3 ceilingWorldPoint = new Vector3(0.0f, currentRoom.ceilingY, 0.0f);

                lastFloorLocalY = sceneRoot.InverseTransformPoint(floorWorldPoint).y;
                lastCeilingLocalY = sceneRoot.InverseTransformPoint(ceilingWorldPoint).y;
            }
        }
    }

    private void LogSceneSummary(Scene scene, RoomBuildResult room)
    {
        int wallCount = scene.SceneObjects.Count(o => o.Kind == SceneObjectKind.Wall);
        int floorCount = scene.SceneObjects.Count(o => o.Kind == SceneObjectKind.Floor);
        int ceilingCount = scene.SceneObjects.Count(o => o.Kind == SceneObjectKind.Ceiling);
        int platformCount = scene.SceneObjects.Count(o => o.Kind == SceneObjectKind.Platform);
        int worldCount = scene.SceneObjects.Count(o => o.Kind == SceneObjectKind.World);

        Debug.Log(
            $"[SU] Objects - Wall:{wallCount}, Floor:{floorCount}, Ceiling:{ceilingCount}, Platform:{platformCount}, World:{worldCount}\n" +
            $"[Room] Walls:{room.detectedWalls.Count}, ClosureEdges:{room.closureEdges.Count}, ClosedEnough:{room.isClosedEnough}, " +
            $"FloorY:{room.floorY:F2}, CeilingY:{room.ceilingY:F2}, Height:{(room.ceilingY - room.floorY):F2}, " +
            $"CameraY:{lastCameraY:F2}, EyeToFloor:{lastEyeToFloor:F2}, Compute:{lastComputeTimeSeconds:F2}s"
        );
    }

    private static float Median(List<float> values)
    {
        if (values == null || values.Count == 0)
        {
            return 0.0f;
        }

        values.Sort();
        int mid = values.Count / 2;

        if (values.Count % 2 == 0)
        {
            return (values[mid - 1] + values[mid]) * 0.5f;
        }

        return values[mid];
    }

    private static UVector2 Average(List<UVector2> points)
    {
        UVector2 sum = UVector2.zero;

        foreach (UVector2 p in points)
        {
            sum += p;
        }

        return sum / Mathf.Max(1, points.Count);
    }

    private static UMatrix4x4 ConvertRightHandedMatrix4x4ToLeftHanded(
        NumMatrix4x4 matrix)
    {
        UMatrix4x4 unityMatrix = new UMatrix4x4();

        unityMatrix.m00 = matrix.M11;
        unityMatrix.m01 = matrix.M21;
        unityMatrix.m02 = -matrix.M31;
        unityMatrix.m03 = matrix.M41;

        unityMatrix.m10 = matrix.M12;
        unityMatrix.m11 = matrix.M22;
        unityMatrix.m12 = -matrix.M32;
        unityMatrix.m13 = matrix.M42;

        unityMatrix.m20 = -matrix.M13;
        unityMatrix.m21 = -matrix.M23;
        unityMatrix.m22 = matrix.M33;
        unityMatrix.m23 = -matrix.M43;

        unityMatrix.m30 = matrix.M14;
        unityMatrix.m31 = matrix.M24;
        unityMatrix.m32 = -matrix.M34;
        unityMatrix.m33 = matrix.M44;

        return unityMatrix;
    }

    private static void SetUnityTransformFromMatrix4x4(
        Transform target,
        UMatrix4x4 matrix,
        bool local)
    {
        UVector3 position = matrix.GetColumn(3);
        Quaternion rotation = matrix.rotation;
        UVector3 scale = matrix.lossyScale;

        if (local)
        {
            target.localPosition = position;
            target.localRotation = rotation;
            target.localScale = scale;
        }
        else
        {
            target.SetPositionAndRotation(position, rotation);
            target.localScale = scale;
        }
    }
}

[Serializable]
public class RoomBuildResult
{
    public List<WallSegment> detectedWalls = new List<WallSegment>();
    public List<WallSegment> closureEdges = new List<WallSegment>();
    public float floorY;
    public float ceilingY;
    public bool isClosedEnough;
}

[Serializable]
public struct WallSegment
{
    public UVector2 start;
    public UVector2 end;
    public UVector2 center;
    public float bottomY;
    public float topY;
    public float height;
    public bool isClosure;

    public float length => UVector2.Distance(start, end);

    public UVector2 direction
    {
        get
        {
            UVector2 d = end - start;
            return d.sqrMagnitude > 0.0001f ? d.normalized : UVector2.right;
        }
    }
}
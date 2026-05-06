using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.MixedReality.SceneUnderstanding;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class SceneUnderstandingMeshObjExporter : MonoBehaviour
{
    public enum FullMeshSource
    {
        WorldOnly,
        AllRawSceneMeshes
    }

    public enum ResidualMeshMode
    {
        RemoveDetectedWallsOnly,
        RemoveRoomSurfaces
    }

    [Header("References")]
    public SceneUnderstandingRoomScanner scanner;

    [Tooltip("수동 벽을 나중에 추가하면 여기에 root를 넣으면 됨. 지금 없으면 비워도 됨.")]
    public Transform manualWallRoot;

    [Tooltip("비워두면 Unity world 좌표 기준으로 OBJ 저장.")]
    public Transform exportOrigin;

    [Header("Editor Export")]
    public bool askExportFolderInEditor = true;
    public string defaultExportFolderName = "SceneUnderstandingObjExports";

    [Header("File Names")]
    public string fullMeshFileName = "room_full_mesh.obj";
    public string residualMeshFileName = "room_without_room_surfaces.obj";

    [Header("Export Options")]
    public FullMeshSource fullMeshSource = FullMeshSource.WorldOnly;
    public ResidualMeshMode residualMeshMode = ResidualMeshMode.RemoveRoomSurfaces;

    [Header("Residual Filtering")]
    public float floorExcludeDistance = 0.10f;
    public float ceilingExcludeDistance = 0.10f;
    public float wallExcludeDistance = 0.12f;
    public float manualWallExcludeDistance = 0.12f;
    public float wallVerticalMargin = 0.15f;

    [Header("Debug")]
    public string lastExportFolder;
    public string lastFullMeshPath;
    public string lastResidualMeshPath;
    public int lastFullVertexCount;
    public int lastFullTriangleCount;
    public int lastResidualVertexCount;
    public int lastResidualTriangleCount;
    public string status;

    public void ExportBothObjForEditorTest()
    {
        if (!TryGetRawMeshFilters(out MeshFilter[] meshFilters))
        {
            return;
        }

        string folderPath = GetExportFolderPath();

        if (string.IsNullOrEmpty(folderPath))
        {
            status = "Export canceled.";
            Debug.LogWarning("[OBJ] Export canceled.");
            return;
        }

        Directory.CreateDirectory(folderPath);
        lastExportFolder = folderPath;

        ExportFullObj(meshFilters, folderPath);
        ExportResidualObj(meshFilters, folderPath);

#if UNITY_EDITOR
        EditorUtility.RevealInFinder(folderPath);
#endif
    }

    public void ExportFullObjOnly()
    {
        if (!TryGetRawMeshFilters(out MeshFilter[] meshFilters))
        {
            return;
        }

        string folderPath = GetExportFolderPath();

        if (string.IsNullOrEmpty(folderPath))
        {
            status = "Export canceled.";
            return;
        }

        Directory.CreateDirectory(folderPath);
        lastExportFolder = folderPath;

        ExportFullObj(meshFilters, folderPath);

#if UNITY_EDITOR
        EditorUtility.RevealInFinder(folderPath);
#endif
    }

    public void ExportResidualObjOnly()
    {
        if (!TryGetRawMeshFilters(out MeshFilter[] meshFilters))
        {
            return;
        }

        string folderPath = GetExportFolderPath();

        if (string.IsNullOrEmpty(folderPath))
        {
            status = "Export canceled.";
            return;
        }

        Directory.CreateDirectory(folderPath);
        lastExportFolder = folderPath;

        ExportResidualObj(meshFilters, folderPath);

#if UNITY_EDITOR
        EditorUtility.RevealInFinder(folderPath);
#endif
    }

    private void ExportFullObj(MeshFilter[] meshFilters, string folderPath)
    {
        string path = Path.Combine(folderPath, fullMeshFileName);

        ObjExportStats stats = ExportObj(
            meshFilters,
            path,
            IsFullMeshAllowed,
            null
        );

        lastFullMeshPath = path;
        lastFullVertexCount = stats.vertexCount;
        lastFullTriangleCount = stats.triangleCount;

        Debug.Log($"[OBJ] Full mesh exported: {path}, vertices:{stats.vertexCount}, triangles:{stats.triangleCount}");
    }

    private void ExportResidualObj(MeshFilter[] meshFilters, string folderPath)
    {
        if (scanner == null || scanner.currentRoom == null)
        {
            status = "Residual export failed: currentRoom is null.";
            Debug.LogError($"[OBJ] {status}");
            return;
        }

        string path = Path.Combine(folderPath, residualMeshFileName);

        ObjExportStats stats = ExportObj(
            meshFilters,
            path,
            IsResidualMeshAllowed,
            ShouldExportResidualTriangle
        );

        lastResidualMeshPath = path;
        lastResidualVertexCount = stats.vertexCount;
        lastResidualTriangleCount = stats.triangleCount;

        Debug.Log($"[OBJ] Residual mesh exported: {path}, vertices:{stats.vertexCount}, triangles:{stats.triangleCount}");
    }

    private bool TryGetRawMeshFilters(out MeshFilter[] meshFilters)
    {
        meshFilters = null;

        if (scanner == null)
        {
            status = "Export failed: scanner is null.";
            Debug.LogError($"[OBJ] {status}");
            return false;
        }

        Transform rawRoot = scanner.RawMeshRootTransform;

        if (rawRoot == null)
        {
            status = "Export failed: raw mesh root is null. Run Scene Understanding first.";
            Debug.LogError($"[OBJ] {status}");
            return false;
        }

        meshFilters = rawRoot.GetComponentsInChildren<MeshFilter>(true);

        if (meshFilters == null || meshFilters.Length == 0)
        {
            status = "Export failed: no mesh filters found.";
            Debug.LogError($"[OBJ] {status}");
            return false;
        }

        return true;
    }

    private string GetExportFolderPath()
    {
#if UNITY_EDITOR
        string defaultPath = Path.Combine(
            Directory.GetParent(Application.dataPath).FullName,
            defaultExportFolderName
        );

        if (!askExportFolderInEditor)
        {
            return defaultPath;
        }

        string selected = EditorUtility.SaveFolderPanel(
            "Select OBJ Export Folder",
            defaultPath,
            string.Empty
        );

        return selected;
#else
        return Path.Combine(Application.persistentDataPath, defaultExportFolderName);
#endif
    }

    private ObjExportStats ExportObj(
        MeshFilter[] meshFilters,
        string path,
        Func<MeshFilter, bool> meshFilterPredicate,
        Func<MeshFilter, Vector3, bool> trianglePredicate)
    {
        StringBuilder builder = new StringBuilder();

        builder.AppendLine("# Scene Understanding OBJ Export");
        builder.AppendLine("# Exported in Unity Editor Play Mode");
        builder.AppendLine("# Coordinates: Unity world space unless exportOrigin is set");
        builder.AppendLine();

        int vertexOffset = 1;
        int totalVertexCount = 0;
        int totalTriangleCount = 0;

        foreach (MeshFilter meshFilter in meshFilters)
        {
            if (meshFilter == null || meshFilter.sharedMesh == null)
            {
                continue;
            }

            if (meshFilterPredicate != null && !meshFilterPredicate(meshFilter))
            {
                continue;
            }

            Mesh mesh = meshFilter.sharedMesh;
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;

            StringBuilder objectBuilder = new StringBuilder();
            int objectVertexCount = 0;
            int objectTriangleCount = 0;

            objectBuilder.AppendLine($"o {SanitizeObjectName(GetObjObjectName(meshFilter))}");

            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector3 w0 = meshFilter.transform.TransformPoint(vertices[triangles[i]]);
                Vector3 w1 = meshFilter.transform.TransformPoint(vertices[triangles[i + 1]]);
                Vector3 w2 = meshFilter.transform.TransformPoint(vertices[triangles[i + 2]]);

                Vector3 centroid = (w0 + w1 + w2) / 3.0f;

                if (trianglePredicate != null && !trianglePredicate(meshFilter, centroid))
                {
                    continue;
                }

                Vector3 e0 = ConvertExportPoint(w0);
                Vector3 e1 = ConvertExportPoint(w1);
                Vector3 e2 = ConvertExportPoint(w2);

                objectBuilder.AppendLine($"v {e0.x:F6} {e0.y:F6} {e0.z:F6}");
                objectBuilder.AppendLine($"v {e1.x:F6} {e1.y:F6} {e1.z:F6}");
                objectBuilder.AppendLine($"v {e2.x:F6} {e2.y:F6} {e2.z:F6}");

                objectBuilder.AppendLine($"f {vertexOffset} {vertexOffset + 1} {vertexOffset + 2}");

                vertexOffset += 3;
                objectVertexCount += 3;
                objectTriangleCount++;
            }

            if (objectTriangleCount > 0)
            {
                builder.Append(objectBuilder);
                builder.AppendLine();

                totalVertexCount += objectVertexCount;
                totalTriangleCount += objectTriangleCount;
            }
        }

        File.WriteAllText(path, builder.ToString(), Encoding.UTF8);

        return new ObjExportStats
        {
            vertexCount = totalVertexCount,
            triangleCount = totalTriangleCount
        };
    }

    private bool IsFullMeshAllowed(MeshFilter meshFilter)
    {
        SceneObjectKind? kind = GuessKindFromHierarchy(meshFilter);

        if (!kind.HasValue)
        {
            return false;
        }

        switch (fullMeshSource)
        {
            case FullMeshSource.WorldOnly:
                return kind.Value == SceneObjectKind.World;

            case FullMeshSource.AllRawSceneMeshes:
                return true;

            default:
                return false;
        }
    }

    private bool IsResidualMeshAllowed(MeshFilter meshFilter)
    {
        SceneObjectKind? kind = GuessKindFromHierarchy(meshFilter);

        if (!kind.HasValue)
        {
            return false;
        }

        if (residualMeshMode == ResidualMeshMode.RemoveDetectedWallsOnly)
        {
            return kind.Value == SceneObjectKind.World;
        }

        if (residualMeshMode == ResidualMeshMode.RemoveRoomSurfaces)
        {
            return kind.Value == SceneObjectKind.World;
        }

        return false;
    }

    private bool ShouldExportResidualTriangle(MeshFilter meshFilter, Vector3 centroid)
    {
        if (residualMeshMode == ResidualMeshMode.RemoveDetectedWallsOnly)
        {
            if (IsNearDetectedWall(centroid))
            {
                return false;
            }

            if (IsNearManualWall(centroid))
            {
                return false;
            }

            return true;
        }

        if (residualMeshMode == ResidualMeshMode.RemoveRoomSurfaces)
        {
            if (IsNearFloor(centroid))
            {
                return false;
            }

            if (IsNearCeiling(centroid))
            {
                return false;
            }

            if (IsNearDetectedWall(centroid))
            {
                return false;
            }

            if (IsNearManualWall(centroid))
            {
                return false;
            }

            return true;
        }

        return false;
    }

    private bool IsNearFloor(Vector3 point)
    {
        if (scanner == null || scanner.currentRoom == null)
        {
            return false;
        }

        return Mathf.Abs(point.y - scanner.currentRoom.floorY) <= floorExcludeDistance;
    }

    private bool IsNearCeiling(Vector3 point)
    {
        if (scanner == null || scanner.currentRoom == null)
        {
            return false;
        }

        return Mathf.Abs(point.y - scanner.currentRoom.ceilingY) <= ceilingExcludeDistance;
    }

    private bool IsNearDetectedWall(Vector3 point)
    {
        if (scanner == null || scanner.currentRoom == null)
        {
            return false;
        }

        float minY = scanner.currentRoom.floorY - wallVerticalMargin;
        float maxY = scanner.currentRoom.ceilingY + wallVerticalMargin;

        if (point.y < minY || point.y > maxY)
        {
            return false;
        }

        Vector2 p = new Vector2(point.x, point.z);

        foreach (WallSegment wall in scanner.currentRoom.detectedWalls)
        {
            if (DistancePointToSegment(p, wall.start, wall.end) <= wallExcludeDistance)
            {
                return true;
            }
        }

        foreach (WallSegment edge in scanner.currentRoom.closureEdges)
        {
            if (DistancePointToSegment(p, edge.start, edge.end) <= wallExcludeDistance)
            {
                return true;
            }
        }

        return false;
    }

    private bool IsNearManualWall(Vector3 point)
    {
        if (manualWallRoot == null)
        {
            return false;
        }

        Collider[] colliders = manualWallRoot.GetComponentsInChildren<Collider>(true);

        foreach (Collider collider in colliders)
        {
            if (collider == null)
            {
                continue;
            }

            Vector3 closest = collider.ClosestPoint(point);
            float distance = Vector3.Distance(point, closest);

            if (distance <= manualWallExcludeDistance)
            {
                return true;
            }
        }

        return false;
    }

    private Vector3 ConvertExportPoint(Vector3 worldPoint)
    {
        if (exportOrigin != null)
        {
            return exportOrigin.InverseTransformPoint(worldPoint);
        }

        return worldPoint;
    }

    private static float DistancePointToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float abSqr = ab.sqrMagnitude;

        if (abSqr < 0.000001f)
        {
            return Vector2.Distance(p, a);
        }

        float t = Vector2.Dot(p - a, ab) / abSqr;
        t = Mathf.Clamp01(t);

        Vector2 projected = a + ab * t;
        return Vector2.Distance(p, projected);
    }

    private static string GetObjObjectName(MeshFilter meshFilter)
    {
        if (meshFilter == null)
        {
            return "mesh";
        }

        if (meshFilter.transform.parent != null)
        {
            return $"{meshFilter.transform.parent.name}_{meshFilter.name}";
        }

        return meshFilter.name;
    }

    private static string SanitizeObjectName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "mesh";
        }

        return name
            .Replace(" ", "_")
            .Replace("/", "_")
            .Replace("\\", "_")
            .Replace(":", "_")
            .Replace("(", "_")
            .Replace(")", "_");
    }

    private static SceneObjectKind? GuessKindFromHierarchy(MeshFilter meshFilter)
    {
        if (meshFilter == null)
        {
            return null;
        }

        SceneObjectKind? fromName = GuessKindFromName(meshFilter.name);

        if (fromName.HasValue)
        {
            return fromName.Value;
        }

        Transform parent = meshFilter.transform.parent;

        while (parent != null)
        {
            SceneObjectKind? fromParent = GuessKindFromName(parent.name);

            if (fromParent.HasValue)
            {
                return fromParent.Value;
            }

            parent = parent.parent;
        }

        return null;
    }

    private static SceneObjectKind? GuessKindFromName(string objectName)
    {
        if (string.IsNullOrEmpty(objectName))
        {
            return null;
        }

        if (objectName.StartsWith("Wall", StringComparison.OrdinalIgnoreCase))
        {
            return SceneObjectKind.Wall;
        }

        if (objectName.StartsWith("Floor", StringComparison.OrdinalIgnoreCase))
        {
            return SceneObjectKind.Floor;
        }

        if (objectName.StartsWith("Ceiling", StringComparison.OrdinalIgnoreCase))
        {
            return SceneObjectKind.Ceiling;
        }

        if (objectName.StartsWith("World", StringComparison.OrdinalIgnoreCase))
        {
            return SceneObjectKind.World;
        }

        if (objectName.StartsWith("Platform", StringComparison.OrdinalIgnoreCase))
        {
            return SceneObjectKind.Platform;
        }

        if (objectName.StartsWith("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            return SceneObjectKind.Unknown;
        }

        return null;
    }

    private struct ObjExportStats
    {
        public int vertexCount;
        public int triangleCount;
    }
}
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using GLTFast;
using UnityEngine;

public class SimulationLoader : MonoBehaviour
{
#if UNITY_EDITOR
    private const string EditorTestGlbAssetFolder = "Assets/TestGLB";
#endif

    [Header("Room Layout Root")]
    [SerializeField] private Transform roomParent;

    [Header("Prefabs (Optional)")]
    [SerializeField] private GameObject wallPrefab;
    [SerializeField] private Material wallMaterial;

    [Header("Editor Test Input")]
    [Tooltip("Editor 테스트용 JSON 파일 경로입니다. 폴더를 넣으면 simulation_input.json을 찾습니다.")]
    public string testJsonAssetPath = "Assets/Scripts/BT/ForTest/simulation_input.json";

    [Header("Model Alignment")]
    [SerializeField] private bool normalizeModelToGround = true;
    [SerializeField] private bool centerModelXZ = true;

    private Transform generatedFloorTransform;

    private async void Start()
    {
        string inputDir = SimulationRunConfig.InputDir;
        string sessionId = SimulationRunConfig.SessionId;
        string jsonPath;
        string glbDir;

#if UNITY_EDITOR
        if (string.IsNullOrEmpty(inputDir))
        {
            sessionId = "room_scan_70630b05-6ef2-4b5a-8741-54d20a5d9365";
            SimulationRunConfig.SessionId = sessionId;
            jsonPath = ResolveEditorTestJsonPath();
            glbDir = ResolveProjectPath(EditorTestGlbAssetFolder);
        }
        else
        {
            jsonPath = ResolveInputJsonPath(inputDir);
            glbDir = ResolveGlbDirectory(inputDir, jsonPath);
        }
#else
        if (string.IsNullOrEmpty(inputDir)) return;

        jsonPath = ResolveInputJsonPath(inputDir);
        glbDir = ResolveGlbDirectory(inputDir, jsonPath);
#endif

        if (roomParent == null) roomParent = transform;

        if (string.IsNullOrEmpty(jsonPath) || !File.Exists(jsonPath))
        {
            Debug.LogError($"[SimulationLoader] JSON file not found: {jsonPath}");
            return;
        }

        string jsonContent = File.ReadAllText(jsonPath);
        SimulationInput inputData = JsonUtility.FromJson<SimulationInput>(jsonContent);
        if (inputData == null)
        {
            Debug.LogError($"[SimulationLoader] Failed to parse JSON: {jsonPath}");
            return;
        }

        if (string.IsNullOrWhiteSpace(sessionId) && !string.IsNullOrWhiteSpace(inputData.session_id))
            SimulationRunConfig.SessionId = inputData.session_id;

        GenerateRoom(inputData.room);
        List<GameObject> loadedFurnitures = await GenerateFurnitureAsync(inputData.furnitures, glbDir);

        ServerSimulationManager serverManager = FindFirstObjectByType<ServerSimulationManager>();
        if (serverManager != null)
        {
            serverManager.InitializeAndStartPipeline(roomParent.gameObject, loadedFurnitures, inputData, generatedFloorTransform);
        }
    }

    private string ResolveInputJsonPath(string inputDirOrJsonPath)
    {
        if (string.IsNullOrWhiteSpace(inputDirOrJsonPath)) return null;
        if (File.Exists(inputDirOrJsonPath)) return inputDirOrJsonPath;

        string jsonFileName = SimulationRunConfig.IsDebugSession
            ? SimulationRunConfig.DebugInputFileName
            : "simulation_input.json";

        return Path.Combine(inputDirOrJsonPath, jsonFileName);
    }

    private string ResolveGlbDirectory(string inputDirOrJsonPath, string resolvedJsonPath)
    {
        if (!string.IsNullOrWhiteSpace(SimulationRunConfig.GlbDir))
            return SimulationRunConfig.GlbDir;

        if (!string.IsNullOrWhiteSpace(inputDirOrJsonPath) && Directory.Exists(inputDirOrJsonPath))
            return inputDirOrJsonPath;

        if (!string.IsNullOrWhiteSpace(resolvedJsonPath))
            return Path.GetDirectoryName(resolvedJsonPath);

        return inputDirOrJsonPath;
    }

#if UNITY_EDITOR
    private string ResolveEditorTestJsonPath()
    {
        string resolved = ResolveProjectPath(testJsonAssetPath);
        if (File.Exists(resolved)) return resolved;

        string jsonFileName = SimulationRunConfig.IsDebugSession
            ? SimulationRunConfig.DebugInputFileName
            : "simulation_input.json";

        return Path.Combine(resolved, jsonFileName);
    }

    private string ResolveProjectPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (Path.IsPathRooted(path)) return path;

        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        return Path.GetFullPath(Path.Combine(projectRoot, path));
    }
#endif

    private void GenerateRoom(RoomData roomData)
    {
        if (roomData == null) return;

        if (roomData.walls != null)
        {
            for (int i = 0; i < roomData.walls.Count; i++)
            {
                GameObject wallObj = CreateRoomBox(roomData.walls[i], $"Generated_Wall_{i}");
                ApplyMaterial(wallObj, wallMaterial);
            }
        }

        if (roomData.floor != null)
        {
            GameObject floorObj = CreateRoomBox(roomData.floor, "Generated_Floor");
            generatedFloorTransform = floorObj != null ? floorObj.transform : null;
        }

        if (roomData.ceiling != null)
        {
            GameObject ceilingObj = CreateRoomBox(roomData.ceiling, "Generated_Ceiling");
            DisableRenderersOnly(ceilingObj);
        }
    }

    private GameObject CreateRoomBox(WallData boxData, string objectName)
    {
        if (boxData == null) return null;

        GameObject obj = wallPrefab != null ? Instantiate(wallPrefab, roomParent) : GameObject.CreatePrimitive(PrimitiveType.Cube);
        obj.name = objectName;
        obj.transform.SetParent(roomParent, false);
        obj.transform.localPosition = boxData.GetCenter();
        obj.transform.localScale = boxData.GetSize();
        obj.transform.localRotation = Quaternion.Euler(boxData.GetEuler());

        if (obj.GetComponentInChildren<Collider>() == null)
            obj.AddComponent<BoxCollider>();

        return obj;
    }

    private void DisableRenderersOnly(GameObject obj)
    {
        if (obj == null) return;

        foreach (Renderer renderer in obj.GetComponentsInChildren<Renderer>(true))
        {
            renderer.enabled = false;
        }
    }

    private void ApplyMaterial(GameObject obj, Material material)
    {
        if (obj == null || material == null) return;

        foreach (Renderer renderer in obj.GetComponentsInChildren<Renderer>(true))
        {
            renderer.sharedMaterial = material;
        }
    }

    private async Task<List<GameObject>> GenerateFurnitureAsync(List<FurnitureData> furnitureList, string inputDir)
    {
        List<GameObject> spawned = new List<GameObject>();
        if (furnitureList == null) return spawned;

        foreach (FurnitureData furData in furnitureList)
        {
            string glbPath = ResolveGlbPath(inputDir, furData.mesh_file, furData.furniture_id, preferCollider: false);
            if (string.IsNullOrEmpty(glbPath))
            {
                Debug.LogWarning($"[SimulationLoader] Visual GLB not found for furniture_id={furData.furniture_id}");
                continue;
            }

            string labelName = BuildFurnitureTagName(furData);
            GameObject furnitureObj = new GameObject(labelName);
            furnitureObj.transform.SetParent(roomParent, false);
            furnitureObj.transform.localPosition = furData.GetPosition();
            furnitureObj.transform.localRotation = furData.GetRotation();
            furnitureObj.transform.localScale = furData.GetScale();

            var gltf = new GltfImport();
            bool success = await gltf.Load(glbPath);

            if (!success)
            {
                Debug.LogWarning($"[SimulationLoader] glTF load failed: {glbPath}");
                Destroy(furnitureObj);
                continue;
            }

            await gltf.InstantiateMainSceneAsync(furnitureObj.transform);
            Vector3 normalizationOffset = NormalizeModelIfNeeded(furnitureObj);

            bool hasCollider = await TryLoadColliderGlbAsync(furnitureObj, furData, inputDir, normalizationOffset);
            if (!hasCollider) EnsureFallbackColliders(furnitureObj);

            ConfigurePhysics(furnitureObj, furData);
            spawned.Add(furnitureObj);
        }

        return spawned;
    }

    private string ResolveGlbPath(string inputDir, string configuredPath, string furnitureId, bool preferCollider)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            string path = Path.IsPathRooted(configuredPath) ? configuredPath : Path.Combine(inputDir, configuredPath);
            if (File.Exists(path)) return path;
        }

        if (string.IsNullOrWhiteSpace(furnitureId) || !Directory.Exists(inputDir)) return null;

        string[] matches = Directory.GetFiles(inputDir, $"*{furnitureId}*.glb", SearchOption.TopDirectoryOnly);
        if (matches.Length == 0) return null;

        Array.Sort(matches, StringComparer.OrdinalIgnoreCase);
        foreach (string match in matches)
        {
            string lower = Path.GetFileName(match).ToLowerInvariant();
            bool looksCollider = lower.Contains("collider") || lower.Contains("convex") || lower.Contains("hull");
            if (preferCollider == looksCollider) return match;
        }

        return preferCollider ? null : matches[0];
    }

    private static string BuildFurnitureTagName(FurnitureData furData)
    {
        if (furData == null) return "furniture_001";

        string id = furData.furniture_id;
        if (!string.IsNullOrWhiteSpace(id))
        {
            string normalizedId = id.Trim();
            if (normalizedId.StartsWith("scene_", StringComparison.OrdinalIgnoreCase))
                normalizedId = normalizedId.Substring("scene_".Length);

            int separatorIndex = normalizedId.LastIndexOf('_');
            if (separatorIndex >= 0 && separatorIndex < normalizedId.Length - 1)
            {
                string prefix = normalizedId.Substring(0, separatorIndex);
                string suffix = normalizedId.Substring(separatorIndex + 1);
                if (int.TryParse(suffix, out int parsedIndex))
                    return $"{prefix}_{parsedIndex:000}";
            }

            return normalizedId;
        }

        string label = string.IsNullOrWhiteSpace(furData.label) ? "furniture" : furData.label.Trim();
        return $"{label}_{Mathf.Max(0, furData.instance_index) + 1:000}";
    }

    private async Task<bool> TryLoadColliderGlbAsync(GameObject furnitureObj, FurnitureData furData, string inputDir, Vector3 visualNormalizationOffset)
    {
        string colliderPath = ResolveGlbPath(inputDir, furData.collider_file, furData.furniture_id, preferCollider: true);
        if (string.IsNullOrEmpty(colliderPath)) return false;

        GameObject colliderRoot = new GameObject("ColliderHull");
        colliderRoot.transform.SetParent(furnitureObj.transform, false);
        colliderRoot.transform.localPosition = visualNormalizationOffset;

        var gltf = new GltfImport();
        bool loaded = await gltf.Load(colliderPath);
        if (!loaded)
        {
            Destroy(colliderRoot);
            return false;
        }

        await gltf.InstantiateMainSceneAsync(colliderRoot.transform);

        bool hasCollider = false;
        foreach (Renderer renderer in colliderRoot.GetComponentsInChildren<Renderer>(true))
            renderer.enabled = false;

        foreach (MeshFilter meshFilter in colliderRoot.GetComponentsInChildren<MeshFilter>(true))
        {
            if (meshFilter == null || meshFilter.sharedMesh == null) continue;
            MeshCollider meshCollider = meshFilter.gameObject.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = meshFilter.sharedMesh;
            meshCollider.convex = true;
            hasCollider = true;
        }

        if (!hasCollider) Destroy(colliderRoot);
        return hasCollider;
    }

    private Vector3 NormalizeModelIfNeeded(GameObject root)
    {
        if (!normalizeModelToGround || root == null)
        {
            return Vector3.zero;
        }

        if (!TryGetLocalRendererBounds(root, out Bounds localBounds))
        {
            return Vector3.zero;
        }

        Vector3 offset = Vector3.zero;
        if (centerModelXZ)
        {
            offset.x = -localBounds.center.x;
            offset.z = -localBounds.center.z;
        }

        offset.y = -localBounds.min.y;

        for (int i = 0; i < root.transform.childCount; i++)
        {
            Transform child = root.transform.GetChild(i);
            child.localPosition += offset;
        }

        return offset;
    }

    private static bool TryGetLocalRendererBounds(GameObject root, out Bounds localBounds)
    {
        localBounds = default;
        if (root == null) return false;

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        bool hasBounds = false;

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
            {
                continue;
            }

            Bounds rendererLocalBounds = renderer.localBounds;
            Vector3 min = rendererLocalBounds.min;
            Vector3 max = rendererLocalBounds.max;

            Vector3[] localCorners =
            {
                new Vector3(min.x, min.y, min.z),
                new Vector3(min.x, min.y, max.z),
                new Vector3(min.x, max.y, min.z),
                new Vector3(min.x, max.y, max.z),
                new Vector3(max.x, min.y, min.z),
                new Vector3(max.x, min.y, max.z),
                new Vector3(max.x, max.y, min.z),
                new Vector3(max.x, max.y, max.z)
            };

            foreach (Vector3 rendererLocalCorner in localCorners)
            {
                Vector3 worldCorner = renderer.transform.TransformPoint(rendererLocalCorner);
                Vector3 rootLocalCorner = root.transform.InverseTransformPoint(worldCorner);

                if (!hasBounds)
                {
                    localBounds = new Bounds(rootLocalCorner, Vector3.zero);
                    hasBounds = true;
                }
                else
                {
                    localBounds.Encapsulate(rootLocalCorner);
                }
            }
        }

        return hasBounds;
    }

    private void EnsureFallbackColliders(GameObject obj)
    {
        if (obj.GetComponentInChildren<Collider>() != null) return;

        foreach (Renderer render in obj.GetComponentsInChildren<Renderer>())
        {
            if (render is MeshRenderer || render is SkinnedMeshRenderer)
                render.gameObject.AddComponent<BoxCollider>();
        }
    }

    private void ConfigurePhysics(GameObject targetObj, FurnitureData data)
    {
        Rigidbody rb = targetObj.GetComponent<Rigidbody>();
        if (rb == null) rb = targetObj.AddComponent<Rigidbody>();

        if (data.mass_kg > 0f) rb.mass = data.mass_kg;

        bool anchored = data.is_fixed;
        if (anchored)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        }
        else
        {
            rb.isKinematic = false;
            rb.useGravity = true;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }
    }
}

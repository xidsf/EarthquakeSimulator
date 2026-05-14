using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using GLTFast;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// 서버 result의 mesh_url을 Unity GameObject로 변환합니다.
///
/// 현재 서버 mesh_url은 GLB(.glb) 파일을 반환하는 전제입니다.
/// glTFast 패키지를 사용해 GLB를 런타임 로드하고, 로드된 root GameObject를 반환합니다.
///
/// 주의:
/// - 이 로더는 서버 result mesh_url 처리에만 사용합니다.
/// - Debug 랜덤 prefab 배치 흐름은 이 로더를 거치지 않습니다.
/// - visual mesh는 렌더링/YOLO 인식용입니다.
/// - physics collider는 서버가 제공하는 collider_mesh_url GLB를 별도로 로드해서
///   FurnitureServerResultPlacer가 MeshCollider(convex)로 붙입니다.
/// </summary>
public class FurnitureServerMeshLoader : MonoBehaviour
{
    [Header("GLB Loading - Server Result")]
    [Tooltip("서버 mesh_url이 .glb인 경우 glTFast로 런타임 로드합니다.")]
    public bool loadGlbUrls = true;

    [Tooltip("mesh_url이 /files/model.glb 같은 상대 경로로 올 경우 이 base url과 결합합니다.")]
    public string meshBaseUrl = "https://api.earquake.xyz";

    [Tooltip("상대 mesh_url을 meshBaseUrl과 결합합니다.")]
    public bool resolveRelativeUrls = true;

    [Tooltip("glTFast GltfImport 인스턴스를 유지합니다. 유지하지 않으면 런타임 생성 Mesh/Material의 수명 관리가 불안정할 수 있습니다.")]
    public bool retainGltfImportsForObjectLifetime = true;

    [Header("AssetBundle Loading - Optional Legacy")]
    [Tooltip("mesh_url이 .assetbundle 또는 .bundle이면 AssetBundle로 다운로드해서 첫 번째 GameObject를 로드합니다. 서버 GLB 흐름에서는 사용하지 않습니다.")]
    public bool loadAssetBundleUrls = false;

    [Header("Fallback")]
    [Tooltip("지원하지 않는 mesh_url이거나 로딩 실패 시 placeholder cube를 생성합니다. 서버 GLB 연동 실패 확인용입니다.")]
    public bool createPlaceholderWhenLoadFails = true;

    public Vector3 placeholderSize = Vector3.one * 0.5f;
    public Material placeholderMaterial;

    [Header("Network")]
    [Min(1)] public int requestTimeoutSeconds = 60;

    [Header("Log")]
    public bool logLoading = true;

    private readonly List<GltfImport> retainedGltfImports = new List<GltfImport>();

    public IEnumerator LoadFurnitureObject(FurnitureServerResultObject serverObject, Action<GameObject, string> onLoaded)
    {
        if (serverObject == null)
        {
            onLoaded?.Invoke(null, "serverObject is null");
            yield break;
        }

        string meshUrl = ResolveMeshUrl(serverObject.mesh_url);
        if (string.IsNullOrWhiteSpace(meshUrl))
        {
            yield return CreateFallback(serverObject, "mesh_url is empty", onLoaded);
            yield break;
        }

        if (loadGlbUrls && LooksLikeGlbUrl(meshUrl))
        {
            yield return LoadGlbObject(
                meshUrl,
                serverObject,
                "VisualGLB",
                disableRenderers: false,
                createFallbackOnFail: true,
                onLoaded: onLoaded);
            yield break;
        }

        if (loadAssetBundleUrls && LooksLikeAssetBundleUrl(meshUrl))
        {
            yield return LoadAssetBundleObject(meshUrl, serverObject, onLoaded);
            yield break;
        }

        yield return CreateFallback(
            serverObject,
            $"Unsupported mesh_url format. Expected server GLB(.glb). url:{meshUrl}",
            onLoaded);
    }

    public IEnumerator LoadColliderObject(FurnitureServerResultObject serverObject, Action<GameObject, string> onLoaded)
    {
        if (serverObject == null)
        {
            onLoaded?.Invoke(null, "serverObject is null");
            yield break;
        }

        string colliderUrl = ResolveMeshUrl(serverObject.collider_mesh_url);
        if (string.IsNullOrWhiteSpace(colliderUrl))
        {
            onLoaded?.Invoke(null, "collider_mesh_url is empty");
            yield break;
        }

        if (!loadGlbUrls || !LooksLikeGlbUrl(colliderUrl))
        {
            onLoaded?.Invoke(null, $"Unsupported collider_mesh_url format. Expected GLB. url:{colliderUrl}");
            yield break;
        }

        yield return LoadGlbObject(
            colliderUrl,
            serverObject,
            "ColliderHull",
            disableRenderers: true,
            createFallbackOnFail: false,
            onLoaded: onLoaded);
    }

    private IEnumerator LoadGlbObject(
        string url,
        FurnitureServerResultObject serverObject,
        string nameSuffix,
        bool disableRenderers,
        bool createFallbackOnFail,
        Action<GameObject, string> onLoaded)
    {
        if (logLoading)
        {
            Debug.Log($"[FurnitureServerMeshLoader] Load GLB via glTFast: {url}");
        }

        GameObject root = new GameObject(BuildObjectName(serverObject, nameSuffix));
        GltfImport gltf = new GltfImport();

        Task<bool> loadTask = null;
        string loadStartError = null;

        try
        {
            loadTask = gltf.Load(url);
        }
        catch (Exception ex)
        {
            loadStartError = $"glTFast Load call failed. {ex.Message}";
        }

        if (!string.IsNullOrEmpty(loadStartError))
        {
            Destroy(root);
            yield return CompleteGlbFailure(serverObject, loadStartError, createFallbackOnFail, onLoaded);
            yield break;
        }

        yield return WaitForTask(loadTask);

        if (loadTask == null || loadTask.IsFaulted || loadTask.IsCanceled || !loadTask.Result)
        {
            string reason = BuildTaskError("glTFast GLB load failed", loadTask);
            Destroy(root);
            yield return CompleteGlbFailure(serverObject, reason, createFallbackOnFail, onLoaded);
            yield break;
        }

        Task<bool> instantiateTask = null;
        string instantiateStartError = null;

        try
        {
            instantiateTask = gltf.InstantiateMainSceneAsync(root.transform);
        }
        catch (Exception ex)
        {
            instantiateStartError = $"glTFast InstantiateMainSceneAsync call failed. {ex.Message}";
        }

        if (!string.IsNullOrEmpty(instantiateStartError))
        {
            Destroy(root);
            yield return CompleteGlbFailure(serverObject, instantiateStartError, createFallbackOnFail, onLoaded);
            yield break;
        }

        yield return WaitForTask(instantiateTask);

        if (instantiateTask == null || instantiateTask.IsFaulted || instantiateTask.IsCanceled || !instantiateTask.Result)
        {
            string reason = BuildTaskError("glTFast GLB instantiate failed", instantiateTask);
            Destroy(root);
            yield return CompleteGlbFailure(serverObject, reason, createFallbackOnFail, onLoaded);
            yield break;
        }

        if (root.transform.childCount == 0 && root.GetComponentsInChildren<MeshFilter>(true).Length == 0)
        {
            Destroy(root);
            yield return CompleteGlbFailure(serverObject, "glTFast loaded GLB but no mesh object was instantiated.", createFallbackOnFail, onLoaded);
            yield break;
        }

        if (disableRenderers)
        {
            DisableRenderers(root);
        }

        if (retainGltfImportsForObjectLifetime)
        {
            retainedGltfImports.Add(gltf);
        }

        if (logLoading)
        {
            Debug.Log($"[FurnitureServerMeshLoader] GLB loaded. object:{root.name}, children:{root.transform.childCount}", root);
        }

        onLoaded?.Invoke(root, string.Empty);
    }

    private IEnumerator CompleteGlbFailure(
        FurnitureServerResultObject serverObject,
        string reason,
        bool createFallbackOnFail,
        Action<GameObject, string> onLoaded)
    {
        if (createFallbackOnFail)
        {
            yield return CreateFallback(serverObject, reason, onLoaded);
            yield break;
        }

        if (!string.IsNullOrWhiteSpace(reason))
        {
            Debug.LogWarning($"[FurnitureServerMeshLoader] {reason}");
        }

        onLoaded?.Invoke(null, reason);
    }

    private IEnumerator LoadAssetBundleObject(string url, FurnitureServerResultObject serverObject, Action<GameObject, string> onLoaded)
    {
        if (logLoading)
        {
            Debug.Log($"[FurnitureServerMeshLoader] Download AssetBundle: {url}");
        }

        using (UnityWebRequest request = UnityWebRequestAssetBundle.GetAssetBundle(url))
        {
            request.timeout = requestTimeoutSeconds;
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                yield return CreateFallback(serverObject, $"AssetBundle download failed. {request.error}", onLoaded);
                yield break;
            }

            AssetBundle bundle = DownloadHandlerAssetBundle.GetContent(request);
            if (bundle == null)
            {
                yield return CreateFallback(serverObject, "Downloaded AssetBundle is null.", onLoaded);
                yield break;
            }

            string[] assetNames = bundle.GetAllAssetNames();
            if (assetNames == null || assetNames.Length == 0)
            {
                bundle.Unload(false);
                yield return CreateFallback(serverObject, "AssetBundle has no assets.", onLoaded);
                yield break;
            }

            AssetBundleRequest loadRequest = bundle.LoadAssetAsync<GameObject>(assetNames[0]);
            yield return loadRequest;

            GameObject prefab = loadRequest.asset as GameObject;
            if (prefab == null)
            {
                bundle.Unload(false);
                yield return CreateFallback(serverObject, $"First AssetBundle asset is not GameObject. asset:{assetNames[0]}", onLoaded);
                yield break;
            }

            GameObject instance = Instantiate(prefab);
            instance.name = BuildObjectName(serverObject, prefab.name);
            bundle.Unload(false);
            onLoaded?.Invoke(instance, string.Empty);
        }
    }

    private IEnumerator CreateFallback(FurnitureServerResultObject serverObject, string reason, Action<GameObject, string> onLoaded)
    {
        if (!string.IsNullOrWhiteSpace(reason))
        {
            Debug.LogWarning($"[FurnitureServerMeshLoader] {reason}");
        }

        if (!createPlaceholderWhenLoadFails)
        {
            onLoaded?.Invoke(null, reason);
            yield break;
        }

        Vector3 size = GetFallbackSize(serverObject);
        GameObject placeholder = new GameObject(BuildObjectName(serverObject, "Placeholder"));
        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
        visual.name = $"{serverObject.label}_FallbackVisual";
        visual.transform.SetParent(placeholder.transform, false);
        visual.transform.localPosition = new Vector3(0f, size.y * 0.5f, 0f);
        visual.transform.localScale = size;

        Collider collider = visual.GetComponent<Collider>();
        if (collider != null)
        {
            collider.enabled = false;
        }

        if (placeholderMaterial != null)
        {
            Renderer renderer = visual.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = placeholderMaterial;
            }
        }

        onLoaded?.Invoke(placeholder, reason);
        yield break;
    }

    private static IEnumerator WaitForTask(Task task)
    {
        while (task != null && !task.IsCompleted)
        {
            yield return null;
        }
    }

    private static string BuildTaskError(string prefix, Task task)
    {
        if (task == null)
        {
            return $"{prefix}. task is null.";
        }

        if (task.IsCanceled)
        {
            return $"{prefix}. task was canceled.";
        }

        if (task.Exception != null)
        {
            Exception inner = task.Exception.GetBaseException();
            return $"{prefix}. {inner.Message}";
        }

        return prefix;
    }

    private string ResolveMeshUrl(string meshUrl)
    {
        if (string.IsNullOrWhiteSpace(meshUrl))
        {
            return string.Empty;
        }

        string trimmed = meshUrl.Trim();

        if (!resolveRelativeUrls)
        {
            return trimmed;
        }

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out _))
        {
            return trimmed;
        }

        if (string.IsNullOrWhiteSpace(meshBaseUrl))
        {
            return trimmed;
        }

        string baseUrl = meshBaseUrl.TrimEnd('/');
        string path = trimmed.TrimStart('/');
        return $"{baseUrl}/{path}";
    }

    private static bool LooksLikeGlbUrl(string url)
    {
        string clean = StripQueryAndFragment(url).ToLowerInvariant();
        return clean.EndsWith(".glb") || clean.Contains(".glb/");
    }

    private static bool LooksLikeAssetBundleUrl(string url)
    {
        string clean = StripQueryAndFragment(url).ToLowerInvariant();
        return clean.EndsWith(".assetbundle") || clean.EndsWith(".bundle") || clean.Contains("assetbundle");
    }

    private static string StripQueryAndFragment(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        int queryIndex = url.IndexOf('?');
        int fragmentIndex = url.IndexOf('#');
        int endIndex = url.Length;

        if (queryIndex >= 0)
        {
            endIndex = Mathf.Min(endIndex, queryIndex);
        }

        if (fragmentIndex >= 0)
        {
            endIndex = Mathf.Min(endIndex, fragmentIndex);
        }

        return url.Substring(0, endIndex);
    }

    private static string BuildObjectName(FurnitureServerResultObject serverObject, string fallback)
    {
        string label = !string.IsNullOrWhiteSpace(serverObject.label) ? serverObject.label : fallback;
        string id = !string.IsNullOrWhiteSpace(serverObject.id) ? serverObject.id : Guid.NewGuid().ToString("N").Substring(0, 8);
        return $"ServerFurniture_{label}_{id}";
    }

    private static void DisableRenderers(GameObject root)
    {
        if (root == null)
        {
            return;
        }

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in renderers)
        {
            if (renderer != null)
            {
                renderer.enabled = false;
            }
        }
    }

    private Vector3 GetFallbackSize(FurnitureServerResultObject serverObject)
    {
        Vector3 size = ToVector3(serverObject.size_world, Vector3.zero);
        if (HasAllPositiveComponents(size))
        {
            return size;
        }

        size = ToVector3(serverObject.target_size, Vector3.zero);
        if (HasAllPositiveComponents(size))
        {
            return size;
        }

        size = ToVector3(serverObject.collider_size, Vector3.zero);
        if (HasAllPositiveComponents(size))
        {
            return size;
        }

        return placeholderSize;
    }

    private static Vector3 ToVector3(float[] values, Vector3 fallback)
    {
        if (values == null || values.Length < 3)
        {
            return fallback;
        }

        return new Vector3(values[0], values[1], values[2]);
    }

    private static bool HasAllPositiveComponents(Vector3 value)
    {
        return value.x > 0f && value.y > 0f && value.z > 0f;
    }

    private void OnDestroy()
    {
        for (int i = 0; i < retainedGltfImports.Count; i++)
        {
            if (retainedGltfImports[i] != null)
            {
                retainedGltfImports[i].Dispose();
            }
        }

        retainedGltfImports.Clear();
    }
}

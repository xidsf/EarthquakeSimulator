using UnityEngine;
using GLTFast;

public class FurnitureObjectFactory : MonoBehaviour
{
    [Header("Parent")]
    public Transform furnitureRoot;

    [Header("Fallback")]
    public Material defaultMaterial;

    [Header("Physics")]
    public bool useMeshCollider = true;
    public bool convexMeshCollider = true;
    public bool addRigidbody = true;

    [Header("Model Alignment")]
    public bool normalizeModelToGround = true;
    public bool centerModelXZ = true;

    [Header("Size")]
    public bool applyTargetSize = true;
    public bool nonUniformScale = true;

    public void CreateFromResult(FurnitureObjectResult result)
    {
        if (result == null)
        {
            Debug.LogError("[FurnitureObjectFactory] Result is null.");
            return;
        }

        if (!string.IsNullOrEmpty(result.mesh_url))
        {
            CreateFromGlb(result);
        }
        else
        {
            CreateFallbackBox(result);
        }
    }

    private async void CreateFromGlb(FurnitureObjectResult result)
    {
        Vector3 position = ToVector3(result.position, new Vector3(0f, 0f, 2f));
        Vector3 rotationEuler = ToVector3(result.rotation, Vector3.zero);
        Vector3 initialScale = ToVector3(result.scale, Vector3.one);

        GameObject root = new GameObject($"Furniture_{result.label}_{result.id}");

        if (furnitureRoot != null)
            root.transform.SetParent(furnitureRoot);

        root.transform.position = position;
        root.transform.rotation = Quaternion.Euler(rotationEuler);
        root.transform.localScale = initialScale;

        AddMetadata(root, result);

        Debug.Log($"[FurnitureObjectFactory] Loading GLB: {result.mesh_url}");

        var gltf = new GltfImport();
        bool loaded = await gltf.Load(result.mesh_url);

        if (!Application.isPlaying || root == null)
            return;

        if (!loaded)
        {
            Debug.LogError($"[FurnitureObjectFactory] Failed to load GLB: {result.mesh_url}");

            CreateFallbackVisualUnderRoot(root, result);
            AddBoxColliderFallback(root, result);
            AddRigidbodyOnly(root, result);

            return;
        }

        bool instantiated = await gltf.InstantiateMainSceneAsync(root.transform);

        if (!Application.isPlaying || root == null)
            return;

        if (!instantiated)
        {
            Debug.LogError($"[FurnitureObjectFactory] Failed to instantiate GLB: {result.mesh_url}");

            CreateFallbackVisualUnderRoot(root, result);
            AddBoxColliderFallback(root, result);
            AddRigidbodyOnly(root, result);

            return;
        }

        if (normalizeModelToGround)
        {
            NormalizeModelToGround(root);
        }

        if (applyTargetSize)
        {
            ApplyTargetSize(root, result);

            if (normalizeModelToGround)
            {
                NormalizeModelToGround(root);
            }
        }

        AddPhysics(root, result);

        Debug.Log($"[FurnitureObjectFactory] Created GLB furniture: {root.name}");
    }

    private GameObject CreateFallbackBox(FurnitureObjectResult result)
    {
        Vector3 position = ToVector3(result.position, new Vector3(0f, 0f, 2f));
        Vector3 rotationEuler = ToVector3(result.rotation, Vector3.zero);
        Vector3 targetSize = GetTargetSize(result);

        GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obj.name = $"Furniture_{result.label}_{result.id}_FallbackBox";

        if (furnitureRoot != null)
            obj.transform.SetParent(furnitureRoot);

        obj.transform.position = position + new Vector3(0f, targetSize.y * 0.5f, 0f);
        obj.transform.rotation = Quaternion.Euler(rotationEuler);
        obj.transform.localScale = targetSize;

        Renderer renderer = obj.GetComponent<Renderer>();
        if (renderer != null && defaultMaterial != null)
            renderer.material = defaultMaterial;

        AddMetadata(obj, result);
        AddRigidbodyOnly(obj, result);

        Debug.Log($"[FurnitureObjectFactory] Created fallback box: {obj.name}");

        return obj;
    }

    private void CreateFallbackVisualUnderRoot(GameObject root, FurnitureObjectResult result)
    {
        Vector3 targetSize = GetTargetSize(result);

        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = $"{result.label}_FallbackVisual";

        cube.transform.SetParent(root.transform);
        cube.transform.localPosition = new Vector3(0f, targetSize.y * 0.5f, 0f);
        cube.transform.localRotation = Quaternion.identity;
        cube.transform.localScale = targetSize;

        BoxCollider cubeCollider = cube.GetComponent<BoxCollider>();
        if (cubeCollider != null)
            cubeCollider.enabled = false;

        Renderer renderer = cube.GetComponent<Renderer>();
        if (renderer != null && defaultMaterial != null)
            renderer.material = defaultMaterial;

        Debug.Log($"[FurnitureObjectFactory] Created fallback visual under {root.name}");
    }

    private void AddMetadata(GameObject root, FurnitureObjectResult result)
    {
        FurnitureMetadata metadata = root.GetComponent<FurnitureMetadata>();
        if (metadata == null)
            metadata = root.AddComponent<FurnitureMetadata>();

        metadata.objectId = result.id;
        metadata.label = result.label;
        metadata.confidence = result.confidence;
        metadata.userConfirmed = false;
    }

    private void AddPhysics(GameObject root, FurnitureObjectResult result)
    {
        if (addRigidbody)
        {
            AddRigidbodyOnly(root, result);
        }

        if (useMeshCollider)
        {
            int meshColliderCount = AddMeshCollidersToChildren(root);

            if (meshColliderCount > 0)
            {
                BoxCollider existingBox = root.GetComponent<BoxCollider>();

                if (existingBox != null)
                    existingBox.enabled = false;

                Debug.Log($"[FurnitureObjectFactory] Added {meshColliderCount} MeshColliders to {root.name}");
                return;
            }

            Debug.LogWarning($"[FurnitureObjectFactory] No MeshFilter found in {root.name}. Using BoxCollider fallback.");
        }

        AddBoxColliderFallback(root, result);
    }

    private void AddRigidbodyOnly(GameObject root, FurnitureObjectResult result)
    {
        if (!addRigidbody)
            return;

        Vector3 size = GetTargetSize(result);

        Rigidbody rb = root.GetComponent<Rigidbody>();
        if (rb == null)
            rb = root.AddComponent<Rigidbody>();

        rb.mass = EstimateMass(size);
        rb.centerOfMass = new Vector3(0f, -size.y * 0.15f, 0f);
        rb.useGravity = true;
        rb.isKinematic = false;
    }

    private int AddMeshCollidersToChildren(GameObject root)
    {
        int count = 0;

        MeshFilter[] meshFilters = root.GetComponentsInChildren<MeshFilter>(true);

        foreach (MeshFilter meshFilter in meshFilters)
        {
            if (meshFilter == null)
                continue;

            if (meshFilter.sharedMesh == null)
                continue;

            GameObject meshObject = meshFilter.gameObject;

            MeshCollider meshCollider = meshObject.GetComponent<MeshCollider>();
            if (meshCollider == null)
                meshCollider = meshObject.AddComponent<MeshCollider>();

            meshCollider.sharedMesh = null;
            meshCollider.sharedMesh = meshFilter.sharedMesh;
            meshCollider.convex = convexMeshCollider;

            count++;

            Debug.Log(
                $"[FurnitureObjectFactory] MeshCollider added: {meshObject.name}, " +
                $"mesh={meshFilter.sharedMesh.name}, convex={meshCollider.convex}"
            );
        }

        return count;
    }

    private void AddBoxColliderFallback(GameObject root, FurnitureObjectResult result)
    {
        Vector3 colliderSize = GetTargetSize(result);
        Vector3 colliderCenter = ToVector3(result.collider_center, colliderSize * 0.5f);

        BoxCollider box = root.GetComponent<BoxCollider>();
        if (box == null)
            box = root.AddComponent<BoxCollider>();

        box.enabled = true;
        box.center = colliderCenter;
        box.size = colliderSize;

        Debug.Log($"[FurnitureObjectFactory] Added fallback BoxCollider to {root.name}");
    }

    private void NormalizeModelToGround(GameObject root)
    {
        if (root == null)
            return;

        if (!TryGetLocalRendererBounds(root, out Bounds localBounds))
        {
            Debug.LogWarning($"[FurnitureObjectFactory] Cannot normalize {root.name}. No renderer bounds found.");
            return;
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

        if (TryGetLocalRendererBounds(root, out Bounds newBounds))
        {
            Debug.Log(
                $"[FurnitureObjectFactory] Normalized {root.name}. " +
                $"Before minY={localBounds.min.y:F3}, center={localBounds.center}, size={localBounds.size}, " +
                $"After minY={newBounds.min.y:F3}, center={newBounds.center}, size={newBounds.size}"
            );
        }
    }

    private void ApplyTargetSize(GameObject root, FurnitureObjectResult result)
    {
        Vector3 targetSize = GetTargetSize(result);

        if (targetSize.x <= 0f || targetSize.y <= 0f || targetSize.z <= 0f)
        {
            Debug.LogWarning($"[FurnitureObjectFactory] Invalid target size for {root.name}: {targetSize}");
            return;
        }

        if (!TryGetLocalRendererBounds(root, out Bounds bounds))
        {
            Debug.LogWarning($"[FurnitureObjectFactory] Cannot apply target size. No renderer bounds found: {root.name}");
            return;
        }

        Vector3 currentSize = bounds.size;

        if (currentSize.x <= 0.0001f || currentSize.y <= 0.0001f || currentSize.z <= 0.0001f)
        {
            Debug.LogWarning($"[FurnitureObjectFactory] Current bounds too small: {root.name}, size={currentSize}");
            return;
        }

        Vector3 scaleFactor;

        if (nonUniformScale)
        {
            scaleFactor = new Vector3(
                targetSize.x / currentSize.x,
                targetSize.y / currentSize.y,
                targetSize.z / currentSize.z
            );
        }
        else
        {
            float factor = Mathf.Min(
                targetSize.x / currentSize.x,
                targetSize.y / currentSize.y,
                targetSize.z / currentSize.z
            );

            scaleFactor = new Vector3(factor, factor, factor);
        }

        scaleFactor.x = Mathf.Clamp(scaleFactor.x, 0.01f, 100f);
        scaleFactor.y = Mathf.Clamp(scaleFactor.y, 0.01f, 100f);
        scaleFactor.z = Mathf.Clamp(scaleFactor.z, 0.01f, 100f);

        for (int i = 0; i < root.transform.childCount; i++)
        {
            Transform child = root.transform.GetChild(i);

            child.localScale = new Vector3(
                child.localScale.x * scaleFactor.x,
                child.localScale.y * scaleFactor.y,
                child.localScale.z * scaleFactor.z
            );
        }

        if (TryGetLocalRendererBounds(root, out Bounds newBounds))
        {
            Debug.Log(
                $"[FurnitureObjectFactory] Applied target size to {root.name}. " +
                $"Target={targetSize}, Before={currentSize}, After={newBounds.size}, ScaleFactor={scaleFactor}"
            );
        }
    }

    private bool TryGetLocalRendererBounds(GameObject root, out Bounds localBounds)
    {
        localBounds = new Bounds();

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);

        bool hasBounds = false;

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
                continue;

            Bounds worldBounds = renderer.bounds;

            Vector3 min = worldBounds.min;
            Vector3 max = worldBounds.max;

            Vector3[] corners =
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

            foreach (Vector3 worldCorner in corners)
            {
                Vector3 localCorner = root.transform.InverseTransformPoint(worldCorner);

                if (!hasBounds)
                {
                    localBounds = new Bounds(localCorner, Vector3.zero);
                    hasBounds = true;
                }
                else
                {
                    localBounds.Encapsulate(localCorner);
                }
            }
        }

        return hasBounds;
    }

    private Vector3 GetTargetSize(FurnitureObjectResult result)
    {
        Vector3 targetSize = ToVector3(result.target_size, Vector3.zero);

        if (targetSize.x > 0f && targetSize.y > 0f && targetSize.z > 0f)
            return targetSize;

        Vector3 colliderSize = ToVector3(result.collider_size, Vector3.zero);

        if (colliderSize.x > 0f && colliderSize.y > 0f && colliderSize.z > 0f)
            return colliderSize;

        return ToVector3(result.scale, Vector3.one);
    }

    private Vector3 ToVector3(float[] arr, Vector3 fallback)
    {
        if (arr == null || arr.Length < 3)
            return fallback;

        return new Vector3(arr[0], arr[1], arr[2]);
    }

    private float EstimateMass(Vector3 size)
    {
        float volume = Mathf.Abs(size.x * size.y * size.z);
        return Mathf.Clamp(volume * 30.0f, 5.0f, 120.0f);
    }
}
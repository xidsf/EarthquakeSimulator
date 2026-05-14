using UnityEngine;
using GLTFast;

public class FurnitureObjectFactory : MonoBehaviour
{
    [Header("Parent")]
    public Transform furnitureRoot;

    [Header("Fallback")]
    public Material defaultMaterial;

    [Header("Physics")]
    public bool useMeshCollider = false;
    public bool convexMeshCollider = true;
    public bool addRigidbody = false;

    [Header("Collider Performance")]
    public bool skipConvexHullForLargeMeshes = true;
    public int maxConvexMeshVertices = 220;
    public bool meshColliderOnlyForFanAndChair = true;

    [Header("Model Alignment")]
    public bool normalizeModelToGround = true;
    public bool centerModelXZ = true;

    [Header("Size")]
    public bool applyTargetSize = true;
    public bool nonUniformScale = true;

    [Header("Label Scale Policy")]
    public bool forceBedRectangularRatio = true;
    public float bedMinLengthWidthRatio = 1.60f;
    public float bedMaxLengthWidthRatio = 1.95f;
    public bool clampBedLengthStretch = true;
    public bool preserveFanAndChairMeshRatio = true;

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
        Vector3 rotationEuler = GetRotationEuler(result);
        Vector3 initialScale = ToVector3(result.scale, Vector3.one);

        GameObject root = new GameObject($"Furniture_{result.label}_{result.id}");

        if (furnitureRoot != null)
            root.transform.SetParent(furnitureRoot, true);

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
        Vector3 rotationEuler = GetRotationEuler(result);
        Vector3 targetSize = FixTargetSizeByLabel(result.label, GetTargetSize(result));

        GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obj.name = $"Furniture_{result.label}_{result.id}_FallbackBox";

        if (furnitureRoot != null)
            obj.transform.SetParent(furnitureRoot, true);

        // Keep the previous convention: server position is used as the object's origin.
        // Do not change vertical semantics here to avoid shifting already-working layouts.
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
        Vector3 targetSize = FixTargetSizeByLabel(result.label, GetTargetSize(result));

        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = $"{result.label}_FallbackVisual";

        cube.transform.SetParent(root.transform, false);
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

        if (useMeshCollider && ShouldTryMeshColliderForLabel(result.label))
        {
            int meshColliderCount = AddMeshCollidersToChildren(root, result.label);

            if (meshColliderCount > 0)
            {
                BoxCollider existingBox = root.GetComponent<BoxCollider>();
                if (existingBox != null)
                    existingBox.enabled = false;

                Debug.Log($"[FurnitureObjectFactory] Added {meshColliderCount} MeshColliders to {root.name}");
                return;
            }

            Debug.LogWarning($"[FurnitureObjectFactory] No usable MeshCollider created for {root.name}. Using BoxCollider fallback.");
        }
        else if (useMeshCollider)
        {
            Debug.Log($"[FurnitureObjectFactory] MeshCollider skipped by label/performance policy for {root.name}, label={result.label}. Using BoxCollider fallback.");
        }

        AddBoxColliderFallback(root, result);
    }

    private void AddRigidbodyOnly(GameObject root, FurnitureObjectResult result)
    {
        if (!addRigidbody)
            return;

        Vector3 size = FixTargetSizeByLabel(result.label, GetTargetSize(result));

        Rigidbody rb = root.GetComponent<Rigidbody>();
        if (rb == null)
            rb = root.AddComponent<Rigidbody>();

        rb.mass = EstimateMass(size);
        rb.centerOfMass = new Vector3(0f, -size.y * 0.15f, 0f);

        bool shouldSimulate = result.simulation_enabled || result.earthquake_simulation;
        bool shouldBeKinematic = result.is_fixed || string.Equals(result.rigidbody_mode, "kinematic");

        rb.useGravity = result.use_gravity || shouldSimulate;
        rb.isKinematic = shouldBeKinematic || !shouldSimulate;
    }

    private int AddMeshCollidersToChildren(GameObject root, string label)
    {
        int count = 0;
        MeshFilter[] meshFilters = root.GetComponentsInChildren<MeshFilter>(true);

        foreach (MeshFilter meshFilter in meshFilters)
        {
            if (meshFilter == null || meshFilter.sharedMesh == null)
                continue;

            Mesh mesh = meshFilter.sharedMesh;

            if (convexMeshCollider && skipConvexHullForLargeMeshes && mesh.vertexCount > maxConvexMeshVertices)
            {
                Debug.LogWarning(
                    $"[FurnitureObjectFactory] Skip convex MeshCollider for {meshFilter.gameObject.name}. " +
                    $"vertexCount={mesh.vertexCount} > {maxConvexMeshVertices}. Using BoxCollider fallback."
                );
                continue;
            }

            GameObject meshObject = meshFilter.gameObject;

            MeshCollider meshCollider = meshObject.GetComponent<MeshCollider>();
            if (meshCollider == null)
                meshCollider = meshObject.AddComponent<MeshCollider>();

            meshCollider.sharedMesh = null;
            meshCollider.sharedMesh = mesh;
            meshCollider.convex = convexMeshCollider;

            count++;

            Debug.Log(
                $"[FurnitureObjectFactory] MeshCollider added: {meshObject.name}, " +
                $"mesh={mesh.name}, vertices={mesh.vertexCount}, convex={meshCollider.convex}"
            );
        }

        return count;
    }

    private void AddBoxColliderFallback(GameObject root, FurnitureObjectResult result)
    {
        Vector3 colliderSize = FixTargetSizeByLabel(result.label, GetTargetSize(result));
        Vector3 colliderCenter = ToVector3(result.collider_center, new Vector3(0f, colliderSize.y * 0.5f, 0f));

        BoxCollider box = root.GetComponent<BoxCollider>();
        if (box == null)
            box = root.AddComponent<BoxCollider>();

        box.enabled = true;
        box.center = colliderCenter;
        box.size = colliderSize;

        Debug.Log($"[FurnitureObjectFactory] Added fallback BoxCollider to {root.name}, size={colliderSize}, center={colliderCenter}");
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
        Vector3 targetSize = FixTargetSizeByLabel(result.label, GetTargetSize(result));

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

        Vector3 scaleFactor = GetScaleFactorByLabel(result.label, targetSize, currentSize);

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
                $"Label={result.label}, Target={targetSize}, Before={currentSize}, After={newBounds.size}, ScaleFactor={scaleFactor}"
            );
        }
    }

    private Vector3 GetScaleFactorByLabel(string label, Vector3 targetSize, Vector3 currentBoundsSize)
    {
        string lower = SafeLower(label);

        bool forceNonUniform = ShouldForceNonUniformSize(lower);
        bool preserveRatio = preserveFanAndChairMeshRatio && ShouldPreserveMeshRatio(lower);

        if (forceNonUniform && nonUniformScale)
        {
            return new Vector3(
                targetSize.x / currentBoundsSize.x,
                targetSize.y / currentBoundsSize.y,
                targetSize.z / currentBoundsSize.z
            );
        }

        if (preserveRatio)
        {
            float referenceFactor = targetSize.y / currentBoundsSize.y;
            if (!float.IsFinite(referenceFactor) || referenceFactor <= 0f)
            {
                referenceFactor = Mathf.Min(
                    targetSize.x / currentBoundsSize.x,
                    targetSize.y / currentBoundsSize.y,
                    targetSize.z / currentBoundsSize.z
                );
            }

            return new Vector3(referenceFactor, referenceFactor, referenceFactor);
        }

        if (nonUniformScale)
        {
            return new Vector3(
                targetSize.x / currentBoundsSize.x,
                targetSize.y / currentBoundsSize.y,
                targetSize.z / currentBoundsSize.z
            );
        }

        float factor = Mathf.Min(
            targetSize.x / currentBoundsSize.x,
            targetSize.y / currentBoundsSize.y,
            targetSize.z / currentBoundsSize.z
        );

        return new Vector3(factor, factor, factor);
    }

    private Vector3 FixTargetSizeByLabel(string label, Vector3 targetSize)
    {
        string lower = SafeLower(label);

        if (targetSize.x <= 0f || targetSize.y <= 0f || targetSize.z <= 0f)
            return targetSize;

        if (lower == "bed" || lower.Contains("bed"))
        {
            float width = Mathf.Max(targetSize.x, 0.85f);
            float height = Mathf.Clamp(Mathf.Max(targetSize.y, 0.35f), 0.30f, 0.85f);
            float minLength = Mathf.Max(width * bedMinLengthWidthRatio, 1.65f);
            float length = Mathf.Max(targetSize.z, minLength);

            if (clampBedLengthStretch)
            {
                float maxLength = Mathf.Max(width * bedMaxLengthWidthRatio, minLength);
                length = Mathf.Min(length, maxLength);
            }

            // Keep server axis convention: X=width, Z=length.
            // This corrects square SAM3D beds but prevents over-stretching.
            return new Vector3(width, height, length);
        }

        if (lower == "tv" || lower.Contains("television"))
        {
            return new Vector3(
                Mathf.Max(targetSize.x, 0.50f),
                Mathf.Max(targetSize.y, 0.28f),
                Mathf.Clamp(targetSize.z, 0.04f, 0.18f)
            );
        }

        if (lower == "picture_frame" || lower.Contains("picture") || lower.Contains("frame"))
        {
            return new Vector3(
                Mathf.Max(targetSize.x, 0.25f),
                Mathf.Max(targetSize.y, 0.25f),
                0.035f
            );
        }

        if (lower == "wall_clock" || lower.Contains("clock"))
        {
            float d = Mathf.Max(targetSize.x, targetSize.y, 0.25f);
            return new Vector3(d, d, 0.04f);
        }

        if (lower == "standing_air_conditioner" || lower.Contains("air_conditioner"))
        {
            return new Vector3(
                Mathf.Clamp(targetSize.x, 0.25f, 0.90f),
                Mathf.Clamp(targetSize.y, 1.00f, 2.20f),
                Mathf.Clamp(targetSize.z, 0.20f, 0.90f)
            );
        }

        if (lower == "bookshelf" || lower.Contains("bookshelf") || lower.Contains("shelf"))
        {
            return new Vector3(
                Mathf.Clamp(targetSize.x, 0.35f, 1.80f),
                Mathf.Clamp(targetSize.y, 0.80f, 2.60f),
                Mathf.Clamp(targetSize.z, 0.20f, 0.90f)
            );
        }

        if (lower == "cabinet" || lower.Contains("cabinet"))
        {
            return new Vector3(
                Mathf.Clamp(targetSize.x, 0.35f, 2.00f),
                Mathf.Clamp(targetSize.y, 0.35f, 2.40f),
                Mathf.Clamp(targetSize.z, 0.20f, 1.10f)
            );
        }

        return targetSize;
    }

    private bool ShouldForceNonUniformSize(string lowerLabel)
    {
        return lowerLabel.Contains("bed") ||
               lowerLabel.Contains("tv") ||
               lowerLabel.Contains("television") ||
               lowerLabel.Contains("cabinet") ||
               lowerLabel.Contains("bookshelf") ||
               lowerLabel.Contains("shelf") ||
               lowerLabel.Contains("air_conditioner") ||
               lowerLabel.Contains("picture") ||
               lowerLabel.Contains("frame") ||
               lowerLabel.Contains("clock");
    }

    private bool ShouldPreserveMeshRatio(string lowerLabel)
    {
        return lowerLabel.Contains("fan") || lowerLabel.Contains("chair");
    }


    private bool ShouldTryMeshColliderForLabel(string label)
    {
        if (!meshColliderOnlyForFanAndChair)
            return true;

        string lower = SafeLower(label);
        return lower.Contains("fan") || lower.Contains("chair");
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

    private Vector3 GetTargetSize(FurnitureObjectResult result)
    {
        Vector3 sizeWorld = ToVector3(result.size_world, Vector3.zero);
        if (sizeWorld.x > 0f && sizeWorld.y > 0f && sizeWorld.z > 0f)
            return sizeWorld;

        Vector3 targetSize = ToVector3(result.target_size, Vector3.zero);
        if (targetSize.x > 0f && targetSize.y > 0f && targetSize.z > 0f)
            return targetSize;

        Vector3 colliderSize = ToVector3(result.collider_size, Vector3.zero);
        if (colliderSize.x > 0f && colliderSize.y > 0f && colliderSize.z > 0f)
            return colliderSize;

        return ToVector3(result.scale, Vector3.one);
    }

    private Vector3 GetRotationEuler(FurnitureObjectResult result)
    {
        Vector3 rotationEuler = ToVector3(result.rotation, Vector3.zero);

        if ((result.rotation == null || result.rotation.Length < 3) && Mathf.Abs(result.rotation_yaw_deg) > 0.0001f)
        {
            rotationEuler = new Vector3(0f, result.rotation_yaw_deg, 0f);
        }

        return rotationEuler;
    }

    private Vector3 ToVector3(float[] arr, Vector3 fallback)
    {
        if (arr == null || arr.Length < 3)
            return fallback;

        return new Vector3(arr[0], arr[1], arr[2]);
    }

    private string SafeLower(string value)
    {
        return string.IsNullOrEmpty(value) ? string.Empty : value.ToLowerInvariant();
    }

    private float EstimateMass(Vector3 size)
    {
        float volume = Mathf.Abs(size.x * size.y * size.z);
        return Mathf.Clamp(volume * 30.0f, 5.0f, 120.0f);
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// HoloLens2 / OpenXR Spatial Mapping mesh를 Unity Scene의 MeshCollider로 생성/유지하는 컴포넌트입니다.
///
/// 목적:
/// - 실제 공간 Spatial Mapping mesh를 Unity Physics.Raycast 대상 Collider로 만들기
/// - HoloLensFurnitureCaptureAndUpload의 depth_samples raycast가 실제 공간 mesh에 hit되도록 하기
///
/// 사용 위치:
/// - 별도 GameObject 예: SpatialMappingSystem
/// - HoloLens UWP 빌드에서 동작
///
/// 주의:
/// - Unity Project Settings > Player > UWP Capabilities에서 SpatialPerception 필요
/// - OpenXR / HoloLens 설정에서 Spatial Mapping 또는 Meshing provider가 활성화되어 있어야 함
/// - depth_samples용 Raycast LayerMask는 이 컴포넌트가 생성하는 mesh layer와 일치해야 함
/// </summary>
public class HoloLensSpatialMappingMeshProvider : MonoBehaviour
{
    [Header("Lifecycle")]
    [Tooltip("OnEnable 시 Spatial Mapping mesh 생성을 시작합니다.")]
    [SerializeField] private bool startOnEnable = true;

    [Tooltip("OnDisable 시 XRMeshSubsystem을 멈춥니다. 생성된 MeshCollider는 clearMeshesOnDisable 값에 따라 유지/삭제됩니다.")]
    [SerializeField] private bool stopSubsystemOnDisable = true;

    [Tooltip("OnDisable 시 생성된 mesh GameObject를 모두 제거합니다. 보통 false를 권장합니다.")]
    [SerializeField] private bool clearMeshesOnDisable = false;

    [Header("Performance")]
    [Tooltip("Spatial mesh density 요청값입니다. 0~1 범위이며 provider가 해석합니다. HoloLens2 부하를 줄이려면 0.2~0.4 권장.")]
    [SerializeField, Range(0.0f, 1.0f)] private float meshDensity = 0.30f;

    [Tooltip("Spatial mesh 변경 사항을 확인하는 주기입니다. 매 프레임 갱신하지 말고 1~2초 이상을 권장합니다.")]
    [SerializeField, Min(0.1f)] private float meshInfoUpdateIntervalSeconds = 1.5f;

    [Tooltip("동시에 GenerateMeshAsync를 요청할 최대 개수입니다. HoloLens2에서는 1~2 권장.")]
    [SerializeField, Range(1, 8)] private int maxConcurrentMeshGenerations = 2;

    [Tooltip("한 번의 업데이트 주기에서 queue에 추가할 최대 mesh 개수입니다.")]
    [SerializeField, Range(1, 64)] private int maxMeshesQueuedPerUpdate = 8;

    [Tooltip("대기열 최대 크기입니다. 너무 크면 오래된 mesh 생성 요청이 쌓입니다.")]
    [SerializeField, Range(8, 256)] private int maxQueuedMeshCount = 48;

    [Tooltip("지정한 시간 후 Spatial Mapping 업데이트를 멈추고 현재 mesh collider만 유지합니다. 0 이하이면 자동 정지하지 않습니다.")]
    [SerializeField, Min(0.0f)] private float autoFreezeAfterSeconds = 0.0f;

    [Header("Collider / Rendering")]
    [Tooltip("true이면 MeshCollider를 생성합니다. depth_samples raycast에는 반드시 true여야 합니다.")]
    [SerializeField] private bool generateMeshColliders = true;

    [Tooltip("true이면 Spatial Mapping mesh를 시각화합니다. 성능상 평소에는 false 권장.")]
    [SerializeField] private bool showSpatialMesh = false;

    [Tooltip("showSpatialMesh가 true일 때 사용할 material입니다. 비워두면 Unity 기본 material이 사용될 수 있습니다.")]
    [SerializeField] private Material spatialMeshMaterial;

    [Tooltip("mesh normal vertex attribute 요청 여부입니다. depth raycast hit normal은 MeshCollider에서 얻을 수 있으므로 기본 false 권장.")]
    [SerializeField] private bool requestMeshNormals = false;

    [Header("Layer")]
    [Tooltip("Spatial mesh GameObject에 적용할 Layer 이름입니다. Project Settings > Tags and Layers에서 먼저 만들어두세요.")]
    [SerializeField] private string spatialMeshLayerName = "SpatialMesh";

    [Tooltip("spatialMeshLayerName을 찾지 못했을 때 사용할 fallback layer index입니다. Default는 0.")]
    [SerializeField, Range(0, 31)] private int fallbackLayerIndex = 0;

    [Header("Debug")]
    [SerializeField] private bool verboseLog = false;

    private XRMeshSubsystem meshSubsystem;
    private readonly List<XRMeshSubsystem> meshSubsystems = new List<XRMeshSubsystem>();
    private readonly List<MeshInfo> meshInfos = new List<MeshInfo>();
    private readonly Dictionary<MeshId, SpatialMeshEntry> meshEntries = new Dictionary<MeshId, SpatialMeshEntry>();
    private readonly Queue<MeshId> meshGenerationQueue = new Queue<MeshId>();
    private readonly HashSet<MeshId> queuedMeshIds = new HashSet<MeshId>();
    private readonly HashSet<MeshId> generatingMeshIds = new HashSet<MeshId>();

    private Transform meshRoot;
    private int resolvedSpatialMeshLayer;
    private float nextMeshInfoUpdateTime;
    private float mappingStartedTime;
    private bool isFrozen;

    public bool IsRunning => meshSubsystem != null && meshSubsystem.running && !isFrozen;
    public bool HasMeshSubsystem => meshSubsystem != null;
    public int MeshObjectCount => meshEntries.Count;
    public int PendingQueueCount => meshGenerationQueue.Count;
    public int GeneratingCount => generatingMeshIds.Count;
    public int SpatialMeshLayer => resolvedSpatialMeshLayer;
    public LayerMask SpatialMeshRaycastMask => 1 << resolvedSpatialMeshLayer;

    private void Awake()
    {
        resolvedSpatialMeshLayer = ResolveSpatialMeshLayer();
        EnsureMeshRoot();
    }

    private void OnEnable()
    {
        if (startOnEnable)
        {
            StartMapping();
        }
    }

    private void OnDisable()
    {
        if (stopSubsystemOnDisable)
        {
            StopMappingKeepMeshes();
        }

        if (clearMeshesOnDisable)
        {
            ClearGeneratedMeshes();
        }
    }

    private void Update()
    {
        if (!IsRunning)
        {
            return;
        }

        if (autoFreezeAfterSeconds > 0.0f && Time.unscaledTime - mappingStartedTime >= autoFreezeAfterSeconds)
        {
            FreezeMappingKeepMeshes();
            return;
        }

        if (Time.unscaledTime >= nextMeshInfoUpdateTime)
        {
            nextMeshInfoUpdateTime = Time.unscaledTime + meshInfoUpdateIntervalSeconds;
            PollMeshInfosAndQueueChanges();
        }

        ProcessMeshGenerationQueue();
    }

    [ContextMenu("Start Mapping")]
    public void StartMapping()
    {
        EnsureMeshRoot();
        resolvedSpatialMeshLayer = ResolveSpatialMeshLayer();
        isFrozen = false;

        if (!TryResolveMeshSubsystem())
        {
            Debug.LogWarning("[SpatialMappingMeshProvider] XRMeshSubsystem을 찾지 못했습니다. OpenXR/HoloLens Meshing 설정과 SpatialPerception capability를 확인하세요.");
            return;
        }

        if (!meshSubsystem.running)
        {
            meshSubsystem.Start();
        }

        meshSubsystem.meshDensity = meshDensity;
        mappingStartedTime = Time.unscaledTime;
        nextMeshInfoUpdateTime = 0.0f;

        Debug.Log(
            "[SpatialMappingMeshProvider] Spatial Mapping 시작\n" +
            $"meshDensity: {meshDensity}\n" +
            $"updateInterval: {meshInfoUpdateIntervalSeconds}s\n" +
            $"layer: {resolvedSpatialMeshLayer} ({LayerMask.LayerToName(resolvedSpatialMeshLayer)})\n" +
            $"raycastMask: {SpatialMeshRaycastMask.value}"
        );
    }

    [ContextMenu("Freeze Mapping Keep Meshes")]
    public void FreezeMappingKeepMeshes()
    {
        StopMappingKeepMeshes();
        isFrozen = true;
        Debug.Log($"[SpatialMappingMeshProvider] Spatial Mapping 업데이트 정지. 현재 mesh collider 유지: {meshEntries.Count}개");
    }

    [ContextMenu("Stop Mapping Keep Meshes")]
    public void StopMappingKeepMeshes()
    {
        if (meshSubsystem != null && meshSubsystem.running)
        {
            meshSubsystem.Stop();
        }

        queuedMeshIds.Clear();
        meshGenerationQueue.Clear();
        generatingMeshIds.Clear();

        Debug.Log($"[SpatialMappingMeshProvider] Spatial Mapping 정지. mesh collider 유지: {meshEntries.Count}개");
    }

    [ContextMenu("Clear Generated Meshes")]
    public void ClearGeneratedMeshes()
    {
        queuedMeshIds.Clear();
        meshGenerationQueue.Clear();
        generatingMeshIds.Clear();

        foreach (KeyValuePair<MeshId, SpatialMeshEntry> pair in meshEntries)
        {
            if (pair.Value != null && pair.Value.gameObject != null)
            {
                Destroy(pair.Value.gameObject);
            }
        }

        meshEntries.Clear();
        Debug.Log("[SpatialMappingMeshProvider] 생성된 Spatial Mesh GameObject를 모두 제거했습니다.");
    }

    public void SetVisible(bool visible)
    {
        showSpatialMesh = visible;

        foreach (KeyValuePair<MeshId, SpatialMeshEntry> pair in meshEntries)
        {
            if (pair.Value != null && pair.Value.renderer != null)
            {
                pair.Value.renderer.enabled = showSpatialMesh;
            }
        }
    }

    public void SetMeshDensity(float density)
    {
        meshDensity = Mathf.Clamp01(density);

        if (meshSubsystem != null)
        {
            meshSubsystem.meshDensity = meshDensity;
        }
    }

    public void SetUpdateInterval(float seconds)
    {
        meshInfoUpdateIntervalSeconds = Mathf.Max(0.1f, seconds);
    }

    private bool TryResolveMeshSubsystem()
    {
        if (meshSubsystem != null)
        {
            return true;
        }

        meshSubsystems.Clear();
        SubsystemManager.GetSubsystems(meshSubsystems);

        for (int i = 0; i < meshSubsystems.Count; i++)
        {
            if (meshSubsystems[i] != null)
            {
                meshSubsystem = meshSubsystems[i];
                return true;
            }
        }

        return false;
    }

    private void PollMeshInfosAndQueueChanges()
    {
        if (meshSubsystem == null)
        {
            return;
        }

        meshInfos.Clear();

        if (!meshSubsystem.TryGetMeshInfos(meshInfos))
        {
            return;
        }

        int queuedThisUpdate = 0;

        for (int i = 0; i < meshInfos.Count; i++)
        {
            MeshInfo info = meshInfos[i];

            switch (info.ChangeState)
            {
                case MeshChangeState.Added:
                case MeshChangeState.Updated:
                    if (queuedThisUpdate >= maxMeshesQueuedPerUpdate)
                    {
                        return;
                    }

                    if (TryQueueMeshGeneration(info.MeshId))
                    {
                        queuedThisUpdate++;
                    }
                    break;

                case MeshChangeState.Removed:
                    RemoveMesh(info.MeshId);
                    break;
            }
        }

        if (verboseLog && queuedThisUpdate > 0)
        {
            Debug.Log(
                "[SpatialMappingMeshProvider] Mesh 변경 감지\n" +
                $"reported: {meshInfos.Count}, queued: {queuedThisUpdate}, existing: {meshEntries.Count}"
            );
        }
    }

    private bool TryQueueMeshGeneration(MeshId meshId)
    {
        if (generatingMeshIds.Contains(meshId) || queuedMeshIds.Contains(meshId))
        {
            return false;
        }

        if (meshGenerationQueue.Count >= maxQueuedMeshCount)
        {
            return false;
        }

        queuedMeshIds.Add(meshId);
        meshGenerationQueue.Enqueue(meshId);
        return true;
    }

    private void ProcessMeshGenerationQueue()
    {
        if (meshSubsystem == null)
        {
            return;
        }

        while (generatingMeshIds.Count < maxConcurrentMeshGenerations && meshGenerationQueue.Count > 0)
        {
            MeshId meshId = meshGenerationQueue.Dequeue();
            queuedMeshIds.Remove(meshId);

            SpatialMeshEntry entry = GetOrCreateMeshEntry(meshId);
            if (entry == null)
            {
                continue;
            }

            generatingMeshIds.Add(meshId);

            MeshVertexAttributes attributes = requestMeshNormals
                ? MeshVertexAttributes.Normals
                : MeshVertexAttributes.None;

            MeshCollider meshCollider = generateMeshColliders ? entry.collider : null;
            meshSubsystem.GenerateMeshAsync(meshId, entry.mesh, meshCollider, attributes, OnMeshGenerated);
        }
    }

    private void OnMeshGenerated(MeshGenerationResult result)
    {
        generatingMeshIds.Remove(result.MeshId);

        if (result.Status != MeshGenerationStatus.Success)
        {
            if (verboseLog)
            {
                Debug.LogWarning($"[SpatialMappingMeshProvider] Mesh 생성 실패: {result.MeshId}, status: {result.Status}");
            }
            return;
        }

        if (!meshEntries.TryGetValue(result.MeshId, out SpatialMeshEntry entry) || entry == null)
        {
            return;
        }

        // 기본 GenerateMeshAsync overload는 vertices를 session space로 생성합니다.
        // 따라서 GameObject transform은 identity로 유지해서 Unity world/session 좌표 raycast와 맞춥니다.
        entry.transform.localPosition = Vector3.zero;
        entry.transform.localRotation = Quaternion.identity;
        entry.transform.localScale = Vector3.one;
        entry.gameObject.layer = resolvedSpatialMeshLayer;

        if (entry.renderer != null)
        {
            entry.renderer.enabled = showSpatialMesh;
            if (spatialMeshMaterial != null)
            {
                entry.renderer.sharedMaterial = spatialMeshMaterial;
            }
        }

        if (verboseLog)
        {
            Debug.Log(
                "[SpatialMappingMeshProvider] Mesh 생성 완료\n" +
                $"meshId: {result.MeshId}\n" +
                $"vertices: {(entry.mesh != null ? entry.mesh.vertexCount : 0)}\n" +
                $"collider: {(entry.collider != null && entry.collider.enabled)}"
            );
        }
    }

    private SpatialMeshEntry GetOrCreateMeshEntry(MeshId meshId)
    {
        if (meshEntries.TryGetValue(meshId, out SpatialMeshEntry existing) && existing != null)
        {
            return existing;
        }

        EnsureMeshRoot();

        GameObject meshObject = new GameObject("SpatialMesh_" + meshEntries.Count.ToString("0000"));
        meshObject.transform.SetParent(meshRoot, false);
        meshObject.transform.localPosition = Vector3.zero;
        meshObject.transform.localRotation = Quaternion.identity;
        meshObject.transform.localScale = Vector3.one;
        meshObject.layer = resolvedSpatialMeshLayer;

        MeshFilter filter = meshObject.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = meshObject.AddComponent<MeshRenderer>();
        meshRenderer.enabled = showSpatialMesh;
        if (spatialMeshMaterial != null)
        {
            meshRenderer.sharedMaterial = spatialMeshMaterial;
        }

        MeshCollider meshCollider = null;
        if (generateMeshColliders)
        {
            meshCollider = meshObject.AddComponent<MeshCollider>();
            meshCollider.convex = false;
            meshCollider.enabled = true;
        }

        Mesh mesh = new Mesh
        {
            name = "SpatialMappingMesh_" + meshEntries.Count.ToString("0000"),
            indexFormat = UnityEngine.Rendering.IndexFormat.UInt32
        };

        filter.sharedMesh = mesh;

        SpatialMeshEntry entry = new SpatialMeshEntry
        {
            meshId = meshId,
            gameObject = meshObject,
            transform = meshObject.transform,
            filter = filter,
            renderer = meshRenderer,
            collider = meshCollider,
            mesh = mesh
        };

        meshEntries.Add(meshId, entry);
        return entry;
    }

    private void RemoveMesh(MeshId meshId)
    {
        queuedMeshIds.Remove(meshId);
        generatingMeshIds.Remove(meshId);

        if (!meshEntries.TryGetValue(meshId, out SpatialMeshEntry entry))
        {
            return;
        }

        if (entry != null && entry.gameObject != null)
        {
            Destroy(entry.gameObject);
        }

        meshEntries.Remove(meshId);

        if (verboseLog)
        {
            Debug.Log($"[SpatialMappingMeshProvider] Mesh 제거: {meshId}");
        }
    }

    private void EnsureMeshRoot()
    {
        if (meshRoot != null)
        {
            return;
        }

        GameObject root = new GameObject("HoloLensSpatialMappingMeshes");
        root.transform.SetParent(transform, false);
        root.transform.localPosition = Vector3.zero;
        root.transform.localRotation = Quaternion.identity;
        root.transform.localScale = Vector3.one;
        meshRoot = root.transform;
    }

    private int ResolveSpatialMeshLayer()
    {
        if (!string.IsNullOrWhiteSpace(spatialMeshLayerName))
        {
            int namedLayer = LayerMask.NameToLayer(spatialMeshLayerName);
            if (namedLayer >= 0)
            {
                return namedLayer;
            }

            Debug.LogWarning(
                "[SpatialMappingMeshProvider] Layer 이름을 찾지 못했습니다: " + spatialMeshLayerName + "\n" +
                $"Project Settings > Tags and Layers에서 '{spatialMeshLayerName}' Layer를 만들거나 fallbackLayerIndex를 사용합니다."
            );
        }

        return Mathf.Clamp(fallbackLayerIndex, 0, 31);
    }

    [Serializable]
    private class SpatialMeshEntry
    {
        public MeshId meshId;
        public GameObject gameObject;
        public Transform transform;
        public MeshFilter filter;
        public MeshRenderer renderer;
        public MeshCollider collider;
        public Mesh mesh;
    }
}

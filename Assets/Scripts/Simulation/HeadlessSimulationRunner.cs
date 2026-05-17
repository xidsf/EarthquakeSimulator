using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using GLTFast;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ?œë²„(Ubuntu, headless) Unity?ì„œ ?¤í–‰?˜ëŠ” ì§€ì§??œë??ˆì´???¤ì??¤íŠ¸?ˆì´?°ì…?ˆë‹¤.
///
/// ?ë¦„:
/// 1. --input-dir/simulation_input.json + ê°€êµ?GLB(visual/convex-hull) ë¡œë“œ
/// 2. ì§€ì§„íŒŒ Yì¶??Œì „ 0/90/180/270Â° 4ê°?arenaë¥??™ì‹œ??êµ¬ì„±(ê°™ì? ë°?ê°€êµ?ë³µì œ)
/// 3. before 5ë·?ìº¡ì²˜ ??4 arena ?™ì‹œ ?œë??ˆì´????after 5ë·?ìº¡ì²˜
/// 4. --output-dir??PNG 40??+ result_transforms.json ê¸°ë¡ ??ì¢…ë£Œ
///
/// .unity ?¬ì„ ?˜ì •?˜ì? ?Šê³  SimulationBootstrap??ScheduleAutoSpawn()?¼ë¡œ ?ì„±?©ë‹ˆ??
/// </summary>
public class HeadlessSimulationRunner : MonoBehaviour
{
    private static readonly int[] SeismicAngles = { 0, 90, 180, 270 };
    private const float ArenaSpacingMeters = 1000f;
    private const float SettleSeconds = 0.7f;
    private const float SimulationTimeoutSeconds = 180f;
    private const float ToppleTiltThresholdDeg = 45f;

    private bool started;

    public static void ScheduleAutoSpawn()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        if (!SimulationRunConfig.HasHeadlessSimulationPaths()) return;

        GameObject go = new GameObject("HeadlessSimulationRunner");
        go.AddComponent<HeadlessSimulationRunner>();
    }

    private void Start()
    {
        if (started) return;
        started = true;
        StartCoroutine(RunPipeline());
    }

    private IEnumerator RunPipeline()
    {
        string inputDir = SimulationRunConfig.InputDir;
        string outputDir = SimulationRunConfig.OutputDir;
        int exitCode = 0;

        SimulationInputDocument input = null;
        try
        {
            string jsonPath = Path.Combine(inputDir, "simulation_input.json");
            if (!File.Exists(jsonPath))
                throw new FileNotFoundException($"simulation_input.json not found: {jsonPath}");

            input = JsonConvert.DeserializeObject<SimulationInputDocument>(File.ReadAllText(jsonPath));
            if (input == null) throw new InvalidOperationException("Failed to parse simulation_input.json");

            if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);
            Debug.Log($"[HeadlessSimulationRunner] Loaded input. session:{input.session_id}, furnitures:{input.furnitures?.Count ?? 0}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[HeadlessSimulationRunner] Input load failed: {ex.Message}\n{ex.StackTrace}");
            Quit(1);
            yield break;
        }

        List<Arena> arenas = new List<Arena>();

        for (int i = 0; i < SeismicAngles.Length; i++)
        {
            Arena arena = new Arena(SeismicAngles[i], new Vector3(i * ArenaSpacingMeters, 0f, 0f));
            arena.BuildRoom(input.room);
            yield return arena.BuildFurnitures(input, inputDir, this);
            arena.ConfigureSimulator(SimulationRunConfig.ThaCsvPath, input.building, ComputeRoomClearHeight(input.room));
            arenas.Add(arena);
        }

        // ë¬¼ë¦¬ ?ˆì •??(ê°€êµ¬ê? ë°”ë‹¥/ì§€ì§€ë©´ì— ?•ì°©).
        yield return new WaitForSeconds(SettleSeconds);

        SimulationCaptureRig rig = new SimulationCaptureRig();

        CaptureAllArenas(arenas, rig, outputDir, "before");

        foreach (Arena arena in arenas) arena.RecordBefore();
        foreach (Arena arena in arenas) arena.StartSimulation();

        float elapsed = 0f;
        yield return null;
        while (!AllArenasDone(arenas) && elapsed < SimulationTimeoutSeconds)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }
        if (elapsed >= SimulationTimeoutSeconds)
            Debug.LogWarning("[HeadlessSimulationRunner] Simulation timed out; capturing current state.");

        // ?œë??ˆì´??ì¢…ë£Œ ???”ì—¬ ?”ë“¤ë¦??•ì°©.
        yield return new WaitForSeconds(SettleSeconds);

        CaptureAllArenas(arenas, rig, outputDir, "after");
        foreach (Arena arena in arenas) arena.RecordAfter();

        try
        {
            SimulationResultDocument result = new SimulationResultDocument { session_id = input.session_id };
            foreach (Arena arena in arenas) result.arenas.Add(arena.BuildResult(ToppleTiltThresholdDeg));

            string resultPath = Path.Combine(outputDir, "result_transforms.json");
            File.WriteAllText(resultPath, JsonConvert.SerializeObject(result, Formatting.Indented));
            Debug.Log($"[HeadlessSimulationRunner] Wrote result: {resultPath}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[HeadlessSimulationRunner] Result write failed: {ex.Message}");
            exitCode = 1;
        }

        rig.Dispose();
        Quit(exitCode);
    }

    private static void CaptureAllArenas(List<Arena> arenas, SimulationCaptureRig rig, string outputDir, string phase)
    {
        foreach (Arena arena in arenas)
        {
            Bounds bounds = arena.GetWorldBounds();
            string prefix = $"rot{arena.SeismicAngle}_{phase}";

            // ?…êµ¬ ?œì‹œ??VLM ?ˆì¶œë¡??ë‹¨?©ìœ¼ë¡?top-down ë·°ì—?œë§Œ ë³´ì´ê²??œë‹¤.
            arena.SetEntranceMarkerVisible(false);
            for (int s = 0; s < SimulationCaptureRig.SideViewCount; s++)
                rig.Capture(bounds, Path.Combine(outputDir, $"{prefix}_side{s}.png"), s);

            arena.SetEntranceMarkerVisible(true);
            rig.Capture(bounds, Path.Combine(outputDir, $"{prefix}_top.png"), -1);
            arena.SetEntranceMarkerVisible(false);
        }
    }

    private static bool AllArenasDone(List<Arena> arenas)
    {
        foreach (Arena arena in arenas)
            if (!arena.IsDone) return false;
        return true;
    }

    private void Quit(int code)
    {
        Debug.Log($"[HeadlessSimulationRunner] Done. exitCode:{code}");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit(code);
#endif
    }

    // ---------------------------------------------------------------------
    // GLB ë¡œë”© (ë¡œì»¬ ?Œì¼, glTFast)
    // ---------------------------------------------------------------------

    public IEnumerator LoadGlb(string absolutePath, Transform parent, bool disableRenderers, Action<bool> onDone)
    {
        if (string.IsNullOrWhiteSpace(absolutePath) || !File.Exists(absolutePath))
        {
            Debug.LogWarning($"[HeadlessSimulationRunner] GLB not found: {absolutePath}");
            onDone?.Invoke(false);
            yield break;
        }

        string uri = new Uri(absolutePath).AbsoluteUri;
        GltfImport gltf = new GltfImport();

        Task<bool> loadTask = null;
        try { loadTask = gltf.Load(uri); }
        catch (Exception ex) { Debug.LogWarning($"[HeadlessSimulationRunner] glTFast Load failed: {ex.Message}"); onDone?.Invoke(false); yield break; }

        while (loadTask != null && !loadTask.IsCompleted) yield return null;
        if (loadTask == null || loadTask.IsFaulted || loadTask.IsCanceled || !loadTask.Result)
        {
            Debug.LogWarning($"[HeadlessSimulationRunner] glTFast Load result false: {absolutePath}");
            onDone?.Invoke(false);
            yield break;
        }

        Task<bool> instTask = null;
        try { instTask = gltf.InstantiateMainSceneAsync(parent); }
        catch (Exception ex) { Debug.LogWarning($"[HeadlessSimulationRunner] glTFast Instantiate failed: {ex.Message}"); onDone?.Invoke(false); yield break; }

        while (instTask != null && !instTask.IsCompleted) yield return null;
        bool ok = instTask != null && !instTask.IsFaulted && !instTask.IsCanceled && instTask.Result;

        if (ok && disableRenderers)
        {
            foreach (Renderer r in parent.GetComponentsInChildren<Renderer>(true))
                if (r != null) r.enabled = false;
        }

        onDone?.Invoke(ok);
    }

    // ---------------------------------------------------------------------
    // Arena: ??ì§€ì§„íŒŒ ?Œì „ê°ì— ?€??ë°?ê°€êµ??œë??ˆì´?????¸íŠ¸
    // ---------------------------------------------------------------------

    private class Arena
    {
        public int SeismicAngle { get; }
        public bool IsDone => simulator == null || !simulator.IsSimulating && simulationStarted;

        private readonly Transform roomRoot;       // ?´ë¼?´ì–¸??confirmedRoomRoot???€??arena ë¡œì»¬ ?ì )
        private readonly Transform roomStructureRoot;
        private readonly Transform furnitureRoot;
        private EarthquakeSimulator simulator;
        private bool simulationStarted;
        private GameObject entranceMarker;

        private readonly List<FurnitureRecord> records = new List<FurnitureRecord>();
        // ê³ ì •/ë²½ê±¸??ê°€êµ¬ì˜ Rigidbody. ë²?ë°”ë‹¥ê³??™ì¼?˜ê²Œ isKinematic ? ì??´ì•¼ ?˜ë©°,
        // EarthquakeSimulatorê°€ ë¬¼ë¦¬ ?¤ì •????–´?????¬ì ???€?ì´??
        private readonly List<Rigidbody> anchoredBodies = new List<Rigidbody>();

        public Arena(int seismicAngle, Vector3 worldOffset)
        {
            SeismicAngle = seismicAngle;

            GameObject arenaGo = new GameObject($"Arena_{seismicAngle}");
            arenaGo.transform.position = worldOffset;

            roomRoot = new GameObject("RoomRoot").transform;
            roomRoot.SetParent(arenaGo.transform, false);

            roomStructureRoot = new GameObject("RoomStructure").transform;
            roomStructureRoot.SetParent(roomRoot, false);

            furnitureRoot = new GameObject("Furniture").transform;
            furnitureRoot.SetParent(roomRoot, false);
        }

        public void BuildRoom(SimulationRoomGeometry room)
        {
            if (room == null) return;
            if (room.walls != null)
                for (int i = 0; i < room.walls.Count; i++) CreateBox(room.walls[i], $"Wall_{i}");
            if (room.floor != null) CreateBox(room.floor, "Floor");
            if (room.ceiling != null) CreateBox(room.ceiling, "Ceiling");

            if (room.entrance != null) CreateEntranceMarker(room.entrance, room.floor);
        }

        // ë°”ë‹¥???…êµ¬ ë°˜ê²½???˜í??´ëŠ” ?©ì‘???ê¸°?? VLM???ˆì¶œë¡œë? ê°€êµ¬ê? ë§‰ëŠ”ì§€
        // ?ë‹¨?˜ëŠ” ?”ì†Œ?´ë©° top-down ë·°ì—?œë§Œ ë³´ì´ê²?? ê??œë‹¤.
        private void CreateEntranceMarker(SimulationEntrance entrance, SimulationBox floor)
        {
            Vector3 local = ToV3(entrance.position);
            float floorTopY = floor != null ? floor.center[1] + floor.size[1] * 0.5f : local.y;
            float radius = entrance.radius > 0f ? entrance.radius : 1.5f;

            entranceMarker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            entranceMarker.name = "EntranceMarker";
            entranceMarker.transform.SetParent(roomRoot, false);
            entranceMarker.transform.localPosition = new Vector3(local.x, floorTopY + 0.02f, local.z);
            // Unity cylinder ê¸°ë³¸ ì§€ë¦?1m ??scale = ì§€ë¦?= 2*radius. ?’ì´???©ì‘?˜ê²Œ.
            entranceMarker.transform.localScale = new Vector3(radius * 2f, 0.02f, radius * 2f);

            Collider markerCollider = entranceMarker.GetComponent<Collider>();
            if (markerCollider != null) UnityEngine.Object.Destroy(markerCollider);

            Renderer markerRenderer = entranceMarker.GetComponent<Renderer>();
            if (markerRenderer != null) markerRenderer.material.color = new Color(0.1f, 0.85f, 0.25f, 1f);

            entranceMarker.SetActive(false);
        }

        public void SetEntranceMarkerVisible(bool visible)
        {
            if (entranceMarker != null) entranceMarker.SetActive(visible);
        }

        private void CreateBox(SimulationBox box, string boxName)
        {
            if (box == null) return;

            GameObject go = new GameObject(boxName);
            go.transform.SetParent(roomStructureRoot, false);
            go.transform.localPosition = ToV3(box.center);
            go.transform.localRotation = Quaternion.Euler(ToV3(box.euler));

            BoxCollider col = go.AddComponent<BoxCollider>();
            col.center = Vector3.zero;
            col.size = ToV3(box.size);

            Rigidbody rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        public IEnumerator BuildFurnitures(SimulationInputDocument input, string inputDir, HeadlessSimulationRunner runner)
        {
            if (input.furnitures == null) yield break;

            foreach (SimulationFurnitureInstance inst in input.furnitures)
            {
                if (inst == null) continue;

                GameObject furnitureGo = new GameObject($"Furniture_{inst.furniture_id}_{inst.instance_index}");
                furnitureGo.transform.SetParent(furnitureRoot, false);
                furnitureGo.transform.localPosition = ToV3(inst.position);
                furnitureGo.transform.localRotation = ToQuat(inst.rotation);
                furnitureGo.transform.localScale = ToV3(inst.scale);

                // Visual GLB
                if (!string.IsNullOrWhiteSpace(inst.mesh_file))
                {
                    bool _ = false;
                    yield return runner.LoadGlb(Path.Combine(inputDir, inst.mesh_file), furnitureGo.transform, false, ok => _ = ok);
                }

                // Convex hull collider GLB
                bool hasCollider = false;
                if (!string.IsNullOrWhiteSpace(inst.collider_file))
                {
                    GameObject colliderHolder = new GameObject("ColliderHull");
                    colliderHolder.transform.SetParent(furnitureGo.transform, false);
                    bool colliderLoaded = false;
                    yield return runner.LoadGlb(Path.Combine(inputDir, inst.collider_file), colliderHolder.transform, true, ok => colliderLoaded = ok);

                    if (colliderLoaded)
                    {
                        foreach (MeshFilter mf in colliderHolder.GetComponentsInChildren<MeshFilter>(true))
                        {
                            if (mf == null || mf.sharedMesh == null) continue;
                            MeshCollider mc = mf.gameObject.AddComponent<MeshCollider>();
                            mc.sharedMesh = mf.sharedMesh;
                            mc.convex = true;
                            hasCollider = true;
                        }
                    }
                }

                if (!hasCollider)
                {
                    // convex hull???†ìœ¼ë©??œê° ë©”ì‰¬ bounds ê¸°ë°˜ BoxCollider ?´ë°±.
                    Bounds b = ComputeLocalRendererBounds(furnitureGo);
                    BoxCollider bc = furnitureGo.AddComponent<BoxCollider>();
                    bc.center = b.center;
                    bc.size = b.size.sqrMagnitude > 0f ? b.size : Vector3.one * 0.3f;
                }

                Rigidbody rb = furnitureGo.AddComponent<Rigidbody>();
                rb.mass = inst.mass_kg > 0f ? inst.mass_kg : 10f;

                // ê³ ì •/ë²½ê±¸??ê°€êµ¬ëŠ” ë²?ë°”ë‹¥ê³??™ì¼?˜ê²Œ isKinematic?¼ë¡œ ê³ ì •?œë‹¤.
                // (ì¡°ì¸??ë¯¸ì‚¬???¨ìˆœ ëª¨ë¸: ë²½ì— ?¬ë¦° ê°€êµ¬ê? ì¤‘ë ¥?¼ë¡œ ?¨ì–´ì§€ì§€ ?Šë„ë¡?
                bool anchored = inst.is_fixed || inst.is_floor_contactless;
                if (anchored)
                {
                    ApplyAnchoredKinematic(rb);
                    anchoredBodies.Add(rb);
                }
                else
                {
                    rb.isKinematic = false;
                    rb.useGravity = true;
                    rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                    rb.interpolation = RigidbodyInterpolation.Interpolate;
                }

                records.Add(new FurnitureRecord
                {
                    id = inst.furniture_id,
                    instanceIndex = inst.instance_index,
                    transform = furnitureGo.transform
                });
            }
        }

        public void ConfigureSimulator(string thaCsvPath, SimulationBuildingInfo building, float roomClearHeight)
        {
            simulator = furnitureRoot.gameObject.AddComponent<EarthquakeSimulator>();
            simulator.SetSimulationMode(EarthquakeSimulator.SimulationMode.AccelerationInertial);
            simulator.SetSceneReferences(null, furnitureRoot.gameObject, roomStructureRoot.gameObject);
            // SetSceneReferences ?´ë? ConfigurePhysicsObjectsê°€ ëª¨ë“  ê°€êµ?Rigidbody??            // useGravity=true / ContinuousDynamic????–´?´ë‹¤. ê³ ì • ê°€êµ¬ëŠ” ë²½ê³¼ ?™ì¼??            // kinematic ?¤ì •?¼ë¡œ ì¦‰ì‹œ ?˜ëŒë¦°ë‹¤.
            ReassertAnchoredBodies();
            simulator.SetSimulationAngle(SeismicAngle);
            if (!string.IsNullOrWhiteSpace(thaCsvPath)) simulator.SetCsvFilePath(thaCsvPath);

            if (building != null)
            {
                simulator.SetBuildingInfo(
                    ParseBuildingType(building.building_type),
                    building.floor,
                    building.total_floors,
                    ParsePiloti(building.piloti));
            }

            if (roomClearHeight > 0f) simulator.SetMeasuredRoomClearHeight(roomClearHeight);

            // ì§„ë„(MMI)???…ë ¥?¼ë¡œ ë°›ì? ?ŠëŠ”?? ê°€êµ??„ë„ ?•ë„/?„í—˜???‰ê?ê°€ ëª©ì ?´ë?ë¡?            // ??ƒ ìµœê°• ?œë‚˜ë¦¬ì˜¤(MMI 9)ë¡?ê°•ì œ?œë‹¤.
            simulator.SetApplyStrongScenarioScaling(true);
            simulator.SetStrongScenarioMmi(9);
        }

        // ë²?ë°”ë‹¥(EarthquakeSimulator.ConfigurePhysicsObjects??room ì²˜ë¦¬)ê³??™ì¼??        // kinematic ?¤ì •. ê³ ì •/ë²½ê±¸??ê°€êµ¬ê? ì¤‘ë ¥?¼ë¡œ ?¨ì–´ì§€ê±°ë‚˜ ê´€?±ë ¥??ë°€ë¦¬ì? ?ŠëŠ”??
        private static void ApplyAnchoredKinematic(Rigidbody rb)
        {
            if (rb == null) return;
            rb.useGravity = false;
            rb.isKinematic = true;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.constraints = RigidbodyConstraints.FreezeRotation;
            rb.interpolation = RigidbodyInterpolation.None;
        }

        private void ReassertAnchoredBodies()
        {
            for (int i = 0; i < anchoredBodies.Count; i++)
                ApplyAnchoredKinematic(anchoredBodies[i]);
        }

        private static EarthquakeSimulator.ResidentialBuildingType ParseBuildingType(string value)
        {
            return Enum.TryParse(value, true, out EarthquakeSimulator.ResidentialBuildingType t)
                ? t
                : EarthquakeSimulator.ResidentialBuildingType.Apartment;
        }

        private static EarthquakeSimulator.PilotiCondition ParsePiloti(string value)
        {
            return Enum.TryParse(value, true, out EarthquakeSimulator.PilotiCondition p)
                ? p
                : EarthquakeSimulator.PilotiCondition.Unknown;
        }

        public void StartSimulation()
        {
            simulationStarted = true;
            // ?œë??ˆì´???œì‘ ì§ì „?ë„ ê³ ì • ê°€êµ?kinematic???¬í™•??ë²½ê³¼ ?™ì¼ ? ì?).
            ReassertAnchoredBodies();
            if (simulator != null) simulator.StartSimulation();
        }

        public void RecordBefore()
        {
            foreach (FurnitureRecord r in records)
            {
                r.posBefore = r.transform.position;
                r.rotBefore = r.transform.rotation;
            }
        }

        public void RecordAfter()
        {
            foreach (FurnitureRecord r in records)
            {
                r.posAfter = r.transform.position;
                r.rotAfter = r.transform.rotation;
            }
        }

        public Bounds GetWorldBounds()
        {
            bool has = false;
            Bounds bounds = new Bounds();

            foreach (Collider c in roomStructureRoot.GetComponentsInChildren<Collider>(true))
            {
                if (c == null) continue;
                if (!has) { bounds = c.bounds; has = true; }
                else bounds.Encapsulate(c.bounds);
            }
            foreach (Renderer rend in furnitureRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (rend == null) continue;
                if (!has) { bounds = rend.bounds; has = true; }
                else bounds.Encapsulate(rend.bounds);
            }

            if (!has) bounds = new Bounds(roomRoot.position, Vector3.one * 3f);
            return bounds;
        }

        public SimulationArenaResult BuildResult(float toppleThresholdDeg)
        {
            SimulationArenaResult arenaResult = new SimulationArenaResult { seismic_angle_deg = SeismicAngle };
            foreach (FurnitureRecord r in records)
            {
                float tilt = Vector3.Angle(r.rotAfter * Vector3.up, Vector3.up);
                arenaResult.furnitures.Add(new SimulationFurnitureResult
                {
                    furniture_id = r.id,
                    instance_index = r.instanceIndex,
                    position_before = ToArray(r.posBefore),
                    rotation_before = ToArray(r.rotBefore),
                    position_after = ToArray(r.posAfter),
                    rotation_after = ToArray(r.rotAfter),
                    tilt_deg_after = tilt,
                    toppled = tilt > toppleThresholdDeg
                });
            }
            return arenaResult;
        }

        private static Bounds ComputeLocalRendererBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return new Bounds(Vector3.zero, Vector3.one * 0.3f);

            Bounds world = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) world.Encapsulate(renderers[i].bounds);

            Vector3 localCenter = root.transform.InverseTransformPoint(world.center);
            Vector3 lossy = root.transform.lossyScale;
            Vector3 localSize = new Vector3(
                lossy.x != 0f ? world.size.x / Mathf.Abs(lossy.x) : world.size.x,
                lossy.y != 0f ? world.size.y / Mathf.Abs(lossy.y) : world.size.y,
                lossy.z != 0f ? world.size.z / Mathf.Abs(lossy.z) : world.size.z);
            return new Bounds(localCenter, localSize);
        }
    }

    private class FurnitureRecord
    {
        public string id;
        public int instanceIndex;
        public Transform transform;
        public Vector3 posBefore;
        public Quaternion rotBefore;
        public Vector3 posAfter;
        public Quaternion rotAfter;
    }

    // ë°??´ë? ?œìˆ˜ ?’ì´ = ì²œì¥ ?„ë«ë©???ë°”ë‹¥ ?—ë©´ (room-local). EarthquakeSimulator??    // ì¸µê³  ì¶”ì •???¬ìš©??ê°€êµ??„ë„ ?‰ê? ?•í™•?„ë? ?’ì¸??
    private static float ComputeRoomClearHeight(SimulationRoomGeometry room)
    {
        if (room == null || room.floor == null || room.ceiling == null) return 0f;
        if (room.floor.center == null || room.floor.size == null) return 0f;
        if (room.ceiling.center == null || room.ceiling.size == null) return 0f;

        float floorTop = room.floor.center[1] + room.floor.size[1] * 0.5f;
        float ceilingBottom = room.ceiling.center[1] - room.ceiling.size[1] * 0.5f;
        return ceilingBottom - floorTop;
    }

    private static Vector3 ToV3(float[] a) =>
        a != null && a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : Vector3.zero;

    private static Quaternion ToQuat(float[] a) =>
        a != null && a.Length >= 4 ? new Quaternion(a[0], a[1], a[2], a[3]) : Quaternion.identity;

    private static float[] ToArray(Vector3 v) => new[] { v.x, v.y, v.z };
    private static float[] ToArray(Quaternion q) => new[] { q.x, q.y, q.z, q.w };
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;

public class ServerSimulationManager : MonoBehaviour
{
    [Header("Managers")]
    public SimulationCaptureManager captureManager;
    public SimulationDataRecorder dataRecorder;
    public EarthquakeSimulator earthquakeSimulator;

    [Header("Prefabs")]
    public GameObject labelPrefab;
    public GameObject entrancePrefab;

    [Header("Simulation")]
    public int[] simulationAngles = { 0, 90, 180, 270 };
    public float simulationDuration = 10.0f;
    public float simulationTimeoutSeconds = 180.0f;

    [Header("Earthquake Mode")]
    public EarthquakeSimulator.SimulationMode earthquakeSimulationMode = EarthquakeSimulator.SimulationMode.ShakeTable;
    public float inertialAccelerationMultiplier = 1.0f;
    public bool includeVerticalInertialAcceleration = false;
    public float shakeTableDisplacementMultiplier = 1.0f;
    public bool includeVerticalShakeTableDisplacement = false;
    public bool returnRoomToOriginAfterSimulation = true;
    public bool restoreFurnitureToOriginAfterCapture = true;

    private GameObject roomRoot;
    private GameObject roomStructureRoot;
    private Rigidbody roomRigidbody;
    private GameObject dynamicFurnitureRoot;
    private GameObject labelsRoot;
    private GameObject entranceMarker;
    private string sessionId;
    private string imageSavePath;
    private string csvSavePath;
    private SimulationInput inputData;

    private struct PoseData
    {
        public Vector3 localPosition;
        public Quaternion localRotation;
        public bool originalKinematic;
        public bool originalUseGravity;
    }

    private readonly Dictionary<GameObject, PoseData> initialPoses = new Dictionary<GameObject, PoseData>();

    public void InitializeAndStartPipeline(GameObject roomRootObject, List<GameObject> loadedFurnitures)
    {
        InitializeAndStartPipeline(roomRootObject, loadedFurnitures, null, null);
    }

    public void InitializeAndStartPipeline(
        GameObject roomRootObject,
        List<GameObject> loadedFurnitures,
        SimulationInput loadedInput,
        Transform floorTransform)
    {
        if (roomRootObject == null)
        {
            Debug.LogError("[ServerManager] roomRoot is null.");
            return;
        }

        Debug.Log("[ServerManager] Room and furniture load complete. Starting server simulation pipeline.");

        inputData = loadedInput;
        sessionId = ResolveSessionId(loadedInput);
        imageSavePath = ResolveImageOutputPath();
        csvSavePath = ResolveCsvOutputPath();
        roomRoot = roomRootObject;
        roomRigidbody = roomRoot.GetComponent<Rigidbody>();
        Debug.Log($"[ServerManager] Image output path: {imageSavePath}");
        Debug.Log($"[ServerManager] CSV output path: {csvSavePath}");

        BuildRuntimeRoots(loadedFurnitures);
        ConfigureLoadedFurniture(loadedFurnitures);
        CreateEntranceMarker(loadedInput != null ? loadedInput.room : null);
        ConfigureCaptureManager(floorTransform);
        ConfigureEarthquakeSimulator();

        StartCoroutine(RunMultiAngleSimulation(loadedFurnitures));
    }

    private string ResolveSessionId(SimulationInput loadedInput)
    {
        if (!string.IsNullOrWhiteSpace(SimulationRunConfig.SessionId)) return SimulationRunConfig.SessionId;
        if (loadedInput != null && !string.IsNullOrWhiteSpace(loadedInput.session_id)) return loadedInput.session_id;
        return DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
    }

    private string ResolveImageOutputPath()
    {
        if (!string.IsNullOrWhiteSpace(SimulationRunConfig.ImageOutputDir)) return SimulationRunConfig.ImageOutputDir;
        if (!string.IsNullOrWhiteSpace(SimulationRunConfig.OutputDir)) return SimulationRunConfig.OutputDir;
        return Path.Combine(Application.persistentDataPath, "ServerSimulationCaptures");
    }

    private string ResolveCsvOutputPath()
    {
        if (!string.IsNullOrWhiteSpace(SimulationRunConfig.CsvOutputDir)) return SimulationRunConfig.CsvOutputDir;
        if (!string.IsNullOrWhiteSpace(SimulationRunConfig.OutputDir)) return SimulationRunConfig.OutputDir;
        return Path.Combine(Application.persistentDataPath, "ServerSimulationCaptures");
    }

    private void BuildRuntimeRoots(List<GameObject> loadedFurnitures)
    {
        roomStructureRoot = new GameObject("RoomStructureRoot");
        roomStructureRoot.transform.SetParent(roomRoot.transform, false);
        ConfigureRoomStructureRigidbody(roomStructureRoot);

        HashSet<Transform> furnitureTransforms = new HashSet<Transform>();
        if (loadedFurnitures != null)
        {
            foreach (GameObject furniture in loadedFurnitures)
                if (furniture != null) furnitureTransforms.Add(furniture.transform);
        }

        List<Transform> childrenToMove = new List<Transform>();
        foreach (Transform child in roomRoot.transform)
        {
            if (child == roomStructureRoot.transform) continue;
            if (furnitureTransforms.Contains(child)) continue;
            childrenToMove.Add(child);
        }

        foreach (Transform child in childrenToMove)
            child.SetParent(roomStructureRoot.transform, true);

        dynamicFurnitureRoot = new GameObject("DynamicFurnitureRoot");
        dynamicFurnitureRoot.transform.SetParent(roomRoot.transform, false);

        labelsRoot = new GameObject("CaptureLabelsRoot");
        labelsRoot.transform.SetParent(roomRoot.transform, false);
    }

    private void ConfigureRoomStructureRigidbody(GameObject target)
    {
        if (target == null) return;

        Rigidbody rb = target.GetComponent<Rigidbody>();
        if (rb == null) rb = target.AddComponent<Rigidbody>();

        rb.useGravity = false;
        rb.isKinematic = true;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.constraints = RigidbodyConstraints.FreezeRotation;
    }

    private void ConfigureLoadedFurniture(List<GameObject> loadedFurnitures)
    {
        initialPoses.Clear();
        if (loadedFurnitures == null) return;

        foreach (GameObject furniture in loadedFurnitures)
        {
            if (furniture == null) continue;

            furniture.transform.SetParent(dynamicFurnitureRoot.transform, true);
            AttachLabel(furniture);

            Rigidbody rb = furniture.GetComponent<Rigidbody>();
            initialPoses[furniture] = new PoseData
            {
                localPosition = furniture.transform.localPosition,
                localRotation = furniture.transform.localRotation,
                originalKinematic = rb != null && rb.isKinematic,
                originalUseGravity = rb == null || rb.useGravity
            };
        }
    }

    private void ConfigureCaptureManager(Transform floorTransform)
    {
        if (captureManager == null) return;

        captureManager.floorTransform = floorTransform != null ? floorTransform : FindGeneratedFloor();
        captureManager.captureRoot = roomRoot != null ? roomRoot.transform : null;

        float captureHeight = ComputeRoomClearHeight(inputData != null ? inputData.room : null);
        if (captureHeight > 0f)
        {
            captureManager.wallHeight = captureHeight;
            Debug.Log($"[ServerManager] Capture room height set from JSON geometry: {captureHeight:F3}m");
        }

        captureManager.SetupCameras();
    }

    private Transform FindGeneratedFloor()
    {
        if (roomStructureRoot == null) return null;
        Transform floor = roomStructureRoot.transform.Find("Generated_Floor");
        if (floor != null) return floor;

        foreach (Transform child in roomStructureRoot.GetComponentsInChildren<Transform>(true))
        {
            if (child.name.IndexOf("floor", StringComparison.OrdinalIgnoreCase) >= 0)
                return child;
        }

        return null;
    }

    private void ConfigureEarthquakeSimulator()
    {
        if (earthquakeSimulator == null)
            earthquakeSimulator = FindFirstObjectByType<EarthquakeSimulator>();

        if (earthquakeSimulator == null)
        {
            earthquakeSimulator = dynamicFurnitureRoot.AddComponent<EarthquakeSimulator>();
        }

        earthquakeSimulator.SetSimulationMode(earthquakeSimulationMode);
        earthquakeSimulator.SetInertialOptions(inertialAccelerationMultiplier, includeVerticalInertialAcceleration);
        earthquakeSimulator.SetShakeTableOptions(shakeTableDisplacementMultiplier, includeVerticalShakeTableDisplacement);
        earthquakeSimulator.SetReturnToOriginAfterSimulation(returnRoomToOriginAfterSimulation);
        earthquakeSimulator.SetSceneReferences(roomRigidbody, dynamicFurnitureRoot, roomStructureRoot);
        Debug.Log($"[ServerManager] Earthquake simulation mode: {earthquakeSimulationMode}");

        if (!string.IsNullOrWhiteSpace(SimulationRunConfig.ThaCsvPath))
            earthquakeSimulator.SetCsvFilePath(SimulationRunConfig.ThaCsvPath);

        if (inputData != null && inputData.building != null)
        {
            earthquakeSimulator.SetBuildingInfo(
                ParseBuildingType(inputData.building.building_type),
                inputData.building.floor,
                inputData.building.total_floors,
                ParsePiloti(inputData.building.piloti));
        }

        float clearHeight = ComputeRoomClearHeight(inputData != null ? inputData.room : null);
        if (clearHeight > 0f) earthquakeSimulator.SetMeasuredRoomClearHeight(clearHeight);

        earthquakeSimulator.SetApplyStrongScenarioScaling(true);
        earthquakeSimulator.SetStrongScenarioMmi(9);
    }

    private IEnumerator RunMultiAngleSimulation(List<GameObject> furnitures)
    {
        string safeSessionId = SanitizeFileNamePart(sessionId);
        string imageSessionRoot = Path.Combine(imageSavePath, safeSessionId);
        string csvSessionRoot = Path.Combine(csvSavePath, safeSessionId);
        Directory.CreateDirectory(imageSessionRoot);
        Directory.CreateDirectory(csvSessionRoot);

        for (int i = 0; i < simulationAngles.Length; i++)
        {
            int angle = simulationAngles[i];
            Debug.Log($"[ServerManager] Starting seismic angle {angle}.");

            string imageAngleDirPath = Path.Combine(imageSessionRoot, $"rot_{angle:000}");
            string csvAngleDirPath = Path.Combine(csvSessionRoot, $"rot_{angle:000}");
            Directory.CreateDirectory(imageAngleDirPath);
            Directory.CreateDirectory(csvAngleDirPath);

            RestoreFurniturePoses(furnitures);
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
            yield return new WaitForSeconds(0.5f);

            if (earthquakeSimulator != null)
            {
                earthquakeSimulator.SetSceneReferences(roomRigidbody, dynamicFurnitureRoot, roomStructureRoot);
                earthquakeSimulator.SetSimulationAngle(angle);
            }

            if (captureManager != null)
            {
                yield return StartCoroutine(captureManager.CaptureSequence(sessionId, angle, "before", imageAngleDirPath, entranceMarker));
            }

            if (dataRecorder != null)
            {
                dataRecorder.StartRecording(csvAngleDirPath, $"{sessionId}_rot{angle:000}", furnitures);
                dataRecorder.AddRecordingTarget(roomStructureRoot);
            }

            yield return StartCoroutine(StartAndWaitForSimulation(angle));

            yield return new WaitForSeconds(0.5f);

            if (captureManager != null)
            {
                yield return StartCoroutine(captureManager.CaptureSequence(sessionId, angle, "after", imageAngleDirPath, entranceMarker));
            }

            if (restoreFurnitureToOriginAfterCapture)
            {
                RestoreFurniturePoses(furnitures);
                RestoreRoomStructurePose();
                Physics.SyncTransforms();
                yield return new WaitForFixedUpdate();

                if (dataRecorder != null)
                    dataRecorder.RecordCurrentFrame();
            }

            if (dataRecorder != null)
            {
                dataRecorder.StopAndSaveRecording();
            }
        }

        Debug.Log("[ServerManager] All server simulation captures completed.");

#if !UNITY_EDITOR
        Application.Quit(0);
#endif
    }

    private IEnumerator StartAndWaitForSimulation(int angle)
    {
        if (earthquakeSimulator == null) yield break;

        bool completed = false;
        void HandleCompleted() => completed = true;

        earthquakeSimulator.SimulationCompleted += HandleCompleted;
        earthquakeSimulator.StartSimulation();

        float elapsed = 0f;
        float timeout = Mathf.Max(simulationTimeoutSeconds, simulationDuration, 1f);
        while (!completed && earthquakeSimulator.IsSimulating && elapsed < timeout)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        earthquakeSimulator.SimulationCompleted -= HandleCompleted;

        if (!completed && earthquakeSimulator.IsSimulating)
        {
            Debug.LogWarning($"[ServerManager] Simulation angle {angle} timed out after {timeout:F1}s. Capturing current state.");
            earthquakeSimulator.StopSimulation();
        }
    }

    private void RestoreFurniturePoses(List<GameObject> furnitures)
    {
        if (furnitures == null) return;

        foreach (GameObject obj in furnitures)
        {
            if (obj == null || !initialPoses.TryGetValue(obj, out PoseData pose)) continue;

            Rigidbody rb = obj.GetComponent<Rigidbody>();
            Vector3 worldPos = dynamicFurnitureRoot.transform.TransformPoint(pose.localPosition);
            Quaternion worldRot = dynamicFurnitureRoot.transform.rotation * pose.localRotation;

            if (rb != null)
            {
                rb.isKinematic = true;
                rb.position = worldPos;
                rb.rotation = worldRot;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.useGravity = pose.originalUseGravity;
                rb.isKinematic = pose.originalKinematic;
            }

            obj.transform.position = worldPos;
            obj.transform.rotation = worldRot;
        }
    }

    private void RestoreRoomStructurePose()
    {
        if (roomStructureRoot == null) return;

        Rigidbody rb = roomStructureRoot.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.position = roomRoot != null ? roomRoot.transform.TransformPoint(Vector3.zero) : Vector3.zero;
            rb.rotation = roomRoot != null ? roomRoot.transform.rotation : Quaternion.identity;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        roomStructureRoot.transform.localPosition = Vector3.zero;
        roomStructureRoot.transform.localRotation = Quaternion.identity;
    }

    private void AttachLabel(GameObject furniture)
    {
        if (labelPrefab == null || furniture == null) return;

        GameObject labelObj = Instantiate(labelPrefab, labelsRoot != null ? labelsRoot.transform : null);
        Renderer[] renderers = furniture.GetComponentsInChildren<Renderer>();
        Vector3 targetPosition = furniture.transform.position + new Vector3(0, 1.5f, 0);

        if (renderers.Length > 0)
        {
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer r in renderers) bounds.Encapsulate(r.bounds);
            targetPosition = new Vector3(bounds.center.x, bounds.max.y + 0.6f, bounds.center.z);
        }

        labelObj.transform.position = targetPosition;
        labelObj.transform.rotation = Quaternion.identity;
        labelObj.transform.localScale = new Vector3(0.005f, 0.005f, 0.005f);

        TMP_Text tmp = labelObj.GetComponentInChildren<TMP_Text>();
        if (tmp != null) tmp.text = furniture.name;

        FurnitureLabel labelScript = labelObj.GetComponent<FurnitureLabel>();
        if (labelScript != null)
        {
            labelScript.Initialize(furniture.transform, 0.6f);
        }
    }

    private void CreateEntranceMarker(RoomData roomData)
    {
        if (roomData == null || roomData.entrance == null) return;

        Vector3 localPosition = roomData.entrance.GetPosition();
        float floorTopY = roomData.floor != null
            ? roomData.floor.GetCenter().y + roomData.floor.GetSize().y * 0.5f
            : localPosition.y;

        if (entrancePrefab != null)
        {
            entranceMarker = Instantiate(entrancePrefab, roomStructureRoot.transform);
        }
        else
        {
            entranceMarker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            entranceMarker.transform.SetParent(roomStructureRoot.transform, false);
            float radius = roomData.entrance.radius > 0f ? roomData.entrance.radius : 1.5f;
            entranceMarker.transform.localScale = new Vector3(radius * 2f, 0.02f, radius * 2f);
            Collider markerCollider = entranceMarker.GetComponent<Collider>();
            if (markerCollider != null) Destroy(markerCollider);
            Renderer markerRenderer = entranceMarker.GetComponent<Renderer>();
            if (markerRenderer != null) markerRenderer.material.color = new Color(0.1f, 0.85f, 0.25f, 1f);
        }

        entranceMarker.name = "EntranceMarker";
        entranceMarker.transform.localPosition = new Vector3(localPosition.x, floorTopY + 0.03f, localPosition.z);
        entranceMarker.transform.localRotation = Quaternion.identity;
        entranceMarker.SetActive(false);
    }

    private static float ComputeRoomClearHeight(RoomData room)
    {
        if (room == null) return 0f;

        if (room.floor != null && room.ceiling != null)
        {
            float floorTop = room.floor.GetCenter().y + room.floor.GetSize().y * 0.5f;
            float ceilingBottom = room.ceiling.GetCenter().y - room.ceiling.GetSize().y * 0.5f;
            float clearHeight = ceilingBottom - floorTop;
            if (clearHeight > 0f) return clearHeight;
        }

        return ComputeWallHeight(room);
    }

    private static float ComputeWallHeight(RoomData room)
    {
        if (room == null || room.walls == null) return 0f;

        float maxHeight = 0f;
        foreach (WallData wall in room.walls)
        {
            if (wall == null) continue;
            maxHeight = Mathf.Max(maxHeight, wall.GetSize().y);
        }

        return maxHeight;
    }

    private static EarthquakeSimulator.ResidentialBuildingType ParseBuildingType(string value)
    {
        return Enum.TryParse(value, true, out EarthquakeSimulator.ResidentialBuildingType result)
            ? result
            : EarthquakeSimulator.ResidentialBuildingType.Apartment;
    }

    private static EarthquakeSimulator.PilotiCondition ParsePiloti(string value)
    {
        return Enum.TryParse(value, true, out EarthquakeSimulator.PilotiCondition result)
            ? result
            : EarthquakeSimulator.PilotiCondition.Unknown;
    }

    private static string SanitizeFileNamePart(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "session";
        foreach (char invalid in Path.GetInvalidFileNameChars())
            value = value.Replace(invalid, '_');
        return value.Replace(' ', '_');
    }
}

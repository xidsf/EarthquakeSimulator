using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Newtonsoft.Json.Linq;

public enum BuildingType
{
    Apartment,
    LowRise,
    HighRiseOffice
}

public enum EarthquakeRuntimeMode
{
    LocalDevelopment,
    DedicatedServer
}

public enum EarthquakeSimulationMode
{
    LegacyDisplacement,
    AccelerationInertial,
    ShakeTable
}

public enum EarthquakeDataPreset
{
    JapanEarthquake,
    ElCentro
}

// 로컬 THA에서 읽거나 생성한 프레임 데이터를 담을 구조체
public struct SeismicFrame
{
    public Vector3 displacement;
    public Vector3 acceleration;
}

public class EarthquakeManager : MonoBehaviour
{
    [Header("Runtime Mode")]
    [Tooltip("LocalDevelopment: 개인 개발 PC/Editor 테스트용. DedicatedServer: 실제 서버 빌드 실행용.")]
    public EarthquakeRuntimeMode runtimeMode = EarthquakeRuntimeMode.LocalDevelopment;

    [Tooltip("Scene 시작 시 자동으로 StartSimulation을 호출합니다. 서버 빌드에서는 켜는 것을 권장합니다.")]
    public bool autoStartOnPlay = false;

    [Tooltip("시뮬레이션 종료 후 방을 원점으로 복귀시킵니다. 서버에서 timeline을 기록할 때는 false를 권장합니다.")]
    public bool returnRoomToOriginAfterSimulation = true;

    [Tooltip("DedicatedServer 모드에서 시뮬레이션 완료 후 앱을 종료합니다.")]
    public bool quitApplicationOnServerComplete = false;

    [Tooltip("명령행 인자(-tha, -thaDb, -server 등)를 읽습니다. 서버 빌드에서 유용합니다.")]
    public bool readCommandLineArguments = true;

    [Header("Local THA Settings")]
    [Tooltip("로컬 THA JSON을 TextAsset으로 지정합니다. 지정되어 있으면 file path보다 우선합니다.")]
    public TextAsset localThaJsonAsset;

    [Tooltip("로컬 THA JSON 파일 경로입니다. 절대 경로 또는 Application.dataPath 기준 상대 경로를 사용할 수 있습니다.")]
    public string localThaJsonFilePath = "";

    [Tooltip("THA 변위 단위가 cm이면 0.01, 이미 m이면 1을 사용합니다.")]
    public float displacementScale = 0.01f;

    [Tooltip("THA 가속도 단위가 gal(cm/s^2)이면 0.01, 이미 m/s^2이면 1을 사용합니다.")]
    public float accelerationScale = 0.01f;

    [Header("Local THA Database Settings")]
    [Tooltip("원본 THA CSV/TXT 데이터베이스 폴더입니다. 상대 경로이면 Application.dataPath, 현재 실행 폴더 순서로 찾습니다.")]
    public string localThaDatabasePath = "THA_Process_Data";

    [Tooltip("THA_Process_Data 안에서 사용할 대표 지진 데이터입니다.")]
    public EarthquakeDataPreset earthquakeDataPreset = EarthquakeDataPreset.JapanEarthquake;

    [Tooltip("THA 데이터 샘플 간격입니다. 현재 전처리 데이터는 100Hz 기준 0.01초입니다.")]
    public float localThaDt = 0.01f;

    [Tooltip("피크 가속도 주변에서 잘라낼 시뮬레이션 길이입니다.")]
    public float localThaMaxDurationSeconds = 20f;

    [Header("Simulation Parameters")]
    public int floor = 1;
    public BuildingType buildingType = BuildingType.Apartment;
    [Range(3, 9)] public int targetMMI = 6;
    public bool isLongPeriod = false;

    [Header("UI")]
    [Tooltip("DedicatedServer 모드에서는 자동으로 무시됩니다.")]
    public bool useUIInLocalDevelopment = true;
    public RoomBuildUIManager uiManager;
    public Slider mmiSlider;
    public TextMeshProUGUI mmiText;

    [Header("Room Objects")]
    public Rigidbody roomRigidbody;

    [Header("Simulation Mode")]
    [Tooltip("LegacyDisplacement: 기존 velocity 기반 방 이동. AccelerationInertial: 방 고정 + 가구 관성가속도. ShakeTable: kinematic 방/바닥을 MovePosition으로 구동.")]
    public EarthquakeSimulationMode simulationMode = EarthquakeSimulationMode.LegacyDisplacement;

    [Tooltip("기존 테스트용 bool입니다. true면 simulationMode가 LegacyDisplacement일 때 AccelerationInertial로 처리합니다.")]
    public bool useAccelerationBasedInertialSimulation = false;

    [Tooltip("가구 오브젝트들이 모두 포함된 Root GameObject입니다. 이 루트 아래 Rigidbody들이 관성가속도 적용 대상입니다.")]
    public GameObject furnitureRoot;

    [Tooltip("벽, 바닥 등 방 구조가 포함된 Root GameObject입니다. 관성력 모드에서는 이 루트 아래 Rigidbody를 기준 구조물로 고정합니다.")]
    public GameObject roomStructureRoot;

    [Tooltip("관성가속도 배율입니다. 1이면 THA floor acceleration을 그대로 사용합니다.")]
    public float inertialAccelerationMultiplier = 1f;

    [Tooltip("끄면 수평 X/Z 가속도만 적용합니다. 가구 전도/이동 테스트는 보통 수평 성분만 사용하는 쪽이 안정적입니다.")]
    public bool includeVerticalInertialAcceleration = false;

    [Header("Shake Table Settings")]
    [Tooltip("ShakeTable 모드에서 roomStructureRoot 하위 Rigidbody들을 MovePosition으로 구동합니다. 비어 있으면 roomRigidbody를 구동합니다.")]
    public bool driveRoomStructureRootInShakeTable = true;

    [Tooltip("ShakeTable 모드에서 사용할 변위 배율입니다. 1이면 THA 변위를 그대로 사용합니다.")]
    public float shakeTableDisplacementMultiplier = 1f;

    [Tooltip("끄면 ShakeTable 모드에서 수평 X/Z 변위만 재생합니다.")]
    public bool includeVerticalShakeTableDisplacement = false;

    private bool isReceiving = false;
    private bool isSimulating = false;
    private bool isReturning = false;
    private bool dataComplete = false;
    private bool simulationFullyCompleted = false;

    public bool IsReceiving => isReceiving;
    public bool IsSimulating => isSimulating;
    public bool IsReturning => isReturning;
    public bool IsSimulationComplete => simulationFullyCompleted;

    private Vector3 initialPosition;
    private ConcurrentQueue<SeismicFrame> frameQueue = new ConcurrentQueue<SeismicFrame>();

    private float serverDt = 0.01f;
    public float ServerDt => serverDt;

    private readonly List<ThaRecord> earthquakeDatabase = new List<ThaRecord>();
    private string loadedThaDatabasePath = "";
    private EarthquakeDataPreset loadedThaDataPreset;
    private readonly List<Rigidbody> furnitureRigidbodies = new List<Rigidbody>();
    private readonly List<Rigidbody> roomStructureRigidbodies = new List<Rigidbody>();
    private readonly Dictionary<Rigidbody, Vector3> roomStructureInitialPositions = new Dictionary<Rigidbody, Vector3>();

    private Vector3 returnVelocity = Vector3.zero;

    public float highestVelocity = 0;

    public event Action SimulationStarted;
    public event Action SimulationCompleted;

    void OnEnable()
    {
        Physics.defaultContactOffset = 0.00001f;

        if (!ShouldUseUI())
            return;

        if (uiManager != null)
            floor = uiManager.RoomFloor;

        if (mmiSlider != null)
        {
            mmiSlider.value = targetMMI;
            UpdateMMIText(targetMMI);
            mmiSlider.onValueChanged.AddListener(OnMMIChanged);
        }
    }

    void OnDisable()
    {
        if (mmiSlider != null)
            mmiSlider.onValueChanged.RemoveListener(OnMMIChanged);
    }

    private bool ShouldUseUI()
    {
        return runtimeMode == EarthquakeRuntimeMode.LocalDevelopment && useUIInLocalDevelopment;
    }

    private EarthquakeSimulationMode EffectiveSimulationMode
    {
        get
        {
            if (simulationMode == EarthquakeSimulationMode.LegacyDisplacement && useAccelerationBasedInertialSimulation)
                return EarthquakeSimulationMode.AccelerationInertial;

            return simulationMode;
        }
    }

    public void SetFloorValue(int value)
    {
        floor = value;
    }

    private void OnMMIChanged(float value)
    {
        targetMMI = Mathf.RoundToInt(value);
        UpdateMMIText(targetMMI);
    }

    private void UpdateMMIText(int value)
    {
        if (mmiText != null)
            mmiText.text = $"진도: {value}";
    }

    void Start()
    {
        if (readCommandLineArguments)
            ApplyCommandLineArguments();

        if (roomRigidbody != null)
        {
            initialPosition = roomRigidbody.position;

            ConfigureRoomRigidbody();
        }
        else
        {
            Debug.LogError("[EarthquakeManager] 방(Room)의 Rigidbody가 연결되지 않았습니다!");
        }

        CacheSimulationRigidbodies();

        if (autoStartOnPlay)
            StartSimulation();
    }

    private void ConfigureRoomRigidbody()
    {
        if (roomRigidbody == null)
            return;

        roomRigidbody.useGravity = false;
        roomRigidbody.mass = 10000000f;
        roomRigidbody.constraints = RigidbodyConstraints.FreezeRotation;
        roomRigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

        EarthquakeSimulationMode mode = EffectiveSimulationMode;

        if (mode == EarthquakeSimulationMode.AccelerationInertial || mode == EarthquakeSimulationMode.ShakeTable)
        {
            roomRigidbody.isKinematic = true;
            ClearRigidbodyVelocity(roomRigidbody);
        }
        else
        {
            // 기존 방식: 방을 거대한 질량의 동적 객체로 두고 THA 변위에 맞춰 속도를 주입합니다.
            roomRigidbody.isKinematic = false;
        }
    }

    private void CacheSimulationRigidbodies()
    {
        furnitureRigidbodies.Clear();
        roomStructureRigidbodies.Clear();
        roomStructureInitialPositions.Clear();

        if (roomStructureRoot != null)
            roomStructureRigidbodies.AddRange(roomStructureRoot.GetComponentsInChildren<Rigidbody>(true));

        if (furnitureRoot != null)
        {
            Rigidbody[] candidates = furnitureRoot.GetComponentsInChildren<Rigidbody>(true);
            foreach (Rigidbody rb in candidates)
            {
                if (rb == null || IsUnderRoot(rb.transform, roomStructureRoot))
                    continue;

                furnitureRigidbodies.Add(rb);
            }
        }

        EarthquakeSimulationMode mode = EffectiveSimulationMode;
        if (mode == EarthquakeSimulationMode.AccelerationInertial || mode == EarthquakeSimulationMode.ShakeTable)
        {
            foreach (Rigidbody rb in roomStructureRigidbodies)
            {
                if (rb == null)
                    continue;

                rb.isKinematic = true;
                rb.useGravity = false;
                ClearRigidbodyVelocity(rb);
                roomStructureInitialPositions[rb] = rb.position;
            }

            if (mode == EarthquakeSimulationMode.AccelerationInertial && furnitureRoot == null)
                Debug.LogWarning("[EarthquakeManager] 관성력 모드가 켜져 있지만 furnitureRoot가 지정되지 않았습니다.");
            else if (mode == EarthquakeSimulationMode.AccelerationInertial)
                Debug.Log($"[EarthquakeManager] 관성력 적용 대상 Rigidbody 수: {furnitureRigidbodies.Count}");

            if (mode == EarthquakeSimulationMode.ShakeTable && driveRoomStructureRootInShakeTable)
                Debug.Log($"[EarthquakeManager] ShakeTable 구동 대상 구조 Rigidbody 수: {roomStructureRigidbodies.Count}");
        }
    }

    private bool IsUnderRoot(Transform target, GameObject root)
    {
        if (target == null || root == null)
            return false;

        Transform current = target;
        Transform rootTransform = root.transform;
        while (current != null)
        {
            if (current == rootTransform)
                return true;

            current = current.parent;
        }

        return false;
    }

    private void ApplyCommandLineArguments()
    {
        string[] args = Environment.GetCommandLineArgs();

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];

            if (arg == "-server")
            {
                runtimeMode = EarthquakeRuntimeMode.DedicatedServer;
                continue;
            }

            if (arg == "-noReturn")
            {
                returnRoomToOriginAfterSimulation = false;
                continue;
            }

            if (arg == "-quitOnComplete")
            {
                quitApplicationOnServerComplete = true;
                continue;
            }

            if (arg == "-tha" && i + 1 < args.Length)
            {
                localThaJsonFilePath = args[++i];
                continue;
            }

            if (arg == "-thaDb" && i + 1 < args.Length)
            {
                localThaDatabasePath = args[++i];
                continue;
            }

            if ((arg == "-earthquake" || arg == "-quake") && i + 1 < args.Length)
            {
                string preset = args[++i];
                if (Enum.TryParse(preset, true, out EarthquakeDataPreset parsedPreset))
                    earthquakeDataPreset = parsedPreset;
                continue;
            }

            if (arg == "-mmi" && i + 1 < args.Length && int.TryParse(args[++i], out int parsedMMI))
            {
                targetMMI = Mathf.Clamp(parsedMMI, 3, 9);
                continue;
            }

            if (arg == "-floor" && i + 1 < args.Length && int.TryParse(args[++i], out int parsedFloor))
            {
                floor = parsedFloor;
                continue;
            }

            if (arg == "-building" && i + 1 < args.Length)
            {
                string building = args[++i];
                if (Enum.TryParse(building, true, out BuildingType parsedBuildingType))
                    buildingType = parsedBuildingType;
            }
        }
    }

    private sealed class ThaRecord
    {
        public string filePath;
        public double pga;
        public double[] rawNs;
        public double[] rawEw;
    }

    private struct ThaSelection
    {
        public ThaRecord record;
        public double scaleFactor;
        public double saDesignGal;
        public int returnPeriod;
        public double period;
    }

    public void StartSimulation()
    {
        try
        {
            if (isReceiving || isSimulating || isReturning)
            {
                Debug.LogWarning("[EarthquakeManager] 이미 시뮬레이션이 실행 중이거나 원점 복귀 중입니다.");
                return;
            }

            ClearFrameQueue();

            isReceiving = true;
            isSimulating = false;
            isReturning = false;
            dataComplete = false;
            simulationFullyCompleted = false;

            returnVelocity = Vector3.zero;
            highestVelocity = 0;

            ConfigureRoomRigidbody();
            CacheSimulationRigidbodies();
            ResetFurnitureMotion();

            if (roomRigidbody != null)
            {
                roomRigidbody.position = initialPosition;
                ClearRigidbodyVelocity(roomRigidbody);
            }

            ResetRoomStructurePose();

            LoadLocalThaData();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[EarthquakeManager] StartSimulation 에러: {ex.Message}");
            isReceiving = false;
        }
    }

    private void ClearFrameQueue()
    {
        while (frameQueue.TryDequeue(out _)) { }
    }

    private void ResetFurnitureMotion()
    {
        foreach (Rigidbody rb in furnitureRigidbodies)
        {
            if (rb == null)
                continue;

            ClearRigidbodyVelocity(rb);
        }
    }

    private void ResetRoomStructurePose()
    {
        foreach (KeyValuePair<Rigidbody, Vector3> pair in roomStructureInitialPositions)
        {
            Rigidbody rb = pair.Key;
            if (rb == null)
                continue;

            rb.position = pair.Value;
            ClearRigidbodyVelocity(rb);
        }
    }

    private void LoadLocalThaData()
    {
        try
        {
            string jsonText = null;

            if (localThaJsonAsset != null)
            {
                jsonText = localThaJsonAsset.text;
                Debug.Log("[EarthquakeManager] TextAsset에서 로컬 THA 데이터를 읽습니다.");
            }
            else if (!string.IsNullOrWhiteSpace(localThaJsonFilePath))
            {
                string resolvedPath = ResolveLocalThaPath(localThaJsonFilePath);

                if (!File.Exists(resolvedPath))
                    throw new FileNotFoundException($"THA JSON 파일을 찾을 수 없습니다: {resolvedPath}");

                jsonText = File.ReadAllText(resolvedPath, Encoding.UTF8);
                Debug.Log($"[EarthquakeManager] 파일에서 로컬 THA 데이터를 읽습니다: {resolvedPath}");
            }
            else
            {
                GenerateFramesFromLocalThaDatabase();
                isReceiving = false;
                dataComplete = true;
                Debug.Log($"[EarthquakeManager] 로컬 THA DB 처리 완료. frame count: {frameQueue.Count}, dt: {serverDt}s");
                return;
            }

            ParseLocalThaJson(jsonText);

            isReceiving = false;
            dataComplete = true;

            Debug.Log($"[EarthquakeManager] 로컬 THA 데이터 로드 완료. frame count: {frameQueue.Count}, dt: {serverDt}s");
        }
        catch (Exception e)
        {
            isReceiving = false;
            dataComplete = true;
            Debug.LogError($"[EarthquakeManager] 로컬 THA 로드 에러: {e.Message}");
        }
    }

    private void GenerateFramesFromLocalThaDatabase()
    {
        string databasePath = ResolveLocalThaDirectory(localThaDatabasePath);
        if (!Directory.Exists(databasePath))
            throw new DirectoryNotFoundException($"THA DB 폴더를 찾을 수 없습니다: {databasePath}");

        LoadEarthquakeDatabase(databasePath);

        if (earthquakeDatabase.Count == 0)
            throw new InvalidOperationException($"THA DB에 사용 가능한 csv/txt 데이터가 없습니다: {databasePath}");

        serverDt = Mathf.Max(0.001f, localThaDt);

        double dt = serverDt;
        double period = GetBuildingPeriod(GetBuildingTypeKey(), floor);
        ThaSelection selection = SelectAndScaleEarthquake(period, dt);

        if (selection.record == null)
            throw new InvalidOperationException("조건에 맞는 THA 데이터를 선택하지 못했습니다.");

        if (TryEnqueueUnityTrajectoryCsvFrames(
                selection.record.filePath,
                selection.scaleFactor,
                out int directFrameCount,
                out float directMaxHorizontalDisplacement,
                out float directMaxVerticalDisplacement))
        {
            Debug.Log(
                $"[EarthquakeManager] Unity trajectory CSV loaded directly: {Path.GetFileName(selection.record.filePath)}, " +
                $"scale:x{selection.scaleFactor:F3}, maxHorizontal:{directMaxHorizontalDisplacement:F4}m, " +
                $"maxVertical:{directMaxVerticalDisplacement:F4}m, frames:{directFrameCount}");
            return;
        }

        if (!TryReadThaFile(selection.record.filePath, out double[] rawNs, out double[] rawEw, out double[] rawUd))
            throw new InvalidOperationException($"선택된 THA 파일에서 데이터를 읽지 못했습니다: {selection.record.filePath}");

        ScaleInPlace(rawNs, selection.scaleFactor);
        ScaleInPlace(rawEw, selection.scaleFactor);
        ScaleInPlace(rawUd, selection.scaleFactor);

        double[] floorNs = ApplySdofTha(rawNs, dt, period, 0.05);
        double[] floorEw = ApplySdofTha(rawEw, dt, period, 0.05);
        double[] floorUd = rawUd;

        double[] dispNs = CalculateDisplacement(floorNs, dt);
        double[] dispEw = CalculateDisplacement(floorEw, dt);
        double[] dispUd = CalculateDisplacement(floorUd, dt);

        int dataLength = Math.Min(Math.Min(dispNs.Length, dispEw.Length), dispUd.Length);
        if (dataLength == 0)
            throw new InvalidOperationException("선택된 THA 데이터의 길이가 0입니다.");

        int peakIndex = FindPeakHorizontalAccelerationIndex(floorNs, floorEw, dataLength);
        int preFrames = Mathf.RoundToInt(5f / serverDt);
        int maxFrames = Mathf.Max(1, Mathf.RoundToInt(localThaMaxDurationSeconds / serverDt));
        int sliceStart = Math.Max(0, peakIndex - preFrames);
        int sliceEnd = Math.Min(dataLength, sliceStart + maxFrames);

        if (sliceEnd <= sliceStart)
            throw new InvalidOperationException("THA 슬라이스 범위가 유효하지 않습니다.");

        double originNs = dispNs[sliceStart];
        double originEw = dispEw[sliceStart];
        double originUd = dispUd[sliceStart];

        for (int i = sliceStart; i < sliceEnd; i++)
        {
            frameQueue.Enqueue(new SeismicFrame
            {
                displacement = new Vector3(
                    (float)((dispEw[i] - originEw) * displacementScale),
                    (float)((dispUd[i] - originUd) * displacementScale),
                    (float)((dispNs[i] - originNs) * displacementScale)),
                acceleration = new Vector3(
                    (float)(floorEw[i] * accelerationScale),
                    (float)(floorUd[i] * accelerationScale),
                    (float)(floorNs[i] * accelerationScale))
            });
        }

        double groundPga = FindMaxHorizontalAcceleration(rawNs, rawEw);
        double floorPga = FindMaxHorizontalAcceleration(floorNs, floorEw);
        Debug.Log(
            $"[EarthquakeManager] THA file selected: {Path.GetFileName(selection.record.filePath)}, " +
            $"floor:{floor}, building:{GetBuildingTypeKey()}, T:{selection.period:F3}s, MMI:{targetMMI}, " +
            $"KDS:{selection.returnPeriod}yr, Sa:{selection.saDesignGal:F1}gal, scale:x{selection.scaleFactor:F3}, " +
            $"groundPGA:{groundPga:F1}gal, floorPGA:{floorPga:F1}gal, frames:{frameQueue.Count}");
    }

    private string ResolveLocalThaDirectory(string path)
    {
        if (Path.IsPathRooted(path))
            return path;

        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
        string[] candidates =
        {
            Path.Combine(Application.dataPath, path),
            Path.Combine(Directory.GetCurrentDirectory(), path),
            Path.Combine(Application.streamingAssetsPath, path),
            Path.Combine(Application.persistentDataPath, path),
            Path.Combine(projectRoot, path)
        };

        foreach (string candidate in candidates)
        {
            if (Directory.Exists(candidate))
                return candidate;
        }

        return candidates[0];
    }

    private void LoadEarthquakeDatabase(string databasePath)
    {
        if (loadedThaDatabasePath == databasePath && loadedThaDataPreset == earthquakeDataPreset && earthquakeDatabase.Count > 0)
            return;

        earthquakeDatabase.Clear();
        loadedThaDatabasePath = databasePath;
        loadedThaDataPreset = earthquakeDataPreset;

        string selectedFilePath = FindSingleThaDataFile(databasePath);
        if (!string.IsNullOrEmpty(selectedFilePath))
            AddThaRecords(new[] { selectedFilePath });

        Debug.Log($"[EarthquakeManager] THA single file loaded. path:{selectedFilePath}, records:{earthquakeDatabase.Count}");
    }

    private string FindSingleThaDataFile(string databasePath)
    {
        List<string> files = new List<string>();
        files.AddRange(Directory.GetFiles(databasePath, "*.csv", SearchOption.AllDirectories));
        files.AddRange(Directory.GetFiles(databasePath, "*.txt", SearchOption.AllDirectories));
        files.Sort(StringComparer.OrdinalIgnoreCase);

        if (files.Count == 0)
            return null;

        string preferredFileName = earthquakeDataPreset == EarthquakeDataPreset.ElCentro
            ? "el_centro_for_unity.csv"
            : "selected_earthquake_for_unity.csv";

        for (int i = 0; i < files.Count; i++)
        {
            string fileName = Path.GetFileName(files[i]);
            if (string.Equals(fileName, preferredFileName, StringComparison.OrdinalIgnoreCase))
                return files[i];
        }

        Debug.LogWarning(
            $"[EarthquakeManager] Preferred THA file not found for {earthquakeDataPreset}: {preferredFileName}. " +
            "Falling back to available data.");

        string targetCategory = isLongPeriod ? "long_period" : "standard";
        string targetSegment = "/" + targetCategory + "/";
        for (int i = 0; i < files.Count; i++)
        {
            string normalizedPath = files[i].Replace('\\', '/');
            if (normalizedPath.IndexOf(targetSegment, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (files.Count > 1)
                {
                    Debug.LogWarning(
                        $"[EarthquakeManager] THA_Process_Data has multiple data files. " +
                        $"Using one {targetCategory} file: {Path.GetFileName(files[i])}");
                }

                return files[i];
            }
        }

        if (files.Count > 1)
        {
            Debug.LogWarning(
                $"[EarthquakeManager] THA_Process_Data has multiple data files. " +
                $"Using first file: {Path.GetFileName(files[0])}");
        }

        return files[0];
    }

    private void AddThaRecords(string[] filePaths)
    {
        foreach (string filePath in filePaths)
        {
            try
            {
                if (!TryReadUnityTrajectoryCsvAcceleration(filePath, out double[] ns, out double[] ew) &&
                    !TryReadThaFile(filePath, out ns, out ew, out _))
                {
                    continue;
                }

                double pga = FindMaxHorizontalAcceleration(ns, ew);
                if (pga <= 0)
                    continue;

                earthquakeDatabase.Add(new ThaRecord
                {
                    filePath = filePath,
                    pga = pga,
                    rawNs = ns,
                    rawEw = ew
                });
            }
            catch
            {
                // 손상되었거나 형식이 다른 관측 파일은 DB 구성에서 제외합니다.
            }
        }
    }

    private bool TryReadThaFile(string filePath, out double[] ns, out double[] ew, out double[] ud)
    {
        ns = Array.Empty<double>();
        ew = Array.Empty<double>();
        ud = Array.Empty<double>();

        string[] lines = File.ReadAllLines(filePath, Encoding.UTF8);
        int startIndex = FindDataStartIndex(lines);

        List<double> nsValues = new List<double>();
        List<double> ewValues = new List<double>();
        List<double> udValues = new List<double>();

        for (int i = startIndex; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("#"))
                continue;

            string[] parts = SplitThaLine(line);
            if (parts.Length < 4)
                continue;

            if (!TryParseDouble(parts[2], out double nsValue) || !TryParseDouble(parts[3], out double ewValue))
                continue;

            double udValue = 0;
            if (parts.Length > 4)
                TryParseDouble(parts[4], out udValue);

            nsValues.Add(nsValue);
            ewValues.Add(ewValue);
            udValues.Add(udValue);
        }

        if (nsValues.Count == 0)
            return false;

        ns = nsValues.ToArray();
        ew = ewValues.ToArray();
        ud = udValues.ToArray();
        return true;
    }

    private int FindDataStartIndex(string[] lines)
    {
        for (int i = 0; i < lines.Length; i++)
        {
            string stripped = lines[i].Trim();
            if (stripped.StartsWith("#Time", StringComparison.OrdinalIgnoreCase))
                return i + 1;

            if (!stripped.StartsWith("#") && SplitThaLine(stripped).Length >= 4)
                return i;
        }

        return Math.Min(18, lines.Length);
    }

    private string[] SplitThaLine(string line)
    {
        if (line.Contains(","))
            return line.Split(',');

        return line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
    }

    private bool TryReadUnityTrajectoryCsvAcceleration(string filePath, out double[] xAcceleration, out double[] zAcceleration)
    {
        xAcceleration = Array.Empty<double>();
        zAcceleration = Array.Empty<double>();

        string[] lines = File.ReadAllLines(filePath, Encoding.UTF8);
        int headerIndex = -1;
        string[] headers = null;

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("#"))
                continue;

            string[] parts = SplitThaLine(line);
            if (parts.Length >= 3 && HasHeader(parts, "ax") && HasHeader(parts, "az"))
            {
                headerIndex = i;
                headers = parts;
            }

            break;
        }

        if (headerIndex < 0 || headers == null)
            return false;

        int axIndex = FindHeaderIndex(headers, "ax", "acc_x", "acceleration_x");
        int azIndex = FindHeaderIndex(headers, "az", "acc_z", "acceleration_z");
        List<double> axValues = new List<double>();
        List<double> azValues = new List<double>();

        for (int i = headerIndex + 1; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("#"))
                continue;

            string[] parts = SplitThaLine(line);
            if (parts.Length <= Math.Max(axIndex, azIndex))
                continue;

            if (!TryParseDouble(parts[axIndex], out double ax) || !TryParseDouble(parts[azIndex], out double az))
                continue;

            axValues.Add(ax);
            azValues.Add(az);
        }

        if (axValues.Count == 0)
            return false;

        xAcceleration = axValues.ToArray();
        zAcceleration = azValues.ToArray();
        return true;
    }

    private bool TryEnqueueUnityTrajectoryCsvFrames(
        string filePath,
        double scaleFactor,
        out int frameCount,
        out float maxHorizontalDisplacement,
        out float maxVerticalDisplacement)
    {
        frameCount = 0;
        maxHorizontalDisplacement = 0f;
        maxVerticalDisplacement = 0f;

        string[] lines = File.ReadAllLines(filePath, Encoding.UTF8);
        int headerIndex = -1;
        string[] headers = null;

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("#"))
                continue;

            string[] parts = SplitThaLine(line);
            if (parts.Length >= 4 && HasHeader(parts, "dx") && HasHeader(parts, "dy") && HasHeader(parts, "dz"))
            {
                headerIndex = i;
                headers = parts;
            }

            break;
        }

        if (headerIndex < 0 || headers == null)
            return false;

        int tIndex = FindHeaderIndex(headers, "t", "time");
        int dxIndex = FindHeaderIndex(headers, "dx", "x", "displacement_x");
        int dyIndex = FindHeaderIndex(headers, "dy", "y", "displacement_y");
        int dzIndex = FindHeaderIndex(headers, "dz", "z", "displacement_z");
        int axIndex = FindHeaderIndex(headers, "ax", "acc_x", "acceleration_x");
        int ayIndex = FindHeaderIndex(headers, "ay", "acc_y", "acceleration_y");
        int azIndex = FindHeaderIndex(headers, "az", "acc_z", "acceleration_z");

        double? previousTime = null;
        double? detectedDt = null;

        for (int i = headerIndex + 1; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("#"))
                continue;

            string[] parts = SplitThaLine(line);
            if (parts.Length <= Math.Max(dxIndex, Math.Max(dyIndex, dzIndex)))
                continue;

            if (!TryParseDouble(parts[dxIndex], out double dx) ||
                !TryParseDouble(parts[dyIndex], out double dy) ||
                !TryParseDouble(parts[dzIndex], out double dz))
            {
                continue;
            }

            double ax = ReadOptionalDouble(parts, axIndex);
            double ay = ReadOptionalDouble(parts, ayIndex);
            double az = ReadOptionalDouble(parts, azIndex);

            if (tIndex >= 0 && tIndex < parts.Length && TryParseDouble(parts[tIndex], out double time))
            {
                if (previousTime.HasValue && !detectedDt.HasValue && time > previousTime.Value)
                    detectedDt = time - previousTime.Value;

                previousTime = time;
            }

            Vector3 displacement = new Vector3(
                (float)(dx * scaleFactor * displacementScale),
                (float)(dy * scaleFactor * displacementScale),
                (float)(dz * scaleFactor * displacementScale));

            Vector3 acceleration = new Vector3(
                (float)(ax * scaleFactor * accelerationScale),
                (float)(ay * scaleFactor * accelerationScale),
                (float)(az * scaleFactor * accelerationScale));

            frameQueue.Enqueue(new SeismicFrame
            {
                displacement = displacement,
                acceleration = acceleration
            });

            frameCount++;
            maxHorizontalDisplacement = Mathf.Max(
                maxHorizontalDisplacement,
                new Vector2(displacement.x, displacement.z).magnitude);
            maxVerticalDisplacement = Mathf.Max(maxVerticalDisplacement, Mathf.Abs(displacement.y));
        }

        if (frameCount == 0)
            return false;

        if (detectedDt.HasValue)
            serverDt = Mathf.Max(0.001f, (float)detectedDt.Value);

        return true;
    }

    private bool HasHeader(string[] headers, string key)
    {
        return FindHeaderIndex(headers, key) >= 0;
    }

    private int FindHeaderIndex(string[] headers, params string[] keys)
    {
        for (int i = 0; i < headers.Length; i++)
        {
            string header = headers[i].Trim();
            for (int j = 0; j < keys.Length; j++)
            {
                if (string.Equals(header, keys[j], StringComparison.OrdinalIgnoreCase))
                    return i;
            }
        }

        return -1;
    }

    private double ReadOptionalDouble(string[] parts, int index)
    {
        if (index < 0 || index >= parts.Length)
            return 0.0;

        return TryParseDouble(parts[index], out double value) ? value : 0.0;
    }

    private bool TryParseDouble(string value, out double parsed)
    {
        return double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed);
    }

    private ThaSelection SelectAndScaleEarthquake(double period, double dt)
    {
        int intensity = Mathf.Clamp(targetMMI, 3, 9);
        int returnPeriod = GetKdsReturnPeriod(intensity);
        double saDesignG = ComputeKdsDesignSpectrum(period, returnPeriod);
        if (intensity == 9)
            saDesignG *= 1.5;

        double saDesignGal = saDesignG * 980.665;
        ThaRecord selectedRecord = earthquakeDatabase.Count > 0 ? earthquakeDatabase[0] : null;
        double scale = 1.0;

        if (selectedRecord != null)
        {
            double saRecord = selectedRecord.pga;
            if (period >= 0.1)
            {
                double saNs = ComputeResponseSpectrumAtPeriod(selectedRecord.rawNs, dt, period);
                double saEw = ComputeResponseSpectrumAtPeriod(selectedRecord.rawEw, dt, period);
                saRecord = Math.Sqrt(saNs * saNs + saEw * saEw);
            }

            scale = Mathf.Clamp((float)(saDesignGal / (saRecord + 1e-5)), 0.3f, 3.0f);
        }

        return new ThaSelection
        {
            record = selectedRecord,
            scaleFactor = scale,
            saDesignGal = saDesignGal,
            returnPeriod = returnPeriod,
            period = period
        };
    }

    private string GetBuildingTypeKey()
    {
        return buildingType.ToString().ToLowerInvariant();
    }

    private int GetKdsReturnPeriod(int intensity)
    {
        switch (intensity)
        {
            case 3: return 50;
            case 4: return 100;
            case 5: return 500;
            case 6: return 1000;
            case 7: return 2400;
            case 8:
            case 9:
                return 4800;
            default:
                return 1000;
        }
    }

    private double GetKdsRiskFactor(int returnPeriod)
    {
        switch (returnPeriod)
        {
            case 50: return 0.40;
            case 100: return 0.57;
            case 200: return 0.73;
            case 500: return 1.0;
            case 1000: return 1.4;
            case 2400: return 2.0;
            case 4800: return 2.6;
            default: return 1.0;
        }
    }

    private double ComputeKdsDesignSpectrum(double period, int returnPeriod)
    {
        double z = 0.11;
        double s = z * GetKdsRiskFactor(returnPeriod);
        double fa;
        double fv;

        if (s <= 0.1)
        {
            fa = 1.7;
            fv = 1.7;
        }
        else if (s <= 0.2)
        {
            fa = 1.7 + ((s - 0.1) / 0.1) * (1.5 - 1.7);
            fv = 1.7 + ((s - 0.1) / 0.1) * (1.6 - 1.7);
        }
        else if (s <= 0.3)
        {
            fa = 1.5 + ((s - 0.2) / 0.1) * (1.3 - 1.5);
            fv = 1.6 + ((s - 0.2) / 0.1) * (1.5 - 1.6);
        }
        else
        {
            fa = 1.3;
            fv = 1.5;
        }

        double sds = s * 2.8 * fa;
        double sd1 = s * 0.84 * fv;
        double t0 = 0.2 * (sd1 / sds);
        double ts = sd1 / sds;
        double tl = 3.0;

        if (period <= t0)
            return sds * (0.4 + 0.6 * (period / t0));
        if (period <= ts)
            return sds;
        if (period <= tl)
            return sd1 / period;

        return (sd1 * tl) / (period * period);
    }

    private double GetBuildingPeriod(string buildingTypeKey, int floorValue)
    {
        double height = Math.Max(1, floorValue) * 3.0;
        double ct;
        double x;

        switch (buildingTypeKey)
        {
            case "lowrise":
                ct = 0.0466;
                x = 0.9;
                break;
            case "highriseoffice":
                ct = 0.0724;
                x = 0.8;
                break;
            default:
                ct = 0.0488;
                x = 0.75;
                break;
        }

        return ct * Math.Pow(height, x);
    }

    private double ComputeResponseSpectrumAtPeriod(double[] accelerationGal, double dt, double period)
    {
        double[] floorAcceleration = ApplySdofTha(accelerationGal, dt, period, 0.05);
        double max = 0;
        for (int i = 0; i < floorAcceleration.Length; i++)
            max = Math.Max(max, Math.Abs(floorAcceleration[i]));

        return max;
    }

    private double[] ApplySdofTha(double[] accelerationGal, double dt, double period, double dampingRatio)
    {
        if (period < 0.1)
            return (double[])accelerationGal.Clone();

        double omega = 2.0 * Math.PI / period;
        double stiffness = omega * omega;
        double damping = 2.0 * dampingRatio * omega;

        double[] output = new double[accelerationGal.Length];
        double displacement = 0;
        double velocity = 0;

        for (int i = 0; i < accelerationGal.Length; i++)
        {
            output[i] = -damping * velocity - stiffness * displacement;
            StepSdof(ref displacement, ref velocity, accelerationGal[i], dt, damping, stiffness);
        }

        return output;
    }

    private void StepSdof(ref double displacement, ref double velocity, double groundAcceleration, double dt, double damping, double stiffness)
    {
        double Accel(double x, double v)
        {
            return -damping * v - stiffness * x - groundAcceleration;
        }

        double k1x = velocity;
        double k1v = Accel(displacement, velocity);

        double k2x = velocity + 0.5 * dt * k1v;
        double k2v = Accel(displacement + 0.5 * dt * k1x, velocity + 0.5 * dt * k1v);

        double k3x = velocity + 0.5 * dt * k2v;
        double k3v = Accel(displacement + 0.5 * dt * k2x, velocity + 0.5 * dt * k2v);

        double k4x = velocity + dt * k3v;
        double k4v = Accel(displacement + dt * k3x, velocity + dt * k3v);

        displacement += (dt / 6.0) * (k1x + 2.0 * k2x + 2.0 * k3x + k4x);
        velocity += (dt / 6.0) * (k1v + 2.0 * k2v + 2.0 * k3v + k4v);
    }

    private double[] CalculateDisplacement(double[] accelerationGal, double dt)
    {
        double[] accelFiltered = HighPassFilter(accelerationGal, dt, 0.1);
        double[] velocity = CumulativeTrapezoid(accelFiltered, dt);
        double[] velocityFiltered = HighPassFilter(velocity, dt, 0.1);
        double[] displacement = CumulativeTrapezoid(velocityFiltered, dt);
        return HighPassFilter(displacement, dt, 0.1);
    }

    private double[] CumulativeTrapezoid(double[] values, double dt)
    {
        double[] integrated = new double[values.Length];
        for (int i = 1; i < values.Length; i++)
            integrated[i] = integrated[i - 1] + (values[i - 1] + values[i]) * 0.5 * dt;

        return integrated;
    }

    private double[] HighPassFilter(double[] values, double dt, double cutoff)
    {
        if (values.Length == 0)
            return Array.Empty<double>();

        double[] filtered = (double[])values.Clone();
        for (int pass = 0; pass < 2; pass++)
        {
            filtered = OnePoleHighPass(filtered, dt, cutoff);
            Array.Reverse(filtered);
            filtered = OnePoleHighPass(filtered, dt, cutoff);
            Array.Reverse(filtered);
        }

        return filtered;
    }

    private double[] OnePoleHighPass(double[] values, double dt, double cutoff)
    {
        double rc = 1.0 / (2.0 * Math.PI * cutoff);
        double alpha = rc / (rc + dt);
        double[] output = new double[values.Length];

        for (int i = 1; i < values.Length; i++)
            output[i] = alpha * (output[i - 1] + values[i] - values[i - 1]);

        return output;
    }

    private int FindPeakHorizontalAccelerationIndex(double[] ns, double[] ew, int length)
    {
        int peakIndex = 0;
        double peak = -1;
        for (int i = 0; i < length; i++)
        {
            double combined = ns[i] * ns[i] + ew[i] * ew[i];
            if (combined > peak)
            {
                peak = combined;
                peakIndex = i;
            }
        }

        return peakIndex;
    }

    private double FindMaxHorizontalAcceleration(double[] ns, double[] ew)
    {
        int length = Math.Min(ns.Length, ew.Length);
        double maxSquared = 0;
        for (int i = 0; i < length; i++)
            maxSquared = Math.Max(maxSquared, ns[i] * ns[i] + ew[i] * ew[i]);

        return Math.Sqrt(maxSquared);
    }

    private void ScaleInPlace(double[] values, double scale)
    {
        for (int i = 0; i < values.Length; i++)
            values[i] *= scale;
    }

    private string ResolveLocalThaPath(string path)
    {
        if (Path.IsPathRooted(path))
            return path;

        string dataPathBased = Path.Combine(Application.dataPath, path);
        if (File.Exists(dataPathBased))
            return dataPathBased;

        string persistentPathBased = Path.Combine(Application.persistentDataPath, path);
        if (File.Exists(persistentPathBased))
            return persistentPathBased;

        return dataPathBased;
    }

    private void ParseLocalThaJson(string jsonText)
    {
        if (string.IsNullOrWhiteSpace(jsonText))
            throw new InvalidOperationException("THA JSON 내용이 비어 있습니다.");

        JToken root = JToken.Parse(jsonText);

        JArray rootArray = root as JArray;
        if (rootArray != null)
        {
            EnqueueFrames(rootArray);
            return;
        }

        JObject rootObject = root as JObject;
        if (rootObject == null)
            throw new InvalidOperationException("지원하지 않는 THA JSON 형식입니다.");

        // 기존 원격 서버 메시지 1개를 파일에 저장한 형태도 지원
        if (rootObject["type"] != null)
        {
            ProcessServerMessage(rootObject.ToString());
            return;
        }

        if (rootObject["dt"] != null)
            serverDt = (float)rootObject["dt"];

        // 여러 원격 서버 메시지를 messages 배열로 저장한 형태 지원
        JArray messages = rootObject["messages"] as JArray;
        if (messages != null)
        {
            foreach (JToken message in messages)
            {
                JObject messageObject = message as JObject;
                if (messageObject != null)
                    ProcessServerMessage(messageObject.ToString());
            }
            return;
        }

        JArray frames = rootObject["frames"] as JArray
                        ?? rootObject["batch"] as JArray
                        ?? rootObject["data"] as JArray;

        if (frames == null)
            throw new InvalidOperationException("THA JSON에서 frames, batch, data 배열을 찾을 수 없습니다.");

        EnqueueFrames(frames);
    }

    private void EnqueueFrames(JArray frames)
    {
        foreach (JToken item in frames)
        {
            SeismicFrame frame = ParseFrame(item);
            frameQueue.Enqueue(frame);
        }
    }

    private SeismicFrame ParseFrame(JToken item)
    {
        SeismicFrame frame = new SeismicFrame();

        float dx = ReadFloat(item, "dx", "x", "displacement_x");
        float dy = ReadFloat(item, "dy", "y", "displacement_y");
        float dz = ReadFloat(item, "dz", "z", "displacement_z");

        frame.displacement = new Vector3(dx, dy, dz) * displacementScale;

        float ax = ReadFloat(item, "ax", "acc_x", "acceleration_x");
        float ay = ReadFloat(item, "ay", "acc_y", "acceleration_y");
        float az = ReadFloat(item, "az", "acc_z", "acceleration_z");

        frame.acceleration = new Vector3(ax, ay, az) * accelerationScale;

        return frame;
    }

    private float ReadFloat(JToken item, params string[] keys)
    {
        foreach (string key in keys)
        {
            if (item[key] != null)
                return (float)item[key];
        }

        return 0f;
    }

    private void ProcessServerMessage(string jsonMessage)
    {
        try
        {
            JObject json = JObject.Parse(jsonMessage);

            if (json.ContainsKey("error"))
            {
                Debug.LogError($"[THA 데이터 에러] {json["error"]}");
                isReceiving = false;
                dataComplete = true;
                return;
            }

            string msgType = json["type"]?.ToString();

            if (msgType == "metadata")
            {
                if (json["dt"] != null)
                {
                    serverDt = (float)json["dt"];
                }
                Debug.Log($"[시뮬레이션 정보] 파일: {json["file"]}, dt: {serverDt}s");
            }
            else if (msgType == "data")
            {
                JArray batch = (JArray)json["batch"];

                foreach (var item in batch)
                {
                    SeismicFrame frame = ParseFrame(item);
                    frameQueue.Enqueue(frame);
                }

                if (json["is_finished"] != null && (bool)json["is_finished"])
                {
                    Debug.Log("[EarthquakeManager] THA 데이터 로드 완료.");
                    isReceiving = false;
                    dataComplete = true;
                }
            }
            else
            {
                // type이 없는 단일 로컬 JSON 메시지를 ProcessServerMessage로 넣었을 가능성 방어
                if (json["dt"] != null)
                    serverDt = (float)json["dt"];

                JArray frames = json["frames"] as JArray
                                ?? json["batch"] as JArray
                                ?? json["data"] as JArray;

                if (frames != null)
                {
                    EnqueueFrames(frames);
                    dataComplete = true;
                    isReceiving = false;
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[EarthquakeManager] JSON 파싱 에러: {e.Message}");
        }
    }

    void FixedUpdate()
    {
        EarthquakeSimulationMode mode = EffectiveSimulationMode;

        if (!isSimulating && !isReturning && frameQueue.Count > 0 && (isReceiving || dataComplete))
        {
            isSimulating = true;
            isReturning = false;
            Debug.Log("[EarthquakeManager] 지진 물리 시뮬레이션 시작!");
            SimulationStarted?.Invoke();
        }

        if (isSimulating)
        {
            int framesToConsume = Mathf.Max(1, Mathf.RoundToInt(Time.fixedDeltaTime / serverDt));

            SeismicFrame lastFrame = new SeismicFrame();
            bool hasData = false;

            for (int i = 0; i < framesToConsume; i++)
            {
                if (frameQueue.TryDequeue(out SeismicFrame frame))
                {
                    lastFrame = frame;
                    hasData = true;
                }
                else break;
            }

            if (hasData)
            {
                if (mode == EarthquakeSimulationMode.AccelerationInertial)
                {
                    ApplyInertialAcceleration(lastFrame);
                }
                else if (mode == EarthquakeSimulationMode.ShakeTable)
                {
                    ApplyShakeTableMotion(lastFrame);
                }
                else if (roomRigidbody != null)
                {
                    Vector3 targetPosition = initialPosition + lastFrame.displacement;

                    // 물리 엔진이 자연스러운 충돌과 마찰을 계산할 수 있도록,
                    // 순간이동(MovePosition) 대신 목표 위치 도달에 필요한 속도를 강제 주입
                    Vector3 requiredVelocity = (targetPosition - roomRigidbody.position) / Time.fixedDeltaTime;
                    roomRigidbody.linearVelocity = requiredVelocity;

                    highestVelocity = Mathf.Max(highestVelocity, requiredVelocity.magnitude);
                }
            }
            else if (!isReceiving && frameQueue.IsEmpty)
            {
                isSimulating = false;

                if (roomRigidbody != null)
                {
                    ClearRigidbodyVelocity(roomRigidbody);
                }

                if (mode == EarthquakeSimulationMode.LegacyDisplacement && returnRoomToOriginAfterSimulation)
                {
                    isReturning = true;
                    returnVelocity = Vector3.zero;
                    Debug.Log("[EarthquakeManager] 지진 종료. 원점 복귀 시작.");
                }
                else
                {
                    Debug.Log("[EarthquakeManager] 지진 종료. 원점 복귀 없이 시뮬레이션을 완료합니다.");
                    CompleteSimulation();
                }
            }
        }

        if (isReturning)
        {
            if (roomRigidbody != null)
            {
                Vector3 smoothPos = Vector3.SmoothDamp(roomRigidbody.position, initialPosition, ref returnVelocity, 0.3f);

                // 복귀 상태에서도 물리 법칙이 깨지지 않도록 속도로 제어
                Vector3 requiredVelocity = (smoothPos - roomRigidbody.position) / Time.fixedDeltaTime;
                roomRigidbody.linearVelocity = requiredVelocity;

                if (Vector3.Distance(roomRigidbody.position, initialPosition) < 0.001f)
                {
                    roomRigidbody.position = initialPosition;
                    ClearRigidbodyVelocity(roomRigidbody);
                    isReturning = false;
                    Debug.Log("[EarthquakeManager] 시뮬레이션이 완전히 종료되었습니다.");
                    CompleteSimulation();
                }
            }
        }
    }

    private void ApplyShakeTableMotion(SeismicFrame frame)
    {
        Vector3 tableDisplacement = frame.displacement * shakeTableDisplacementMultiplier;
        if (!includeVerticalShakeTableDisplacement)
            tableDisplacement.y = 0f;

        bool movedStructure = false;
        if (driveRoomStructureRootInShakeTable && roomStructureRigidbodies.Count > 0)
        {
            for (int i = 0; i < roomStructureRigidbodies.Count; i++)
            {
                Rigidbody rb = roomStructureRigidbodies[i];
                if (rb == null)
                    continue;

                if (!roomStructureInitialPositions.TryGetValue(rb, out Vector3 origin))
                    origin = rb.position;

                rb.MovePosition(origin + tableDisplacement);
                movedStructure = true;
            }
        }

        if (!movedStructure && roomRigidbody != null)
            roomRigidbody.MovePosition(initialPosition + tableDisplacement);

        float shakeVelocity = tableDisplacement.magnitude / Mathf.Max(Time.fixedDeltaTime, 0.0001f);
        highestVelocity = Mathf.Max(highestVelocity, shakeVelocity);
    }

    private void ApplyInertialAcceleration(SeismicFrame frame)
    {
        Vector3 inertialAcceleration = -frame.acceleration * inertialAccelerationMultiplier;
        if (!includeVerticalInertialAcceleration)
            inertialAcceleration.y = 0f;

        for (int i = 0; i < furnitureRigidbodies.Count; i++)
        {
            Rigidbody rb = furnitureRigidbodies[i];
            if (rb == null || rb.isKinematic)
                continue;

            rb.AddForce(inertialAcceleration, ForceMode.Acceleration);
            highestVelocity = Mathf.Max(highestVelocity, rb.linearVelocity.magnitude);
        }

        if (roomRigidbody != null)
        {
            ClearRigidbodyVelocity(roomRigidbody);
        }
    }

    private void ClearRigidbodyVelocity(Rigidbody rb)
    {
        if (rb == null || rb.isKinematic)
            return;

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
    }

    private void CompleteSimulation()
    {
        if (simulationFullyCompleted)
            return;

        simulationFullyCompleted = true;
        SimulationCompleted?.Invoke();

        if (runtimeMode == EarthquakeRuntimeMode.DedicatedServer && quitApplicationOnServerComplete)
        {
            Debug.Log("[EarthquakeManager] DedicatedServer 모드: Application.Quit 호출.");
            Application.Quit();
        }
    }

    [ContextMenu("Start Simulation")]
    public void ContextStartSimulation()
    {
        StartSimulation();
    }

    [ContextMenu("Start Local THA Simulation")]
    public void ContextStartLocalSimulation()
    {
        StartSimulation();
    }
}

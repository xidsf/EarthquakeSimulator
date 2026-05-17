using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

/// <summary>
/// Local CSV/THA 기반 주거공간 전도 위험 시뮬레이터.
/// 
/// 설계 의도:
/// - 사용자는 건물 종류, 현재 층수, 선택적으로 전체 층수와 필로티 여부만 제공한다.
/// - 방 내부 높이는 HoloLens2 방 인식 결과로 외부에서 주입한다.
/// - 층간 구조 보정값, 강진 시나리오, 감쇠비, 대표 전체층수 추정값은 내부 자동값으로 처리한다.
/// - KDS 41 17 00의 근사고유주기 Ta = Ct * hn^x 및 층응답가속도 개념을 참고한 간략 시간이력 기반 전도 위험 시뮬레이션이다.
/// 
/// 주의:
/// - 이 스크립트는 정식 구조설계용 시간이력해석기가 아니다.
/// - 일반 사용자에게 제공 가능한 입력만으로 해당 층 바닥가속도를 근사하고,
///   그 바닥가속도를 가구 전도/침범 위험 시뮬레이션에 적용하기 위한 목적이다.
/// </summary>
public class EarthquakeSimulator : MonoBehaviour
{
    public enum ResidentialBuildingType
    {
        Apartment,
        Villa,
        DetachedHouse
    }

    public enum PilotiCondition
    {
        Unknown,
        No,
        Yes
    }

    public enum SimulationMode
    {
        /// <summary>
        /// 방은 고정하고 가구 Rigidbody에 -floorAcceleration 관성가속도를 직접 적용한다.
        /// 전도 위험 분석 기본 모드.
        /// </summary>
        AccelerationInertial,

        /// <summary>
        /// 방 구조를 floor displacement로 움직인다.
        /// 시각적 흔들림 확인용. 변위 적분 품질에 민감하다.
        /// </summary>
        ShakeTable,

        /// <summary>
        /// 기존 방식 호환용. roomRigidbody에 목표 위치까지의 속도를 주입한다.
        /// 새 전도 위험 분석에는 권장하지 않는다.
        /// </summary>
        LegacyDisplacement
    }

    public enum AccelerationUnit
    {
        Gal,
        MeterPerSecondSquared,
        G
    }

    public enum CsvAxisConvention
    {
        /// <summary>CSV ax/ay/az를 Unity x/y/z로 그대로 사용한다.</summary>
        AsWritten,

        /// <summary>CSV가 ax=NS, ay=UP, az=EW인 경우. Unity x=NS, y=UP, z=EW.</summary>
        UnityX_NS_Y_UP_Z_EW,

        /// <summary>CSV가 ax=NS, ay=UP, az=EW인 경우. Unity x=EW, y=UP, z=NS.</summary>
        UnityX_EW_Y_UP_Z_NS
    }

    private const double GravityGal = 980.665;
    private const float KdsDefaultDampingRatio = 0.05f;
    private const float DefaultRoomClearHeightMeters = 2.4f;
    private const float DefaultFloorAssemblyAllowanceMeters = 0.35f;
    private const int DefaultStrongScenarioMmi = 9;
    private const float DefaultFallbackDt = 0.02f;

    private struct GroundMotion
    {
        public double dt;
        public double[] xGal;
        public double[] yGal;
        public double[] zGal;
        public int count;
    }

    private struct FloorMotion
    {
        public double dt;
        public Vector3[] displacementMeters;
        public Vector3[] accelerationMetersPerSecondSquared;
        public int count;

        public double buildingPeriod;
        public double floorModeShapeRatio;
        public double scaleFactor;
        public double inputPgaGal;
        public double floorPgaGal;
        public int effectiveTotalFloors;
        public double estimatedStoryHeightMeters;
        public double estimatedBuildingHeightMeters;
        public double pilotiMultiplier;
    }

    private struct SeismicFrame
    {
        public Vector3 displacement;
        public Vector3 acceleration;
    }

    [Header("Input CSV / THA")]
    [Tooltip("CSV TextAsset을 지정하면 file path보다 우선합니다.")]
    [SerializeField] private TextAsset localCsvAsset;

    [Tooltip("CSV 파일 경로입니다. 절대 경로 또는 Application.dataPath 기준 상대 경로를 사용할 수 있습니다.")]
    [SerializeField] private string localCsvFilePath = "THA_Process_Data/el_centro/el_centro_for_unity.csv";

    [Tooltip("외부에서 SetCsvText()로 직접 전달한 CSV 문자열입니다. 있으면 TextAsset과 file path보다 우선합니다.")]
    [SerializeField, HideInInspector] private string runtimeCsvText;

    [Tooltip("CSV 가속도 단위입니다. el_centro_for_unity.csv는 Gal 권장.")]
    [SerializeField] private AccelerationUnit inputAccelerationUnit = AccelerationUnit.Gal;

    [Tooltip("CSV 축 매핑 규칙입니다. 현재 el_centro_for_unity.csv는 UnityX_NS_Y_UP_Z_EW 권장.")]
    [SerializeField] private CsvAxisConvention csvAxisConvention = CsvAxisConvention.UnityX_NS_Y_UP_Z_EW;

    [Header("Simulation")]
    [SerializeField] private SimulationMode simulationMode = SimulationMode.AccelerationInertial;
    [SerializeField] private bool autoStartOnPlay = false;
    [Min(0.01f)]
    [SerializeField] private float playbackSpeed = 1.0f;
    [SerializeField] private float inertialAccelerationMultiplier = 1.0f;
    [SerializeField] private bool includeVerticalInertialAcceleration = false;
    [SerializeField] private float shakeTableDisplacementMultiplier = 1.0f;
    [SerializeField] private bool includeVerticalShakeTableDisplacement = false;
    [SerializeField] private bool returnToOriginAfterSimulation = true;

    [Header("Scene Objects")]
    [SerializeField] private Rigidbody roomRigidbody;
    [SerializeField] private GameObject furnitureRoot;
    [SerializeField] private GameObject roomStructureRoot;
    [SerializeField] private bool autoFindFurnitureRigidbodiesIfRootMissing = false;

    [Header("Physics Setup")]
    [SerializeField] private bool setRoomStructureKinematicForInertialMode = true;
    [SerializeField] private bool setRoomStructureKinematicForShakeTableMode = true;
    [SerializeField] private bool setFurnitureContinuousDynamic = true;
    [SerializeField] private bool enableFurnitureInterpolation = true;

    [Header("Building Info")]
    [Tooltip("주거 건물 종류")]
    [SerializeField] private ResidentialBuildingType buildingType = ResidentialBuildingType.Apartment;

    [Tooltip("거주(시뮬레이션 대상) 층수")]
    [Min(1)]
    [SerializeField] private int currentFloor = 1;

    [Tooltip("전체 층수. 0이면 건물 종류별 대표값을 자동 사용한다.")]
    [Min(0)]
    [SerializeField] private int explicitTotalFloors = 0;

    [Tooltip("필로티 구조 여부")]
    [SerializeField] private PilotiCondition pilotiCondition = PilotiCondition.Unknown;

    [Header("Strong-Motion Trimming")]
    [Tooltip("강진 구간(peak 주변)만 잘라 재생합니다. 비활성화 시 전체 시간이력 재생.")]
    [SerializeField] private bool enableStrongMotionTrimming = true;

    [Tooltip("Peak 시점 이전에 포함할 시간(초). 본진 도입부를 보존하기 위함.")]
    [Range(0f, 15f)]
    [SerializeField] private float strongMotionPreSeconds = 5f;

    [Tooltip("시뮬레이션 총 길이(초). Peak − preSeconds 시점부터 이만큼.")]
    [Range(5f, 60f)]
    [SerializeField] private float strongMotionTotalSeconds = 25f;

    [Header("Runtime Debug - Read Only")]
    [SerializeField] private bool isLoaded;
    [SerializeField] private bool isSimulating;
    [SerializeField] private int loadedFrameCount;
    [SerializeField] private float detectedDt;
    [SerializeField] private float computedBuildingPeriod;
    [SerializeField] private float computedFloorRatio;
    [SerializeField] private float computedScaleFactor = 1f;
    [SerializeField] private float inputPgaGal;
    [SerializeField] private float floorPgaGal;
    [SerializeField] private int effectiveTotalFloors;
    [SerializeField] private float estimatedStoryHeightMeters;
    [SerializeField] private float estimatedBuildingHeightMeters;
    [SerializeField] private float pilotiResponseMultiplier;
    [SerializeField] private float elapsedSimulationTime;

    public bool IsLoaded => isLoaded;
    public bool IsSimulating => isSimulating;
    public int LoadedFrameCount => loadedFrameCount;
    public float DetectedDt => detectedDt;
    public float ComputedBuildingPeriod => computedBuildingPeriod;
    public float ComputedFloorRatio => computedFloorRatio;
    public float ComputedScaleFactor => computedScaleFactor;
    public float InputPgaGal => inputPgaGal;
    public float FloorPgaGal => floorPgaGal;
    public int EffectiveTotalFloors => effectiveTotalFloors;
    public float EstimatedStoryHeightMeters => estimatedStoryHeightMeters;
    public float EstimatedBuildingHeightMeters => estimatedBuildingHeightMeters;

    public event Action SimulationStarted;
    public event Action SimulationCompleted;

    private float measuredRoomClearHeightMeters = DefaultRoomClearHeightMeters;
    private bool hasMeasuredRoomClearHeight;
    private float floorAssemblyAllowanceMeters = DefaultFloorAssemblyAllowanceMeters;

    private int defaultApartmentTotalFloors = 15;
    private int defaultVillaTotalFloors = 5;
    private int defaultDetachedHouseTotalFloors = 2;

    private bool applyStrongScenarioScaling = true;
    private int strongScenarioMmi = DefaultStrongScenarioMmi;
    private float minScaleFactor = 0.3f;
    private float maxScaleFactor = 3.0f;
    private float fallbackDt = DefaultFallbackDt;
    private bool removeInitialAccelerationOffset = false;
    private bool applyVerticalFloorResponse = false;

    private float pilotiYesResponseMultiplier = 1.25f;
    private float pilotiUnknownResponseMultiplier = 1.0f;

    private readonly List<SeismicFrame> frames = new List<SeismicFrame>();
    private readonly List<Rigidbody> furnitureRigidbodies = new List<Rigidbody>();
    private readonly List<Rigidbody> roomStructureRigidbodies = new List<Rigidbody>();
    private readonly Dictionary<Rigidbody, Vector3> roomInitialPositions = new Dictionary<Rigidbody, Vector3>();
    private readonly Dictionary<Rigidbody, Quaternion> roomInitialRotations = new Dictionary<Rigidbody, Quaternion>();

    private Vector3 roomInitialPosition;
    private Quaternion roomInitialRotation;
    private float accumulatedTime;
    private float totalDuration;
    private bool completedEventRaised;

    // =========================================================
    // ★ 추가됨: 외부 매니저에서 현재 시뮬레이션 각도를 전달받는 변수
    // =========================================================
    private int currentSimulationAngle = 0;

    private void Awake()
    {
        CacheRigidbodies();
        StoreInitialTransforms();
        ConfigurePhysicsObjects();
    }

    private void Start()
    {
        if (autoStartOnPlay)
            StartSimulation();
    }

    // ---------------------------------------------------------------------
    // 외부 입력 API
    // ---------------------------------------------------------------------

    // =========================================================
    // ★ 추가됨: 매니저에서 지진파의 각도를 설정하기 위해 호출하는 함수
    // =========================================================
    public void SetSimulationAngle(int angle)
    {
        currentSimulationAngle = angle;
    }

    public void SetBuildingInfo(
        ResidentialBuildingType type,
        int livingFloor,
        int totalFloors = 0,
        PilotiCondition piloti = PilotiCondition.Unknown)
    {
        buildingType = type;
        currentFloor = Mathf.Max(1, livingFloor);
        explicitTotalFloors = Mathf.Max(0, totalFloors);

        if (explicitTotalFloors > 0)
            explicitTotalFloors = Mathf.Max(explicitTotalFloors, currentFloor);

        pilotiCondition = piloti;
    }

    public void SetCurrentFloor(int livingFloor)
    {
        currentFloor = Mathf.Max(1, livingFloor);

        if (explicitTotalFloors > 0)
            explicitTotalFloors = Mathf.Max(explicitTotalFloors, currentFloor);
    }

    public void SetTotalFloors(int totalFloors)
    {
        explicitTotalFloors = Mathf.Max(0, totalFloors);

        if (explicitTotalFloors > 0)
            explicitTotalFloors = Mathf.Max(explicitTotalFloors, currentFloor);
    }

    public void SetPilotiCondition(PilotiCondition piloti)
    {
        pilotiCondition = piloti;
    }

    public void SetMeasuredRoomClearHeight(float heightMeters)
    {
        measuredRoomClearHeightMeters = Mathf.Clamp(heightMeters, 1.8f, 4.5f);
        hasMeasuredRoomClearHeight = true;
    }

    public void SetFloorAssemblyAllowance(float allowanceMeters)
    {
        floorAssemblyAllowanceMeters = Mathf.Clamp(allowanceMeters, 0.0f, 1.2f);
    }

    public void SetDefaultTotalFloorsByBuildingType(
        int apartmentFloors,
        int villaFloors,
        int detachedHouseFloors)
    {
        defaultApartmentTotalFloors = Mathf.Max(1, apartmentFloors);
        defaultVillaTotalFloors = Mathf.Max(1, villaFloors);
        defaultDetachedHouseTotalFloors = Mathf.Max(1, detachedHouseFloors);
    }

    public void SetStrongScenarioMmi(int mmi)
    {
        strongScenarioMmi = Mathf.Clamp(mmi, 5, 9);
    }

    public void SetApplyStrongScenarioScaling(bool enabled)
    {
        applyStrongScenarioScaling = enabled;
    }

    public void SetScaleFactorClamp(float minValue, float maxValue)
    {
        minScaleFactor = Mathf.Max(0.01f, minValue);
        maxScaleFactor = Mathf.Max(minScaleFactor, maxValue);
    }

    public void SetPilotiResponseMultipliers(float yesMultiplier, float unknownMultiplier = 1.0f)
    {
        pilotiYesResponseMultiplier = Mathf.Clamp(yesMultiplier, 1.0f, 2.0f);
        pilotiUnknownResponseMultiplier = Mathf.Clamp(unknownMultiplier, 1.0f, 1.5f);
    }

    public void SetCsvText(string csvText)
    {
        runtimeCsvText = csvText ?? string.Empty;
    }

    public void SetCsvTextAsset(TextAsset csvAsset)
    {
        localCsvAsset = csvAsset;
    }

    public void SetCsvFilePath(string path)
    {
        localCsvFilePath = path ?? string.Empty;
    }

    public void SetInputAccelerationUnit(AccelerationUnit unit)
    {
        inputAccelerationUnit = unit;
    }

    public void SetCsvAxisConvention(CsvAxisConvention convention)
    {
        csvAxisConvention = convention;
    }

    public void SetFallbackDt(float dt)
    {
        fallbackDt = Mathf.Max(0.001f, dt);
    }

    public void SetRemoveInitialAccelerationOffset(bool enabled)
    {
        removeInitialAccelerationOffset = enabled;
    }

    public void SetApplyVerticalFloorResponse(bool enabled)
    {
        applyVerticalFloorResponse = enabled;
    }

    public void SetSimulationMode(SimulationMode mode)
    {
        simulationMode = mode;
        ConfigurePhysicsObjects();
    }

    public void SetPlaybackSpeed(float speed)
    {
        playbackSpeed = Mathf.Max(0.01f, speed);
    }

    public void SetInertialOptions(float multiplier, bool includeVertical)
    {
        inertialAccelerationMultiplier = multiplier;
        includeVerticalInertialAcceleration = includeVertical;
    }

    public void SetShakeTableOptions(float displacementMultiplier, bool includeVertical)
    {
        shakeTableDisplacementMultiplier = displacementMultiplier;
        includeVerticalShakeTableDisplacement = includeVertical;
    }

    public void SetSceneReferences(
        Rigidbody roomBody,
        GameObject furnitureRootObject,
        GameObject roomStructureRootObject)
    {
        roomRigidbody = roomBody;
        furnitureRoot = furnitureRootObject;
        roomStructureRoot = roomStructureRootObject;

        CacheRigidbodies();
        StoreInitialTransforms();
        ConfigurePhysicsObjects();
    }

    public void SetFurnitureRoot(GameObject root)
    {
        furnitureRoot = root;
        CacheRigidbodies();
        ConfigurePhysicsObjects();
    }

    public void SetRoomStructureRoot(GameObject root)
    {
        roomStructureRoot = root;
        CacheRigidbodies();
        StoreInitialTransforms();
        ConfigurePhysicsObjects();
    }

    public void SetRoomRigidbody(Rigidbody rb)
    {
        roomRigidbody = rb;
        CacheRigidbodies();
        StoreInitialTransforms();
        ConfigurePhysicsObjects();
    }

    // ---------------------------------------------------------------------
    // 실행 API
    // ---------------------------------------------------------------------

    [ContextMenu("Start Simulation")]
    public void StartSimulation()
    {
        try
        {
            StopSimulationInternal(resetTransforms: true, raiseCompletedEvent: false);

            LoadAndBuildFloorMotion();

            if (frames.Count == 0)
            {
                Debug.LogError("[EarthquakeSimulator] 시뮬레이션 프레임이 없습니다.");
                return;
            }

            accumulatedTime = 0f;
            elapsedSimulationTime = 0f;
            completedEventRaised = false;
            isSimulating = true;

            SimulationStarted?.Invoke();

            Debug.Log(
                $"[EarthquakeSimulator] Simulation started. (Angle: {currentSimulationAngle}도) " +
                $"building={buildingType}, floor={currentFloor}/{effectiveTotalFloors}, piloti={pilotiCondition}, " +
                $"frames={frames.Count}, dt={detectedDt:F4}, period={computedBuildingPeriod:F3}s, " +
                $"floorRatio={computedFloorRatio:F3}, scale={computedScaleFactor:F3}, " +
                $"inputPGA={inputPgaGal:F2}gal, floorPGA={floorPgaGal:F2}gal"
            );
        }
        catch (Exception ex)
        {
            isLoaded = false;
            isSimulating = false;
            Debug.LogError($"[EarthquakeSimulator] StartSimulation failed: {ex.Message}\n{ex.StackTrace}");
        }
    }

    [ContextMenu("Stop Simulation")]
    public void StopSimulation()
    {
        StopSimulationInternal(resetTransforms: returnToOriginAfterSimulation, raiseCompletedEvent: true);
    }

    [ContextMenu("Reload CSV Only")]
    public void ReloadCsvOnly()
    {
        LoadAndBuildFloorMotion();
    }

    private void FixedUpdate()
    {
        if (!isSimulating || frames.Count == 0)
            return;

        accumulatedTime += Time.fixedDeltaTime * playbackSpeed;
        elapsedSimulationTime = accumulatedTime;

        if (accumulatedTime >= totalDuration)
        {
            ApplyFrame(frames[frames.Count - 1]);

            isSimulating = false;

            if (returnToOriginAfterSimulation)
                ResetRoomTransforms();

            ClearDynamicVelocities();
            RaiseCompletedOnce();
            return;
        }

        SeismicFrame frame = EvaluateFrame(accumulatedTime);
        ApplyFrame(frame);
    }

    // ---------------------------------------------------------------------
    // CSV → Ground motion → Floor motion
    // ---------------------------------------------------------------------

    private void LoadAndBuildFloorMotion()
    {
        string csvText = GetCsvText();
        GroundMotion groundMotion = ParseGroundMotionCsv(csvText);
        FloorMotion floorMotion = BuildFloorMotion(groundMotion);

        frames.Clear();

        // =========================================================
        // ★ 추가됨: 여기서 지진파 벡터(가속도, 변위)에 회전을 적용합니다.
        // =========================================================
        Quaternion rotationOffset = Quaternion.Euler(0, currentSimulationAngle, 0);

        for (int i = 0; i < floorMotion.count; i++)
        {
            frames.Add(new SeismicFrame
            {
                // Y축을 기준으로 currentSimulationAngle 만큼 회전
                displacement = rotationOffset * floorMotion.displacementMeters[i],
                acceleration = rotationOffset * floorMotion.accelerationMetersPerSecondSquared[i]
            });
        }

        isLoaded = true;
        loadedFrameCount = floorMotion.count;
        detectedDt = (float)floorMotion.dt;
        computedBuildingPeriod = (float)floorMotion.buildingPeriod;
        computedFloorRatio = (float)floorMotion.floorModeShapeRatio;
        computedScaleFactor = (float)floorMotion.scaleFactor;
        inputPgaGal = (float)floorMotion.inputPgaGal;
        floorPgaGal = (float)floorMotion.floorPgaGal;
        effectiveTotalFloors = floorMotion.effectiveTotalFloors;
        estimatedStoryHeightMeters = (float)floorMotion.estimatedStoryHeightMeters;
        estimatedBuildingHeightMeters = (float)floorMotion.estimatedBuildingHeightMeters;
        pilotiResponseMultiplier = (float)floorMotion.pilotiMultiplier;
        totalDuration = Mathf.Max(0f, (frames.Count - 1) * detectedDt);
    }

    private string GetCsvText()
    {
        if (!string.IsNullOrWhiteSpace(runtimeCsvText))
            return runtimeCsvText;

        if (localCsvAsset != null)
            return localCsvAsset.text;

        string path = ResolvePath(localCsvFilePath);
        if (!File.Exists(path))
            throw new FileNotFoundException($"CSV 파일을 찾을 수 없습니다: {path}");

        return File.ReadAllText(path);
    }

    private string ResolvePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;

        if (Path.IsPathRooted(path))
            return path;

        string dataPathBased = Path.Combine(Application.dataPath, path);
        if (File.Exists(dataPathBased))
            return dataPathBased;

        string streamingAssetsBased = Path.Combine(Application.streamingAssetsPath, path);
        if (File.Exists(streamingAssetsBased))
            return streamingAssetsBased;

        string persistentPathBased = Path.Combine(Application.persistentDataPath, path);
        if (File.Exists(persistentPathBased))
            return persistentPathBased;

        string currentDirectoryBased = Path.Combine(Directory.GetCurrentDirectory(), path);
        if (File.Exists(currentDirectoryBased))
            return currentDirectoryBased;

        return dataPathBased;
    }

    private GroundMotion ParseGroundMotionCsv(string csvText)
    {
        if (string.IsNullOrWhiteSpace(csvText))
            throw new InvalidOperationException("CSV 내용이 비어 있습니다.");

        string[] lines = csvText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        int headerLineIndex = -1;
        string[] headers = null;

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("#"))
                continue;

            string[] parts = SplitLine(line);
            if (ContainsHeader(parts, "ax") || ContainsHeader(parts, "acc_x") || ContainsHeader(parts, "acceleration_x"))
            {
                headerLineIndex = i;
                headers = NormalizeHeaders(parts);
                break;
            }
        }

        if (headerLineIndex < 0 || headers == null)
            throw new InvalidOperationException("CSV에서 ax/ay/az 헤더를 찾을 수 없습니다. 권장 헤더: t,dx,dy,dz,ax,ay,az");

        int tIndex = FindHeaderIndex(headers, "t", "time");
        int axIndex = FindHeaderIndex(headers, "ax", "acc_x", "acceleration_x");
        int ayIndex = FindHeaderIndex(headers, "ay", "acc_y", "acceleration_y");
        int azIndex = FindHeaderIndex(headers, "az", "acc_z", "acceleration_z");

        if (axIndex < 0 || azIndex < 0)
            throw new InvalidOperationException("CSV에는 최소한 ax와 az 열이 필요합니다.");

        List<double> timeValues = new List<double>();
        List<double> rawX = new List<double>();
        List<double> rawY = new List<double>();
        List<double> rawZ = new List<double>();

        for (int i = headerLineIndex + 1; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("#"))
                continue;

            string[] parts = SplitLine(line);
            if (parts.Length <= Math.Max(axIndex, azIndex))
                continue;

            if (!TryParseDouble(parts[axIndex], out double ax) ||
                !TryParseDouble(parts[azIndex], out double az))
            {
                continue;
            }

            double ay = ReadOptionalDouble(parts, ayIndex);

            if (tIndex >= 0 && tIndex < parts.Length && TryParseDouble(parts[tIndex], out double t))
                timeValues.Add(t);

            ApplyAxisConvention(ref ax, ref ay, ref az);

            rawX.Add(ConvertAccelerationToGal(ax));
            rawY.Add(ConvertAccelerationToGal(ay));
            rawZ.Add(ConvertAccelerationToGal(az));
        }

        if (rawX.Count == 0)
            throw new InvalidOperationException("CSV에서 유효한 가속도 데이터를 읽지 못했습니다.");

        double dt = DetectDt(timeValues);
        int count = rawX.Count;

        double[] x = rawX.ToArray();
        double[] y = rawY.ToArray();
        double[] z = rawZ.ToArray();

        if (removeInitialAccelerationOffset && count > 0)
        {
            RemoveInitialOffsetInPlace(x);
            RemoveInitialOffsetInPlace(y);
            RemoveInitialOffsetInPlace(z);
        }

        return new GroundMotion
        {
            dt = dt,
            xGal = x,
            yGal = y,
            zGal = z,
            count = count
        };
    }

    private FloorMotion BuildFloorMotion(GroundMotion ground)
    {
        if (ground.count == 0)
            throw new InvalidOperationException("GroundMotion이 비어 있습니다.");

        double[] groundX = (double[])ground.xGal.Clone();
        double[] groundY = (double[])ground.yGal.Clone();
        double[] groundZ = (double[])ground.zGal.Clone();

        int totalFloorsForComputation = GetEffectiveTotalFloors();
        double storyHeight = GetEstimatedStoryHeightMeters();
        double buildingHeight = totalFloorsForComputation * storyHeight;
        double period = GetApproximateBuildingPeriod(buildingType, buildingHeight);
        double pilotiMultiplier = GetPilotiMultiplier();

        double scale = 1.0;
        double inputPga = FindMaxHorizontalAcceleration(groundX, groundZ);

        if (applyStrongScenarioScaling)
        {
            scale = ComputeDesignScaleFactor(groundX, groundZ, ground.dt, period);
            ScaleInPlace(groundX, scale);
            ScaleInPlace(groundY, scale);
            ScaleInPlace(groundZ, scale);
        }

        int clampedFloor = Mathf.Clamp(currentFloor, 1, Math.Max(1, totalFloorsForComputation));
        double normalizedHeight = (double)clampedFloor / Math.Max(1, totalFloorsForComputation);

        double[] modePeriods =
        {
            period,
            period / 3.0,
            period / 5.0
        };
        double[] modeShapes =
        {
            Math.Sin(0.5 * Math.PI * normalizedHeight),
            Math.Sin(1.5 * Math.PI * normalizedHeight),
            Math.Sin(2.5 * Math.PI * normalizedHeight)
        };
        double[] participationFactors =
        {
            4.0 / Math.PI,
            4.0 / (3.0 * Math.PI),
            4.0 / (5.0 * Math.PI)
        };

        double weightSum = 0.0;
        double[] modeWeights = new double[3];
        for (int m = 0; m < 3; m++)
        {
            modeWeights[m] = participationFactors[m] * modeShapes[m];
            weightSum += modeWeights[m];
        }
        double groundResidual = 1.0 - weightSum;

        double[] floorX = new double[ground.count];
        double[] floorZ = new double[ground.count];
        double[] floorY = applyVerticalFloorResponse ? new double[ground.count] : groundY;

        for (int m = 0; m < 3; m++)
        {
            double[] modeRespX = ApplySdofAbsoluteAccelerationGal(groundX, ground.dt, modePeriods[m], KdsDefaultDampingRatio);
            double[] modeRespZ = ApplySdofAbsoluteAccelerationGal(groundZ, ground.dt, modePeriods[m], KdsDefaultDampingRatio);
            double w = modeWeights[m];
            for (int i = 0; i < ground.count; i++)
            {
                floorX[i] += w * modeRespX[i];
                floorZ[i] += w * modeRespZ[i];
            }

            if (applyVerticalFloorResponse)
            {
                double[] modeRespY = ApplySdofAbsoluteAccelerationGal(groundY, ground.dt, modePeriods[m], KdsDefaultDampingRatio);
                for (int i = 0; i < ground.count; i++)
                    floorY[i] += w * modeRespY[i];
            }
        }

        for (int i = 0; i < ground.count; i++)
        {
            floorX[i] += groundResidual * groundX[i];
            floorZ[i] += groundResidual * groundZ[i];
            if (applyVerticalFloorResponse)
                floorY[i] += groundResidual * groundY[i];
        }

        ApplyMultiplierInPlace(floorX, pilotiMultiplier);
        ApplyMultiplierInPlace(floorZ, pilotiMultiplier);
        if (applyVerticalFloorResponse)
            ApplyMultiplierInPlace(floorY, pilotiMultiplier);

        double[] dispXcm = CalculateDisplacementCm(floorX, ground.dt);
        double[] dispYcm = CalculateDisplacementCm(floorY, ground.dt);
        double[] dispZcm = CalculateDisplacementCm(floorZ, ground.dt);

        int sliceStart = 0;
        int sliceEnd = ground.count;
        if (enableStrongMotionTrimming && ground.count > 0)
        {
            int peakIndex = FindPeakHorizontalAccelerationIndex(floorX, floorZ, ground.count);
            int preFrames = Math.Max(0, (int)Math.Round(strongMotionPreSeconds / ground.dt));
            int maxFrames = Math.Max(1, (int)Math.Round(strongMotionTotalSeconds / ground.dt));
            sliceStart = Math.Max(0, peakIndex - preFrames);
            sliceEnd = Math.Min(ground.count, sliceStart + maxFrames);
            if (sliceEnd <= sliceStart)
            {
                sliceStart = 0;
                sliceEnd = ground.count;
            }
        }

        int count = sliceEnd - sliceStart;

        double dxOrigin = dispXcm[sliceStart];
        double dyOrigin = dispYcm[sliceStart];
        double dzOrigin = dispZcm[sliceStart];

        Vector3[] displacementMeters = new Vector3[count];
        Vector3[] accelerationMps2 = new Vector3[count];

        for (int i = 0; i < count; i++)
        {
            int idx = sliceStart + i;
            displacementMeters[i] = new Vector3(
                (float)((dispXcm[idx] - dxOrigin) * 0.01),
                (float)((dispYcm[idx] - dyOrigin) * 0.01),
                (float)((dispZcm[idx] - dzOrigin) * 0.01)
            );

            accelerationMps2[i] = new Vector3(
                (float)(floorX[idx] * 0.01),
                (float)(floorY[idx] * 0.01),
                (float)(floorZ[idx] * 0.01)
            );
        }

        double slicedFloorPga = ComputeMaxHorizontalAccelerationInRange(floorX, floorZ, sliceStart, sliceEnd);

        return new FloorMotion
        {
            dt = ground.dt,
            displacementMeters = displacementMeters,
            accelerationMetersPerSecondSquared = accelerationMps2,
            count = count,
            buildingPeriod = period,
            floorModeShapeRatio = modeShapes[0],
            scaleFactor = scale,
            inputPgaGal = inputPga,
            floorPgaGal = slicedFloorPga,
            effectiveTotalFloors = totalFloorsForComputation,
            estimatedStoryHeightMeters = storyHeight,
            estimatedBuildingHeightMeters = buildingHeight,
            pilotiMultiplier = pilotiMultiplier
        };
    }

    private int FindPeakHorizontalAccelerationIndex(double[] xGal, double[] zGal, int length)
    {
        int count = Math.Min(length, Math.Min(xGal.Length, zGal.Length));
        int peakIndex = 0;
        double peakSquared = -1.0;

        for (int i = 0; i < count; i++)
        {
            double combined = xGal[i] * xGal[i] + zGal[i] * zGal[i];
            if (combined > peakSquared)
            {
                peakSquared = combined;
                peakIndex = i;
            }
        }

        return peakIndex;
    }

    private double ComputeMaxHorizontalAccelerationInRange(double[] xGal, double[] zGal, int startInclusive, int endExclusive)
    {
        int safeStart = Math.Max(0, startInclusive);
        int safeEnd = Math.Min(Math.Min(xGal.Length, zGal.Length), endExclusive);
        double maxSquared = 0.0;

        for (int i = safeStart; i < safeEnd; i++)
        {
            double combined = xGal[i] * xGal[i] + zGal[i] * zGal[i];
            if (combined > maxSquared)
                maxSquared = combined;
        }

        return Math.Sqrt(maxSquared);
    }

    // ---------------------------------------------------------------------
    // 시뮬레이션 적용
    // ---------------------------------------------------------------------

    private void ApplyFrame(SeismicFrame frame)
    {
        switch (simulationMode)
        {
            case SimulationMode.AccelerationInertial:
                ApplyInertialAcceleration(frame.acceleration);
                break;

            case SimulationMode.ShakeTable:
                ApplyShakeTableDisplacement(frame.displacement);
                break;

            case SimulationMode.LegacyDisplacement:
                ApplyLegacyDisplacement(frame.displacement);
                break;
        }
    }

    private void ApplyInertialAcceleration(Vector3 floorAcceleration)
    {
        Vector3 inertialAcceleration = -floorAcceleration * inertialAccelerationMultiplier;

        if (!includeVerticalInertialAcceleration)
            inertialAcceleration.y = 0f;

        for (int i = 0; i < furnitureRigidbodies.Count; i++)
        {
            Rigidbody rb = furnitureRigidbodies[i];
            if (rb == null || rb.isKinematic)
                continue;

            rb.AddForce(inertialAcceleration, ForceMode.Acceleration);
        }
    }

    private void ApplyShakeTableDisplacement(Vector3 floorDisplacement)
    {
        Vector3 displacement = floorDisplacement * shakeTableDisplacementMultiplier;

        if (!includeVerticalShakeTableDisplacement)
            displacement.y = 0f;

        if (roomStructureRigidbodies.Count > 0)
        {
            for (int i = 0; i < roomStructureRigidbodies.Count; i++)
            {
                Rigidbody rb = roomStructureRigidbodies[i];
                if (rb == null)
                    continue;

                if (!roomInitialPositions.TryGetValue(rb, out Vector3 initialPosition))
                    initialPosition = rb.position;

                rb.MovePosition(initialPosition + displacement);
            }

            return;
        }

        if (roomRigidbody != null)
            roomRigidbody.MovePosition(roomInitialPosition + displacement);
    }

    private void ApplyLegacyDisplacement(Vector3 floorDisplacement)
    {
        if (roomRigidbody == null)
            return;

        Vector3 targetPosition = roomInitialPosition + floorDisplacement;
        Vector3 requiredVelocity = (targetPosition - roomRigidbody.position) / Mathf.Max(Time.fixedDeltaTime, 0.0001f);
        roomRigidbody.linearVelocity = requiredVelocity;
    }

    private SeismicFrame EvaluateFrame(float time)
    {
        if (frames.Count == 0)
            return default;

        if (time <= 0f)
            return frames[0];

        float framePosition = time / Mathf.Max(detectedDt, 0.0001f);
        int index0 = Mathf.FloorToInt(framePosition);
        int index1 = Mathf.Min(index0 + 1, frames.Count - 1);

        if (index0 >= frames.Count - 1)
            return frames[frames.Count - 1];

        float t = framePosition - index0;

        SeismicFrame a = frames[index0];
        SeismicFrame b = frames[index1];

        return new SeismicFrame
        {
            displacement = Vector3.LerpUnclamped(a.displacement, b.displacement, t),
            acceleration = Vector3.LerpUnclamped(a.acceleration, b.acceleration, t)
        };
    }

    // ---------------------------------------------------------------------
    // 건물/층/강진 시나리오 자동 추정
    // ---------------------------------------------------------------------

    private int GetEffectiveTotalFloors()
    {
        if (explicitTotalFloors > 0)
            return Mathf.Max(currentFloor, explicitTotalFloors);

        int defaultValue;

        switch (buildingType)
        {
            case ResidentialBuildingType.DetachedHouse:
                defaultValue = defaultDetachedHouseTotalFloors;
                break;

            case ResidentialBuildingType.Villa:
                defaultValue = defaultVillaTotalFloors;
                break;

            case ResidentialBuildingType.Apartment:
            default:
                defaultValue = defaultApartmentTotalFloors;
                break;
        }

        return Mathf.Max(currentFloor, defaultValue);
    }

    private double GetEstimatedStoryHeightMeters()
    {
        float clearHeight = hasMeasuredRoomClearHeight ? measuredRoomClearHeightMeters : DefaultRoomClearHeightMeters;
        float storyHeight = clearHeight + floorAssemblyAllowanceMeters;
        return Math.Max(2.0, storyHeight);
    }

    private double GetApproximateBuildingPeriod(ResidentialBuildingType type, double buildingHeightMeters)
    {
        double ct;
        double exponent;

        switch (type)
        {
            case ResidentialBuildingType.Apartment:
                ct = 0.0488;
                exponent = 0.75;
                break;

            case ResidentialBuildingType.Villa:
                ct = 0.0466;
                exponent = 0.90;
                break;

            case ResidentialBuildingType.DetachedHouse:
            default:
                ct = 0.0488;
                exponent = 0.75;
                break;
        }

        double period = ct * Math.Pow(Math.Max(1.0, buildingHeightMeters), exponent);
        return Math.Max(0.05, period);
    }

    private double GetPilotiMultiplier()
    {
        if (pilotiCondition == PilotiCondition.Yes)
            return pilotiYesResponseMultiplier;

        if (pilotiCondition == PilotiCondition.Unknown)
            return pilotiUnknownResponseMultiplier;

        return 1.0;
    }

    private double ComputeDesignScaleFactor(double[] groundXGal, double[] groundZGal, double dt, double period)
    {
        int intensity = Mathf.Clamp(strongScenarioMmi, 5, 9);
        int returnPeriod = GetReturnPeriodFromMmi(intensity);

        double designSaG = ComputeKdsLikeDesignSpectrumG(period, returnPeriod);

        if (intensity == 9)
            designSaG *= 1.5;

        double designSaGal = designSaG * GravityGal;

        double saX = ComputeResponseSpectrumAtPeriodGal(groundXGal, dt, period);
        double saZ = ComputeResponseSpectrumAtPeriodGal(groundZGal, dt, period);
        double recordSaGal = Math.Sqrt(saX * saX + saZ * saZ);

        if (recordSaGal <= 1e-6)
            return 1.0;

        double unclamped = designSaGal / recordSaGal;
        return Clamp(unclamped, minScaleFactor, maxScaleFactor);
    }

    private int GetReturnPeriodFromMmi(int intensity)
    {
        switch (intensity)
        {
            case 5: return 500;
            case 6: return 1000;
            case 7: return 2400;
            case 8:
            case 9: return 4800;
            default: return 1000;
        }
    }

    private double GetKdsRiskFactor(int returnPeriod)
    {
        switch (returnPeriod)
        {
            case 50: return 0.40;
            case 100: return 0.57;
            case 200: return 0.73;
            case 500: return 1.00;
            case 1000: return 1.40;
            case 2400: return 2.00;
            case 4800: return 2.60;
            default: return 1.00;
        }
    }

    private double ComputeKdsLikeDesignSpectrumG(double period, int returnPeriod)
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

    // ---------------------------------------------------------------------
    // SDOF 시간이력 응답 계산
    // ---------------------------------------------------------------------

    private double ComputeResponseSpectrumAtPeriodGal(double[] groundAccelerationGal, double dt, double period)
    {
        double[] floorAcceleration = ApplySdofAbsoluteAccelerationGal(groundAccelerationGal, dt, period, KdsDefaultDampingRatio);
        double max = 0.0;

        for (int i = 0; i < floorAcceleration.Length; i++)
            max = Math.Max(max, Math.Abs(floorAcceleration[i]));

        return max;
    }

    private double[] ApplySdofAbsoluteAccelerationGal(double[] groundAccelerationGal, double dt, double period, double damping)
    {
        if (groundAccelerationGal == null || groundAccelerationGal.Length == 0)
            return new double[0];

        if (period < 0.05)
            return (double[])groundAccelerationGal.Clone();

        double omega = 2.0 * Math.PI / period;
        double stiffness = omega * omega;
        double dampingCoefficient = 2.0 * damping * omega;

        double[] absoluteAcceleration = new double[groundAccelerationGal.Length];

        double relativeDisplacement = 0.0;
        double relativeVelocity = 0.0;

        for (int i = 0; i < groundAccelerationGal.Length; i++)
        {
            absoluteAcceleration[i] = -dampingCoefficient * relativeVelocity - stiffness * relativeDisplacement;

            StepSdofRungeKutta(
                ref relativeDisplacement,
                ref relativeVelocity,
                groundAccelerationGal[i],
                dt,
                dampingCoefficient,
                stiffness
            );
        }

        return absoluteAcceleration;
    }

    private void StepSdofRungeKutta(
        ref double displacement,
        ref double velocity,
        double groundAcceleration,
        double dt,
        double damping,
        double stiffness)
    {
        double k1x = velocity;
        double k1v = SdofRelativeAcceleration(displacement, velocity, groundAcceleration, damping, stiffness);

        double k2x = velocity + 0.5 * dt * k1v;
        double k2v = SdofRelativeAcceleration(displacement + 0.5 * dt * k1x, velocity + 0.5 * dt * k1v, groundAcceleration, damping, stiffness);

        double k3x = velocity + 0.5 * dt * k2v;
        double k3v = SdofRelativeAcceleration(displacement + 0.5 * dt * k2x, velocity + 0.5 * dt * k2v, groundAcceleration, damping, stiffness);

        double k4x = velocity + dt * k3v;
        double k4v = SdofRelativeAcceleration(displacement + dt * k3x, velocity + dt * k3v, groundAcceleration, damping, stiffness);

        displacement += (dt / 6.0) * (k1x + 2.0 * k2x + 2.0 * k3x + k4x);
        velocity += (dt / 6.0) * (k1v + 2.0 * k2v + 2.0 * k3v + k4v);
    }

    private double SdofRelativeAcceleration(
        double displacement,
        double velocity,
        double groundAcceleration,
        double damping,
        double stiffness)
    {
        return -damping * velocity - stiffness * displacement - groundAcceleration;
    }

    // ---------------------------------------------------------------------
    // 변위 적분 및 필터
    // ---------------------------------------------------------------------

    private double[] CalculateDisplacementCm(double[] accelerationGal, double dt)
    {
        const double cutoffHz = 0.05;
        double[] accelerationFiltered = HighPassFilter(accelerationGal, dt, cutoffHz);
        double[] velocity = CumulativeTrapezoid(accelerationFiltered, dt);
        double[] velocityFiltered = HighPassFilter(velocity, dt, cutoffHz);
        double[] displacement = CumulativeTrapezoid(velocityFiltered, dt);
        return HighPassFilter(displacement, dt, cutoffHz);
    }

    private double[] CumulativeTrapezoid(double[] values, double dt)
    {
        double[] integrated = new double[values.Length];

        for (int i = 1; i < values.Length; i++)
            integrated[i] = integrated[i - 1] + (values[i - 1] + values[i]) * 0.5 * dt;

        return integrated;
    }

    private double[] HighPassFilter(double[] values, double dt, double cutoffHz)
    {
        if (values == null || values.Length == 0)
            return new double[0];

        double[] filtered = OnePoleHighPass(values, dt, cutoffHz);
        Array.Reverse(filtered);
        filtered = OnePoleHighPass(filtered, dt, cutoffHz);
        Array.Reverse(filtered);
        return filtered;
    }

    private double[] OnePoleHighPass(double[] values, double dt, double cutoffHz)
    {
        double rc = 1.0 / (2.0 * Math.PI * cutoffHz);
        double alpha = rc / (rc + dt);
        double[] output = new double[values.Length];

        for (int i = 1; i < values.Length; i++)
            output[i] = alpha * (output[i - 1] + values[i] - values[i - 1]);

        return output;
    }

    // ---------------------------------------------------------------------
    // Rigidbody cache / reset
    // ---------------------------------------------------------------------

    private void CacheRigidbodies()
    {
        furnitureRigidbodies.Clear();
        roomStructureRigidbodies.Clear();

        if (furnitureRoot != null)
        {
            furnitureRigidbodies.AddRange(furnitureRoot.GetComponentsInChildren<Rigidbody>(true));
        }
        else if (autoFindFurnitureRigidbodiesIfRootMissing)
        {
            Rigidbody[] all = FindObjectsByType<Rigidbody>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                Rigidbody rb = all[i];
                if (rb == null || rb == roomRigidbody)
                    continue;

                if (roomStructureRoot != null && rb.transform.IsChildOf(roomStructureRoot.transform))
                    continue;

                furnitureRigidbodies.Add(rb);
            }
        }

        if (roomStructureRoot != null)
            roomStructureRigidbodies.AddRange(roomStructureRoot.GetComponentsInChildren<Rigidbody>(true));
        else if (roomRigidbody != null)
            roomStructureRigidbodies.Add(roomRigidbody);
    }

    private void StoreInitialTransforms()
    {
        if (roomRigidbody != null)
        {
            roomInitialPosition = roomRigidbody.position;
            roomInitialRotation = roomRigidbody.rotation;
        }

        roomInitialPositions.Clear();
        roomInitialRotations.Clear();

        for (int i = 0; i < roomStructureRigidbodies.Count; i++)
        {
            Rigidbody rb = roomStructureRigidbodies[i];
            if (rb == null)
                continue;

            roomInitialPositions[rb] = rb.position;
            roomInitialRotations[rb] = rb.rotation;
        }
    }

    private void ConfigurePhysicsObjects()
    {
        bool needsKinematicRoom =
            simulationMode == SimulationMode.ShakeTable && setRoomStructureKinematicForShakeTableMode ||
            simulationMode == SimulationMode.AccelerationInertial && setRoomStructureKinematicForInertialMode;

        for (int i = 0; i < roomStructureRigidbodies.Count; i++)
        {
            Rigidbody rb = roomStructureRigidbodies[i];
            if (rb == null)
                continue;

            rb.useGravity = false;
            rb.isKinematic = needsKinematicRoom;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.constraints = RigidbodyConstraints.FreezeRotation;
            ClearVelocity(rb);
        }

        if (simulationMode == SimulationMode.LegacyDisplacement && roomRigidbody != null)
        {
            roomRigidbody.isKinematic = false;
            roomRigidbody.useGravity = false;
            roomRigidbody.mass = Mathf.Max(roomRigidbody.mass, 10000000f);
            roomRigidbody.constraints = RigidbodyConstraints.FreezeRotation;
            roomRigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        }

        for (int i = 0; i < furnitureRigidbodies.Count; i++)
        {
            Rigidbody rb = furnitureRigidbodies[i];
            if (rb == null)
                continue;

            rb.useGravity = true;

            if (setFurnitureContinuousDynamic)
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            if (enableFurnitureInterpolation)
                rb.interpolation = RigidbodyInterpolation.Interpolate;
        }
    }

    private void StopSimulationInternal(bool resetTransforms, bool raiseCompletedEvent)
    {
        isSimulating = false;
        accumulatedTime = 0f;
        elapsedSimulationTime = 0f;

        ClearDynamicVelocities();

        if (resetTransforms)
            ResetRoomTransforms();

        if (raiseCompletedEvent)
            RaiseCompletedOnce();
    }

    private void ResetRoomTransforms()
    {
        if (roomRigidbody != null)
        {
            roomRigidbody.position = roomInitialPosition;
            roomRigidbody.rotation = roomInitialRotation;
            ClearVelocity(roomRigidbody);
        }

        for (int i = 0; i < roomStructureRigidbodies.Count; i++)
        {
            Rigidbody rb = roomStructureRigidbodies[i];
            if (rb == null)
                continue;

            if (roomInitialPositions.TryGetValue(rb, out Vector3 position))
                rb.position = position;

            if (roomInitialRotations.TryGetValue(rb, out Quaternion rotation))
                rb.rotation = rotation;

            ClearVelocity(rb);
        }
    }

    private void ClearDynamicVelocities()
    {
        if (roomRigidbody != null)
            ClearVelocity(roomRigidbody);

        for (int i = 0; i < roomStructureRigidbodies.Count; i++)
            ClearVelocity(roomStructureRigidbodies[i]);

        for (int i = 0; i < furnitureRigidbodies.Count; i++)
            ClearVelocity(furnitureRigidbodies[i]);
    }

    private void ClearVelocity(Rigidbody rb)
    {
        if (rb == null)
            return;

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
    }

    private void RaiseCompletedOnce()
    {
        if (completedEventRaised)
            return;

        completedEventRaised = true;
        SimulationCompleted?.Invoke();
    }

    // ---------------------------------------------------------------------
    // 유틸
    // ---------------------------------------------------------------------

    private double FindMaxHorizontalAcceleration(double[] xGal, double[] zGal)
    {
        int count = Math.Min(xGal.Length, zGal.Length);
        double maxSquared = 0.0;

        for (int i = 0; i < count; i++)
            maxSquared = Math.Max(maxSquared, xGal[i] * xGal[i] + zGal[i] * zGal[i]);

        return Math.Sqrt(maxSquared);
    }

    private void ScaleInPlace(double[] values, double scale)
    {
        ApplyMultiplierInPlace(values, scale);
    }

    private void ApplyMultiplierInPlace(double[] values, double multiplier)
    {
        if (values == null)
            return;

        for (int i = 0; i < values.Length; i++)
            values[i] *= multiplier;
    }

    private void RemoveInitialOffsetInPlace(double[] values)
    {
        if (values == null || values.Length == 0)
            return;

        double offset = values[0];
        for (int i = 0; i < values.Length; i++)
            values[i] -= offset;
    }

    private double ConvertAccelerationToGal(double value)
    {
        switch (inputAccelerationUnit)
        {
            case AccelerationUnit.MeterPerSecondSquared:
                return value * 100.0;

            case AccelerationUnit.G:
                return value * GravityGal;

            case AccelerationUnit.Gal:
            default:
                return value;
        }
    }

    private void ApplyAxisConvention(ref double ax, ref double ay, ref double az)
    {
        switch (csvAxisConvention)
        {
            case CsvAxisConvention.UnityX_EW_Y_UP_Z_NS:
                double ns = ax;
                double up = ay;
                double ew = az;
                ax = ew;
                ay = up;
                az = ns;
                break;

            case CsvAxisConvention.AsWritten:
            case CsvAxisConvention.UnityX_NS_Y_UP_Z_EW:
            default:
                break;
        }
    }

    private double DetectDt(List<double> timeValues)
    {
        if (timeValues != null && timeValues.Count >= 2)
        {
            for (int i = 1; i < timeValues.Count; i++)
            {
                double dt = timeValues[i] - timeValues[i - 1];
                if (dt > 1e-6)
                    return dt;
            }
        }

        return Math.Max(0.001, fallbackDt);
    }

    private string[] NormalizeHeaders(string[] headers)
    {
        string[] normalized = new string[headers.Length];

        for (int i = 0; i < headers.Length; i++)
            normalized[i] = headers[i].Trim().ToLowerInvariant();

        return normalized;
    }

    private bool ContainsHeader(string[] headers, string key)
    {
        if (headers == null)
            return false;

        for (int i = 0; i < headers.Length; i++)
        {
            if (string.Equals(headers[i].Trim(), key, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private int FindHeaderIndex(string[] headers, params string[] keys)
    {
        if (headers == null)
            return -1;

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

    private string[] SplitLine(string line)
    {
        if (line.Contains(","))
            return line.Split(',');

        return line.Split(new[] { ' ', '\t', ';' }, StringSplitOptions.RemoveEmptyEntries);
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

    private double Clamp(double value, double min, double max)
    {
        if (value < min)
            return min;

        if (value > max)
            return max;

        return value;
    }

    private void OnValidate()
    {
        playbackSpeed = Mathf.Max(0.01f, playbackSpeed);
        fallbackDt = Mathf.Max(0.001f, fallbackDt);

        currentFloor = Mathf.Max(1, currentFloor);
        explicitTotalFloors = Mathf.Max(0, explicitTotalFloors);
        if (explicitTotalFloors > 0)
            explicitTotalFloors = Mathf.Max(explicitTotalFloors, currentFloor);

        minScaleFactor = Mathf.Max(0.01f, minScaleFactor);
        maxScaleFactor = Mathf.Max(minScaleFactor, maxScaleFactor);
        strongScenarioMmi = Mathf.Clamp(strongScenarioMmi, 5, 9);
    }
}
using System;
using System.Collections.Generic;
using MixedReality.Toolkit;
using MixedReality.Toolkit.SpatialManipulation;
using UnityEngine;
using UnityEngine.Serialization;

public enum FurnitureScaleStepResult
{
    Applied,
    BlockedByRoomHeight,
    BlockedByOverlap,
    BlockedByFixed,
    Ignored
}

/// <summary>
/// ?쒕쾭/?몄떇/?붾쾭洹??뚯씠?꾨씪?몄씠 ?앹꽦??媛쒕퀎 媛援??ㅻ툕?앺듃??遺숇뒗 ?고???愿由?而댄룷?뚰듃?낅땲??
/// ?섏젙 ?댁뿭: 踰쎄구??Floor Contactless) 媛援??앸퀎 諛??ㅼ떆媛?踰쎈㈃ ?ㅻ깄 濡쒖쭅 異붽?
/// </summary>
public class PlacedFurniture : MonoBehaviour
{
    [Header("Runtime Metadata")]
    [SerializeField] private FurniturePlacementState state = FurniturePlacementState.None;
    [SerializeField] private string furnitureId;
    [SerializeField] private string label;
    [SerializeField] private string displayName;
    [SerializeField] private string source;
    [SerializeField] private string physicsMode;
    [SerializeField] private bool simulationEnabled = true;
    [SerializeField] private string rigidbodyMode;
    [SerializeField] private bool simulationUseGravity;

    [Header("Floor Contact")]
    [Tooltip("True if this furniture is not supported by the floor. It may touch or attach to a wall, ceiling, or fixed furniture.")]
    [FormerlySerializedAs("isWallMounted")]
    public bool isFloorContactless = false;
    [Tooltip("踰쎈㈃ ?ㅻ깄???쒕룄??理쒕? 嫄곕━(誘명꽣)?낅땲??")]
    public float wallSnapDistance = 0.08f;
    [Min(0.0f)] public float wallSnapOffsetMeters = 0.0f;

    [Header("Fixed Placement")]
    [Tooltip("true?대㈃ ?좏깮? 媛?ν븯吏留??대룞/?뚯쟾/?ш린 議곗젙???좉툒?덈떎.")]
    public bool isFixed = false;
    public bool disableManipulationWhenFixed = true;

    [Header("Move Control")]
    public Behaviour[] moveBehaviours;
    public Collider[] heavyCollidersToDisableWhileMoving;
    public bool ensureKinematicRigidbody = true;

    [Tooltip("?대룞 以?鍮?踰쎄구??媛援ъ쓽 諛붾떏??留??꾨젅??諛?諛붾떏??怨좎젙?? 踰?履쎌쑝濡??????꾨옒濡?媛?쇱븠???꾩긽??諛⑹??⑸땲??")]
    public bool lockToFloorWhileMoving = true;
    [Tooltip("?대룞 以?諛붾떏肉??꾨땲??梨낆긽/?좊컲 媛숈? ?ㅻⅨ 媛援??곷떒??吏吏硫댁쑝濡??ъ슜?⑸땲??")]
    public bool lockToSupportSurfaceWhileMoving = true;
    [Min(0.0f)] public float supportSurfaceSnapDistance = 0.08f;

    [Header("Placement Assist")]
    [Min(0.0f)] public float nearbySurfaceSnapDistance = 0.04f;

    public bool autoCollectPlacementColliders = true;
    public Collider[] placementColliders;

    [Header("Touch Selection")]
    public bool selectWhenTouchedOrMoved = true;

    [Header("Transform Move Monitor")]
    public bool autoDetectTransformMoveEnd = true;
    public float moveEndIdleSeconds = 0.2f;
    public float movePositionEpsilon = 0.0005f;
    public float moveRotationEpsilonDegrees = 0.15f;
    public float moveScaleEpsilon = 0.0005f;

    [Header("Scale Limit")]
    [Tooltip("?????ㅼ???議곗옉 ??媛援?localScale ??異뺤쓽 理쒕?媛믪엯?덈떎. ?쒓퀎 ?곸슜 ?쒖뿉??鍮꾩쑉(GLB ?먮낯)? ?좎??⑸땲??")]
    [Min(1f)] public float maxScalePerAxis = 10f;
    [Tooltip("媛援?localScale ??異뺤쓽 理쒖냼媛믪엯?덈떎. ?덈Т ?묒븘吏??寃껋쓣 諛⑹??⑸땲??")]
    [Min(0.001f)] public float minScalePerAxis = 0.1f;

    private readonly List<Collider> runtimePlacementColliders = new List<Collider>();
    // 諛붾떏 ?뺣젹???쒓컖 ?뚮뜑?щ? 1??罹먯떆?쒕떎. 留??꾨젅??GetComponentsInChildren濡?
    // 怨꾩링 ?쒗쉶/諛곗뿴 ?좊떦???쇱뼱?섏? ?딅룄濡?placement collider? ?숈씪?섍쾶 罹먯떆.
    private Renderer[] cachedVisualRenderers = Array.Empty<Renderer>();
    private FurniturePlacementManager ownerManager;
    private Vector3 lastValidPosition;
    private Quaternion lastValidRotation;
    private Vector3 lastObservedPosition;
    private Quaternion lastObservedRotation;
    private Vector3 lastObservedScale = Vector3.one;
    private float lastTransformChangeTime;
    private bool initialized;
    private bool isMovable;
    private bool isMoving;
    private GameObject selectionBoxObject;
    private FurnitureManipulationMode manipulationMode = FurnitureManipulationMode.None;
    private bool placementValidationFailed;

    public event Action<PlacedFurniture> MoveStarted;
    public event Action<PlacedFurniture> MoveEnded;
    public event Action<PlacedFurniture> Touched;

    public FurniturePlacementState State => state;
    public string FurnitureId => furnitureId;
    public string Label => label;
    public string DisplayName => !string.IsNullOrWhiteSpace(displayName) ? displayName : gameObject.name;
    public string Source => source;
    public bool IsMovable => isMovable;
    public bool IsMoving => isMoving;
    public FurnitureManipulationMode ManipulationMode => manipulationMode;
    public bool IsFixed => isFixed;
    public bool IsFloorContactless => isFloorContactless;

    // 諛곗튂 ?꾨즺 寃利앹뿉??議곌굔 誘몄땐議깆쑝濡??먯젙?섏뼱 boxcollider??誘몄땐議?material??
    // ?쒖떆 以묒씤吏 ?щ?. 媛援щ? ?좏깮/?대룞?섎뒗 ???ㅻⅨ ?묒뾽???섎㈃ ?댁젣?섎ŉ,
    // 諛곗튂 ?꾨즺 踰꾪듉???ㅼ떆 ?뚮윭 ?ш?利앺븷 ?뚮쭔 ?ㅼ떆 ?ㅼ젙?쒕떎.
    public bool PlacementValidationFailed => placementValidationFailed;
    public void SetPlacementValidationFailed(bool failed) => placementValidationFailed = failed;

    public void Initialize(FurniturePlacementManager manager, FurniturePlacementMetadata metadata)
    {
        ownerManager = manager;

        if (metadata == null) metadata = FurniturePlacementMetadata.CreateDefault(gameObject);
        metadata.EnsureDefaults(gameObject);

        furnitureId = metadata.furnitureId;
        label = metadata.label;
        displayName = metadata.displayName;
        source = metadata.source;
        physicsMode = metadata.physicsMode;
        simulationEnabled = metadata.simulationEnabled;
        rigidbodyMode = metadata.rigidbodyMode;
        simulationUseGravity = metadata.useGravity;

        CollectPlacementColliders();
        CollectVisualRenderers();
        ConfigureRigidbody();
        WarnIfMeshCollidersAreNotConvex();

        SaveCurrentPoseAsValid();
        ResetTransformMonitor();
        SetState(FurniturePlacementState.Registered);
        SetMovable(false);
        initialized = true;
    }

    private void Update()
    {
        if (!initialized || !autoDetectTransformMoveEnd || !isMovable) return;


        // ?대룞 以묒씠怨?踰쎄구??媛援щ씪硫??ㅼ떆媛꾩쑝濡?踰쎌뿉 ?ㅻ깄?쒗궢?덈떎.
        // ?????ㅼ???議곗옉 以묒뿉??鍮꾩쑉(GLB ?먮낯)???좎???梨?理쒖냼/理쒕? ?ш린濡??쒗븳?쒕떎.
        if (isMoving && manipulationMode == FurnitureManipulationMode.Scale)
        {
            ClampScaleToLimits();
        }

        // ?대룞 以?鍮?踰쎄구??媛援щ뒗 諛붾떏??怨좎젙???꾨옒濡?媛?쇱븠嫄곕굹 ?좎삤瑜대뒗 寃껋쓣 諛⑹??쒕떎.
        if (isMoving && lockToFloorWhileMoving && !isFloorContactless && ownerManager?.roomGeometryProvider != null)
        {
            ConfirmedRoomGeometryProvider geometry = ownerManager.roomGeometryProvider;
            if (geometry.IsInitialized &&
                (geometry.TryGetFurnitureVisualBounds(this, out Bounds floorBounds) ||
                 geometry.TryGetFurnitureBounds(this, out floorBounds)))
            {
                float supportTopY = geometry.FloorTopY;
                if (lockToSupportSurfaceWhileMoving)
                {
                    geometry.TryGetBestSupportTopY(this, supportSurfaceSnapDistance, out supportTopY);
                }

                float deltaY = supportTopY - floorBounds.min.y;
                if (Mathf.Abs(deltaY) > 0.0005f)
                {
                    transform.position += Vector3.up * deltaY;
                }
            }
        }

        bool changed = HasTransformChangedSinceLastObservation();
        if (changed)
        {
            if (!isMoving)
            {
                lastValidPosition = lastObservedPosition;
                lastValidRotation = lastObservedRotation;
                BeginMove("TransformChanged", saveCurrentAsValid: false);
            }

            lastTransformChangeTime = Time.time;
            lastObservedPosition = transform.position;
            lastObservedRotation = transform.rotation;
            return;
        }

        if (isMoving && Time.time - lastTransformChangeTime >= moveEndIdleSeconds)
        {
            EndMove("TransformIdle");
        }
    }

    public void SetState(FurniturePlacementState nextState) => state = nextState;
    public void SetMovable(bool movable) => SetManipulationMode(movable ? FurnitureManipulationMode.Move : FurnitureManipulationMode.None);

    public void SetManipulationMode(FurnitureManipulationMode mode)
    {
        if (disableManipulationWhenFixed && isFixed)
        {
            mode = FurnitureManipulationMode.None;
        }

        if (manipulationMode != FurnitureManipulationMode.None && mode == FurnitureManipulationMode.None && isMoving) EndMove("MoveDisabled");

        manipulationMode = mode;
        isMovable = mode != FurnitureManipulationMode.None;
        TransformFlags allowedManipulations = ToTransformFlags(mode);

        if (moveBehaviours != null)
        {
            foreach (Behaviour behaviour in moveBehaviours)
            {
                if (behaviour != null)
                {
                    if (behaviour is ObjectManipulator manipulator) manipulator.AllowedManipulations = allowedManipulations;
                    behaviour.enabled = isMovable;
                }
            }
        }

        ResetTransformMonitor();
    }

    private static TransformFlags ToTransformFlags(FurnitureManipulationMode mode)
    {
        return mode switch
        {
            FurnitureManipulationMode.Move => TransformFlags.Move,
            FurnitureManipulationMode.Rotate => TransformFlags.Rotate,
            FurnitureManipulationMode.Scale => TransformFlags.Scale,
            _ => TransformFlags.None,
        };
    }

    public void NotifyTouched() => NotifyTouched("Touch");
    public void NotifyTouched(string inputSource)
    {
        if (!initialized) return;
        Touched?.Invoke(this);
        ownerManager?.HandleFurnitureTouched(this, inputSource);
    }

    public void NotifySelectedByController(string inputSource)
    {
        if (!initialized) return;
        ownerManager?.HandleFurnitureSelected(this, inputSource);
    }

    public void NotifyMoveStarted() { if (initialized) BeginMove("ExternalEvent", saveCurrentAsValid: true); }
    public void NotifyMoveEnded() { if (initialized) EndMove("ExternalEvent"); }

    public void SetFixed(bool fixedState)
    {
        if (isFixed == fixedState) return;

        isFixed = fixedState;
        if (isFixed)
        {
            SetManipulationMode(FurnitureManipulationMode.None);
            Debug.Log($"[PlacedFurniture] Furniture fixed. Manipulation locked: {DisplayName}", this);
        }
        else
        {
            Debug.Log($"[PlacedFurniture] Furniture unfixed. Manipulation available: {DisplayName}", this);
        }
    }

    public void ToggleFixed()
    {
        SetFixed(!isFixed);
    }

    public void SetFloorContactless(bool floorContactless)
    {
        if (isFloorContactless == floorContactless) return;
        isFloorContactless = floorContactless;
        Debug.Log($"[PlacedFurniture] Floor contactless {(isFloorContactless ? "enabled" : "disabled")}: {DisplayName}", this);
    }

    public void ToggleFloorContactless()
    {
        SetFloorContactless(!isFloorContactless);
    }

    public void SaveCurrentPoseAsValid()
    {
        lastValidPosition = transform.position;
        lastValidRotation = transform.rotation;
        ResetTransformMonitor();
    }

    // ?ㅼ??쇱? ?섎룄?곸쑝濡?蹂듭썝?섏? ?딅뒗?? 諛⑸낫????媛援щ? 以꾩뿬 留욎텛???꾩쨷 寃利??ㅽ뙣濡?
    // ?ㅼ??쇱씠 ?먮옒(?? 媛믪쑝濡??섎룎?꾧?硫??곸쁺 以꾩씪 ???녿뒗 援먯갑??諛쒖깮?섍린 ?뚮Ц?대떎.
    public void RestoreLastValidPose()
    {
        transform.position = lastValidPosition;
        transform.rotation = lastValidRotation;
        ResetTransformMonitor();
    }

    // 踰꾪듉??洹좎씪 ?ㅼ??? 鍮꾩쑉???좎???梨?factor瑜?怨깊븯怨??쒓퀎濡??대옩????利됱떆 ?ш?利?蹂댁젙?쒕떎.
    public FurnitureScaleStepResult ApplyUniformScaleStep(float factor)
    {
        if (isFixed)
        {
            return FurnitureScaleStepResult.BlockedByFixed;
        }

        if (!initialized || factor <= 0f)
        {
            return FurnitureScaleStepResult.Ignored;
        }

        Vector3 previousScale = transform.localScale;
        Vector3 previousPosition = transform.position;
        Quaternion previousRotation = transform.rotation;

        transform.localScale *= factor;
        ClampScaleToLimits();
        Physics.SyncTransforms();

        bool growing = factor > 1f;

        // ?ㅼ슦???ㅽ뀦?먯꽌 媛援ш? 泥쒖옣~諛붾떏 ?믪씠瑜??섏쑝硫??섎룎由ш퀬 ?쒓퀎?꾩쓣 ?뚮┛??
        if (growing && ExceedsRoomHeight())
        {
            transform.localScale = previousScale;
            Physics.SyncTransforms();
            return FurnitureScaleStepResult.BlockedByRoomHeight;
        }

        // 踰?紐⑥꽌由??ㅻⅨ 媛援ъ? 寃뱀튂硫?HandleFurnitureScaleChanged ?대???
        // ValidateAndCorrectFurniturePose媛 ?꾩튂瑜??대룞?쒖폒 ?섏슜???쒕룄?쒕떎.
        bool valid = ownerManager == null || ownerManager.HandleFurnitureScaleChanged(this);

        // ?ㅼ슦?붾뜲 ?대룞?대룄 怨듦컙?????섏삤硫??대쾲 ?ㅽ뀦留?痍⑥냼?쒕떎.
        // (異뺤냼????긽 ?덉슜????媛援ш? 以꾩? 紐삵븯??援먯갑??留됰뒗??)
        if (growing && !valid)
        {
            transform.localScale = previousScale;
            transform.SetPositionAndRotation(previousPosition, previousRotation);
            Physics.SyncTransforms();
            return FurnitureScaleStepResult.BlockedByOverlap;
        }

        return FurnitureScaleStepResult.Applied;
    }

    // ??異뺣쭔 誘몄꽭 議곗젙?쒕떎. 踰꾪듉??異뺣퀎 議곗젙?⑹씠誘濡??섎룄?곸쑝濡?鍮꾩쑉??源④퀬 ?대떦 異뺣쭔 [min,max]濡??대옩?꾪븳??
    public FurnitureScaleStepResult ApplyAxisScaleStep(int axis, float factor)
    {
        if (isFixed)
        {
            return FurnitureScaleStepResult.BlockedByFixed;
        }

        if (!initialized || factor <= 0f || axis < 0 || axis > 2)
        {
            return FurnitureScaleStepResult.Ignored;
        }

        Vector3 previousScale = transform.localScale;
        Vector3 previousPosition = transform.position;
        Quaternion previousRotation = transform.rotation;

        Vector3 scale = previousScale;
        scale[axis] = Mathf.Clamp(scale[axis] * factor, minScalePerAxis, maxScalePerAxis);
        transform.localScale = scale;
        Physics.SyncTransforms();

        bool growing = factor > 1f;

        if (growing && ExceedsRoomHeight())
        {
            transform.localScale = previousScale;
            Physics.SyncTransforms();
            return FurnitureScaleStepResult.BlockedByRoomHeight;
        }

        bool valid = ownerManager == null || ownerManager.HandleFurnitureScaleChanged(this);

        if (growing && !valid)
        {
            transform.localScale = previousScale;
            transform.SetPositionAndRotation(previousPosition, previousRotation);
            Physics.SyncTransforms();
            return FurnitureScaleStepResult.BlockedByOverlap;
        }

        return FurnitureScaleStepResult.Applied;
    }

    private bool ExceedsRoomHeight()
    {
        ConfirmedRoomGeometryProvider geometry = ownerManager != null ? ownerManager.roomGeometryProvider : null;
        return geometry != null && geometry.IsInitialized && geometry.ExceedsRoomHeight(this);
    }

    public void PrepareForSimulation()
    {
        SetMovable(false);
        SetHeavyCollidersEnabled(true);
        // HoloLens2?먯꽌???쒕??덉씠?섏쓣 吏꾪뻾?섏? ?딅뒗?? 臾쇰━ ?곗궛? ?붾컮?댁뒪 遺?섎?
        // ?좊컻?섍퀬, ?꾨즺 ??Rigidbody媛 dynamic?쇰줈 諛붾뚮㈃ 媛援ш? ?⑥뼱吏꾨떎.
        // ?곕씪??Rigidbody瑜?kinematic?쇰줈 ?먯? ?딄퀬 ?꾩삁 ?쒓굅?쒕떎.
        // (?쒕쾭 ?쒕??덉씠?섏? ?꾩넚??JSON?쇰줈 ?먯껜 Rigidbody瑜?援ъ꽦?섎?濡??곹뼢 ?놁쓬)
        RemoveRigidbodyForClient();
    }

    public Collider[] GetActivePlacementColliders()
    {
        if (runtimePlacementColliders.Count == 0) CollectPlacementColliders();
        return runtimePlacementColliders.ToArray();
    }

    // ?좏깮??媛援ъ쓽 BoxCollider ?곸뿭??諛섑닾紐?諛뺤뒪濡??쒖떆???ш린/?꾩튂 議곗젅 UX瑜??뺣뒗??
    public void SetSelectionBoxVisible(bool show, Material boxMaterial)
    {
        BoxCollider box = GetComponent<BoxCollider>();
        if (!show || box == null || boxMaterial == null)
        {
            if (selectionBoxObject != null) selectionBoxObject.SetActive(false);
            return;
        }

        if (selectionBoxObject == null)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "SelectionBoxVisual";

            Collider cubeCollider = cube.GetComponent<Collider>();
            if (cubeCollider != null) Destroy(cubeCollider);

            cube.transform.SetParent(transform, false);
            selectionBoxObject = cube;
        }

        // BoxCollider center/size??root local 媛믪씠怨?湲곕낯 Cube 硫붿돩??1?좊떅?대?濡?洹몃?濡?留ㅽ븨?섎㈃ ?쇱튂?쒕떎.
        selectionBoxObject.transform.localPosition = box.center;
        selectionBoxObject.transform.localRotation = Quaternion.identity;
        selectionBoxObject.transform.localScale = box.size;

        Renderer boxRenderer = selectionBoxObject.GetComponent<Renderer>();
        if (boxRenderer != null) boxRenderer.sharedMaterial = boxMaterial;

        selectionBoxObject.SetActive(true);
    }

    private void BeginMove(string reason, bool saveCurrentAsValid)
    {
        if (isMoving) return;
        if (disableManipulationWhenFixed && isFixed) return;

        // ?대룞? "?ㅻⅨ ?묒뾽"?대?濡?誘몄땐議??쒖떆瑜??댁젣?쒕떎(諛곗튂 ?꾨즺 ?ш?利??꾧퉴吏 蹂듦??섏? ?딆쓬).
        placementValidationFailed = false;

        if (selectWhenTouchedOrMoved) ownerManager?.HandleFurnitureTouched(this, reason);

        isMoving = true;
        lastTransformChangeTime = Time.time;
        if (saveCurrentAsValid) SaveCurrentPoseAsValid();

        SetHeavyCollidersEnabled(false);
        MoveStarted?.Invoke(this);
    }

    private void EndMove(string reason)
    {
        if (!isMoving) return;

        isMoving = false;
        SetHeavyCollidersEnabled(true);
        MoveEnded?.Invoke(this);
        ownerManager?.HandleFurnitureMoveEnded(this);
        ResetTransformMonitor();
    }

    private bool HasTransformChangedSinceLastObservation()
    {
        return Vector3.Distance(transform.position, lastObservedPosition) > movePositionEpsilon ||
               Quaternion.Angle(transform.rotation, lastObservedRotation) > moveRotationEpsilonDegrees ||
               Vector3.Distance(transform.localScale, lastObservedScale) > moveScaleEpsilon;
    }

    private void ResetTransformMonitor()
    {
        lastObservedPosition = transform.position;
        lastObservedRotation = transform.rotation;
        lastObservedScale = transform.localScale;
        lastTransformChangeTime = Time.time;
    }

    // 鍮꾩쑉??源⑥? ?딅룄濡???異?湲곗??쇰줈 ?ㅻⅨ 異뺤쓣 ?뺥븯吏 ?딄퀬, ?꾩껜 踰≫꽣???숈씪 factor瑜?怨깊빐 ?쒗븳?쒕떎.
    private void ClampScaleToLimits()
    {
        Vector3 scale = transform.localScale;
        float maxComponent = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        float minComponent = Mathf.Min(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));

        float factor = 1f;
        if (maxComponent > maxScalePerAxis && maxComponent > 0.0001f)
        {
            factor = maxScalePerAxis / maxComponent;
        }
        else if (minComponent < minScalePerAxis && minComponent > 0.0001f)
        {
            factor = minScalePerAxis / minComponent;
        }

        if (!Mathf.Approximately(factor, 1f))
        {
            transform.localScale = scale * factor;
        }
    }

    // ?쒓컖 ?뚮뜑?щ? 1???섏쭛?쒕떎. SelectionBoxVisual? Initialize ?댄썑 ?앹꽦?섎?濡?
    // ??罹먯떆???ы븿?섏? ?딅뒗???먯뿰 ?쒖쇅). collider GLB ?뚮뜑?щ뒗 鍮꾪솢???곹깭??
    // ?ъ슜 ??enabled ?꾪꽣濡??쒖쇅?쒕떎.
    private void CollectVisualRenderers()
    {
        cachedVisualRenderers = GetComponentsInChildren<Renderer>(true);
    }

    public Renderer[] GetCachedVisualRenderers() => cachedVisualRenderers;

    private void CollectPlacementColliders()
    {
        runtimePlacementColliders.Clear();
        if (placementColliders != null && placementColliders.Length > 0)
        {
            foreach (Collider c in placementColliders) if (c != null && !runtimePlacementColliders.Contains(c)) runtimePlacementColliders.Add(c);
            return;
        }

        if (!autoCollectPlacementColliders) return;

        Collider[] childColliders = GetComponentsInChildren<Collider>(true);

        // ?쒕쾭 convex hull MeshCollider媛 ?덉쑝硫?寃利?異⑸룎? 洹??뺣? collider留??ъ슜?쒕떎.
        // ?볦? interaction??BoxCollider(AddBroadInteractionBoxCollider)瑜?寃利앹뿉???쒖쇅??
        // 諛????몃?쨌移⑦닾 ?먯젙??怨쇰룄?섍쾶 蹂댁닔?곸쑝濡??섏뼱 ?뺤긽 諛곗튂媛 嫄곕??섎뒗 寃껋쓣 留됰뒗??
        foreach (Collider c in childColliders)
        {
            if (c is MeshCollider meshCollider && meshCollider.convex && !runtimePlacementColliders.Contains(c))
            {
                runtimePlacementColliders.Add(c);
            }
        }

        if (runtimePlacementColliders.Count > 0) return;

        // ?쒕쾭 MeshCollider瑜?諛쏆? 紐삵븳 寃쎌슦?먮쭔 BoxCollider ?깆쑝濡?fallback?쒕떎.
        foreach (Collider c in childColliders)
        {
            if (c != null && !runtimePlacementColliders.Contains(c)) runtimePlacementColliders.Add(c);
        }
    }

    private void ConfigureRigidbody()
    {
        if (!ensureKinematicRigidbody) return;
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();

        rb.isKinematic = true;
        rb.useGravity = false;
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
    }

    // HoloLens2???쒕??덉씠?섏쓣 ?섏? ?딆쑝誘濡??꾨즺 ??Rigidbody瑜??꾩쟾???쒓굅??
    // 臾쇰━ ?곗궛 遺?섏? 媛援??숉븯 ?꾩긽??紐⑤몢 ?놁븻?? ?쒕쾭 ?꾩넚 JSON?먮뒗 Rigidbody
    // ?뺣낫媛 ?ы븿?섏? ?딆쑝誘濡?transform/is_fixed/is_floor_contactless留?吏곷젹?? ?쒕쾭
    // ?쒕??덉씠?섏뿉???곹뼢???녿떎.
    private void RemoveRigidbodyForClient()
    {
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null) Destroy(rb);
    }

    private void WarnIfMeshCollidersAreNotConvex()
    {
        foreach (Collider collider in GetActivePlacementColliders())
        {
            if (collider is MeshCollider meshCollider && !meshCollider.convex)
                Debug.LogWarning($"[PlacedFurniture] MeshCollider is not convex: {meshCollider.name}. HoloLens2 ?대룞/寃利앹뿉?쒕뒗 Convex MeshCollider ?먮뒗 ?⑥닚 Proxy Collider瑜?沅뚯옣?⑸땲??", meshCollider);
        }
    }

    private void SetHeavyCollidersEnabled(bool enabled)
    {
        if (heavyCollidersToDisableWhileMoving == null) return;
        foreach (Collider collider in heavyCollidersToDisableWhileMoving) if (collider != null) collider.enabled = enabled;
    }
}

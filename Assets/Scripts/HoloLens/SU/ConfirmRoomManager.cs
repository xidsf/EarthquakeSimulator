using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Confirm Room ?④퀎?먯꽌 諛??ロ옒 寃利? ?대┛ gap marker ?쒖떆, 理쒖쥌 ConfirmedRoomRoot ?쒓컖?붾? ?대떦?⑸땲??
/// WorkflowManager???곹깭 ?꾪솚留?愿由ы븯怨? ConfirmRoomManager媛 ?먮룞 踰?+ ?섎룞 踰?segment瑜?湲곕컲?쇰줈 寃利??앹꽦???섑뻾?⑸땲??
/// </summary>
public class ConfirmRoomManager : MonoBehaviour
{
    private struct Segment2D
    {
        public Vector2 start;
        public Vector2 end;
    }

    private struct WallFootprint2D
    {
        public Vector2 center;
        public Vector2 direction;
        public Vector2 normal;
        public float halfLength;
        public float halfThickness;
    }

    private struct EndpointRef
    {
        public int segmentIndex;
        public bool isStart;
        public Vector2 point;
    }

    [Header("References")]
    public SceneUnderstandingRoomScanner scanner;
    public ManualWallBuilder manualWallBuilder;

    [Tooltip("理쒖쥌 ?뺤젙 諛??ㅻ툕?앺듃?ㅼ씠 ?앹꽦??Root?낅땲?? 蹂댄넻 RoomBuildWorkflowManager??ConfirmedRoomRoot瑜??곌껐?⑸땲??")]
    public Transform confirmedRoomRoot;

    [Header("Furniture Placement")]
    [Tooltip("?뺤젙??踰?諛붾떏/泥쒖옣 ?뺣낫瑜?媛援?諛곗튂/?대룞 寃利??쒖뒪?쒖뿉 ?꾨떖?⑸땲??")]
    public ConfirmedRoomGeometryProvider confirmedRoomGeometryProvider;

    [Tooltip("諛??뺤젙 ?깃났 ??ConfirmedRoomGeometryProvider瑜??먮룞 珥덇린?뷀빀?덈떎.")]
    public bool bindFurniturePlacementGeometryOnConfirm = true;

    [Header("Boundary Sources")]
    public bool includeDetectedWallsInClosedCheck = true;
    public bool includeClosureEdgesInClosedCheck = true;
    public bool includeManualWallsInClosedCheck = true;

    [Header("Boundary Segment Cleanup")]
    public bool normalizeBoundarySegmentsBeforeConfirm = true;
    public float endpointSnapDistance = 0.12f;
    public float collinearMergeAngle = 5.0f;
    public float collinearMergeDistance = 0.10f;
    public float boundaryMinSegmentLength = 0.05f;

    [Header("Corner Gap Tolerance")]
    [Tooltip("踰?endpoint媛 ?ㅻⅨ 踰?segment??以묎컙 洹쇱쿂???우븘 ?덈뒗??endpoint?쇰━??留욎? ?딅뒗 T-junction/肄붾꼫 ?ㅼ감瑜?蹂댁젙?⑸땲??")]
    public bool snapEndpointsToNearbySegments = true;

    [Tooltip("endpoint瑜?二쇰? segment ?꾨줈 遺숈씪 理쒕? 嫄곕━?낅땲?? ?먮룞 ?몄떇 踰?紐⑥꽌由ш? ?댁쭩 ?닿툔?섎뒗 寃쎌슦 ??媛??덉뿉??媛숈? 肄붾꼫濡?遊낅땲??")]
    public float endpointToSegmentSnapDistance = 0.18f;

    [Tooltip("?뺢퇋???꾩뿉???⑥? ?대┛ endpoint?쇰━ 嫄곕━媛 媛源뚯슦硫?寃利앹슜 吏㏃? bridge segment瑜?異붽???肄붾꼫 gap???レ뒿?덈떎.")]
    public bool bridgeSmallOpenEndpointGaps = true;

    [Tooltip("?대┛ endpoint ?ъ씠瑜??먮룞 bridge濡??レ쓣 理쒕? 嫄곕━?낅땲?? 臾??듬줈源뚯? ?レ? ?딅룄濡??덈Т ?ш쾶 ?먯? ?딅뒗 寃껋쓣 沅뚯옣?⑸땲??")]
    public float openEndpointBridgeDistance = 0.24f;

    [Tooltip("endpoint-to-segment 蹂댁젙 ??segment瑜??섎늻吏 ?딆쓣 endpoint 洹쇱쿂 t ?ъ쑀媛믪엯?덈떎.")]
    public float endpointProjectionSplitEpsilon = 0.03f;

    [Header("User Room Flood Fill Validation")]
    public float gridCellSize = 0.12f;
    public float gridBoundsMargin = 0.75f;
    public float wallBlockDistance = 0.08f;
    public int maxGridCellsPerAxis = 220;
    public int nearestFreeCellSearchRadius = 8;
    public bool trimConfirmedBoundarySegmentsToSelectedRoom = true;
    public float confirmedBoundaryTrimSampleStepMultiplier = 0.5f;
    public float confirmedBoundaryTrimSelectedCellRadiusMultiplier = 2.75f;
    public float confirmedBoundaryTrimEndPaddingMeters = 0.24f;

    [Header("Cube Footprint Flood Fill Validation")]
    [Tooltip("踰쎌쓣 ?뉗? ?좊텇???꾨땲??Cube??XZ footprint ?ш컖???μ븷臾쇰줈 蹂닿퀬 flood fill???섑뻾?⑸땲?? 紐⑥꽌由?endpoint媛 ?꾩쟾??遺숈? ?딆븘???ㅼ젣 踰??먭퍡/?앸떒 ?⑤뵫?쇰줈 留됲엳硫??ロ엺 怨듦컙?쇰줈 ?먮떒?⑸땲??")]
    public bool useCubeFootprintFloodFill = true;

    [Tooltip("?ロ옒 寃利앹뿉 ?ъ슜??理쒖냼 踰?footprint ?먭퍡?낅땲?? ?ㅼ젣 ConfirmedRoom 踰??먭퍡, ManualWallBuilder.wallThickness, wallBlockDistance*2蹂대떎 ?묒? 寃쎌슦 ?먮룞?쇰줈 ????媛믪쓣 ?ъ슜?⑸땲??")]
    public float cubeFootprintWallThickness = 0.16f;

    [Tooltip("踰?segment ???앹쓣 ??嫄곕━留뚰겮 湲몄씠 諛⑺뼢?쇰줈 ?뺤옣?댁꽌 Cube ?앸떒??肄붾꼫?먯꽌 ?댁쭩 留욌Ъ由щ뒗 寃껋쑝濡??먮떒?⑸땲?? ?먮룞 ?몄떇 踰쎈겮由?肄붾꼫媛 議곌툑 踰뚯뼱吏??臾몄젣瑜??꾪솕?⑸땲??")]
    public float cubeFootprintEndPadding = 0.10f;

    [Tooltip("grid cell center? ?대룞 ?좊텇??footprint? 鍮꾧탳?????ш컖?뺤쓣 異붽?濡??뺤옣?섎뒗 ?ъ쑀媛믪엯?덈떎.")]
    public float cubeFootprintCollisionPadding = 0.01f;

    [Tooltip("Cube footprint 寃利앹쓣 ?ъ슜???뚮룄 open endpoint媛 ?섎굹?쇰룄 ?덉쑝硫??ㅽ뙣 泥섎━?⑸땲?? 湲곕낯媛?false?대㈃ open endpoint??gap marker/debug ?⑸룄濡쒕쭔 ?ъ슜?⑸땲??")]
    public bool openEndpointsBlockCubeFootprintValidation = false;

    [Tooltip("Cube footprint 寃利?以묒뿉???묒? open endpoint gap??bridge segment濡??먮룞 異붽??⑸땲?? 湲곕낯媛?false?대㈃ ?ㅼ젣 segment?먮뒗 ?멸났 bridge瑜?異붽??섏? ?딄퀬, footprint ?먭퍡/?앸떒 ?⑤뵫?쇰줈留??ロ옒???먮떒?⑸땲??")]
    public bool useAutoBridgeSegmentsForCubeFootprintValidation = false;


    [Header("Confirmed Room Visualization")]
    public Material confirmedRoomWallMaterial;
    public Material confirmedRoomHorizontalMaterial;
    public float confirmedWallThickness = 0.045f;
    public float confirmedFloorYOffset = 0.025f;
    public float confirmedCeilingYOffset = -0.025f;
    public bool useConfirmedHorizontalBoxSlabs = true;
    public float confirmedHorizontalSlabThickness = 0.06f;
    public float confirmedHorizontalSlabPadding = 0.25f;

    [Header("Confirm Room Review")]
    public bool roomReady;
    public string validationStatus;
    public bool finalRoomClosed;
    public bool floodReachedOutside;
    public int selectedCellCount;
    public int confirmedBoundarySegmentCount;
    public int manualWallCount;
    public int openEndpointCount = -1;

    [Header("Debug - Segment Source Counts")]
    [Tooltip("留덉?留?Confirm Room 寃利?誘몃━蹂닿린?먯꽌 ?ъ슜??detected wall segment 媛쒖닔?낅땲??")]
    public int lastDetectedWallSourceCount;

    [Tooltip("留덉?留?Confirm Room 寃利?誘몃━蹂닿린?먯꽌 ?ъ슜??closure edge segment 媛쒖닔?낅땲??")]
    public int lastClosureEdgeSourceCount;

    [Tooltip("留덉?留?Confirm Room 寃利?誘몃━蹂닿린?먯꽌 ?ъ슜??吏곸젒 ?앹꽦 Manual Wall segment 媛쒖닔?낅땲?? manualWallRoot媛 ?④꺼???덉뼱???ы븿?⑸땲??")]
    public int lastManualWallSourceCount;

    [Tooltip("留덉?留?Confirm Room 誘몃━蹂닿린濡??앹꽦??wall cube 媛쒖닔?낅땲??")]
    public int lastReviewWallSegmentCount;

    [Tooltip("留덉?留??뺢퇋?붿뿉??endpoint瑜?二쇰? segment ?꾨줈 遺숈씤 ?잛닔?낅땲??")]
    public int lastEndpointToSegmentSnapCount;

    [Tooltip("留덉?留??뺢퇋?붿뿉???대┛ 肄붾꼫 gap???リ린 ?꾪빐 ?먮룞 異붽???bridge segment 媛쒖닔?낅땲??")]
    public int lastAutoBridgeSegmentCount;

    [Tooltip("留덉?留?Cube footprint flood fill???ъ슜??踰?footprint 媛쒖닔?낅땲??")]
    public int lastCubeFootprintCount;

    [Tooltip("留덉?留?Cube footprint flood fill?먯꽌 踰?footprint??留됲엺 cell ?먯젙 ?잛닔?낅땲?? Debug ?뺤씤?⑹엯?덈떎.")]
    public int lastCubeBlockedCellCheckCount;

    [Tooltip("留덉?留?Cube footprint flood fill?먯꽌 ?대룞 寃쎈줈媛 踰?footprint瑜?媛濡쒖쭏??李⑤떒???잛닔?낅땲?? Debug ?뺤씤?⑹엯?덈떎.")]
    public int lastCubeBlockedMovementCheckCount;

    [Header("Gap Markers")]
    public bool showGapMarkers = true;
    public Transform gapMarkerRoot;
    public Material gapMarkerMaterial;
    public float gapMarkerSize = 0.12f;
    public float gapMarkerYOffset = 0.12f;

    [Header("Entrance")]
    public bool requireEntranceBeforeProceed = true;
    public bool entrancePlacementMode;
    public bool hasEntrance;
    public Vector3 entrancePointWorld;
    public float entranceRadiusMeters = 1.5f;
    public Transform entranceMarkerRoot;
    public Material entrancePointMarkerMaterial;
    public float entrancePointMarkerSize = 0.12f;

    private readonly HashSet<Vector2Int> selectedCells = new HashSet<Vector2Int>();
    private readonly List<Segment2D> confirmedBoundarySegments = new List<Segment2D>();
    private readonly List<Segment2D> normalizedBoundarySegmentsForConfirm = new List<Segment2D>();
    private readonly List<GameObject> confirmedRoomWallObjects = new List<GameObject>();

    private Vector2 gridMin;
    private int gridWidth;
    private int gridHeight;

    private Material runtimeWallMaterial;
    private Material runtimeHorizontalMaterial;
    private Material runtimeGapMarkerMaterial;
    private bool warnedRuntimeMaterialShaderMissing;

    private GameObject confirmedRoomFloorObject;
    private GameObject confirmedRoomCeilingObject;
    private GameObject entrancePointMarkerObject;

    public GameObject ConfirmedRoomFloorObject => confirmedRoomFloorObject;
    public GameObject ConfirmedRoomCeilingObject => confirmedRoomCeilingObject;
    public IReadOnlyList<GameObject> ConfirmedRoomWallObjects => confirmedRoomWallObjects;

    public void BindFromWorkflow(RoomBuildWorkflowManager workflow)
    {
        if (workflow == null)
        {
            return;
        }

        // WorkflowManager???곹깭 ?꾪솚留??대떦?⑸땲??
        // Confirm Room 寃利??쒓컖???듭뀡? ConfirmRoomManager Inspector?먯꽌 吏곸젒 愿由ы빀?덈떎.
        scanner = workflow.scanner;
        manualWallBuilder = workflow.manualWallBuilder;

        EnsureRoots(workflow.transform);
    }

    public void EnsureRoots(Transform fallbackParent = null)
    {
        if (confirmedRoomRoot == null)
        {
            GameObject rootObject = new GameObject("ConfirmedRoomRoot");
            if (fallbackParent != null)
            {
                rootObject.transform.SetParent(fallbackParent, false);
            }
            confirmedRoomRoot = rootObject.transform;
        }

        if (gapMarkerRoot == null)
        {
            GameObject markerRootObject = new GameObject("ConfirmRoomGapMarkers");
            if (fallbackParent != null)
            {
                markerRootObject.transform.SetParent(fallbackParent, false);
            }
            gapMarkerRoot = markerRootObject.transform;
        }

        if (entranceMarkerRoot == null)
        {
            GameObject entranceRootObject = new GameObject("ConfirmRoomEntranceMarkers");
            if (fallbackParent != null)
            {
                entranceRootObject.transform.SetParent(fallbackParent, false);
            }
            entranceMarkerRoot = entranceRootObject.transform;
        }
    }

    public bool ValidateRoom()
    {
        ResetValidationData(clearMarkers: true);

        if (scanner == null)
        {
            validationStatus = "Confirm Room blocked. Scanner is null.";
            return false;
        }

        if (scanner.currentRoom == null)
        {
            validationStatus = "Confirm Room blocked. Current room is null.";
            return false;
        }

        if (manualWallBuilder != null && !manualWallBuilder.CanConfirmManualWallWorkflow(out string builderReason))
        {
            validationStatus = builderReason;
            return false;
        }

        List<Segment2D> allSegments = BuildFinalBoundarySegments();

        if (normalizeBoundarySegmentsBeforeConfirm)
        {
            allSegments = NormalizeBoundarySegments(allSegments);
        }

        normalizedBoundarySegmentsForConfirm.AddRange(allSegments);
        manualWallCount = CountManualWalls();

        List<Vector2> openEndpoints = FindOpenEndpointPoints(allSegments);
        openEndpointCount = openEndpoints.Count;
        CreateGapMarkers(openEndpoints);

        if (allSegments.Count < 3)
        {
            floodReachedOutside = true;
            validationStatus = "Confirm Room blocked. Boundary segment count is less than 3.";
            return false;
        }

        // ?좊텇 湲곕컲 寃利앹뿉?쒕뒗 open endpoint媛 怨??ㅽ뙣 議곌굔?낅땲??
        // Cube footprint 湲곕컲 寃利앹뿉?쒕뒗 ?ㅼ젣 踰?Cube???먭퍡/?앸떒 ?⑤뵫?쇰줈 留됲? ?덉쑝硫??ロ엺 諛⑹쑝濡??몄젙?섎?濡?
        // open endpoint??湲곕낯?곸쑝濡?gap marker/debug ?⑸룄濡쒕쭔 ?ъ슜?⑸땲??
        if (openEndpointCount > 0 && (!useCubeFootprintFloodFill || openEndpointsBlockCubeFootprintValidation))
        {
            floodReachedOutside = true;
            validationStatus = $"Confirm Room blocked. Open endpoints:{openEndpointCount}. Close the highlighted gaps first.";
            return false;
        }

        bool floodFillSucceeded = useCubeFootprintFloodFill
            ? TryRunCubeFootprintFloodFill(allSegments)
            : TryRunUserRoomFloodFill(allSegments);

        if (!floodFillSucceeded)
        {
            finalRoomClosed = false;
            validationStatus = "Confirm Room blocked. User position is not inside a valid room area.";
            return false;
        }

        selectedCellCount = selectedCells.Count;
        finalRoomClosed = !floodReachedOutside;

        if (!finalRoomClosed)
        {
            validationStatus = useCubeFootprintFloodFill
                ? "Confirm Room blocked. Cube footprint flood fill escaped outside boundary. Room is not closed."
                : "Confirm Room blocked. Flood fill escaped outside boundary. Room is not closed.";
            return false;
        }

        BuildConfirmedBoundarySegments(allSegments);

        confirmedBoundarySegmentCount = confirmedBoundarySegments.Count;

        if (confirmedBoundarySegmentCount < 3)
        {
            finalRoomClosed = false;
            validationStatus = "Confirm Room blocked. Confirmed boundary segment count is less than 3.";
            return false;
        }

        roomReady = true;
        string algorithm = useCubeFootprintFloodFill ? "cube footprint" : "line segment";
        validationStatus =
            $"Confirm Room ready ({algorithm}). cells:{selectedCellCount}, boundary:{confirmedBoundarySegmentCount}, " +
            $"manual walls:{manualWallCount}, open endpoints:{openEndpointCount}, " +
            $"footprints:{lastCubeFootprintCount}, corner snaps:{lastEndpointToSegmentSnapCount}, bridges:{lastAutoBridgeSegmentCount}";
        return true;
    }


    /// <summary>
    /// Confirm Room 寃???④퀎?먯꽌 ?ъ슜?먭? 蹂닿쾶 ??諛?誘몃━蹂닿린?낅땲??
    /// ?먮룞 ?앹꽦 ?쒓컖???ㅻ툕?앺듃媛 ?꾨땲?? ?꾩옱 scanner.currentRoom??諛섏쁺??detected/closure segment?
    /// ManualWallBuilder??manual wall segment snapshot???⑹튇 寃곌낵瑜?ConfirmedRoomRoot ?꾨옒???ㅼ떆 ?앹꽦?⑸땲??
    /// ?곕씪??ManualWall ?④퀎?먯꽌 ?섎씪???먮룞 踰??섏젙怨??덈줈 留뚮뱺 ?섎룞 踰쎌씠 紐⑤몢 諛섏쁺?⑸땲??
    /// </summary>
    public bool BuildReviewRoomVisualization()
    {
        ClearConfirmedRoomObjectsOnly();

        if (scanner == null || scanner.currentRoom == null)
        {
            return false;
        }

        EnsureRoots(transform);

        float floorY = scanner.currentRoom.floorY + confirmedFloorYOffset;
        float ceilingY = scanner.currentRoom.ceilingY + confirmedCeilingYOffset;

        if (float.IsNaN(floorY) || float.IsNaN(ceilingY) || ceilingY <= floorY)
        {
            return false;
        }

        List<Segment2D> wallSegments = BuildReviewWallSegmentsForVisualization();
        lastReviewWallSegmentCount = wallSegments.Count;

        if (wallSegments.Count == 0)
        {
            return false;
        }

        BuildConfirmedHorizontalBoxSlabsFromSegments(wallSegments, floorY, ceilingY);
        BuildConfirmedWallCubesFromSegments(wallSegments, floorY, ceilingY);

        Debug.Log(
            "[ConfirmRoom] Review visualization built. " +
            $"detected:{lastDetectedWallSourceCount}, closure:{lastClosureEdgeSourceCount}, " +
            $"manual:{lastManualWallSourceCount}, snap:{lastEndpointToSegmentSnapCount}, " +
            $"bridge:{lastAutoBridgeSegmentCount}, preview walls:{confirmedRoomWallObjects.Count}"
        );

        return confirmedRoomWallObjects.Count > 0;
    }

    public bool BuildConfirmedRoomVisualization()
    {
        ClearConfirmedRoomObjectsOnly();

        if (!roomReady)
        {
            validationStatus = string.IsNullOrEmpty(validationStatus)
                ? "Confirm Room blocked. Room validation is not ready."
                : validationStatus;
            return false;
        }

        if (scanner == null || scanner.currentRoom == null)
        {
            validationStatus = "Confirm Room blocked. Current room is null.";
            return false;
        }

        EnsureRoots(transform);

        float floorY = scanner.currentRoom.floorY + confirmedFloorYOffset;
        float ceilingY = scanner.currentRoom.ceilingY + confirmedCeilingYOffset;

        if (float.IsNaN(floorY) || float.IsNaN(ceilingY))
        {
            validationStatus = "Confirm Room blocked. Floor or ceiling height is invalid.";
            return false;
        }

        if (ceilingY <= floorY)
        {
            validationStatus = "Confirm Room blocked. Ceiling height is lower than floor height.";
            return false;
        }

        List<Segment2D> wallSegments = BuildConfirmedWallSegmentsForVisualization();

        if (wallSegments.Count < 3)
        {
            validationStatus = "Confirm Room blocked. Not enough wall segments to visualize.";
            return false;
        }

        // ?꾩옱 ?뚮줈?곗뿉?쒕뒗 諛붾떏/泥쒖옣??polygon???꾨땲??box slab?쇰줈 ?앹꽦?섎뒗 寃껋씠 ???덉젙?곸엯?덈떎.
        BuildConfirmedHorizontalBoxSlabsFromSegments(wallSegments, floorY, ceilingY);
        BuildConfirmedWallCubesFromSegments(wallSegments, floorY, ceilingY);

        validationStatus = $"Confirmed room visualization built. walls:{confirmedRoomWallObjects.Count}";

        bool success =
            confirmedRoomFloorObject != null &&
            confirmedRoomCeilingObject != null &&
            confirmedRoomWallObjects.Count > 0;

        if (success && bindFurniturePlacementGeometryOnConfirm)
        {
            BindConfirmedRoomToFurniturePlacement();
        }

        validationStatus = $"Confirmed room visualization built. walls:{confirmedRoomWallObjects.Count}";
        return success;
    }

    public void BeginEntrancePlacementMode()
    {
        EnsureRoots(transform);

        if (!CanPlaceEntrance(out string reason))
        {
            entrancePlacementMode = false;
            validationStatus = reason;
            Debug.LogWarning($"[ConfirmRoomManager] {validationStatus}");
            return;
        }

        EnsureEntranceSelectableSurface(confirmedRoomFloorObject);
        entrancePlacementMode = true;
        validationStatus = "?낃뎄 ?꾩튂瑜?諛붾떏?먯꽌 ?좏깮?섏꽭??";
        Debug.Log("[ConfirmRoomManager] Entrance placement mode started.");
    }

    public void CancelEntrancePlacementMode()
    {
        entrancePlacementMode = false;
        validationStatus = hasEntrance ? "Entrance point assigned." : "Entrance point is not assigned.";
    }

    public void ClearEntrance()
    {
        hasEntrance = false;
        entrancePlacementMode = false;
        entrancePointWorld = Vector3.zero;

        if (entrancePointMarkerObject != null)
        {
            Destroy(entrancePointMarkerObject);
            entrancePointMarkerObject = null;
        }

        validationStatus = "Entrance point is not assigned.";
        Debug.Log("[ConfirmRoomManager] Entrance cleared.");
    }

    public void SetEntrancePoint(Vector3 worldPoint)
    {
        TrySetEntrancePoint(worldPoint);
    }

    public bool TrySetEntrancePoint(Vector3 worldPoint)
    {
        if (!CanPlaceEntrance(out string reason))
        {
            entrancePlacementMode = false;
            validationStatus = reason;
            Debug.LogWarning($"[ConfirmRoomManager] {validationStatus}");
            return false;
        }

        if (!TrySnapEntrancePointToNearestWall(worldPoint, out Vector3 snappedWallPoint))
        {
            validationStatus = "?낃뎄 ?꾩튂瑜?遺李⑺븷 踰쎈㈃??李얠쓣 ???놁뒿?덈떎.";
            Debug.LogWarning($"[ConfirmRoomManager] {validationStatus}");
            return false;
        }

        entrancePointWorld = snappedWallPoint;
        hasEntrance = true;
        entrancePlacementMode = false;
        CreateOrUpdateEntrancePointMarker(snappedWallPoint);
        SetEntranceMarkerVisible(true);
        validationStatus = "?낃뎄 ?꾩튂 吏???꾨즺";
        Debug.Log(
            $"[ConfirmRoomManager] Entrance point assigned. " +
            $"floorPoint:{worldPoint}, snappedWallPoint:{entrancePointWorld}, radius:{entranceRadiusMeters:0.00}m");
        return true;
    }

    public bool CanPlaceEntrance(out string reason)
    {
        reason = string.Empty;

        if (!roomReady || !finalRoomClosed)
        {
            reason = "?낃뎄 ?꾩튂???ロ엺 諛⑹뿉?쒕쭔 吏?뺥븷 ???덉뒿?덈떎.";
            return false;
        }

        if (confirmedRoomFloorObject == null)
        {
            reason = "?낃뎄 ?꾩튂瑜?吏?뺥븷 ConfirmedRoom_Floor媛 ?놁뒿?덈떎.";
            return false;
        }

        List<Segment2D> wallSegments = BuildConfirmedWallSegmentsForVisualization();
        if (wallSegments.Count == 0)
        {
            reason = "?낃뎄 ?꾩튂瑜?遺李⑺븷 踰쎈㈃???놁뒿?덈떎.";
            return false;
        }

        return true;
    }

    public bool CanProceedWithEntrance(out string reason)
    {
        reason = string.Empty;

        if (!requireEntranceBeforeProceed)
        {
            return true;
        }

        if (!roomReady || !finalRoomClosed)
        {
            reason = "?낃뎄 ?꾩튂???ロ엺 諛⑹뿉?쒕쭔 吏?뺥븷 ???덉뒿?덈떎.";
            return false;
        }

        if (!hasEntrance)
        {
            reason = "?낃뎄 ?꾩튂瑜?癒쇱? 吏?뺥븯?몄슂.";
            return false;
        }

        return true;
    }

    private void EnsureEntranceSelectableSurface(GameObject floorObject)
    {
        if (floorObject == null)
        {
            return;
        }

        Collider floorCollider = floorObject.GetComponent<Collider>();
        if (floorCollider == null)
        {
            floorObject.AddComponent<BoxCollider>();
        }

        MixedReality.Toolkit.StatefulInteractable interactable =
            floorObject.GetComponent<MixedReality.Toolkit.StatefulInteractable>();
        if (interactable == null)
        {
            floorObject.AddComponent<MixedReality.Toolkit.StatefulInteractable>();
        }

        ConfirmRoomEntranceSelectableSurface selectable =
            floorObject.GetComponent<ConfirmRoomEntranceSelectableSurface>();
        if (selectable == null)
        {
            selectable = floorObject.AddComponent<ConfirmRoomEntranceSelectableSurface>();
        }

        selectable.confirmRoomManager = this;
    }

    private float GetEntranceFloorY(float fallbackY)
    {
        if (confirmedRoomFloorObject != null)
        {
            Renderer renderer = confirmedRoomFloorObject.GetComponent<Renderer>();
            if (renderer != null)
            {
                return renderer.bounds.max.y;
            }

            Collider collider = confirmedRoomFloorObject.GetComponent<Collider>();
            if (collider != null)
            {
                return collider.bounds.max.y;
            }
        }

        if (scanner != null && scanner.currentRoom != null && !float.IsNaN(scanner.currentRoom.floorY))
        {
            return scanner.currentRoom.floorY + confirmedFloorYOffset;
        }

        return fallbackY;
    }

    private bool TrySnapEntrancePointToNearestWall(Vector3 floorPoint, out Vector3 snappedWallPoint)
    {
        snappedWallPoint = floorPoint;

        List<Segment2D> wallSegments = BuildConfirmedWallSegmentsForVisualization();
        if (wallSegments.Count == 0)
        {
            return false;
        }

        Vector2 floorPoint2D = new Vector2(floorPoint.x, floorPoint.z);
        Vector2 bestPoint = Vector2.zero;
        Vector2 bestDirection = Vector2.zero;
        float bestDistanceSqr = float.MaxValue;

        foreach (Segment2D segment in wallSegments)
        {
            Vector2 segmentVector = segment.end - segment.start;
            float segmentLengthSqr = segmentVector.sqrMagnitude;

            if (segmentLengthSqr < boundaryMinSegmentLength * boundaryMinSegmentLength)
            {
                continue;
            }

            float t = Mathf.Clamp01(Vector2.Dot(floorPoint2D - segment.start, segmentVector) / segmentLengthSqr);
            Vector2 candidatePoint = segment.start + segmentVector * t;
            float distanceSqr = (floorPoint2D - candidatePoint).sqrMagnitude;

            if (distanceSqr < bestDistanceSqr)
            {
                bestDistanceSqr = distanceSqr;
                bestPoint = candidatePoint;
                bestDirection = segmentVector.normalized;
            }
        }

        if (bestDirection.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        Vector2 wallNormal = new Vector2(-bestDirection.y, bestDirection.x);
        Vector2 toSelectedFloorPoint = floorPoint2D - bestPoint;

        if (toSelectedFloorPoint.sqrMagnitude > 0.0001f &&
            Vector2.Dot(wallNormal, toSelectedFloorPoint) < 0.0f)
        {
            wallNormal = -wallNormal;
        }

        float wallSurfaceOffset = Mathf.Max(0.0f, confirmedWallThickness * 0.5f);
        Vector2 snappedPoint2D = bestPoint + wallNormal * wallSurfaceOffset;

        snappedWallPoint = new Vector3(
            snappedPoint2D.x,
            GetEntranceFloorY(floorPoint.y),
            snappedPoint2D.y
        );

        return true;
    }

    private void CreateOrUpdateEntrancePointMarker(Vector3 point)
    {
        EnsureRoots(transform);

        if (entrancePointMarkerObject == null)
        {
            entrancePointMarkerObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            entrancePointMarkerObject.name = "ConfirmedRoom_EntrancePoint";
            entrancePointMarkerObject.transform.SetParent(entranceMarkerRoot, true);

            Collider collider = entrancePointMarkerObject.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;
            }
        }

        entrancePointMarkerObject.transform.position = point;
        entrancePointMarkerObject.transform.localScale = Vector3.one * Mathf.Max(0.02f, entrancePointMarkerSize);

        Renderer markerRenderer = entrancePointMarkerObject.GetComponent<Renderer>();
        if (markerRenderer != null && entrancePointMarkerMaterial != null)
        {
            markerRenderer.sharedMaterial = entrancePointMarkerMaterial;
        }
    }

    private void BindConfirmedRoomToFurniturePlacement()
    {
        if (confirmedRoomGeometryProvider == null)
        {
            Debug.LogWarning("[ConfirmRoomManager] ConfirmedRoomGeometryProvider is not assigned.");
            return;
        }

        bool bound = confirmedRoomGeometryProvider.SetConfirmedRoom(
            confirmedRoomWallObjects,
            confirmedRoomFloorObject,
            confirmedRoomCeilingObject
        );

        Debug.Log(
            $"[ConfirmRoomManager] Furniture placement geometry bind result:{bound}, " +
            $"walls:{confirmedRoomWallObjects.Count}, " +
            $"floor:{confirmedRoomFloorObject != null}, " +
            $"ceiling:{confirmedRoomCeilingObject != null}"
        );
    }

    public void ClearConfirmedRoomObjectsOnly()
    {
        confirmedRoomFloorObject = null;
        confirmedRoomCeilingObject = null;
        confirmedRoomWallObjects.Clear();

        if (confirmedRoomRoot == null)
        {
            return;
        }

        for (int i = confirmedRoomRoot.childCount - 1; i >= 0; i--)
        {
            Destroy(confirmedRoomRoot.GetChild(i).gameObject);
        }
    }

    public void SetConfirmedRoomRenderersVisible(bool visible)
    {
        if (confirmedRoomRoot == null)
        {
            return;
        }

        Renderer[] renderers = confirmedRoomRoot.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
            {
                renderers[i].enabled = visible;
            }
        }
    }

    public void SetEntranceMarkerVisible(bool visible)
    {
        if (entranceMarkerRoot != null)
        {
            entranceMarkerRoot.gameObject.SetActive(visible);
        }

        if (entrancePointMarkerObject != null)
        {
            entrancePointMarkerObject.SetActive(visible);
        }
    }

    public void ClearConfirmedRoomVisualization()
    {
        ClearConfirmedRoomObjectsOnly();
        ClearGapMarkers();
        confirmedBoundarySegments.Clear();
        normalizedBoundarySegmentsForConfirm.Clear();
        selectedCells.Clear();
        roomReady = false;
        finalRoomClosed = false;
        floodReachedOutside = false;
        selectedCellCount = 0;
        confirmedBoundarySegmentCount = 0;
        openEndpointCount = -1;
        hasEntrance = false;
        entrancePlacementMode = false;
        entrancePointWorld = Vector3.zero;
        if (entrancePointMarkerObject != null)
        {
            Destroy(entrancePointMarkerObject);
            entrancePointMarkerObject = null;
        }
        validationStatus = string.Empty;
    }

    public void ResetReviewState()
    {
        roomReady = false;
        validationStatus = string.Empty;
        finalRoomClosed = false;
        floodReachedOutside = false;
        selectedCellCount = 0;
        confirmedBoundarySegmentCount = 0;
        manualWallCount = 0;
        openEndpointCount = -1;
        lastDetectedWallSourceCount = 0;
        lastClosureEdgeSourceCount = 0;
        lastManualWallSourceCount = 0;
        lastReviewWallSegmentCount = 0;
        lastEndpointToSegmentSnapCount = 0;
        lastAutoBridgeSegmentCount = 0;
        lastCubeFootprintCount = 0;
        lastCubeBlockedCellCheckCount = 0;
        lastCubeBlockedMovementCheckCount = 0;
        ClearGapMarkers();
    }

    private void ResetValidationData(bool clearMarkers)
    {
        roomReady = false;
        validationStatus = "Confirm Room validation has not run.";
        finalRoomClosed = false;
        floodReachedOutside = false;
        selectedCellCount = 0;
        confirmedBoundarySegmentCount = 0;
        manualWallCount = 0;
        openEndpointCount = -1;
        lastDetectedWallSourceCount = 0;
        lastClosureEdgeSourceCount = 0;
        lastManualWallSourceCount = 0;
        lastReviewWallSegmentCount = 0;
        lastEndpointToSegmentSnapCount = 0;
        lastAutoBridgeSegmentCount = 0;
        lastCubeFootprintCount = 0;
        lastCubeBlockedCellCheckCount = 0;
        lastCubeBlockedMovementCheckCount = 0;
        selectedCells.Clear();
        confirmedBoundarySegments.Clear();
        normalizedBoundarySegmentsForConfirm.Clear();

        if (clearMarkers)
        {
            ClearGapMarkers();
        }
    }

    private List<Segment2D> BuildFinalBoundarySegments()
    {
        List<Segment2D> result = new List<Segment2D>();

        lastDetectedWallSourceCount = 0;
        lastClosureEdgeSourceCount = 0;
        lastManualWallSourceCount = 0;

        if (scanner != null && scanner.currentRoom != null)
        {
            if (includeDetectedWallsInClosedCheck && scanner.currentRoom.detectedWalls != null)
            {
                foreach (WallSegment wall in scanner.currentRoom.detectedWalls)
                {
                    if (Vector2.Distance(wall.start, wall.end) < boundaryMinSegmentLength)
                    {
                        continue;
                    }

                    result.Add(new Segment2D { start = wall.start, end = wall.end });
                    lastDetectedWallSourceCount++;
                }
            }

            if (includeClosureEdgesInClosedCheck && scanner.currentRoom.closureEdges != null)
            {
                foreach (WallSegment edge in scanner.currentRoom.closureEdges)
                {
                    if (Vector2.Distance(edge.start, edge.end) < boundaryMinSegmentLength)
                    {
                        continue;
                    }

                    result.Add(new Segment2D { start = edge.start, end = edge.end });
                    lastClosureEdgeSourceCount++;
                }
            }
        }

        if (includeManualWallsInClosedCheck)
        {
            AddManualWallSegments(result);
        }

        return result;
    }

    private void AddManualWallSegments(List<Segment2D> target)
    {
        if (manualWallBuilder == null)
        {
            return;
        }

        // Confirm Room ?④퀎?먯꽌??manualWallRoot瑜??④릿 ??寃利?誘몃━蹂닿린瑜?留뚮뱾 ???덉뒿?덈떎.
        // ?곕씪??activeInHierarchy媛 false??吏곸젒 ?앹꽦 ManualWall??諛섎뱶???ы븿?⑸땲??
        List<ManualWallBuilder.ManualWallSegmentSnapshot> snapshots =
            manualWallBuilder.GetManualWallSegmentsSnapshot(includeInactiveObjects: true);

        foreach (ManualWallBuilder.ManualWallSegmentSnapshot snapshot in snapshots)
        {
            if (Vector2.Distance(snapshot.start, snapshot.end) < boundaryMinSegmentLength)
            {
                continue;
            }

            target.Add(new Segment2D { start = snapshot.start, end = snapshot.end });
            lastManualWallSourceCount++;
        }
    }

    private int CountManualWalls()
    {
        if (manualWallBuilder == null)
        {
            return 0;
        }

        return manualWallBuilder.GetManualWallSegmentsSnapshot(includeInactiveObjects: true).Count;
    }

    private List<Segment2D> NormalizeBoundarySegments(List<Segment2D> source)
    {
        lastEndpointToSegmentSnapCount = 0;
        lastAutoBridgeSegmentCount = 0;

        List<Segment2D> cleaned = new List<Segment2D>();

        foreach (Segment2D segment in source)
        {
            if (Vector2.Distance(segment.start, segment.end) >= boundaryMinSegmentLength)
            {
                cleaned.Add(segment);
            }
        }

        // 1. ?ㅼ젣濡?援먯감?섎뒗 ?좊텇? 援먯감?먯뿉???섎닏?덈떎.
        cleaned = SplitSegmentsAtIntersections(cleaned);

        // 2. ?먮룞 ?몄떇 踰쎌뿉???먯＜ ?앷린??"endpoint媛 ?ㅻⅨ 踰?以묎컙 洹쇱쿂???우븘 ?덈뒗" 肄붾꼫 ?ㅼ감瑜?蹂댁젙?⑸땲??
        //    ???④퀎媛 ?놁쑝硫?踰쎌? ?쒓컖?곸쑝濡??ロ? 蹂댁뿬??endpoint graph?먯꽌???대┛ endpoint濡??⑥뒿?덈떎.
        if (snapEndpointsToNearbySegments)
        {
            cleaned = SnapEndpointsToNearbySegmentsAndSplit(cleaned, out lastEndpointToSegmentSnapCount);
        }

        // 3. endpoint?쇰━ 媛源뚯슫 ?먯? 媛숈? 肄붾꼫濡??⑹묩?덈떎.
        SnapCloseEndpoints(cleaned);
        RemoveShortSegments(cleaned);

        // 4. 媛숈? ???꾩뿉???욧굅??寃뱀튇 segment瑜??⑹묩?덈떎.
        MergeCollinearTouchingSegments(cleaned);
        RemoveShortSegments(cleaned);

        // 5. 洹몃옒???⑥? ?묒? 肄붾꼫 gap? 寃利?誘몃━蹂닿린??bridge segment濡??レ뒿?덈떎.
        //    臾??듬줈瑜??レ? ?딅룄濡?openEndpointBridgeDistance??endpointSnapDistance蹂대떎 ?쎄컙 ???뺣룄濡쒕쭔 ?〓땲??
        if (bridgeSmallOpenEndpointGaps && (!useCubeFootprintFloodFill || useAutoBridgeSegmentsForCubeFootprintValidation))
        {
            AddBridgeSegmentsForNearbyOpenEndpoints(cleaned, out lastAutoBridgeSegmentCount);

            if (lastAutoBridgeSegmentCount > 0)
            {
                cleaned = SplitSegmentsAtIntersections(cleaned);
                SnapCloseEndpoints(cleaned);
                RemoveShortSegments(cleaned);
                MergeCollinearTouchingSegments(cleaned);
                RemoveShortSegments(cleaned);
            }
        }

        return cleaned;
    }

    private List<Segment2D> SplitSegmentsAtIntersections(List<Segment2D> source)
    {
        List<List<float>> splitTs = new List<List<float>>();

        for (int i = 0; i < source.Count; i++)
        {
            splitTs.Add(new List<float> { 0.0f, 1.0f });
        }

        for (int i = 0; i < source.Count; i++)
        {
            Segment2D a = source[i];

            for (int j = i + 1; j < source.Count; j++)
            {
                Segment2D b = source[j];

                if (TryGetSegmentIntersectionParameters(a.start, a.end, b.start, b.end, out float ta, out float tb))
                {
                    if (ta > 0.001f && ta < 0.999f)
                    {
                        splitTs[i].Add(ta);
                    }

                    if (tb > 0.001f && tb < 0.999f)
                    {
                        splitTs[j].Add(tb);
                    }
                }
            }
        }

        List<Segment2D> result = new List<Segment2D>();

        for (int i = 0; i < source.Count; i++)
        {
            List<float> ts = splitTs[i];
            ts.Sort();

            for (int j = 0; j < ts.Count - 1; j++)
            {
                float t0 = ts[j];
                float t1 = ts[j + 1];

                if (t1 - t0 < 0.001f)
                {
                    continue;
                }

                Vector2 start = Vector2.Lerp(source[i].start, source[i].end, t0);
                Vector2 end = Vector2.Lerp(source[i].start, source[i].end, t1);

                if (Vector2.Distance(start, end) >= boundaryMinSegmentLength)
                {
                    result.Add(new Segment2D { start = start, end = end });
                }
            }
        }

        return result;
    }

    private void SnapCloseEndpoints(List<Segment2D> segments)
    {
        if (segments == null || segments.Count == 0)
        {
            return;
        }

        List<EndpointRef> endpoints = new List<EndpointRef>();

        for (int i = 0; i < segments.Count; i++)
        {
            endpoints.Add(new EndpointRef { segmentIndex = i, isStart = true, point = segments[i].start });
            endpoints.Add(new EndpointRef { segmentIndex = i, isStart = false, point = segments[i].end });
        }

        int endpointCount = endpoints.Count;
        int[] parent = new int[endpointCount];

        for (int i = 0; i < endpointCount; i++)
        {
            parent[i] = i;
        }

        float snapDistance = Mathf.Max(0.001f, endpointSnapDistance);

        for (int i = 0; i < endpointCount; i++)
        {
            for (int j = i + 1; j < endpointCount; j++)
            {
                if (Vector2.Distance(endpoints[i].point, endpoints[j].point) <= snapDistance)
                {
                    Union(parent, i, j);
                }
            }
        }

        Dictionary<int, List<int>> clusters = new Dictionary<int, List<int>>();

        for (int i = 0; i < endpointCount; i++)
        {
            int root = Find(parent, i);
            if (!clusters.TryGetValue(root, out List<int> cluster))
            {
                cluster = new List<int>();
                clusters.Add(root, cluster);
            }
            cluster.Add(i);
        }

        foreach (KeyValuePair<int, List<int>> pair in clusters)
        {
            if (pair.Value.Count <= 1)
            {
                continue;
            }

            Vector2 sum = Vector2.zero;
            foreach (int endpointIndex in pair.Value)
            {
                sum += endpoints[endpointIndex].point;
            }

            Vector2 snappedPoint = sum / pair.Value.Count;

            foreach (int endpointIndex in pair.Value)
            {
                EndpointRef endpoint = endpoints[endpointIndex];
                Segment2D segment = segments[endpoint.segmentIndex];

                if (endpoint.isStart)
                {
                    segment.start = snappedPoint;
                }
                else
                {
                    segment.end = snappedPoint;
                }

                segments[endpoint.segmentIndex] = segment;
            }
        }
    }

    private void RemoveShortSegments(List<Segment2D> segments)
    {
        for (int i = segments.Count - 1; i >= 0; i--)
        {
            if (Vector2.Distance(segments[i].start, segments[i].end) < boundaryMinSegmentLength)
            {
                segments.RemoveAt(i);
            }
        }
    }

    private void MergeCollinearTouchingSegments(List<Segment2D> segments)
    {
        bool changed = true;
        int guard = 0;

        while (changed && guard < 64)
        {
            changed = false;
            guard++;

            for (int i = 0; i < segments.Count && !changed; i++)
            {
                for (int j = i + 1; j < segments.Count && !changed; j++)
                {
                    if (TryMergeCollinearSegments(segments[i], segments[j], out Segment2D merged))
                    {
                        segments[i] = merged;
                        segments.RemoveAt(j);
                        changed = true;
                    }
                }
            }
        }
    }

    private bool TryMergeCollinearSegments(Segment2D a, Segment2D b, out Segment2D merged)
    {
        merged = default;

        Vector2 aDir = a.end - a.start;
        Vector2 bDir = b.end - b.start;

        if (aDir.sqrMagnitude < 0.0001f || bDir.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        aDir.Normalize();
        bDir.Normalize();

        float angle = Vector2.Angle(aDir, bDir);
        angle = Mathf.Min(angle, Mathf.Abs(180.0f - angle));

        if (angle > collinearMergeAngle)
        {
            return false;
        }

        if (DistancePointToSegment(b.start, a.start, a.end) > collinearMergeDistance &&
            DistancePointToSegment(b.end, a.start, a.end) > collinearMergeDistance &&
            DistancePointToSegment(a.start, b.start, b.end) > collinearMergeDistance &&
            DistancePointToSegment(a.end, b.start, b.end) > collinearMergeDistance)
        {
            return false;
        }

        List<Vector2> points = new List<Vector2> { a.start, a.end, b.start, b.end };
        Vector2 origin = points[0];
        Vector2 dir = aDir;

        float minT = float.MaxValue;
        float maxT = float.MinValue;
        Vector2 minPoint = origin;
        Vector2 maxPoint = origin;

        foreach (Vector2 point in points)
        {
            float t = Vector2.Dot(point - origin, dir);

            if (t < minT)
            {
                minT = t;
                minPoint = point;
            }

            if (t > maxT)
            {
                maxT = t;
                maxPoint = point;
            }
        }

        if (Vector2.Distance(minPoint, maxPoint) < boundaryMinSegmentLength)
        {
            return false;
        }

        merged = new Segment2D { start = minPoint, end = maxPoint };
        return true;
    }

    private List<Vector2> FindOpenEndpointPoints(List<Segment2D> segments)
    {
        List<Vector2> result = new List<Vector2>();
        List<EndpointRef> refs = FindOpenEndpointRefs(segments);

        foreach (EndpointRef endpointRef in refs)
        {
            result.Add(endpointRef.point);
        }

        return result;
    }

    private List<EndpointRef> FindOpenEndpointRefs(List<Segment2D> segments)
    {
        List<EndpointRef> result = new List<EndpointRef>();

        if (segments == null || segments.Count == 0)
        {
            return result;
        }

        List<EndpointRef> endpoints = new List<EndpointRef>();

        for (int i = 0; i < segments.Count; i++)
        {
            if (Vector2.Distance(segments[i].start, segments[i].end) < boundaryMinSegmentLength)
            {
                continue;
            }

            endpoints.Add(new EndpointRef { segmentIndex = i, isStart = true, point = segments[i].start });
            endpoints.Add(new EndpointRef { segmentIndex = i, isStart = false, point = segments[i].end });
        }

        int endpointCount = endpoints.Count;
        if (endpointCount == 0)
        {
            return result;
        }

        float snapDistance = Mathf.Max(0.01f, endpointSnapDistance);
        int[] parent = new int[endpointCount];

        for (int i = 0; i < endpointCount; i++)
        {
            parent[i] = i;
        }

        for (int i = 0; i < endpointCount; i++)
        {
            for (int j = i + 1; j < endpointCount; j++)
            {
                if (Vector2.Distance(endpoints[i].point, endpoints[j].point) <= snapDistance)
                {
                    Union(parent, i, j);
                }
            }
        }

        Dictionary<int, List<int>> clusters = new Dictionary<int, List<int>>();
        for (int i = 0; i < endpointCount; i++)
        {
            int root = Find(parent, i);
            if (!clusters.TryGetValue(root, out List<int> cluster))
            {
                cluster = new List<int>();
                clusters.Add(root, cluster);
            }
            cluster.Add(i);
        }

        foreach (KeyValuePair<int, List<int>> pair in clusters)
        {
            if (pair.Value.Count == 1)
            {
                result.Add(endpoints[pair.Value[0]]);
            }
        }

        return result;
    }

    private List<Segment2D> SnapEndpointsToNearbySegmentsAndSplit(List<Segment2D> source, out int snapCount)
    {
        snapCount = 0;

        if (source == null || source.Count == 0)
        {
            return new List<Segment2D>();
        }

        List<Segment2D> working = new List<Segment2D>(source);
        List<List<float>> splitTs = new List<List<float>>();

        for (int i = 0; i < working.Count; i++)
        {
            splitTs.Add(new List<float> { 0.0f, 1.0f });
        }

        float snapDistance = Mathf.Max(0.001f, endpointToSegmentSnapDistance);
        float splitEpsilon = Mathf.Clamp(endpointProjectionSplitEpsilon, 0.001f, 0.45f);

        for (int segmentIndex = 0; segmentIndex < working.Count; segmentIndex++)
        {
            TrySnapEndpointToNearestSegment(working, splitTs, segmentIndex, isStart: true, snapDistance, splitEpsilon, ref snapCount);
            TrySnapEndpointToNearestSegment(working, splitTs, segmentIndex, isStart: false, snapDistance, splitEpsilon, ref snapCount);
        }

        List<Segment2D> result = new List<Segment2D>();

        for (int i = 0; i < working.Count; i++)
        {
            List<float> ts = splitTs[i];
            ts.Sort();

            for (int tIndex = 0; tIndex < ts.Count - 1; tIndex++)
            {
                float t0 = ts[tIndex];
                float t1 = ts[tIndex + 1];

                if (t1 - t0 < 0.001f)
                {
                    continue;
                }

                Vector2 start = Vector2.Lerp(working[i].start, working[i].end, t0);
                Vector2 end = Vector2.Lerp(working[i].start, working[i].end, t1);

                if (Vector2.Distance(start, end) >= boundaryMinSegmentLength)
                {
                    result.Add(new Segment2D { start = start, end = end });
                }
            }
        }

        return result;
    }

    private bool TrySnapEndpointToNearestSegment(
        List<Segment2D> segments,
        List<List<float>> splitTs,
        int endpointSegmentIndex,
        bool isStart,
        float snapDistance,
        float splitEpsilon,
        ref int snapCount)
    {
        Segment2D endpointSegment = segments[endpointSegmentIndex];
        Vector2 endpoint = isStart ? endpointSegment.start : endpointSegment.end;

        float bestDistance = snapDistance;
        int bestSegmentIndex = -1;
        float bestT = 0.0f;
        Vector2 bestPoint = endpoint;

        for (int i = 0; i < segments.Count; i++)
        {
            if (i == endpointSegmentIndex)
            {
                continue;
            }

            Segment2D candidate = segments[i];
            if (Vector2.Distance(candidate.start, candidate.end) < boundaryMinSegmentLength)
            {
                continue;
            }

            float t = ProjectPointToSegment01(endpoint, candidate.start, candidate.end);
            Vector2 projected = Vector2.Lerp(candidate.start, candidate.end, t);
            float distance = Vector2.Distance(endpoint, projected);

            if (distance <= bestDistance)
            {
                bestDistance = distance;
                bestSegmentIndex = i;
                bestT = t;
                bestPoint = projected;
            }
        }

        if (bestSegmentIndex < 0)
        {
            return false;
        }

        if (Vector2.Distance(endpoint, bestPoint) < 0.0001f)
        {
            return false;
        }

        if (isStart)
        {
            endpointSegment.start = bestPoint;
        }
        else
        {
            endpointSegment.end = bestPoint;
        }

        segments[endpointSegmentIndex] = endpointSegment;
        snapCount++;

        if (bestT > splitEpsilon && bestT < 1.0f - splitEpsilon)
        {
            AddUniqueSplitT(splitTs[bestSegmentIndex], bestT);
        }

        return true;
    }

    private void AddBridgeSegmentsForNearbyOpenEndpoints(List<Segment2D> segments, out int bridgeCount)
    {
        bridgeCount = 0;

        if (segments == null || segments.Count == 0)
        {
            return;
        }

        List<EndpointRef> openEndpoints = FindOpenEndpointRefs(segments);
        bool[] used = new bool[openEndpoints.Count];
        float bridgeDistance = Mathf.Max(endpointSnapDistance, openEndpointBridgeDistance);

        for (int i = 0; i < openEndpoints.Count; i++)
        {
            if (used[i])
            {
                continue;
            }

            int bestIndex = -1;
            float bestDistance = bridgeDistance;

            for (int j = i + 1; j < openEndpoints.Count; j++)
            {
                if (used[j])
                {
                    continue;
                }

                if (openEndpoints[i].segmentIndex == openEndpoints[j].segmentIndex)
                {
                    continue;
                }

                float distance = Vector2.Distance(openEndpoints[i].point, openEndpoints[j].point);
                if (distance <= bestDistance && distance >= boundaryMinSegmentLength)
                {
                    bestDistance = distance;
                    bestIndex = j;
                }
            }

            if (bestIndex < 0)
            {
                continue;
            }

            segments.Add(new Segment2D
            {
                start = openEndpoints[i].point,
                end = openEndpoints[bestIndex].point
            });

            used[i] = true;
            used[bestIndex] = true;
            bridgeCount++;
        }
    }

    private bool TryRunCubeFootprintFloodFill(List<Segment2D> segments)
    {
        selectedCells.Clear();
        lastCubeFootprintCount = 0;
        lastCubeBlockedCellCheckCount = 0;
        lastCubeBlockedMovementCheckCount = 0;

        if (Camera.main == null)
        {
            floodReachedOutside = true;
            return false;
        }

        if (segments == null || segments.Count == 0)
        {
            floodReachedOutside = true;
            return false;
        }

        List<WallFootprint2D> footprints = BuildWallFootprints(segments);
        lastCubeFootprintCount = footprints.Count;

        if (footprints.Count == 0)
        {
            floodReachedOutside = true;
            return false;
        }

        BuildGridBounds(segments);

        Vector3 cameraPosition = Camera.main.transform.position;
        Vector2 seed = new Vector2(cameraPosition.x, cameraPosition.z);

        if (!TryWorldToCell(seed, out Vector2Int startCell))
        {
            floodReachedOutside = true;
            return false;
        }

        if (IsBlockedCellByFootprints(startCell, footprints))
        {
            if (!TryFindNearestFreeCellByFootprints(startCell, footprints, out startCell))
            {
                floodReachedOutside = true;
                return false;
            }
        }

        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        HashSet<Vector2Int> visited = new HashSet<Vector2Int>();

        queue.Enqueue(startCell);
        visited.Add(startCell);
        floodReachedOutside = false;

        Vector2Int[] directions =
        {
            new Vector2Int(1, 0),
            new Vector2Int(-1, 0),
            new Vector2Int(0, 1),
            new Vector2Int(0, -1)
        };

        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();
            selectedCells.Add(current);

            if (IsBorderCell(current))
            {
                floodReachedOutside = true;
            }

            Vector2 currentWorld = CellToWorld(current);

            foreach (Vector2Int direction in directions)
            {
                Vector2Int next = current + direction;

                if (!IsInsideGrid(next))
                {
                    floodReachedOutside = true;
                    continue;
                }

                if (visited.Contains(next))
                {
                    continue;
                }

                if (IsBlockedCellByFootprints(next, footprints))
                {
                    continue;
                }

                Vector2 nextWorld = CellToWorld(next);

                if (MovementCrossesAnyFootprint(currentWorld, nextWorld, footprints))
                {
                    continue;
                }

                visited.Add(next);
                queue.Enqueue(next);
            }
        }

        return selectedCells.Count > 0;
    }

    private List<WallFootprint2D> BuildWallFootprints(List<Segment2D> segments)
    {
        List<WallFootprint2D> footprints = new List<WallFootprint2D>();
        float thickness = GetEffectiveCubeFootprintThickness();
        float halfThickness = Mathf.Max(0.001f, thickness * 0.5f);
        float endPadding = Mathf.Max(0.0f, cubeFootprintEndPadding);

        foreach (Segment2D segment in segments)
        {
            Vector2 delta = segment.end - segment.start;
            float length = delta.magnitude;

            if (length < boundaryMinSegmentLength)
            {
                continue;
            }

            Vector2 direction = delta / length;
            Vector2 normal = new Vector2(direction.y, -direction.x);

            footprints.Add(new WallFootprint2D
            {
                center = (segment.start + segment.end) * 0.5f,
                direction = direction,
                normal = normal,
                halfLength = length * 0.5f + endPadding,
                halfThickness = halfThickness
            });
        }

        return footprints;
    }

    private float GetEffectiveCubeFootprintThickness()
    {
        float thickness = Mathf.Max(0.001f, cubeFootprintWallThickness);
        thickness = Mathf.Max(thickness, confirmedWallThickness);
        thickness = Mathf.Max(thickness, wallBlockDistance * 2.0f);

        if (manualWallBuilder != null)
        {
            thickness = Mathf.Max(thickness, manualWallBuilder.wallThickness);
        }

        return thickness;
    }

    private bool IsBlockedCellByFootprints(Vector2Int cell, List<WallFootprint2D> footprints)
    {
        Vector2 point = CellToWorld(cell);

        foreach (WallFootprint2D footprint in footprints)
        {
            if (IsPointInsideFootprint(point, footprint, cubeFootprintCollisionPadding))
            {
                lastCubeBlockedCellCheckCount++;
                return true;
            }
        }

        return false;
    }

    private bool TryFindNearestFreeCellByFootprints(
        Vector2Int center,
        List<WallFootprint2D> footprints,
        out Vector2Int freeCell)
    {
        for (int radius = 1; radius <= nearestFreeCellSearchRadius; radius++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    Vector2Int candidate = center + new Vector2Int(dx, dy);

                    if (!IsInsideGrid(candidate))
                    {
                        continue;
                    }

                    if (!IsBlockedCellByFootprints(candidate, footprints))
                    {
                        freeCell = candidate;
                        return true;
                    }
                }
            }
        }

        freeCell = default;
        return false;
    }

    private bool MovementCrossesAnyFootprint(
        Vector2 from,
        Vector2 to,
        List<WallFootprint2D> footprints)
    {
        foreach (WallFootprint2D footprint in footprints)
        {
            if (SegmentIntersectsFootprint(from, to, footprint, cubeFootprintCollisionPadding))
            {
                lastCubeBlockedMovementCheckCount++;
                return true;
            }
        }

        return false;
    }

    private static bool IsPointInsideFootprint(
        Vector2 point,
        WallFootprint2D footprint,
        float padding)
    {
        Vector2 relative = point - footprint.center;
        float along = Vector2.Dot(relative, footprint.direction);
        float side = Vector2.Dot(relative, footprint.normal);

        return Mathf.Abs(along) <= footprint.halfLength + padding &&
               Mathf.Abs(side) <= footprint.halfThickness + padding;
    }

    private static bool SegmentIntersectsFootprint(
        Vector2 from,
        Vector2 to,
        WallFootprint2D footprint,
        float padding)
    {
        if (IsPointInsideFootprint(from, footprint, padding) ||
            IsPointInsideFootprint(to, footprint, padding))
        {
            return true;
        }

        GetFootprintCorners(footprint, padding, out Vector2 a, out Vector2 b, out Vector2 c, out Vector2 d);

        return SegmentsIntersect(from, to, a, b) ||
               SegmentsIntersect(from, to, b, c) ||
               SegmentsIntersect(from, to, c, d) ||
               SegmentsIntersect(from, to, d, a);
    }

    private static void GetFootprintCorners(
        WallFootprint2D footprint,
        float padding,
        out Vector2 a,
        out Vector2 b,
        out Vector2 c,
        out Vector2 d)
    {
        Vector2 along = footprint.direction * (footprint.halfLength + padding);
        Vector2 side = footprint.normal * (footprint.halfThickness + padding);

        a = footprint.center + along + side;
        b = footprint.center + along - side;
        c = footprint.center - along - side;
        d = footprint.center - along + side;
    }

    private bool TryRunUserRoomFloodFill(List<Segment2D> segments)
    {
        selectedCells.Clear();

        if (Camera.main == null)
        {
            floodReachedOutside = true;
            return false;
        }

        BuildGridBounds(segments);

        Vector3 cameraPosition = Camera.main.transform.position;
        Vector2 seed = new Vector2(cameraPosition.x, cameraPosition.z);

        if (!TryWorldToCell(seed, out Vector2Int startCell))
        {
            floodReachedOutside = true;
            return false;
        }

        if (IsBlockedCell(startCell, segments))
        {
            if (!TryFindNearestFreeCell(startCell, segments, out startCell))
            {
                floodReachedOutside = true;
                return false;
            }
        }

        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        HashSet<Vector2Int> visited = new HashSet<Vector2Int>();

        queue.Enqueue(startCell);
        visited.Add(startCell);

        floodReachedOutside = false;

        Vector2Int[] directions =
        {
            new Vector2Int(1, 0),
            new Vector2Int(-1, 0),
            new Vector2Int(0, 1),
            new Vector2Int(0, -1)
        };

        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();
            selectedCells.Add(current);

            if (IsBorderCell(current))
            {
                floodReachedOutside = true;
            }

            Vector2 currentWorld = CellToWorld(current);

            foreach (Vector2Int direction in directions)
            {
                Vector2Int next = current + direction;

                if (!IsInsideGrid(next))
                {
                    floodReachedOutside = true;
                    continue;
                }

                if (visited.Contains(next))
                {
                    continue;
                }

                if (IsBlockedCell(next, segments))
                {
                    continue;
                }

                Vector2 nextWorld = CellToWorld(next);

                if (MovementCrossesAnySegment(currentWorld, nextWorld, segments))
                {
                    continue;
                }

                visited.Add(next);
                queue.Enqueue(next);
            }
        }

        return selectedCells.Count > 0;
    }

    private void BuildGridBounds(List<Segment2D> segments)
    {
        float minX = float.MaxValue;
        float maxX = float.MinValue;
        float minZ = float.MaxValue;
        float maxZ = float.MinValue;

        foreach (Segment2D segment in segments)
        {
            minX = Mathf.Min(minX, segment.start.x, segment.end.x);
            maxX = Mathf.Max(maxX, segment.start.x, segment.end.x);
            minZ = Mathf.Min(minZ, segment.start.y, segment.end.y);
            maxZ = Mathf.Max(maxZ, segment.start.y, segment.end.y);
        }

        minX -= gridBoundsMargin;
        maxX += gridBoundsMargin;
        minZ -= gridBoundsMargin;
        maxZ += gridBoundsMargin;

        gridWidth = Mathf.CeilToInt((maxX - minX) / gridCellSize);
        gridHeight = Mathf.CeilToInt((maxZ - minZ) / gridCellSize);

        gridWidth = Mathf.Clamp(gridWidth, 1, maxGridCellsPerAxis);
        gridHeight = Mathf.Clamp(gridHeight, 1, maxGridCellsPerAxis);

        gridMin = new Vector2(minX, minZ);
    }

    private bool TryWorldToCell(Vector2 world, out Vector2Int cell)
    {
        int x = Mathf.FloorToInt((world.x - gridMin.x) / gridCellSize);
        int y = Mathf.FloorToInt((world.y - gridMin.y) / gridCellSize);

        cell = new Vector2Int(x, y);
        return IsInsideGrid(cell);
    }

    private Vector2 CellToWorld(Vector2Int cell)
    {
        return new Vector2(
            gridMin.x + (cell.x + 0.5f) * gridCellSize,
            gridMin.y + (cell.y + 0.5f) * gridCellSize
        );
    }

    private bool IsInsideGrid(Vector2Int cell)
    {
        return cell.x >= 0 && cell.y >= 0 && cell.x < gridWidth && cell.y < gridHeight;
    }

    private bool IsBorderCell(Vector2Int cell)
    {
        return cell.x == 0 || cell.y == 0 || cell.x == gridWidth - 1 || cell.y == gridHeight - 1;
    }

    private bool IsBlockedCell(Vector2Int cell, List<Segment2D> segments)
    {
        Vector2 point = CellToWorld(cell);

        foreach (Segment2D segment in segments)
        {
            if (DistancePointToSegment(point, segment.start, segment.end) <= wallBlockDistance)
            {
                return true;
            }
        }

        return false;
    }

    private bool TryFindNearestFreeCell(Vector2Int center, List<Segment2D> segments, out Vector2Int freeCell)
    {
        for (int radius = 1; radius <= nearestFreeCellSearchRadius; radius++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    Vector2Int candidate = center + new Vector2Int(dx, dy);

                    if (!IsInsideGrid(candidate))
                    {
                        continue;
                    }

                    if (!IsBlockedCell(candidate, segments))
                    {
                        freeCell = candidate;
                        return true;
                    }
                }
            }
        }

        freeCell = default;
        return false;
    }

    private bool MovementCrossesAnySegment(Vector2 from, Vector2 to, List<Segment2D> segments)
    {
        foreach (Segment2D segment in segments)
        {
            if (SegmentsIntersect(from, to, segment.start, segment.end))
            {
                return true;
            }
        }

        return false;
    }

    private void BuildConfirmedBoundarySegments(List<Segment2D> allSegments)
    {
        confirmedBoundarySegments.Clear();

        foreach (Segment2D segment in allSegments)
        {
            if (trimConfirmedBoundarySegmentsToSelectedRoom)
            {
                AddSelectedRoomAdjacentSegmentPortions(segment, confirmedBoundarySegments);
            }
            else if (IsSegmentAdjacentToSelectedRoom(segment))
            {
                confirmedBoundarySegments.Add(segment);
            }
        }

        if (trimConfirmedBoundarySegmentsToSelectedRoom)
        {
            SnapCloseEndpoints(confirmedBoundarySegments);
            RemoveShortSegments(confirmedBoundarySegments);
            MergeCollinearTouchingSegments(confirmedBoundarySegments);
            RemoveShortSegments(confirmedBoundarySegments);

            if (confirmedBoundarySegments.Count < 3)
            {
                confirmedBoundarySegments.Clear();
                foreach (Segment2D segment in allSegments)
                {
                    if (IsSegmentAdjacentToSelectedRoom(segment))
                    {
                        confirmedBoundarySegments.Add(segment);
                    }
                }
            }
        }
    }

    private void AddSelectedRoomAdjacentSegmentPortions(Segment2D segment, List<Segment2D> target)
    {
        float length = Vector2.Distance(segment.start, segment.end);
        if (length < boundaryMinSegmentLength)
        {
            return;
        }

        float step = Mathf.Max(
            boundaryMinSegmentLength,
            gridCellSize * Mathf.Max(0.1f, confirmedBoundaryTrimSampleStepMultiplier)
        );

        int sampleCount = Mathf.Max(2, Mathf.CeilToInt(length / step));
        bool inRun = false;
        float runStartT = 0.0f;

        for (int i = 0; i < sampleCount; i++)
        {
            float t0 = i / (float)sampleCount;
            float t1 = (i + 1) / (float)sampleCount;
            float midT = (t0 + t1) * 0.5f;
            Vector2 midPoint = Vector2.Lerp(segment.start, segment.end, midT);
            bool nearSelectedRoom = IsPointNearSelectedRoomCell(midPoint);

            if (nearSelectedRoom && !inRun)
            {
                inRun = true;
                runStartT = t0;
            }

            if ((!nearSelectedRoom || i == sampleCount - 1) && inRun)
            {
                float runEndT = nearSelectedRoom && i == sampleCount - 1 ? t1 : t0;
                float endPaddingT = Mathf.Clamp01(Mathf.Max(0.0f, confirmedBoundaryTrimEndPaddingMeters) / length);
                float paddedStartT = Mathf.Clamp01(runStartT - endPaddingT);
                float paddedEndT = Mathf.Clamp01(runEndT + endPaddingT);
                Vector2 start = Vector2.Lerp(segment.start, segment.end, paddedStartT);
                Vector2 end = Vector2.Lerp(segment.start, segment.end, paddedEndT);

                if (Vector2.Distance(start, end) >= boundaryMinSegmentLength)
                {
                    target.Add(new Segment2D { start = start, end = end });
                }

                inRun = false;
            }
        }
    }

    private bool IsPointNearSelectedRoomCell(Vector2 point)
    {
        if (selectedCells.Count == 0 || !TryWorldToCell(point, out Vector2Int centerCell))
        {
            return false;
        }

        float radius = Mathf.Max(
            wallBlockDistance + gridCellSize,
            gridCellSize * Mathf.Max(0.5f, confirmedBoundaryTrimSelectedCellRadiusMultiplier)
        );
        int radiusCells = Mathf.Max(1, Mathf.CeilToInt(radius / gridCellSize));

        for (int x = -radiusCells; x <= radiusCells; x++)
        {
            for (int y = -radiusCells; y <= radiusCells; y++)
            {
                Vector2Int cell = new Vector2Int(centerCell.x + x, centerCell.y + y);
                if (!selectedCells.Contains(cell))
                {
                    continue;
                }

                if (Vector2.Distance(CellToWorld(cell), point) <= radius)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool IsSegmentAdjacentToSelectedRoom(Segment2D segment)
    {
        float length = Vector2.Distance(segment.start, segment.end);
        int sampleCount = Mathf.Max(2, Mathf.CeilToInt(length / gridCellSize));
        Vector2 dir = (segment.end - segment.start).normalized;

        if (dir.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        Vector2 normal = new Vector2(-dir.y, dir.x);

        for (int i = 0; i <= sampleCount; i++)
        {
            float t = i / (float)sampleCount;
            Vector2 p = Vector2.Lerp(segment.start, segment.end, t);
            Vector2 sideA = p + normal * gridCellSize * 0.75f;
            Vector2 sideB = p - normal * gridCellSize * 0.75f;

            bool aSelected = TryWorldToCell(sideA, out Vector2Int cellA) && selectedCells.Contains(cellA);
            bool bSelected = TryWorldToCell(sideB, out Vector2Int cellB) && selectedCells.Contains(cellB);

            if (aSelected || bSelected)
            {
                return true;
            }
        }

        return false;
    }


    private List<Segment2D> BuildReviewWallSegmentsForVisualization()
    {
        List<Segment2D> result = new List<Segment2D>();

        if (normalizedBoundarySegmentsForConfirm.Count == 0)
        {
            List<Segment2D> allSegments = BuildFinalBoundarySegments();
            if (normalizeBoundarySegmentsBeforeConfirm)
            {
                allSegments = NormalizeBoundarySegments(allSegments);
            }

            foreach (Segment2D segment in allSegments)
            {
                AddUniqueCandidateSegment(result, segment);
            }

            return result;
        }

        foreach (Segment2D segment in normalizedBoundarySegmentsForConfirm)
        {
            AddUniqueCandidateSegment(result, segment);
        }

        return result;
    }

    private List<Segment2D> BuildConfirmedWallSegmentsForVisualization()
    {
        List<Segment2D> result = new List<Segment2D>();

        foreach (Segment2D segment in confirmedBoundarySegments)
        {
            AddUniqueCandidateSegment(result, segment);
        }

        return result;
    }

    private void AddUniqueCandidateSegment(List<Segment2D> target, Segment2D segment)
    {
        if (Vector2.Distance(segment.start, segment.end) < boundaryMinSegmentLength)
        {
            return;
        }

        if (!IsSegmentAlreadyCovered(target, segment))
        {
            target.Add(segment);
        }
    }

    private bool IsSegmentAlreadyCovered(List<Segment2D> segments, Segment2D candidate)
    {
        foreach (Segment2D segment in segments)
        {
            bool sameDirection = Vector2.Distance(segment.start, candidate.start) <= endpointSnapDistance &&
                                 Vector2.Distance(segment.end, candidate.end) <= endpointSnapDistance;
            bool reversed = Vector2.Distance(segment.start, candidate.end) <= endpointSnapDistance &&
                            Vector2.Distance(segment.end, candidate.start) <= endpointSnapDistance;

            if (sameDirection || reversed)
            {
                return true;
            }
        }

        return false;
    }

    private void BuildConfirmedHorizontalBoxSlabsFromSegments(List<Segment2D> segments, float floorY, float ceilingY)
    {
        if (!TryGetBoundsFromSegments(segments, out Vector2 min, out Vector2 max))
        {
            return;
        }

        min -= Vector2.one * confirmedHorizontalSlabPadding;
        max += Vector2.one * confirmedHorizontalSlabPadding;

        Vector2 center = (min + max) * 0.5f;
        Vector2 size = max - min;

        CreateConfirmedHorizontalBoxSlab(
            "ConfirmedRoom_Floor",
            new Vector3(center.x, floorY - confirmedHorizontalSlabThickness * 0.5f, center.y),
            new Vector3(size.x, confirmedHorizontalSlabThickness, size.y),
            true
        );

        CreateConfirmedHorizontalBoxSlab(
            "ConfirmedRoom_Ceiling",
            new Vector3(center.x, ceilingY + confirmedHorizontalSlabThickness * 0.5f, center.y),
            new Vector3(size.x, confirmedHorizontalSlabThickness, size.y),
            false
        );
    }

    private bool TryGetBoundsFromSegments(List<Segment2D> segments, out Vector2 min, out Vector2 max)
    {
        min = new Vector2(float.MaxValue, float.MaxValue);
        max = new Vector2(float.MinValue, float.MinValue);

        if (segments == null || segments.Count == 0)
        {
            return false;
        }

        foreach (Segment2D segment in segments)
        {
            min.x = Mathf.Min(min.x, segment.start.x, segment.end.x);
            min.y = Mathf.Min(min.y, segment.start.y, segment.end.y);
            max.x = Mathf.Max(max.x, segment.start.x, segment.end.x);
            max.y = Mathf.Max(max.y, segment.start.y, segment.end.y);
        }

        return true;
    }

    private void CreateConfirmedHorizontalBoxSlab(string objectName, Vector3 position, Vector3 scale, bool isFloor)
    {
        GameObject slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
        slab.name = objectName;
        slab.transform.position = position;
        slab.transform.rotation = Quaternion.identity;
        slab.transform.localScale = scale;
        slab.transform.SetParent(confirmedRoomRoot, true);

        MeshRenderer renderer = slab.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = GetHorizontalMaterial();
        }

        BoxCollider boxCollider = slab.GetComponent<BoxCollider>();
        if (boxCollider == null)
        {
            slab.AddComponent<BoxCollider>();
        }

        if (isFloor)
        {
            confirmedRoomFloorObject = slab;
            EnsureEntranceSelectableSurface(slab);
        }
        else
        {
            confirmedRoomCeilingObject = slab;
        }
    }

    private void BuildConfirmedWallCubesFromSegments(List<Segment2D> segments, float floorY, float ceilingY)
    {
        foreach (Segment2D segment in segments)
        {
            CreateConfirmedWallCube(segment.start, segment.end, floorY, ceilingY);
        }
    }

    private void CreateConfirmedWallCube(Vector2 start, Vector2 end, float floorY, float ceilingY)
    {
        float length = Vector2.Distance(start, end);

        if (length < boundaryMinSegmentLength)
        {
            return;
        }

        float height = Mathf.Max(0.1f, ceilingY - floorY);
        Vector2 mid = (start + end) * 0.5f;
        Vector2 dir = (end - start).normalized;

        if (dir.sqrMagnitude < 0.0001f)
        {
            return;
        }

        float yaw = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;

        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "ConfirmedRoom_Wall";
        wall.transform.position = new Vector3(mid.x, floorY + height * 0.5f, mid.y);
        wall.transform.rotation = Quaternion.Euler(0.0f, yaw, 0.0f);
        wall.transform.localScale = new Vector3(confirmedWallThickness, height, length);
        wall.transform.SetParent(confirmedRoomRoot, true);

        MeshRenderer renderer = wall.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = GetWallMaterial();
        }

        BoxCollider boxCollider = wall.GetComponent<BoxCollider>();
        if (boxCollider == null)
        {
            wall.AddComponent<BoxCollider>();
        }

        confirmedRoomWallObjects.Add(wall);
    }

    private void CreateGapMarkers(List<Vector2> openEndpoints)
    {
        ClearGapMarkers();

        if (!showGapMarkers || openEndpoints == null || openEndpoints.Count == 0)
        {
            return;
        }

        EnsureRoots(transform);

        float y = 0.0f;

        if (scanner != null && scanner.currentRoom != null && !float.IsNaN(scanner.currentRoom.floorY))
        {
            y = scanner.currentRoom.floorY + gapMarkerYOffset;
        }
        else if (Camera.main != null)
        {
            y = Camera.main.transform.position.y - 1.2f;
        }

        float markerSize = Mathf.Max(0.02f, gapMarkerSize);

        for (int i = 0; i < openEndpoints.Count; i++)
        {
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = $"ConfirmRoom_OpenEndpoint_{i:00}";
            marker.transform.SetParent(gapMarkerRoot, false);
            marker.transform.position = new Vector3(openEndpoints[i].x, y, openEndpoints[i].y);
            marker.transform.localScale = Vector3.one * markerSize;

            Collider collider = marker.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }

            Renderer renderer = marker.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = GetGapMarkerMaterial();
            }
        }
    }

    public void ClearGapMarkers()
    {
        if (gapMarkerRoot == null)
        {
            return;
        }

        for (int i = gapMarkerRoot.childCount - 1; i >= 0; i--)
        {
            Destroy(gapMarkerRoot.GetChild(i).gameObject);
        }
    }

    private Material GetWallMaterial()
    {
        if (confirmedRoomWallMaterial != null)
        {
            return confirmedRoomWallMaterial;
        }

        if (runtimeWallMaterial == null)
        {
            runtimeWallMaterial = CreateRuntimeMaterial(
                "Runtime_ConfirmedRoom_Wall_Material",
                new Color(0.1f, 0.75f, 1.0f, 0.45f)
            );
        }

        return runtimeWallMaterial;
    }

    private Material GetHorizontalMaterial()
    {
        if (confirmedRoomHorizontalMaterial != null)
        {
            return confirmedRoomHorizontalMaterial;
        }

        if (runtimeHorizontalMaterial == null)
        {
            runtimeHorizontalMaterial = CreateRuntimeMaterial(
                "Runtime_ConfirmedRoom_Horizontal_Material",
                new Color(0.1f, 1.0f, 0.45f, 0.35f)
            );
        }

        return runtimeHorizontalMaterial;
    }

    private Material GetGapMarkerMaterial()
    {
        if (gapMarkerMaterial != null)
        {
            return gapMarkerMaterial;
        }

        if (runtimeGapMarkerMaterial == null)
        {
            runtimeGapMarkerMaterial = CreateRuntimeMaterial(
                "Runtime_ConfirmRoom_GapMarker_Material",
                new Color(1.0f, 0.12f, 0.05f, 0.85f)
            );
        }

        return runtimeGapMarkerMaterial;
    }

    private Material CreateRuntimeMaterial(string materialName, Color color)
    {
        Shader shader = FindSupportedShader();

        if (shader == null)
        {
            if (!warnedRuntimeMaterialShaderMissing)
            {
                warnedRuntimeMaterialShaderMissing = true;
                Debug.LogWarning("[ConfirmRoom] Runtime material shader is missing. Primitive default material will be used.");
            }

            return null;
        }

        Material material = new Material(shader)
        {
            name = materialName,
            color = color
        };

        return material;
    }

    private Shader FindSupportedShader()
    {
        string[] shaderNames =
        {
            "Graphics Tools/Standard",
            "Mixed Reality Toolkit/Standard",
            "Universal Render Pipeline/Unlit",
            "Universal Render Pipeline/Lit",
            "Unlit/Color",
            "Standard"
        };

        foreach (string shaderName in shaderNames)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader != null)
            {
                return shader;
            }
        }

        return null;
    }

    private static float ProjectPointToSegment01(Vector2 point, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float abSqr = ab.sqrMagnitude;

        if (abSqr < 0.0001f)
        {
            return 0.0f;
        }

        return Mathf.Clamp01(Vector2.Dot(point - a, ab) / abSqr);
    }

    private static void AddUniqueSplitT(List<float> splitTs, float t)
    {
        for (int i = 0; i < splitTs.Count; i++)
        {
            if (Mathf.Abs(splitTs[i] - t) < 0.001f)
            {
                return;
            }
        }

        splitTs.Add(t);
    }

    private static int Find(int[] parent, int index)
    {
        if (parent[index] != index)
        {
            parent[index] = Find(parent, parent[index]);
        }

        return parent[index];
    }

    private static void Union(int[] parent, int a, int b)
    {
        int rootA = Find(parent, a);
        int rootB = Find(parent, b);

        if (rootA != rootB)
        {
            parent[rootB] = rootA;
        }
    }

    private static float DistancePointToSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float abSqr = ab.sqrMagnitude;

        if (abSqr < 0.0001f)
        {
            return Vector2.Distance(point, a);
        }

        float t = Vector2.Dot(point - a, ab) / abSqr;
        t = Mathf.Clamp01(t);
        Vector2 projection = a + ab * t;
        return Vector2.Distance(point, projection);
    }

    private static bool SegmentsIntersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        float o1 = Orientation(a, b, c);
        float o2 = Orientation(a, b, d);
        float o3 = Orientation(c, d, a);
        float o4 = Orientation(c, d, b);

        if (o1 * o2 < 0.0f && o3 * o4 < 0.0f)
        {
            return true;
        }

        const float epsilon = 0.0001f;

        if (Mathf.Abs(o1) < epsilon && IsPointOnSegment(c, a, b)) return true;
        if (Mathf.Abs(o2) < epsilon && IsPointOnSegment(d, a, b)) return true;
        if (Mathf.Abs(o3) < epsilon && IsPointOnSegment(a, c, d)) return true;
        if (Mathf.Abs(o4) < epsilon && IsPointOnSegment(b, c, d)) return true;

        return false;
    }

    private static bool TryGetSegmentIntersectionParameters(
        Vector2 a,
        Vector2 b,
        Vector2 c,
        Vector2 d,
        out float ta,
        out float tb)
    {
        ta = 0.0f;
        tb = 0.0f;

        Vector2 r = b - a;
        Vector2 s = d - c;
        float denominator = Cross(r, s);

        if (Mathf.Abs(denominator) < 0.0001f)
        {
            return false;
        }

        Vector2 diff = c - a;
        ta = Cross(diff, s) / denominator;
        tb = Cross(diff, r) / denominator;

        return ta >= -0.0001f && ta <= 1.0001f && tb >= -0.0001f && tb <= 1.0001f;
    }

    private static float Cross(Vector2 a, Vector2 b)
    {
        return a.x * b.y - a.y * b.x;
    }

    private static float Orientation(Vector2 a, Vector2 b, Vector2 c)
    {
        return Cross(b - a, c - a);
    }

    private static bool IsPointOnSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        return point.x >= Mathf.Min(a.x, b.x) - 0.0001f &&
               point.x <= Mathf.Max(a.x, b.x) + 0.0001f &&
               point.y >= Mathf.Min(a.y, b.y) - 0.0001f &&
               point.y <= Mathf.Max(a.y, b.y) + 0.0001f;
    }
}

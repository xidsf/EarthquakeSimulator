using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.MixedReality.SceneUnderstanding;
using MixedReality.Toolkit.UX;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

public partial class SceneUnderstandingRoomScanner
{
    [Header("Simple Debug UI")]
    public TMP_Text debugText;

    [Header("Simple Debug UI - MRTK Buttons")]
    public PressableButton forceUpdateButton;
    public PressableButton autoUpdateToggleButton;
    public PressableButton lockToggleButton;
    public PressableButton toggleRawMeshButton;
    public PressableButton toggleWallSegmentButton;
    public PressableButton resetButton;

    [Header("Simple Debug UI - Options")]
    public bool captureUnityLogsInDebugText = true;
    public float debugTextRefreshInterval = 0.25f;
    public int maxDebugLogLines = 20;

    [Tooltip("화면 중앙 Debug UI에는 SceneObject 개수와 중요 로그만 표시합니다. SU의 실시간 상태/Room/View 값은 로그 보존을 위해 숨깁니다.")]
    public bool useCompactDebugText = true;

    [Tooltip("반복적으로 발생하는 SceneUnderstanding/Room 재구성 상태 로그를 화면 Log 큐에 넣지 않습니다. Workflow, ManualWall, FurnitureCapture 로그를 보기 위한 옵션입니다.")]
    public bool filterRealtimeSceneUnderstandingLogs = true;

    [Tooltip("Graphics Tools/Standard shader의 ProximityLight 개수 초과 경고를 화면 Log 큐에서 숨깁니다. 렌더링 품질 경고이며 방 생성 로직에는 영향이 없습니다.")]
    public bool filterProximityLightCountWarnings = true;

    private readonly StringBuilder debugBuilder = new StringBuilder();
    private readonly Queue<string> debugLogs = new Queue<string>();

    private Coroutine debugRefreshCoroutine;

    private UnityAction forceUpdateAction;
    private UnityAction autoUpdateToggleAction;
    private UnityAction lockToggleAction;
    private UnityAction toggleRawMeshAction;
    private UnityAction toggleWallSegmentAction;
    private UnityAction clearAction;
    private UnityAction resetAction;

    public float lastSceneRootY;
    public float lastFloorLocalY;
    public float lastCeilingWorldY;
    public float lastCeilingLocalY;
    public float lastEyeToCeiling;

    private void OnEnable()
    {
        CreateDebugUiActions();
        RegisterDebugButtons();

        if (captureUnityLogsInDebugText)
        {
            Application.logMessageReceived += OnUnityLogReceivedForDebugUi;
        }

        debugRefreshCoroutine = StartCoroutine(DebugUiRefreshLoop());
    }

    private void OnDisable()
    {
        UnregisterDebugButtons();

        if (captureUnityLogsInDebugText)
        {
            Application.logMessageReceived -= OnUnityLogReceivedForDebugUi;
        }

        if (debugRefreshCoroutine != null)
        {
            StopCoroutine(debugRefreshCoroutine);
            debugRefreshCoroutine = null;
        }
    }

    private void CreateDebugUiActions()
    {
        forceUpdateAction ??= DebugForceUpdate;
        autoUpdateToggleAction ??= DebugToggleAutoUpdate;
        lockToggleAction ??= DebugToggleLock;
        toggleRawMeshAction ??= DebugToggleRawMesh;
        toggleWallSegmentAction ??= DebugToggleWallSegment;
        clearAction ??= DebugClearVisuals;
        resetAction ??= DebugReset;
    }

    private void RegisterDebugButtons()
    {
        AddClick(forceUpdateButton, forceUpdateAction);
        AddClick(autoUpdateToggleButton, autoUpdateToggleAction);
        AddClick(lockToggleButton, lockToggleAction);
        AddClick(toggleRawMeshButton, toggleRawMeshAction);
        AddClick(toggleWallSegmentButton, toggleWallSegmentAction);
        AddClick(resetButton, resetAction);
    }

    private void UnregisterDebugButtons()
    {
        RemoveClick(forceUpdateButton, forceUpdateAction);
        RemoveClick(autoUpdateToggleButton, autoUpdateToggleAction);
        RemoveClick(lockToggleButton, lockToggleAction);
        RemoveClick(toggleRawMeshButton, toggleRawMeshAction);
        RemoveClick(toggleWallSegmentButton, toggleWallSegmentAction);
        RemoveClick(resetButton, resetAction);
    }

    private static void AddClick(PressableButton button, UnityAction action)
    {
        if (button == null || action == null)
        {
            return;
        }

        button.OnClicked.RemoveListener(action);
        button.OnClicked.AddListener(action);
    }

    private static void RemoveClick(PressableButton button, UnityAction action)
    {
        if (button == null || action == null)
        {
            return;
        }

        button.OnClicked.RemoveListener(action);
    }

    private IEnumerator DebugUiRefreshLoop()
    {
        while (true)
        {
            RefreshDebugText();
            yield return new WaitForSeconds(debugTextRefreshInterval);
        }
    }

    public void DebugForceUpdate()
    {
        AddDebugLog("force update");
        ForceUpdate();
        RefreshDebugText();
    }

    public void DebugToggleAutoUpdate()
    {
        if (isRunning)
        {
            AddDebugLog("auto update stop");
            StopScanning();
        }
        else
        {
            AddDebugLog("auto update start");
            StartScanning();
        }

        RefreshDebugText();
    }

    public void DebugToggleLock()
    {
        if (lockRoom)
        {
            AddDebugLog("unlock room");
            UnlockRoom();
        }
        else
        {
            AddDebugLog("lock room");
            LockRoom();
        }

        RefreshDebugText();
    }
    public void DebugToggleRawMesh()
    {
        showRawSceneMeshes = false;
        AddDebugLog("raw mesh output removed. reconstructed room only.");

        RebuildCurrentSceneVisualsForDebug();
        RefreshDebugText();
    }

    public void DebugToggleWallSegment()
    {
        showDetectedWallSegments = !showDetectedWallSegments;
        AddDebugLog($"wall segment: {showDetectedWallSegments}");

        RebuildCurrentSceneVisualsForDebug();
        RefreshDebugText();
    }

    public void DebugClearVisuals()
    {
        DestroyExistingGeneratedSceneChildren();

        AddDebugLog("clear visuals");
        RefreshDebugText();
    }

    public void DebugReset()
    {
        previousScene = null;
        currentScene = null;
        currentRoom = null;

        DebugClearVisuals();

        AddDebugLog("reset scene history");
        RefreshDebugText();
    }

    private void RebuildCurrentSceneVisualsForDebug()
    {
        if (currentScene == null || isComputing)
        {
            return;
        }

        ClearChildren();

        currentRoom = BuildRoomFromScene(currentScene);
        DisplayRoomBuildResult(currentRoom);
    }

    private void RefreshDebugText()
    {
        if (debugText == null)
        {
            return;
        }

        debugBuilder.Clear();

        if (useCompactDebugText)
        {
            AppendSceneObjectCounts();
            AppendRecentLogs();
            debugText.text = debugBuilder.ToString();
            return;
        }

        debugBuilder.AppendLine("=== Scene Understanding Test ===");
        debugBuilder.Append("supported: ");
        debugBuilder.AppendLine(SceneObserver.IsSupported().ToString());

        debugBuilder.Append("running: ");
        debugBuilder.AppendLine(isRunning.ToString());

        debugBuilder.Append("computing: ");
        debugBuilder.AppendLine(isComputing.ToString());

        debugBuilder.Append("locked: ");
        debugBuilder.AppendLine(lockRoom.ToString());

        debugBuilder.Append("scene: ");
        debugBuilder.AppendLine(currentScene != null ? "exists" : "none");

        debugBuilder.Append("aligned: ");
        debugBuilder.AppendLine(sceneRootAligned.ToString());

        debugBuilder.Append("align status: ");
        debugBuilder.AppendLine(sceneRootAlignStatus);

        float cameraY = Camera.main != null ? Camera.main.transform.position.y : 0.0f;
        float floorWorldY = currentRoom != null && sceneRoot != null
            ? sceneRoot.TransformPoint(new Vector3(0, currentRoom.floorY, 0)).y
            : 0.0f;

        float eyeToFloor = cameraY - floorWorldY;

        debugBuilder.AppendLine($"camera y: {cameraY:F2}");
        debugBuilder.AppendLine($"floor world y: {floorWorldY:F2}");
        debugBuilder.AppendLine($"eye-floor: {eyeToFloor:F2}");

        debugBuilder.AppendLine($"camera y: {lastCameraY:F2}");
        debugBuilder.AppendLine($"sceneRoot y: {lastSceneRootY:F2}");
        debugBuilder.AppendLine($"floor world y: {lastFloorWorldY:F2}");
        debugBuilder.AppendLine($"floor local y: {lastFloorLocalY:F2}");
        debugBuilder.AppendLine($"ceiling world y: {lastCeilingWorldY:F2}");
        debugBuilder.AppendLine($"ceiling local y: {lastCeilingLocalY:F2}");
        debugBuilder.AppendLine($"eye floor: {lastEyeToFloor:F2}");
        debugBuilder.AppendLine($"eye ceiling: {lastEyeToCeiling:F2}");

        debugBuilder.AppendLine();

        AppendSceneObjectCounts();
        AppendRoomResult();
        AppendViewOptions();
        AppendRecentLogs();

        debugText.text = debugBuilder.ToString();
    }

    private void AppendSceneObjectCounts()
    {
        debugBuilder.AppendLine("=== Scene Objects ===");

        if (currentScene == null)
        {
            debugBuilder.AppendLine("no scene");
            debugBuilder.AppendLine();
            return;
        }

        int wallCount = currentScene.SceneObjects.Count(o => o.Kind == SceneObjectKind.Wall);
        int floorCount = currentScene.SceneObjects.Count(o => o.Kind == SceneObjectKind.Floor);
        int ceilingCount = currentScene.SceneObjects.Count(o => o.Kind == SceneObjectKind.Ceiling);
        int platformCount = currentScene.SceneObjects.Count(o => o.Kind == SceneObjectKind.Platform);
        int worldCount = currentScene.SceneObjects.Count(o => o.Kind == SceneObjectKind.World);

        debugBuilder.AppendLine($"wall: {wallCount}");
        debugBuilder.AppendLine($"floor: {floorCount}");
        debugBuilder.AppendLine($"ceiling: {ceilingCount}");
        debugBuilder.AppendLine($"platform: {platformCount}");
        debugBuilder.AppendLine($"world: {worldCount}");
        debugBuilder.AppendLine();
    }

    private void AppendRoomResult()
    {
        debugBuilder.AppendLine("=== Room Result ===");

        if (currentRoom == null)
        {
            debugBuilder.AppendLine("no room result");
            debugBuilder.AppendLine();
            return;
        }

        float roomHeight = currentRoom.ceilingY - currentRoom.floorY;

        debugBuilder.AppendLine($"closed: {currentRoom.isClosedEnough}");
        debugBuilder.AppendLine($"detected walls: {currentRoom.detectedWalls.Count}");
        debugBuilder.AppendLine($"closure edges: {currentRoom.closureEdges.Count}");
        debugBuilder.AppendLine($"floor y: {currentRoom.floorY:F2}");
        debugBuilder.AppendLine($"ceiling y: {currentRoom.ceilingY:F2}");
        debugBuilder.AppendLine($"height: {roomHeight:F2}");
        debugBuilder.AppendLine();
    }

    private void AppendViewOptions()
    {
        debugBuilder.AppendLine("=== View ===");
        debugBuilder.AppendLine("raw mesh: removed");
        debugBuilder.AppendLine($"reconstructed floor: {showReconstructedFloor}");
        debugBuilder.AppendLine($"reconstructed ceiling: {showReconstructedCeiling}");
        debugBuilder.AppendLine($"wall segment: {showDetectedWallSegments}");
        debugBuilder.AppendLine($"closure edge: {showClosureEdges}");
        debugBuilder.AppendLine();
    }

    private void AppendRecentLogs()
    {
        debugBuilder.AppendLine("=== Log ===");

        foreach (string line in debugLogs)
        {
            debugBuilder.AppendLine(line);
        }
    }

    private void AddDebugLog(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        string line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        debugLogs.Enqueue(line);

        while (debugLogs.Count > maxDebugLogLines)
        {
            debugLogs.Dequeue();
        }
    }

    private void OnUnityLogReceivedForDebugUi(string condition, string stackTrace, LogType type)
    {
        if (!ShouldCaptureUnityLog(condition, type))
        {
            return;
        }

        switch (type)
        {
            case LogType.Warning:
                AddDebugLog($"WARN: {condition}");
                break;

            case LogType.Error:
            case LogType.Exception:
            case LogType.Assert:
                AddDebugLog($"ERROR: {condition}");
                break;

            default:
                AddDebugLog(condition);
                break;
        }
    }

    private bool ShouldCaptureUnityLog(string condition, LogType type)
    {
        if (!filterRealtimeSceneUnderstandingLogs)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(condition))
        {
            return false;
        }

        string log = condition.TrimStart();

        if (filterProximityLightCountWarnings &&
            type == LogType.Warning &&
            log.Contains("Max proximity light count") &&
            log.Contains("ProximityLight"))
        {
            return false;
        }

        // SceneUnderstandingRoomScanner가 주기적으로 출력하는 상태/요약 로그는
        // 이미 화면의 Scene Objects 섹션에서 확인 가능하므로 Log 큐에 넣지 않습니다.
        if (log.StartsWith("[SU]", StringComparison.Ordinal))
        {
            return false;
        }

        if (log.StartsWith("[Room] Walls:", StringComparison.Ordinal) ||
            log.StartsWith("[Room] Created", StringComparison.Ordinal) ||
            log.StartsWith("[Room] Failed to calculate floor or ceiling Y", StringComparison.Ordinal) ||
            log.StartsWith("[Room] Invalid floor/ceiling order", StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

}
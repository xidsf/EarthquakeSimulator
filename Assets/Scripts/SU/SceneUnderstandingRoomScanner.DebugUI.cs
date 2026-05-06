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
    public int maxDebugLogLines = 8;

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
        showRawSceneMeshes = !showRawSceneMeshes;
        AddDebugLog($"raw mesh: {showRawSceneMeshes}");

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
        if (rawRoot != null)
        {
            Destroy(rawRoot);
            rawRoot = null;
        }

        if (roomRoot != null)
        {
            Destroy(roomRoot);
            roomRoot = null;
        }

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

        if (showRawSceneMeshes)
        {
            DisplayRawSceneObjects(currentScene);
        }

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
        //AppendRecentLogs();

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
        debugBuilder.AppendLine($"raw mesh: {showRawSceneMeshes}");
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
        string line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        debugLogs.Enqueue(line);

        while (debugLogs.Count > maxDebugLogLines)
        {
            debugLogs.Dequeue();
        }
    }

    private void OnUnityLogReceivedForDebugUi(string condition, string stackTrace, LogType type)
    {
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
}
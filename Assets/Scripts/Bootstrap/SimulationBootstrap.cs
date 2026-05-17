using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SimulationBootstrap : MonoBehaviour
{
    [Header("Next Scene")]
    [SerializeField] private string nextSceneName = "MainSimulationScene";

    [Header("Default Config")]
    [SerializeField] private string defaultApiBaseUrl = "https://api.earquake.xyz";

    [Header("Debug Session Config")]
    [SerializeField]
    private string[] debugSessionIds =
    {
        "debug",
        "debug_session",
        "__debug__"
    };

    [SerializeField] private string defaultDebugInputFileName = "debug_session_input.json";

#if UNITY_EDITOR
    [Header("Editor Test Only")]
    [SerializeField] private string editorFallbackSessionId = "debug";
#endif

    private void Awake()
    {
        Dictionary<string, string> args = ParseCommandLineArgs();

        SimulationRunConfig.Clear();
        SimulationRunConfig.ApiBaseUrl = defaultApiBaseUrl.TrimEnd('/');
        SimulationRunConfig.DebugInputFileName = defaultDebugInputFileName;

        if (args.TryGetValue("--session-id", out string sessionId))
        {
            SimulationRunConfig.SessionId = sessionId;
        }

        if (args.TryGetValue("--api-base", out string apiBaseUrl))
        {
            SimulationRunConfig.ApiBaseUrl = apiBaseUrl.TrimEnd('/');
        }

        if (args.TryGetValue("--debug-file", out string debugFileName))
        {
            SimulationRunConfig.DebugInputFileName = SanitizeFileName(debugFileName);
        }

        if (args.TryGetValue("--input-dir", out string inputDir))
        {
            SimulationRunConfig.InputDir = inputDir;
        }

        if (args.TryGetValue("--output-dir", out string outputDir))
        {
            SimulationRunConfig.OutputDir = outputDir;
        }

        if (args.TryGetValue("--tha-csv", out string thaCsv))
        {
            SimulationRunConfig.ThaCsvPath = thaCsv;
        }

#if UNITY_EDITOR
        if (string.IsNullOrWhiteSpace(SimulationRunConfig.SessionId))
        {
            SimulationRunConfig.SessionId = editorFallbackSessionId;
            Debug.LogWarning($"No --session-id provided. Using editor fallback session id: {editorFallbackSessionId}");
        }
#endif

        if (string.IsNullOrWhiteSpace(SimulationRunConfig.SessionId))
        {
            Debug.LogError("Missing required command line argument: --session-id");
            QuitWithError();
            return;
        }

        if (IsDebugSessionId(SimulationRunConfig.SessionId))
        {
            SimulationRunConfig.IsDebugSession = true;

            Debug.LogWarning(
                $"[Bootstrap] Debug session enabled. " +
                $"session id: {SimulationRunConfig.SessionId}, " +
                $"debug file: {SimulationRunConfig.DebugInputFileName}"
            );
        }

        string sceneToLoad = ResolveSceneName(nextSceneName);

        Debug.Log($"[Bootstrap] Session ID: {SimulationRunConfig.SessionId}");
        Debug.Log($"[Bootstrap] API Base URL: {SimulationRunConfig.ApiBaseUrl}");
        Debug.Log($"[Bootstrap] Is Debug Session: {SimulationRunConfig.IsDebugSession}");
        Debug.Log($"[Bootstrap] Input Dir: {SimulationRunConfig.InputDir}");
        Debug.Log($"[Bootstrap] Output Dir: {SimulationRunConfig.OutputDir}");
        Debug.Log($"[Bootstrap] Loading scene: {sceneToLoad}");

        // 헤드리스 시뮬레이션 입출력 경로가 주어지면, 씬 로드 후 자동으로
        // HeadlessSimulationRunner를 생성하도록 예약한다(.unity 씬 수정 불필요).
        if (SimulationRunConfig.HasHeadlessSimulationPaths())
        {
            //HeadlessSimulationRunner.ScheduleAutoSpawn();
        }

        SceneManager.LoadScene(sceneToLoad);
    }

    // BootstrapScene 인스펙터에 잘못된 씬명이 저장돼 있어도 빌드에 포함된
    // 실제 시뮬레이션 씬으로 폴백한다.
    private string ResolveSceneName(string preferred)
    {
        if (!string.IsNullOrWhiteSpace(preferred) && Application.CanStreamedLevelBeLoaded(preferred))
        {
            return preferred;
        }

        string[] fallbacks = { "SimulationScene", "MainSimulationScene" };
        foreach (string candidate in fallbacks)
        {
            if (Application.CanStreamedLevelBeLoaded(candidate))
            {
                if (!string.Equals(candidate, preferred))
                {
                    Debug.LogWarning($"[Bootstrap] Scene '{preferred}' not loadable. Falling back to '{candidate}'.");
                }
                return candidate;
            }
        }

        Debug.LogError($"[Bootstrap] No loadable simulation scene found (tried '{preferred}', SimulationScene, MainSimulationScene). Check Build Settings.");
        return preferred;
    }

    private bool IsDebugSessionId(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return false;

        foreach (string debugId in debugSessionIds)
        {
            if (string.Equals(sessionId, debugId, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private Dictionary<string, string> ParseCommandLineArgs()
    {
        string[] rawArgs = Environment.GetCommandLineArgs();
        Dictionary<string, string> result = new Dictionary<string, string>();

        for (int i = 0; i < rawArgs.Length; i++)
        {
            string current = rawArgs[i];

            if (!current.StartsWith("--"))
                continue;

            if (current.Contains("="))
            {
                string[] split = current.Split(new[] { '=' }, 2);

                if (split.Length == 2)
                {
                    string key = split[0];
                    string value = split[1];

                    result[key] = value;
                }

                continue;
            }

            if (i + 1 < rawArgs.Length && !rawArgs[i + 1].StartsWith("-"))
            {
                result[current] = rawArgs[i + 1];
                i++;
            }
            else
            {
                result[current] = "true";
            }
        }

        return result;
    }

    private string SanitizeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return defaultDebugInputFileName;

        string onlyFileName = Path.GetFileName(fileName);

        if (string.IsNullOrWhiteSpace(onlyFileName))
            return defaultDebugInputFileName;

        return onlyFileName;
    }

    private void QuitWithError()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit(1);
#endif
    }
}
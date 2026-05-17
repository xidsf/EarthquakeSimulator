public static class SimulationRunConfig
{
    public static string SessionId;
    public static string ApiBaseUrl = "https://api.earquake.xyz";

    public static bool IsDebugSession;
    public static string DebugInputFileName = "debug_session_input.json";

    // 헤드리스 시뮬레이션 입출력 경로 (커맨드라인 --input-dir / --output-dir).
    // InputDir 안에 simulation_input.json + 가구 GLB가 존재한다고 가정합니다.
    public static string InputDir;
    public static string OutputDir;

    // 선택: THA CSV 경로 (--tha-csv). 비어 있으면 EarthquakeSimulator 기본 경로를 사용합니다.
    public static string ThaCsvPath;

    public static bool HasSessionId()
    {
        return !string.IsNullOrWhiteSpace(SessionId);
    }

    public static bool HasHeadlessSimulationPaths()
    {
        return !string.IsNullOrWhiteSpace(InputDir) && !string.IsNullOrWhiteSpace(OutputDir);
    }

    public static void Clear()
    {
        SessionId = null;
        ApiBaseUrl = "https://api.earquake.xyz";

        IsDebugSession = false;
        DebugInputFileName = "debug_session_input.json";

        InputDir = null;
        OutputDir = null;
        ThaCsvPath = null;
    }
}

public static class SimulationRunConfig
{
    public static string SessionId;
    public static string ApiBaseUrl = "https://api.earquake.xyz";

    public static bool IsDebugSession;
    public static string DebugInputFileName = "debug_session_input.json";

    public static bool HasSessionId()
    {
        return !string.IsNullOrWhiteSpace(SessionId);
    }

    public static void Clear()
    {
        SessionId = null;
        ApiBaseUrl = "https://api.earquake.xyz";

        IsDebugSession = false;
        DebugInputFileName = "debug_session_input.json";
    }
}
using System.Text.Json;
using EcoSim.Core.Data;

namespace EcoSim.Core.Sim;

/// <summary>Append-only NDJSON logger for agent debug sessions.</summary>
public static class DebugSessionLog
{
    private static readonly object Lock = new();
    private static string? _logPath;

    public static void Write(string hypothesisId, string location, string message, object data, string runId = "pre-fix")
    {
        try
        {
            string path = ResolveLogPath();
            var payload = new Dictionary<string, object?>
            {
                ["sessionId"] = "cef561",
                ["hypothesisId"] = hypothesisId,
                ["location"] = location,
                ["message"] = message,
                ["data"] = data,
                ["runId"] = runId,
                ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            };
            string line = JsonSerializer.Serialize(payload);
            lock (Lock)
            {
                File.AppendAllText(path, line + "\n");
            }
        }
        catch
        {
            // ignore logging failures
        }
    }

    public static bool ShouldSample(int creatureId, double tGlobal, double intervalSec = 2.0)
    {
        int bucket = (int)Math.Floor(tGlobal / intervalSec);
        return bucket % 3 == creatureId % 3;
    }

    private static string ResolveLogPath()
    {
        if (_logPath != null) return _logPath;
        try
        {
            _logPath = Path.Combine(DataPaths.RepoRoot, "debug-cef561.log");
            return _logPath;
        }
        catch
        {
            _logPath = Path.Combine(AppContext.BaseDirectory, "debug-cef561.log");
            return _logPath;
        }
    }
}

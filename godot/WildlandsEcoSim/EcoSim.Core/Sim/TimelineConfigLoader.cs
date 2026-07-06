using System.Text.Json;
using EcoSim.Core.Data;

namespace EcoSim.Core.Sim;

public sealed class TimelineConfig
{
    public double SnapshotIntervalSec { get; set; } = 1;
    public double HeartbeatIntervalSec { get; set; } = 5;
}

public static class TimelineConfigLoader
{
    public static TimelineConfig Load(string? repoRoot = null)
    {
        string root = repoRoot ?? DataPaths.RepoRoot;
        string path = Path.Combine(root, "config", "timeline-config.json");
        if (!File.Exists(path))
        {
            return new TimelineConfig();
        }

        try
        {
            string json = File.ReadAllText(path);
            var cfg = JsonSerializer.Deserialize<TimelineConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
            return cfg ?? new TimelineConfig();
        }
        catch
        {
            return new TimelineConfig();
        }
    }
}

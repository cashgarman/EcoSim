using System.Text.Json;
using System.Text.Json.Nodes;
using EcoSim.Core.Behavior;
using EcoSim.Core.Data;
using EcoSim.Core.Sim;

namespace EcoSim.Core.Batch;

public sealed class BalanceOverridesFile
{
    public Dictionary<string, Dictionary<string, object>>? SpeciesOverrides { get; set; }
    public Dictionary<string, JsonObject>? BehaviorLibraryOverrides { get; set; }
    public Dictionary<string, JsonObject>? BehaviorSpeciesOverrides { get; set; }
}

public static class BalanceConfigLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static BalanceOverridesFile LoadFromFile(string path)
    {
        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<BalanceOverridesFile>(json, JsonOptions)
            ?? new BalanceOverridesFile();
    }

    public static void Apply(SimSession session, BalanceOverridesFile overrides)
    {
        if (overrides.SpeciesOverrides is { Count: > 0 })
        {
            session.Species.ApplyOverrides(overrides.SpeciesOverrides);
        }

        var beh = new BehaviorOverrides();
        if (overrides.BehaviorLibraryOverrides != null)
        {
            foreach (var (key, val) in overrides.BehaviorLibraryOverrides)
            {
                beh.Library[key] = val;
            }
        }
        if (overrides.BehaviorSpeciesOverrides != null)
        {
            foreach (var (key, val) in overrides.BehaviorSpeciesOverrides)
            {
                beh.Species[key] = val;
            }
        }

        if (beh.Library.Count > 0 || beh.Species.Count > 0)
        {
            session.Behaviors.SetOverrides(beh);
            session.Behaviors.RecompileAll(session.Species);
        }
    }
}

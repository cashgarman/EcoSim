using EcoSim.Core;
using EcoSim.Core.Behavior;
using EcoSim.Core.Data;
using Godot;

namespace WildlandsEcoSim;

/// <summary>Autoload — loads species/behavior data at boot (Phase 0 foundation).</summary>
public partial class EcoSimHost : Node
{
    public SpeciesCatalog Species { get; private set; } = null!;
    public BehaviorLibrary Behaviors { get; private set; } = null!;

    public override void _Ready()
    {
        string dataRoot = ResolveDataRoot();
        DataPaths.SetDataRoot(dataRoot);
        (Species, Behaviors) = EcoSimBootstrap.LoadBaseData(dataRoot);
        GD.Print($"EcoSim loaded {Species.SpeciesKeys.Count} species from {dataRoot}");
    }

    private static string ResolveDataRoot()
    {
        string resData = ProjectSettings.GlobalizePath("res://data");
        if (Directory.Exists(resData))
        {
            string? dir = Directory.GetParent(resData)?.FullName;
            while (!string.IsNullOrEmpty(dir))
            {
                if (File.Exists(Path.Combine(dir, "data", "species.json")))
                {
                    return dir;
                }

                dir = Directory.GetParent(dir)?.FullName;
            }
        }

        string? walk = OS.GetExecutablePath().GetBaseDir();
        for (int i = 0; i < 8 && !string.IsNullOrEmpty(walk); i++)
        {
            if (File.Exists(Path.Combine(walk, "data", "species.json")))
            {
                return walk;
            }

            walk = Directory.GetParent(walk)?.FullName;
        }

        throw new InvalidOperationException("Could not resolve EcoSim data directory");
    }
}

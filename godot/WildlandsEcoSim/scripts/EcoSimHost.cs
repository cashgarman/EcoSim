using EcoSim.Core;
using EcoSim.Core.Data;
using EcoSim.Core.Rng;
using EcoSim.Core.Sim;
using Godot;

namespace WildlandsEcoSim;

/// <summary>Autoload — owns SimSession and shared data catalogs.</summary>
public partial class EcoSimHost : Node
{
    public SpeciesCatalog? Species { get; private set; }
    public EcoSim.Core.Behavior.BehaviorLibrary? Behaviors { get; private set; }
    private bool _bootstrapped;
    public SimSession? Session { get; private set; }

    public bool HasWorld => Session?.State.Ready == true && Session.State.W > 0;

    public override void _Ready()
    {
        if (Engine.IsEditorHint()) return;
        BootstrapIfNeeded();
        GD.Print($"EcoSim loaded {Species!.SpeciesKeys.Count} species");
    }

    private void BootstrapIfNeeded()
    {
        if (_bootstrapped) return;
        string dataRoot = ResolveDataRoot();
        DataPaths.SetDataRoot(dataRoot);
        (Species, Behaviors) = EcoSimBootstrap.LoadBaseData(dataRoot);
        _bootstrapped = true;
    }

    public SimSession EnsureSession(uint seed = 1)
    {
        if (Session != null) return Session;
        BootstrapIfNeeded();
        Session = SimSession.Create(ResolveDataRoot(), seed);
        Session.State.Speed = 1;
        return Session;
    }

    public int GenerateWorld(string size = "s", uint seed = 1)
    {
        var session = EnsureSession(seed);
        session.State.Seed = seed;
        GlobalRng.SetSeed(seed);
        session.State.Cfg = new WorldGenConfig
        {
            Size = size,
            Sea = 0.46,
            Temp = 0.5,
            Moist = 0.5,
            Relief = 0.6,
            Animals = 0.45,
        };
        int pop = session.GenerateWorld();
        GD.Print($"World generated {session.State.W}x{session.State.H}, pop={pop}, seed={seed}");
        return pop;
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

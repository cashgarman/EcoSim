using EcoSim.Core.Behavior;
using EcoSim.Core.Data;

namespace EcoSim.Core.Sim;

public sealed class SimSession
{
    public SimState State { get; }
    public SpeciesCatalog Species { get; }
    public BehaviorLibrary Behaviors { get; }
    public CreatureSystem Creatures { get; }
    public WorldGenerator World { get; }
    public BehaviorTree BehaviorTree { get; }
    public Simulation Simulation { get; }
    public LifeStory LifeStory { get; } = new();
    public SpeciesStatsTracker SpeciesStats { get; } = new();
    public PlayerProgress Progress { get; } = new();
    public PlayerControlSystem Player { get; }
    public EvolutionSystem Evolutions { get; }

    private SimSession(SimState state, SpeciesCatalog species, BehaviorLibrary behaviors)
    {
        State = state;
        Species = species;
        Behaviors = behaviors;
        BehaviorTree = new BehaviorTree(state, species);
        Creatures = new CreatureSystem(state, species, BehaviorTree);
        Creatures.LifeStory = LifeStory;
        Creatures.SpeciesStats = SpeciesStats;
        Player = new PlayerControlSystem(state, species, Creatures, Progress);
        Creatures.PlayerControl = Player;
        Evolutions = new EvolutionSystem(state, species, behaviors, EvolutionCatalog.Load(species), Progress);
        World = new WorldGenerator(state);
        Simulation = new Simulation(state, species, Creatures, World);
    }

    public static SimSession Create(string? dataRoot = null, uint? seed = null)
    {
        if (dataRoot != null) DataPaths.SetDataRoot(dataRoot);
        var (species, behaviors) = EcoSimBootstrap.LoadBaseData(dataRoot);
        var state = new SimState();
        if (seed.HasValue) state.Seed = seed.Value;
        return new SimSession(state, species, behaviors);
    }

    public int GenerateWorld(WorldGenConfig? cfg = null)
    {
        State.Creatures.Clear();
        State.Grid.Clear();
        State.NextId = 1;
        State.GenerationMax = 1;
        State.TGlobal = 0;
        State.Day = 0;
        State.TimeOfDay = 0.3;
        State.MigrantTimer = 0;
        State.Ready = true;
        if (cfg != null) State.Cfg = cfg;
        SpeciesStats.Init(Species);
        World.Generate();
        Creatures.StockLife();
        return Creatures.AliveCount();
    }
}

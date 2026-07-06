using EcoSim.Core.Data;
using EcoSim.Core.Numerics;
using EcoSim.Core.Rng;
using EcoSim.Core.Behavior;

namespace EcoSim.Core.Sim;

public sealed class Simulation
{
    private readonly SimState _state;
    private readonly SpeciesCatalog _catalog;
    private readonly CreatureSystem _creatures;
    private readonly WorldGenerator _world;

    public Simulation(SimState state, SpeciesCatalog catalog, CreatureSystem creatures, WorldGenerator world)
    {
        _state = state;
        _catalog = catalog;
        _creatures = creatures;
        _world = world;
    }

    public void UpdateDayNight()
    {
        _state.LightLevel = SimMath.LightLevelFromTimeOfDay(_state.TimeOfDay);
        _state.IsNight = _state.LightLevel < 0.28;
    }

    public void Tick(double dt)
    {
        _state.TimeOfDay = (_state.TimeOfDay + dt / SimConstants.SimDaySeconds) % 1.0;
        UpdateDayNight();
        _state.Day = (int)Math.Floor(_state.TGlobal / SimConstants.SimDaySeconds);

        _creatures.RebuildGrid();
        int creatureCount = _state.Creatures.Count;
        for (int i = 0; i < creatureCount; i++)
        {
            var c = _state.Creatures[i];
            if (!c.Dead) _creatures.StepCreature(c, dt);
        }

        _world.GrowVegetation(dt);
        RunMigrantPulse(dt);
        _creatures.PruneDead();
    }

    public void RunMigrantPulse(double dt)
    {
        if (_state.BatchMode)
        {
            if (_state.BatchConfig?.AutoMigration != true) return;
        }
        else if (!_state.AutoMigrationEnabled) return;

        _state.MigrantTimer += dt;
        if (_state.MigrantTimer <= 6) return;
        _state.MigrantTimer = 0;

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string sp in _catalog.SpeciesKeys) counts[sp] = 0;
        int aliveTotal = 0;
        foreach (var c in _state.Creatures)
        {
            if (c.Dead) continue;
            counts[c.Sp] = counts.GetValueOrDefault(c.Sp) + 1;
            aliveTotal++;
        }

        foreach (string sp in _catalog.SpeciesKeys)
        {
            var def = _catalog.Get(sp);
            bool isPredator = def.Diet >= 1;
            bool preyAround = def.Hunts == null || def.Hunts.Any(p => counts.GetValueOrDefault(p) > 2);
            if (counts.GetValueOrDefault(sp) <= 1
                && aliveTotal < SimConstants.MaxPop * 0.7
                && GlobalRng.Next() < (isPredator ? 0.25 : 0.6)
                && (!isPredator || preyAround))
            {
                int n = isPredator ? 1 : GlobalRng.Ri(2, 3);
                for (int i = 0; i < n; i++)
                {
                    var t = _creatures.FindSpawnTile(sp);
                    if (!t.HasValue) continue;
                    var c = _creatures.MakeCreature(sp, t.Value.X, t.Value.Y);
                    c.Hunger = 85;
                    c.Thirst = 85;
                }
            }
        }
    }
}

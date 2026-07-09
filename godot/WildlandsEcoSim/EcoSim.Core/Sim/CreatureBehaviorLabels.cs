using EcoSim.Core.Numerics;

namespace EcoSim.Core.Sim;

/// <summary>Player-facing behavior labels derived from sim state + creature position.</summary>
public static class CreatureBehaviorLabels
{
    public static bool IsActivelyGrazing(Creature creature, SimState state)
    {
        int ti = TileIndex(creature, state);
        return GrazeFood.IsEdible(state, ti);
    }

    public static string GetDisplayLabel(Creature creature, SimState state)
    {
        return creature.State switch
        {
            "graze" => IsActivelyGrazing(creature, state) ? "Grazing" : "Searching for food",
            "wander" => "Wandering",
            "flee" => "Fleeing!",
            "thirst" => "Seeking water",
            "hunt" => "Hunting",
            "huntSearch" => "Stalking",
            "mate" => "Mating",
            "rest" => "Resting",
            _ => creature.State,
        };
    }

    public static string GetTooltipCacheKey(Creature creature, SimState state)
        => $"{creature.Id}:{creature.State}:{GetGrazePhase(creature, state)}";

    private static string GetGrazePhase(Creature creature, SimState state)
    {
        if (creature.State != "graze") return "";
        return IsActivelyGrazing(creature, state) ? "eat" : "search";
    }

    private static int TileIndex(Creature creature, SimState state)
    {
        int tx = (int)SimMath.Clamp(Math.Round(creature.X), 0, state.W - 1);
        int ty = (int)SimMath.Clamp(Math.Round(creature.Y), 0, state.H - 1);
        return GridHelpers.Idx(state, tx, ty);
    }
}

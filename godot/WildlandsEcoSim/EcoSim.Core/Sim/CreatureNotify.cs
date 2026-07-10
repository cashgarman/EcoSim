using EcoSim.Core.Data;

namespace EcoSim.Core.Sim;

public static class CreatureNotify
{
    public static string RefineDeathCause(Creature c)
    {
        if (c.Cause != "exhaustion") return c.Cause;
        if (c.Age >= c.Genome.Lifespan) return "old age";
        if (c.Hunger <= 0 && c.Thirst <= 0) return "starvation and dehydration";
        if (c.Hunger <= 0) return "starvation";
        if (c.Thirst <= 0) return "dehydration";
        return c.Cause;
    }

    public static string DeathCausePhrase(string cause) => cause switch
    {
        "predation" => "was killed by a predator",
        "starvation" => "starved",
        "dehydration" => "died of thirst",
        "starvation and dehydration" => "starved and died of thirst",
        "old age" => "died of old age",
        "exhaustion" => "succumbed to exhaustion",
        "meteor" => "was killed by a meteor",
        "removed" => "was removed",
        _ => $"died ({cause})",
    };

    public static string SexSymbol(string sex) => sex == "male" ? "♂" : "♀";

    public static string FormatFollowedDeathMessage(SpeciesCatalog catalog, Creature c, SimState state)
    {
        var def = catalog.Get(c.Sp);
        string sex = SexSymbol(c.Sex);
        string cause = RefineDeathCause(c);
        int? killerId = InferKillerId(c, state);
        if (killerId != null)
        {
            var killer = state.Creatures.FirstOrDefault(x => x.Id == killerId);
            if (killer != null)
            {
                var kdef = catalog.Get(killer.Sp);
                return $"{def.Emoji} {def.Label} {sex} (#{c.Id}) was preyed on by {kdef.Emoji} {kdef.Label} #{killer.Id}.";
            }
        }

        if (cause == "predation")
        {
            return $"{def.Emoji} {def.Label} {sex} (#{c.Id}) was killed by a predator.";
        }

        return $"{def.Emoji} {def.Label} {sex} (#{c.Id}) {DeathCausePhrase(cause)}.";
    }

    public static int? InferKillerId(Creature prey, SimState state)
    {
        if (prey.KilledById != null) return prey.KilledById;

        foreach (var c in state.Creatures)
        {
            if (c.Dead || c.Target != prey.Id) continue;
            if (c.State is "hunt" or "huntSearch") return c.Id;
        }

        if (prey.LifeStory != null)
        {
            var preyEvent = prey.LifeStory.Events.LastOrDefault(e => e.Kind == "preyedOn");
            if (preyEvent?.Detail != null && int.TryParse(preyEvent.Detail, out int killerId))
            {
                return killerId;
            }
        }

        return null;
    }

    public static string FormatDiedEvent(SpeciesCatalog catalog, Creature c, int? killerId, SimState state)
    {
        string cause = RefineDeathCause(c);
        var def = catalog.Get(c.Sp);
        if (killerId != null)
        {
            var killer = state.Creatures.FirstOrDefault(x => x.Id == killerId);
            if (killer != null)
            {
                var kdef = catalog.Get(killer.Sp);
                return $"Day {state.Day}: {def.Emoji} {def.Label} #{c.Id} was preyed on by {kdef.Emoji} {kdef.Label} #{killer.Id}";
            }
        }

        return $"Day {state.Day}: {def.Emoji} {def.Label} #{c.Id} died ({cause})";
    }

    public static string FormatBornEvent(SpeciesCatalog catalog, Creature c, SimState state)
    {
        var def = catalog.Get(c.Sp);
        string sex = c.Sex == "male" ? "♂" : "♀";
        return $"Day {state.Day}: {def.Emoji} {def.Label} #{c.Id} {sex} was born";
    }
}

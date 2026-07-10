using EcoSim.Core.Data;
using EcoSim.Core.Numerics;
using EcoSim.Core.Rng;

namespace EcoSim.Core.Sim;

/// <summary>
/// Shared creature action effects, callable from both the behavior-tree executor
/// and the player control system so AI and player use identical rules.
/// </summary>
public static class CreatureActions
{
    public static bool TryDrink(SimState state, Creature c, bool canSwim, double dt)
    {
        if (!Navigation.CanDrinkHere(state, c.X, c.Y, canSwim)) return false;
        Drink(c, dt);
        return true;
    }

    public static void Drink(Creature c, double dt)
    {
        c.Thirst = Math.Min(100, c.Thirst + 60 * dt);
    }

    public static bool TryGrazeBite(SimState state, Creature c, double dt)
    {
        int ti = GridHelpers.Idx(state,
            (int)SimMath.Clamp(Math.Round(c.X), 0, state.W - 1),
            (int)SimMath.Clamp(Math.Round(c.Y), 0, state.H - 1));
        if (!GrazeFood.IsEdible(state, ti)) return false;
        double bite = Math.Min(state.Veg[ti], 3.5 * dt);
        state.Veg[ti] -= (float)bite;
        c.Hunger = Math.Min(100, c.Hunger + bite * 26);
        state.VegDirty = true;
        return true;
    }

    public static void Rest(Creature c, double dt)
    {
        c.Energy = Math.Min(100, c.Energy + 9 * dt);
        c.Vx *= 0.8;
        c.Vy *= 0.8;
    }

    /// <summary>
    /// Attempts one hunt strike against prey: requires being within strike range and
    /// passing the aggression-based strike roll. Records the attacker on the prey so
    /// death transfer / notices know who the killer was.
    /// </summary>
    public static bool TryHuntStrike(CreatureSystem creatures, Creature hunter, Creature prey)
    {
        if (prey.Dead) return false;
        double pdist = SimMath.Hypot(prey.X - hunter.X, prey.Y - hunter.Y);
        double strikeR = creatures.HuntStrikeRange(hunter, prey);
        if (pdist >= strikeR) return false;
        if (GlobalRng.Next() >= creatures.HuntStrikeChance(hunter)) return false;

        prey.Hp -= 30 + hunter.Genome.Size * 15;
        prey.Cause = "predation";
        prey.KilledById = hunter.Id;
        if (prey.Hp <= 0)
        {
            creatures.Die(prey, "predation");
            hunter.Hunger = Math.Min(100, hunter.Hunger + 50);
            hunter.Energy = Math.Min(100, hunter.Energy + 12);
        }
        return true;
    }

    /// <summary>
    /// Attempts to consummate mating between two creatures standing within 1.0 tile.
    /// Either argument order works; the female becomes pregnant.
    /// </summary>
    public static bool TryConsummateMate(SpeciesCatalog catalog, Creature a, Creature b)
    {
        if (a.Dead || b.Dead) return false;
        if (SimMath.Hypot(b.X - a.X, b.Y - a.Y) >= 1.0) return false;

        var female = a.Sex == "female" ? a : b;
        var male = a.Sex == "male" ? a : b;
        if (female.Sex != "female" || male.Sex != "male") return false;
        if (female.Pregnant > 0 || female.MateCd > 0 || male.MateCd > 0) return false;

        female.Pregnant = catalog.SampleGestation(female.Sp);
        female.MatePartner = male.Genome;
        female.MatePartnerId = male.Id;
        female.LitterQ = Math.Max(1, (int)Math.Round(female.Genome.Litter * GlobalRng.Rf(0.7, 1.15)));
        double cd = catalog.SampleMateCooldown(female.Sp);
        female.MateCd = cd;
        male.MateCd = cd * 0.6;
        female.Energy -= 20;
        male.Energy -= 12;
        return true;
    }
}

using System.Text.Json.Nodes;
using EcoSim.Core.Data;
using EcoSim.Core.Sim;

namespace EcoSim.Core.Behavior;

public sealed class BehaviorContext
{
    public required Creature Creature { get; init; }
    public required SpeciesDefinition Species { get; init; }
    public required Dictionary<string, double> Thresholds { get; init; }
    public Creature? Threat { get; init; }
    public double Tdist { get; init; }
    public Creature? Prey { get; init; }
    public double Pdist { get; init; }
    public Creature? Mate { get; init; }
    public double Mdist { get; init; }
    public double SenseR { get; init; }
    public bool CanSwim { get; init; }
    public bool IsNight { get; init; }
}

public static class BehaviorContextBuilder
{
    public static BehaviorContext Build(Creature creature, CreatureSystem creatures, SimState state, SpeciesCatalog catalog)
    {
        var S = catalog.Get(creature.Sp);
        double senseR = creature.Genome.Sense;
        double senseR2 = senseR * senseR;
        var pos = creatures.SimPos(creature);
        var neigh = creatures.Nearby(creature, senseR);
        uint preyMask = S.PreyMask;
        uint huntsMask = S.HuntsMask;
        double selfSize = creatures.ESize(creature);
        bool selfAdult = creatures.IsAdult(creature);

        Creature? threat = null, prey = null, mate = null;
        double tdist2 = 1e18, pdist2 = 1e18, mdist2 = 1e18;

        foreach (var o in neigh)
        {
            var op = creatures.SimPos(o);
            double ddx = op.X - pos.X, ddy = op.Y - pos.Y;
            double d2 = ddx * ddx + ddy * ddy;
            if (d2 >= senseR2) continue;

            if (!catalog.SpeciesIndex.TryGetValue(o.Sp, out int osp)) continue;
            uint bit = osp >= 0 && osp < 30 ? 1u << osp : 0;

            if ((preyMask & bit) != 0)
            {
                double oSize = creatures.ESize(o);
                if (oSize > selfSize * 0.7 && d2 < tdist2) { tdist2 = d2; threat = o; }
            }
            if ((huntsMask & bit) != 0)
            {
                double oSize = creatures.ESize(o);
                if (oSize < selfSize * 1.25 && d2 < pdist2) { pdist2 = d2; prey = o; }
            }
            if (o.Sp == creature.Sp && o.Sex != creature.Sex && creatures.IsAdult(o) && selfAdult
                && o.MateCd <= 0 && creature.MateCd <= 0
                && o.Pregnant <= 0 && creature.Pregnant <= 0 && o.Energy > 45 && creature.Energy > 45)
            {
                if (d2 < mdist2) { mdist2 = d2; mate = o; }
            }
        }

        return new BehaviorContext
        {
            Creature = creature,
            Species = S,
            Thresholds = S.BehaviorConfig?.Thresholds ?? new Dictionary<string, double>(),
            Threat = threat,
            Tdist = tdist2 < 1e18 ? Math.Sqrt(tdist2) : 1e9,
            Prey = prey,
            Pdist = pdist2 < 1e18 ? Math.Sqrt(pdist2) : 1e9,
            Mate = mate,
            Mdist = mdist2 < 1e18 ? Math.Sqrt(mdist2) : 1e9,
            SenseR = senseR,
            CanSwim = SpeciesCatalog.SpeciesCanSwim(S),
            IsNight = state.IsNight,
        };
    }

    public static double Threshold(BehaviorContext ctx, string key, double fallback = 0)
    {
        return ctx.Thresholds.TryGetValue(key, out var v) ? v : fallback;
    }
}

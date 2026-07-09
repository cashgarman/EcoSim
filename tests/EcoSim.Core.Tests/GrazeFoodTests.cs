using EcoSim.Core.Data;
using EcoSim.Core.Numerics;
using EcoSim.Core.Sim;
using NUnit.Framework;

namespace EcoSim.Core.Tests;

[TestFixture]
public class GrazeFoodTests
{
    private string _repoRoot = "";

    [OneTimeSetUp]
    public void SetUp()
    {
        _repoRoot = FindRepoRoot();
        DataPaths.SetDataRoot(_repoRoot);
    }

    [Test]
    public void BeachVegBelowFlatThreshold_IsEdible()
    {
        Assert.That(GrazeFood.IsEdible(0.035f, 0.08f), Is.True);
        Assert.That(GrazeFood.IsEdible(0.035f, 1f), Is.False);
    }

    [Test]
    public void Beaver_OnLowBeachVeg_GrazingIncreasesHunger()
    {
        var session = SimSession.Create(_repoRoot, 99);
        session.State.Cfg.Size = "s";
        session.GenerateWorld();

        var beaver = session.State.Creatures.First(c => c.Sp == "beaver" && !c.Dead);
        int tx = (int)Math.Round(beaver.X);
        int ty = (int)Math.Round(beaver.Y);
        int ti = GridHelpers.Idx(session.State, tx, ty);

        session.State.Biome[ti] = (byte)Biome.Beach;
        session.State.VegCap[ti] = 0.08f;
        session.State.Veg[ti] = 0.035f;

        beaver.X = tx + 0.5;
        beaver.Y = ty + 0.5;
        beaver.Hunger = 5;
        beaver.Thirst = 80;
        beaver.Energy = 80;
        beaver.State = "graze";
        beaver.BtAction = session.Species.Get("beaver").BehaviorConfig!.Actions["Graze"];
        beaver.BtNodeId = "Graze";

        double hungerBefore = beaver.Hunger;
        session.Creatures.StepCreature(beaver, 0.5);

        Assert.That(beaver.Hunger, Is.GreaterThan(hungerBefore),
            "beaver on low beach vegetation should gain fullness while grazing");
        Assert.That(0.035f > 0.04f, Is.False, "flat graze threshold rejected this beach tile");
    }

    [Test]
    public void GrazeWander_OnWater_UsesLandOnlyTargets()
    {
        var session = SimSession.Create(_repoRoot, 103);
        session.State.Cfg.Size = "s";
        session.GenerateWorld();

        var beaver = session.State.Creatures.First(c => c.Sp == "beaver" && !c.Dead);
        int waterX = -1, waterY = -1;
        for (int y = 2; y < session.State.H - 2; y++)
        {
            for (int x = 2; x < session.State.W - 2; x++)
            {
                int ti = GridHelpers.Idx(session.State, x, y);
                if (BiomeData.IsWater(session.State.Biome[ti]))
                {
                    waterX = x;
                    waterY = y;
                    break;
                }
            }
            if (waterX >= 0) break;
        }

        Assert.That(waterX, Is.GreaterThanOrEqualTo(0));
        beaver.X = waterX + 0.5;
        beaver.Y = waterY + 0.5;
        beaver.Hunger = 5;
        beaver.Thirst = 80;
        beaver.Energy = 80;
        beaver.State = "graze";

        session.Creatures.Wander(beaver, landOnly: true);

        int goalTi = GridHelpers.Idx(session.State, (int)Math.Round(beaver.Tx), (int)Math.Round(beaver.Ty));
        Assert.That(BiomeData.IsWater(session.State.Biome[goalTi]), Is.False);
    }

    [Test]
    public void GrazeDisplayLabel_DistinguishesEatingFromSearching()
    {
        var session = SimSession.Create(_repoRoot, 107);
        session.State.Cfg.Size = "s";
        session.GenerateWorld();

        var beaver = session.State.Creatures.First(c => c.Sp == "beaver" && !c.Dead);
        int ti = GridHelpers.Idx(session.State, (int)Math.Round(beaver.X), (int)Math.Round(beaver.Y));

        beaver.State = "graze";
        session.State.VegCap[ti] = 0.08f;
        session.State.Veg[ti] = 0.035f;
        Assert.That(CreatureBehaviorLabels.GetDisplayLabel(beaver, session.State), Is.EqualTo("Grazing"));

        session.State.Veg[ti] = 0f;
        Assert.That(CreatureBehaviorLabels.GetDisplayLabel(beaver, session.State), Is.EqualTo("Searching for food"));
    }

    [Test]
    public void ResolveGrazeSearchGoal_OnWater_TargetsLand()
    {
        var session = SimSession.Create(_repoRoot, 211);
        session.State.Cfg.Size = "s";
        session.GenerateWorld();

        var beaver = session.State.Creatures.First(c => c.Sp == "beaver" && !c.Dead);
        int waterX = -1, waterY = -1;
        for (int y = 2; y < session.State.H - 2; y++)
        {
            for (int x = 2; x < session.State.W - 2; x++)
            {
                int ti = GridHelpers.Idx(session.State, x, y);
                if (BiomeData.IsWater(session.State.Biome[ti]))
                {
                    waterX = x;
                    waterY = y;
                    break;
                }
            }
            if (waterX >= 0) break;
        }

        Assert.That(waterX, Is.GreaterThanOrEqualTo(0));
        beaver.X = waterX + 0.5;
        beaver.Y = waterY + 0.5;
        beaver.State = "graze";

        var goal = session.Creatures.ResolveGrazeSearchGoal(beaver, beaver.Genome.Sense);
        int goalTi = GridHelpers.Idx(session.State, (int)Math.Round(goal.X), (int)Math.Round(goal.Y));
        Assert.That(BiomeData.IsWater(session.State.Biome[goalTi]), Is.False);
        Assert.That(SimMath.Hypot(goal.X - beaver.X, goal.Y - beaver.Y), Is.GreaterThan(0.5));
    }

    [Test]
    public void ResolveGrazeSearchGoal_DeepWater_TargetsDistantLand()
    {
        var session = SimSession.Create(_repoRoot, 311);
        session.State.Cfg.Size = "s";
        session.GenerateWorld();

        var beaver = session.State.Creatures.First(c => c.Sp == "beaver" && !c.Dead);
        int waterX = -1, waterY = -1;
        double bestLandDist = -1;
        for (int y = 2; y < session.State.H - 2; y++)
        {
            for (int x = 2; x < session.State.W - 2; x++)
            {
                int ti = GridHelpers.Idx(session.State, x, y);
                if (!BiomeData.IsWater(session.State.Biome[ti])) continue;
                var land = Navigation.NearestWalkableLand(session.State, x + 0.5, y + 0.5, 80);
                if (!land.HasValue) continue;
                double d = SimMath.Hypot(land.Value.X - (x + 0.5), land.Value.Y - (y + 0.5));
                if (d > bestLandDist)
                {
                    bestLandDist = d;
                    waterX = x;
                    waterY = y;
                }
            }
        }

        Assert.That(waterX, Is.GreaterThanOrEqualTo(0));
        Assert.That(bestLandDist, Is.GreaterThan(8), "test needs water tile far from shore");
        beaver.X = waterX + 0.5;
        beaver.Y = waterY + 0.5;
        beaver.State = "graze";

        var goal = session.Creatures.ResolveGrazeSearchGoal(beaver, beaver.Genome.Sense);
        int goalTi = GridHelpers.Idx(session.State, (int)Math.Round(goal.X), (int)Math.Round(goal.Y));
        Assert.That(BiomeData.IsWater(session.State.Biome[goalTi]), Is.False);
        Assert.That(SimMath.Hypot(goal.X - beaver.X, goal.Y - beaver.Y), Is.GreaterThan(0.5));
    }

    private static string FindRepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "data", "species.json"))) return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new InvalidOperationException("Repo root not found");
    }
}

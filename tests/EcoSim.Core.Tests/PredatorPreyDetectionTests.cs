using EcoSim.Core.Batch;
using EcoSim.Core.Data;
using EcoSim.Core.Sim;
using NUnit.Framework;

namespace EcoSim.Core.Tests;

[TestFixture]
public class PredatorPreyDetectionTests
{
    private string _repoRoot = "";

    [OneTimeSetUp]
    public void SetUp()
    {
        _repoRoot = FindRepoRoot();
        DataPaths.SetDataRoot(_repoRoot);
    }

    [Test]
    public void Seed42_Foxes_SeeRabbitsAsPrey()
    {
        var session = SimSession.Create(_repoRoot, 42);
        var harness = new BatchHarness(session);
        harness.Init(new BatchRunConfig { Seed = 42, Size = "s" });
        harness.GenerateWorld();

        var fox = session.State.Creatures.First(c => c.Sp == "fox" && !c.Dead);
        var wolf = session.Species.Get("wolf");
        Assert.That(wolf.HuntsMask, Is.Not.EqualTo(0u));

        var nearby = session.Creatures.Nearby(fox, fox.Genome.Sense);
        int rabbits = nearby.Count(c => c.Sp == "rabbit");
        TestContext.WriteLine($"fox sense={fox.Genome.Sense} nearby={nearby.Count} rabbits={rabbits}");

        fox.Hunger = 20;
        var decision = session.BehaviorTree.Decide(fox, session.Creatures);
        Assert.That(decision, Is.Not.Null);
        TestContext.WriteLine($"fox state={decision!.Action["state"]} prey={decision.Ctx.Prey?.Sp}");
        Assert.That(decision.Ctx.Prey, Is.Not.Null, "hungry fox should perceive nearby prey");
    }

    private static string FindRepoRoot() => TestPaths.FindRepoRoot();
}

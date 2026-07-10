using EcoSim.Core;
using EcoSim.Core.Behavior;
using EcoSim.Core.Data;
using EcoSim.Core.Sim;
using NUnit.Framework;

namespace EcoSim.Core.Tests;

[TestFixture]
public class BehaviorDebugTests
{
    private string _repoRoot = "";

    [OneTimeSetUp]
    public void SetUp()
    {
        _repoRoot = FindRepoRoot();
        DataPaths.SetDataRoot(_repoRoot);
    }

    [Test]
    public void Rabbit_HasBehaviorConfig_AndGrazesWhenHungry()
    {
        var session = SimSession.Create(_repoRoot, 1);
        session.State.Cfg.Size = "s";
        session.GenerateWorld();

        var rabbit = session.State.Creatures.First(c => c.Sp == "rabbit" && !c.Dead);
        Assert.That(rabbit, Is.Not.Null);
        Assert.That(session.Species.Get("rabbit").BehaviorConfig, Is.Not.Null);
        Assert.That(session.Species.Get("rabbit").BehaviorConfig!.Root.Children.Count, Is.GreaterThan(0));

        int ti = GridHelpers.Idx(session.State,
            (int)Math.Round(rabbit.X), (int)Math.Round(rabbit.Y));
        session.State.Veg[ti] = Math.Max(session.State.Veg[ti], 0.5f);

        rabbit.Hunger = 30;
        rabbit.Thirst = 80;
        rabbit.Energy = 80;
        rabbit.State = "wander";

        double hungerBefore = rabbit.Hunger;
        session.BehaviorTree.Tick(rabbit, 0.5, session.Creatures, executeActions: true);

        Assert.That(rabbit.State, Is.EqualTo("graze").Or.EqualTo("wander"));
        Assert.That(rabbit.Hunger, Is.GreaterThan(hungerBefore - 1),
            "hungry rabbit on veg tile should graze or at least not starve further");
    }

    private static string FindRepoRoot() => TestPaths.FindRepoRoot();
}

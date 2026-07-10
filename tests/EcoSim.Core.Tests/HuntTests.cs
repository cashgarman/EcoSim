using EcoSim.Core;
using EcoSim.Core.Data;
using EcoSim.Core.Sim;
using NUnit.Framework;

namespace EcoSim.Core.Tests;

[TestFixture]
public class HuntTests
{
    private string _repoRoot = "";

    [OneTimeSetUp]
    public void SetUp()
    {
        _repoRoot = FindRepoRoot();
        DataPaths.SetDataRoot(_repoRoot);
    }

    [Test]
    public void Wolf_HuntsNearbyRabbit_WhenHungry()
    {
        var session = SimSession.Create(_repoRoot, 99);
        session.State.Cfg.Size = "s";
        session.GenerateWorld();

        var wolf = session.Creatures.MakeCreature("wolf", 20.0, 20.0);
        var rabbit = session.Creatures.MakeCreature("rabbit", 20.5, 20.0);
        wolf.Hunger = 20;
        wolf.Thirst = 80;
        wolf.Energy = 80;
        wolf.Age = 20;
        wolf.State = "wander";
        rabbit.Hp = 100;
        rabbit.Age = 5;

        session.Creatures.SyncGrid();
        for (int i = 0; i < 20; i++)
        {
            session.Creatures.SyncGrid();
            session.BehaviorTree.Tick(wolf, 0.5, session.Creatures, executeActions: true);
            if (rabbit.Hp < 100) break;
        }

        Assert.That(wolf.State, Is.EqualTo("hunt").Or.EqualTo("huntSearch"));
        Assert.That(rabbit.Hp, Is.LessThan(100).Or.EqualTo(0));
    }

    [Test]
    public void StockFox_OnSeed42World_DamagesAdjacentRabbit()
    {
        var session = SimSession.Create(_repoRoot, 42);
        session.State.Cfg.Size = "s";
        session.GenerateWorld();
        session.State.Creatures.RemoveAll(c => c.Sp != "fox" && c.Sp != "rabbit");
        session.Creatures.RebuildGrid();

        var fox = session.State.Creatures.First(c => c.Sp == "fox" && !c.Dead);
        var rabbit = session.State.Creatures.First(c => c.Sp == "rabbit" && !c.Dead);
        session.State.Creatures.Clear();
        session.State.Creatures.Add(fox);
        session.State.Creatures.Add(rabbit);
        session.Creatures.RebuildGrid();
        fox.X = 40;
        fox.Y = 40;
        rabbit.X = 40.4;
        rabbit.Y = 40;
        fox.Hunger = 15;
        fox.Thirst = 80;
        fox.Energy = 80;
        rabbit.Hp = 100;

        session.Creatures.SyncGrid();
        for (int i = 0; i < 40; i++)
        {
            session.Creatures.SyncGrid();
            session.Simulation.Tick(0.5);
            if (rabbit.Hp < 100) break;
        }

        Assert.That(rabbit.Hp, Is.LessThan(100), "adjacent fox should land hunt damage within ~20 sim seconds");
        Assert.That(rabbit.Cause, Is.EqualTo("predation"));
    }

    private static string FindRepoRoot() => TestPaths.FindRepoRoot();
}

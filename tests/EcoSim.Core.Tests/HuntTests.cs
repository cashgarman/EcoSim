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

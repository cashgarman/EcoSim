using EcoSim.Core.Batch;
using EcoSim.Core.Data;
using EcoSim.Core.Sim;
using NUnit.Framework;

namespace EcoSim.Core.Tests;

[TestFixture]
public class EcologyRegressionTests
{
    private string _repoRoot = "";

    [OneTimeSetUp]
    public void SetUp()
    {
        _repoRoot = FindRepoRoot();
        DataPaths.SetDataRoot(_repoRoot);
    }

    [Test]
    public void Seed42_After5Days_RabbitsDeclineViaPredation()
    {
        var session = SimSession.Create(_repoRoot, 42);
        var harness = new BatchHarness(session);
        harness.Init(new BatchRunConfig { Seed = 42, Size = "s" });
        harness.GenerateWorld();

        int startRabbits = session.State.Creatures.Count(c => c.Sp == "rabbit" && !c.Dead);
        harness.Run(new BatchRunOptions { TargetDays = 5, MaxWallMs = 120_000, SampleEveryDays = 5 });

        int endRabbits = session.State.Creatures.Count(c => c.Sp == "rabbit" && !c.Dead);
        int predationDeaths = session.State.Creatures.Count(c => c.Sp == "rabbit" && c.Dead && c.Cause == "predation");

        Assert.That(endRabbits, Is.LessThan(startRabbits),
            "seed 42 day 5 should reduce rabbit population via predation");
        Assert.That(predationDeaths, Is.GreaterThan(0), "predation should register rabbit deaths");
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

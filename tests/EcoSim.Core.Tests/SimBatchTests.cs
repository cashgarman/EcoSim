using EcoSim.Core.Batch;
using EcoSim.Core.Data;
using EcoSim.Core.Sim;
using NUnit.Framework;

namespace EcoSim.Core.Tests;

[TestFixture]
public class SimBatchTests
{
    private string _repoRoot = "";

    [OneTimeSetUp]
    public void SetUp()
    {
        _repoRoot = FindRepoRoot();
        DataPaths.SetDataRoot(_repoRoot);
    }

    [Test]
    public void WorldGen_SizeS_HasExpectedDimensions()
    {
        var session = SimSession.Create(_repoRoot, 42);
        session.State.Cfg.Size = "s";
        session.GenerateWorld();
        Assert.That(session.State.W, Is.EqualTo(160));
        Assert.That(session.State.H, Is.EqualTo(160));
    }

    [Test]
    public void BatchRun_Seed1_5Days_CompletesWithLivingPopulation()
    {
        var session = SimSession.Create(_repoRoot, 1);
        var harness = new BatchHarness(session);
        harness.Init(new BatchRunConfig
        {
            Seed = 1,
            Size = "s",
            TargetDays = 5,
            SampleEveryDays = 1,
        });
        int pop = harness.GenerateWorld();
        Assert.That(pop, Is.GreaterThan(50));

        var result = harness.Run(new BatchRunOptions { TargetDays = 5, MaxWallMs = 30_000 });
        var report = harness.GetReport("test-run", result);
        harness.Teardown();

        Assert.That(report.Samples.Count, Is.GreaterThan(0));
        Assert.That(report.Summary.FinalPop, Is.GreaterThan(0));
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

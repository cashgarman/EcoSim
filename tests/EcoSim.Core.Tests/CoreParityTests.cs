using EcoSim.Core.Data;
using EcoSim.Core.Numerics;
using EcoSim.Core.Rng;
using NUnit.Framework;

namespace EcoSim.Core.Tests;

[TestFixture]
public class SimRngTests
{
    [Test]
    public void LcgSequence_MatchesRegressionGoldenValues()
    {
        var rng = new SimRng(12345);
        double[] expected =
        [
            0.02040268573909998,
            0.01654784823767841,
            0.5431557944975793,
            0.6349040560889989,
            0.9100295137614012,
        ];

        for (int i = 0; i < expected.Length; i++)
        {
            Assert.That(rng.Next(), Is.EqualTo(expected[i]).Within(1e-12), $"index {i}");
        }
    }

    [Test]
    public void GlobalRng_ResetSeed_ReproducesSequence()
    {
        GlobalRng.SetSeed(42);
        double a = GlobalRng.Next();
        GlobalRng.SetSeed(42);
        double b = GlobalRng.Next();
        Assert.That(a, Is.EqualTo(b));
    }

    [Test]
    public void Fbm_AtFixedPoint_IsStable()
    {
        double v = Noise.Fbm(12.5, 7.25, 999, 5, 0.02, 0.55);
        Assert.That(v, Is.EqualTo(0.3000747391040622).Within(1e-10));
    }
}

[TestFixture]
public class SpeciesCatalogTests
{
    [OneTimeSetUp]
    public void SetUp()
    {
        DataPaths.SetDataRoot(FindRepoRoot());
    }

    [Test]
    public void LoadSpeciesData_HasExpectedSpeciesCount()
    {
        var catalog = SpeciesCatalog.LoadFromFile();
        Assert.That(catalog.SpeciesKeys.Count, Is.EqualTo(11));
        Assert.That(catalog.Species.ContainsKey("rabbit"), Is.True);
        Assert.That(catalog.GeneKeys.Length, Is.EqualTo(10));
    }

    [Test]
    public void SpeciesKeys_PreserveJsonFileOrder()
    {
        var catalog = SpeciesCatalog.LoadFromFile();
        string[] expected =
        [
            "rabbit", "mouse", "deer", "elk", "beaver", "boar",
            "fox", "wolf", "hawk", "owl", "bear",
        ];
        Assert.That(catalog.SpeciesKeys, Is.EqualTo(expected));
    }

    [Test]
    public void HuntsMask_Rabbit_HasPredatorBits()
    {
        var catalog = SpeciesCatalog.LoadFromFile();
        var rabbit = catalog.Get("rabbit");
        Assert.That(rabbit.PreyMask, Is.Not.EqualTo(0u));
        Assert.That(rabbit.HuntsMask, Is.EqualTo(0u));
    }

    private static string FindRepoRoot() => TestPaths.FindRepoRoot();
}

[TestFixture]
public class BehaviorLibraryTests
{
    [OneTimeSetUp]
    public void SetUp()
    {
        DataPaths.SetDataRoot(FindRepoRoot());
    }

    [Test]
    public void LoadBehaviorLibrary_CompilesAllSpeciesTrees()
    {
        var species = SpeciesCatalog.LoadFromFile();
        var library = new Behavior.BehaviorLibrary();
        library.Load(species);

        foreach (string sp in species.SpeciesKeys)
        {
            var cfg = species.Get(sp).BehaviorConfig;
            Assert.That(cfg, Is.Not.Null, sp);
            Assert.That(cfg!.Root.Children.Count, Is.GreaterThan(0), sp);
            Assert.That(cfg.Thresholds.ContainsKey("thirstUrgent"), Is.True, sp);
        }
    }

    private static string FindRepoRoot() => TestPaths.FindRepoRoot();
}

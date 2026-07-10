using EcoSim.Core.Data;
using EcoSim.Core.Sim;
using NUnit.Framework;

namespace EcoSim.Core.Tests;

[TestFixture]
public class EvolutionTests
{
    private string _repoRoot = "";

    [OneTimeSetUp]
    public void SetUp()
    {
        _repoRoot = TestPaths.FindRepoRoot();
        DataPaths.SetDataRoot(_repoRoot);
    }

    private SimSession NewSession(uint seed = 5)
    {
        var session = SimSession.Create(_repoRoot, seed);
        session.State.Cfg.Size = "s";
        session.GenerateWorld();
        return session;
    }

    [Test]
    public void Catalog_LoadsATreeForEverySpecies()
    {
        var session = NewSession();
        foreach (string sp in session.Species.SpeciesKeys)
        {
            var tree = session.Evolutions.Catalog.TreeFor(sp);
            Assert.That(tree, Is.Not.Null, $"species '{sp}' should have an evolution tree");
            Assert.That(tree!.Nodes, Has.Count.GreaterThanOrEqualTo(5), $"tree for '{sp}' looks too small");
        }
    }

    [Test]
    public void Validation_RejectsDuplicateIds_UnknownGenes_Cycles()
    {
        var session = NewSession();

        var dup = new EvolutionTreeFile
        {
            Species = "rabbit",
            Nodes =
            [
                new EvolutionNode { Id = "a", Cost = 1 },
                new EvolutionNode { Id = "a", Cost = 1 },
            ],
        };
        Assert.Throws<InvalidOperationException>(() => EvolutionCatalog.Validate("rabbit", dup, session.Species));

        var badGene = new EvolutionTreeFile
        {
            Species = "rabbit",
            Nodes =
            [
                new EvolutionNode
                {
                    Id = "a",
                    Cost = 1,
                    Effects = new EvolutionEffects { Genes = new() { ["wings"] = new GeneOp { Mul = 2 } } },
                },
            ],
        };
        Assert.Throws<InvalidOperationException>(() => EvolutionCatalog.Validate("rabbit", badGene, session.Species));

        var cyclic = new EvolutionTreeFile
        {
            Species = "rabbit",
            Nodes =
            [
                new EvolutionNode { Id = "a", Cost = 1, Requires = ["b"] },
                new EvolutionNode { Id = "b", Cost = 1, Requires = ["a"] },
            ],
        };
        Assert.Throws<InvalidOperationException>(() => EvolutionCatalog.Validate("rabbit", cyclic, session.Species));
    }

    [Test]
    public void Purchase_EnforcesPointsAndPrerequisites()
    {
        var session = NewSession();

        Assert.That(session.Evolutions.CanPurchase("rabbit", "fleet1", out string reason), Is.False);
        Assert.That(reason, Does.Contain("point"));

        session.Progress.AddPoint("rabbit");
        Assert.That(session.Evolutions.CanPurchase("rabbit", "fleet1", out _), Is.True);

        // fleet2 requires fleet1.
        session.Progress.AddPoint("rabbit");
        session.Progress.AddPoint("rabbit");
        Assert.That(session.Evolutions.CanPurchase("rabbit", "fleet2", out reason), Is.False);
        Assert.That(reason, Does.Contain("Fleet Feet I"));

        Assert.That(session.Evolutions.Purchase("rabbit", "fleet1"), Is.True);
        Assert.That(session.Evolutions.Purchase("rabbit", "fleet1"), Is.False, "cannot buy twice");
        Assert.That(session.Evolutions.Purchase("rabbit", "fleet2"), Is.True);
        Assert.That(session.Progress.Points("rabbit"), Is.EqualTo(0), "points must be spent");
    }

    [Test]
    public void Purchase_PatchesSpeciesBase_AndLivingGenomes()
    {
        var session = NewSession();
        double baseSpeed = session.Species.Get("rabbit").Base.Speed;
        var living = session.State.Creatures.First(c => c.Sp == "rabbit" && !c.Dead);
        double livingSpeed = living.Genome.Speed;

        session.Progress.AddPoint("rabbit");
        Assert.That(session.Evolutions.Purchase("rabbit", "fleet1"), Is.True);

        Assert.That(session.Species.Get("rabbit").Base.Speed, Is.EqualTo(baseSpeed * 1.10).Within(1e-9),
            "species base must be evolved so future spawns inherit");
        Assert.That(living.Genome.Speed, Is.EqualTo(livingSpeed * 1.10).Within(1e-9),
            "living creatures must be patched so bred offspring inherit");
    }

    [Test]
    public void Purchase_OffspringInheritEvolvedTrait()
    {
        var session = NewSession();
        session.Progress.AddPoint("rabbit");
        session.Evolutions.Purchase("rabbit", "fleet1");

        var mother = session.State.Creatures.First(c => c.Sp == "rabbit" && !c.Dead && c.Sex == "female");
        double motherSpeed = mother.Genome.Speed;
        mother.LitterQ = 1;
        mother.MatePartner = mother.Genome;
        int countBefore = session.State.Creatures.Count;
        session.Creatures.GiveBirth(mother);

        var baby = session.State.Creatures[countBefore];
        // BreedGenome averages parents + mutation noise; the evolved parent speed must carry through.
        Assert.That(baby.Genome.Speed, Is.EqualTo(motherSpeed).Within(0.4),
            "offspring genome must derive from evolved parent genomes");
    }

    [Test]
    public void Purchase_ClampsToGeneRange()
    {
        var session = NewSession();
        var huge = new EvolutionTreeFile
        {
            Species = "rabbit",
            Nodes =
            [
                new EvolutionNode
                {
                    Id = "mega",
                    Cost = 1,
                    Effects = new EvolutionEffects { Genes = new() { ["speed"] = new GeneOp { Mul = 100 } } },
                },
            ],
        };
        string dir = Path.Combine(Path.GetTempPath(), "ecosim-evo-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "rabbit.json"),
                System.Text.Json.JsonSerializer.Serialize(huge));
            var catalog = EvolutionCatalog.Load(session.Species, dir);
            var evo = new EvolutionSystem(session.State, session.Species, session.Behaviors, catalog, session.Progress);

            session.Progress.AddPoint("rabbit");
            Assert.That(evo.Purchase("rabbit", "mega"), Is.True);

            double cap = session.Species.GeneRange["speed"][1];
            Assert.That(session.Species.Get("rabbit").Base.Speed, Is.EqualTo(cap).Within(1e-9));
            foreach (var c in session.State.Creatures.Where(c => c.Sp == "rabbit" && !c.Dead))
            {
                Assert.That(c.Genome.Speed, Is.LessThanOrEqualTo(cap + 1e-9));
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Test]
    public void Purchase_CanSwimAbility_FlipsSpeciesFlag()
    {
        var session = NewSession();
        Assert.That(SpeciesCatalog.SpeciesCanSwim(session.Species.Get("rabbit")), Is.False);

        for (int i = 0; i < 10; i++) session.Progress.AddPoint("rabbit");
        Assert.That(session.Evolutions.Purchase("rabbit", "fleet1"), Is.True);
        Assert.That(session.Evolutions.Purchase("rabbit", "keenEars"), Is.True);
        Assert.That(session.Evolutions.Purchase("rabbit", "fleet2"), Is.True);
        Assert.That(session.Evolutions.Purchase("rabbit", "hardyCoat"), Is.True);
        Assert.That(session.Evolutions.Purchase("rabbit", "marshRabbit"), Is.True);

        Assert.That(SpeciesCatalog.SpeciesCanSwim(session.Species.Get("rabbit")), Is.True,
            "swim ability must apply species-wide immediately");
    }

    [Test]
    public void BehaviorConfigs_SurvivePurchasesAndReset()
    {
        var session = NewSession();
        double pristineSpeed = session.Species.Get("rabbit").Base.Speed;

        session.Progress.AddPoint("rabbit");
        session.Evolutions.Purchase("rabbit", "fleet1");
        foreach (string sp in session.Species.SpeciesKeys)
        {
            Assert.That(session.Species.Get(sp).BehaviorConfig, Is.Not.Null,
                $"'{sp}' behavior tree must remain attached after a purchase");
        }

        session.Evolutions.ResetAll();
        Assert.That(session.Species.Get("rabbit").Base.Speed, Is.EqualTo(pristineSpeed).Within(1e-9),
            "reset must restore pristine species defs");
        Assert.That(session.Progress.TotalPointsEarned, Is.EqualTo(0));
        foreach (string sp in session.Species.SpeciesKeys)
        {
            Assert.That(session.Species.Get(sp).BehaviorConfig, Is.Not.Null,
                $"'{sp}' behavior tree must be recompiled after reset");
        }
    }
}

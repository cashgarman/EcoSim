using EcoSim.Core.Data;
using EcoSim.Core.Rng;
using EcoSim.Core.Sim;
using NUnit.Framework;

namespace EcoSim.Core.Tests;

[TestFixture]
public class SpawnPlacementTests
{
    private string _repoRoot = "";

    [OneTimeSetUp]
    public void SetUp()
    {
        _repoRoot = FindRepoRoot();
        DataPaths.SetDataRoot(_repoRoot);
    }

    [Test]
    public void Seed42_AllStockedCreaturesOnWalkableLand()
    {
        var session = SimSession.Create(_repoRoot, 42);
        session.State.Cfg = new WorldGenConfig
        {
            Size = "s",
            Sea = 0.46,
            Temp = 0.5,
            Moist = 0.5,
            Relief = 0.6,
            Animals = 0.45,
        };
        GlobalRng.SetSeed(42);
        session.GenerateWorld();

        foreach (var c in session.State.Creatures)
        {
            Assert.That(c.Dead, Is.False);
            bool canSwim = SpeciesCatalog.SpeciesCanSwim(session.Species.Get(c.Sp));
            int tx = (int)Math.Round(c.X);
            int ty = (int)Math.Round(c.Y);
            Assert.That(GridHelpers.InBounds(session.State, tx, ty), Is.True,
                $"{c.Sp} #{c.Id} at ({c.X:F2},{c.Y:F2}) out of bounds");
            Assert.That(Navigation.IsTileWalkable(session.State, tx, ty, canSwim), Is.True,
                $"{c.Sp} #{c.Id} at ({c.X:F2},{c.Y:F2}) on non-walkable tile");
            var biome = (Biome)session.State.Biome[GridHelpers.Idx(session.State, tx, ty)];
            Assert.That(biome, Is.Not.EqualTo(Biome.Peak),
                $"{c.Sp} #{c.Id} spawned on peak");
        }
    }

    [Test]
    public void FindSpawnTile_NeverReturnsWaterBiome()
    {
        var session = SimSession.Create(_repoRoot, 42);
        session.State.Cfg = new WorldGenConfig { Size = "s", Sea = 0.46, Animals = 0.45 };
        GlobalRng.SetSeed(42);
        session.World.Generate();

        foreach (string sp in session.Species.SpeciesKeys)
        {
            bool canSwim = SpeciesCatalog.SpeciesCanSwim(session.Species.Get(sp));
            for (int i = 0; i < 50; i++)
            {
                var t = session.Creatures.FindSpawnTile(sp);
                if (!t.HasValue) continue;

                int tx = (int)Math.Round(t.Value.X);
                int ty = (int)Math.Round(t.Value.Y);
                Assert.That(GridHelpers.InBounds(session.State, tx, ty), Is.True);
                Assert.That(Navigation.IsTileWalkable(session.State, tx, ty, canSwim), Is.True,
                    $"{sp} spawn at ({t.Value.X:F2},{t.Value.Y:F2}) not walkable");
            }
        }
    }

    private static string FindRepoRoot() => TestPaths.FindRepoRoot();
}

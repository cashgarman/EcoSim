using EcoSim.Core.Data;
using EcoSim.Core.Sim;
using NUnit.Framework;

namespace EcoSim.Core.Tests;

[TestFixture]
public class ThirstNavigationTests
{
    private string _repoRoot = "";

    [OneTimeSetUp]
    public void SetUp()
    {
        _repoRoot = FindRepoRoot();
        DataPaths.SetDataRoot(_repoRoot);
    }

    [Test]
    public void Thirst_OneTileFromLake_ReachesShoreAndDrinks()
    {
        var session = SimSession.Create(_repoRoot, 1);
        var state = session.State;
        state.W = 8;
        state.H = 8;
        state.Biome = new byte[state.W * state.H];
        state.Veg = new float[state.W * state.H];
        state.VegCap = new float[state.W * state.H];
        state.PassMask = new byte[state.W * state.H];
        state.Temp = new float[state.W * state.H];
        state.Moist = new float[state.W * state.H];
        state.Elev = new float[state.W * state.H];
        state.WaterDist = new float[state.W * state.H];
        state.Ready = true;

        for (int i = 0; i < state.W * state.H; i++)
        {
            state.Biome[i] = (byte)Biome.Grass;
            state.VegCap[i] = 1f;
        }

        SetBiome(state, 5, 4, Biome.Lake);
        Navigation.BuildWaterDistanceField(state);

        var hawk = session.Creatures.MakeCreature("hawk", 2.5, 4.5);
        hawk.Thirst = 10;
        hawk.Hunger = 80;
        hawk.Energy = 80;
        hawk.Hp = 100;
        hawk.State = "thirst";
        session.Creatures.RebuildGrid();

        Assert.That(Navigation.AtWaterEdge(state, hawk.X, hawk.Y), Is.False);

        double thirstBefore = hawk.Thirst;
        for (int tick = 0; tick < 240 && hawk.Thirst < 55; tick++)
        {
            state.TGlobal += 1.0 / 24.0;
            session.Creatures.StepCreature(hawk, 1.0 / 24.0);
        }

        Assert.That(Navigation.AtWaterEdge(state, hawk.X, hawk.Y), Is.True,
            "hawk should step onto the shore tile adjacent to the lake");
        Assert.That(hawk.Thirst, Is.GreaterThan(thirstBefore + 20),
            "hawk should drink once it reaches the shoreline");
    }

    private static void SetBiome(SimState state, int x, int y, Biome biome)
    {
        state.Biome[y * state.W + x] = (byte)biome;
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

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

        Assert.That(Navigation.CanDrinkHere(state, hawk.X, hawk.Y, false), Is.True,
            "hawk should reach a drinkable shore tile adjacent to the lake");
        Assert.That(hawk.Thirst, Is.GreaterThan(thirstBefore + 20),
            "hawk should drink once it reaches the shoreline");
    }

    [Test]
    public void Beaver_GrazingAtShore_DrinksWithoutEnteringThirstState()
    {
        var session = SimSession.Create(_repoRoot, 2);
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
            state.VegCap[i] = 0.5f;
            state.Veg[i] = 0f;
        }

        SetBiome(state, 5, 4, Biome.Lake);
        Navigation.BuildWaterDistanceField(state);

        var beaver = session.Creatures.MakeCreature("beaver", 4.5, 4.5);
        beaver.Thirst = 40;
        beaver.Hunger = 10;
        beaver.Energy = 80;
        beaver.Hp = 100;
        beaver.State = "graze";
        beaver.BtAction = session.Species.Get("beaver").BehaviorConfig!.Actions["Graze"];
        session.Creatures.RebuildGrid();

        Assert.That(Navigation.AtWaterEdge(state, beaver.X, beaver.Y), Is.True);

        double thirstBefore = beaver.Thirst;
        session.Creatures.StepCreature(beaver, 0.5);

        Assert.That(beaver.Thirst, Is.GreaterThan(thirstBefore),
            "beaver grazing at shoreline should drink when thirsty");
        Assert.That(beaver.State, Is.EqualTo("graze"));
    }

    [Test]
    public void Beaver_Thirst_OneTileFromLake_ReachesShoreAndDrinks()
    {
        var session = SimSession.Create(_repoRoot, 3);
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

        var beaver = session.Creatures.MakeCreature("beaver", 2.5, 4.5);
        beaver.Thirst = 10;
        beaver.Hunger = 80;
        beaver.Energy = 80;
        beaver.Hp = 100;
        beaver.State = "thirst";
        session.Creatures.RebuildGrid();

        double thirstBefore = beaver.Thirst;
        for (int tick = 0; tick < 240 && beaver.Thirst < 55; tick++)
        {
            state.TGlobal += 1.0 / 24.0;
            session.Creatures.StepCreature(beaver, 1.0 / 24.0);
        }

        Assert.That(Navigation.CanDrinkHere(state, beaver.X, beaver.Y, true), Is.True);
        Assert.That(beaver.Thirst, Is.GreaterThan(thirstBefore + 20));
    }

    [Test]
    public void Thirst_StopsOneTileShortOfShore_StillDrinks()
    {
        var session = SimSession.Create(_repoRoot, 48);
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

        var beaver = session.Creatures.MakeCreature("beaver", 2.5, 4.5);
        beaver.Thirst = 5;
        beaver.Hunger = 80;
        beaver.Energy = 80;
        beaver.Hp = 100;
        beaver.State = "thirst";
        beaver.NavGoalX = 4.5;
        beaver.NavGoalY = 4.5;
        session.Creatures.RebuildGrid();

        Assert.That(Navigation.AtWaterEdge(state, beaver.X, beaver.Y), Is.False);
        Assert.That(Navigation.CanDrinkHere(state, beaver.X, beaver.Y, true), Is.False);

        double thirstBefore = beaver.Thirst;
        for (int tick = 0; tick < 240 && beaver.Thirst < 55; tick++)
        {
            state.TGlobal += 1.0 / 24.0;
            session.Creatures.StepCreature(beaver, 1.0 / 24.0);
        }

        Assert.That(Navigation.CanDrinkHere(state, beaver.X, beaver.Y, true), Is.True);
        Assert.That(beaver.Thirst, Is.GreaterThan(thirstBefore + 20));
    }

    [Test]
    public void CanDrinkHere_SlightlyPastTileCenter_StillTrueOnShore()
    {
        var session = SimSession.Create(_repoRoot, 45);
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

        Assert.That(Navigation.CanDrinkOnTile(state, 4, 4, true), Is.True);
        Assert.That(Navigation.CanDrinkHere(state, 4.52, 4.52, true), Is.True,
            "positions just past tile center should still drink on shore tile");
    }

    private static void SetBiome(SimState state, int x, int y, Biome biome)
    {
        state.Biome[y * state.W + x] = (byte)biome;
    }

    private static string FindRepoRoot() => TestPaths.FindRepoRoot();
}

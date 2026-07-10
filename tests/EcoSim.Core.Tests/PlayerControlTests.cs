using EcoSim.Core.Data;
using EcoSim.Core.Sim;
using NUnit.Framework;

namespace EcoSim.Core.Tests;

[TestFixture]
public class PlayerControlTests
{
    private string _repoRoot = "";

    [OneTimeSetUp]
    public void SetUp()
    {
        _repoRoot = TestPaths.FindRepoRoot();
        DataPaths.SetDataRoot(_repoRoot);
    }

    private SimSession NewSession(uint seed = 7)
    {
        var session = SimSession.Create(_repoRoot, seed);
        session.State.Cfg.Size = "s";
        session.GenerateWorld();
        return session;
    }

    private static Creature SpawnOnLand(SimSession session, string sp)
    {
        var t = session.Creatures.FindSpawnTile(sp)!;
        return session.Creatures.MakeCreature(sp, t.Value.X, t.Value.Y);
    }

    private static (int LandX, int LandY, int WaterX, int WaterY) FindWaterEdgeTile(SimSession session)
    {
        var state = session.State;
        for (int y = 2; y < state.H - 2; y++)
        {
            for (int x = 2; x < state.W - 2; x++)
            {
                if (!Navigation.IsTileWalkable(state, x, y, canSwim: false)) continue;
                foreach ((int dx, int dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    if (BiomeData.IsWater(state.Biome[(y + dy) * state.W + (x + dx)]))
                    {
                        return (x, y, x + dx, y + dy);
                    }
                }
            }
        }
        throw new InvalidOperationException("no shoreline found on the generated map");
    }

    [Test]
    public void Possession_GatesBehaviorTree_WhileNeedsStillDrain()
    {
        var session = NewSession();
        var rabbit = SpawnOnLand(session, "rabbit");
        session.Player.Possess(rabbit);
        double hunger = rabbit.Hunger;
        double age = rabbit.Age;

        for (int i = 0; i < 10; i++)
        {
            session.Creatures.SyncGrid();
            session.Creatures.StepCreature(rabbit, 0.5);
        }

        Assert.That(rabbit.BtAction, Is.Null, "behavior tree must not drive a possessed creature");
        Assert.That(rabbit.Hunger, Is.LessThan(hunger), "needs must keep draining");
        Assert.That(rabbit.Age, Is.GreaterThan(age), "aging must continue");
    }

    [Test]
    public void Wasd_MovesCreature_AndCancelsClickOrder()
    {
        var session = NewSession();
        var rabbit = SpawnOnLand(session, "rabbit");
        session.Player.Possess(rabbit);

        // Make the clicked tile bare ground so the order is a plain move (not a graze).
        int cx = (int)Math.Round(rabbit.X) + 5, cy = (int)Math.Round(rabbit.Y);
        int cti = cy * session.State.W + cx;
        session.State.Biome[cti] = (byte)Biome.Desert;
        session.State.Veg[cti] = 0f;
        session.State.VegCap[cti] = 0f;

        session.Player.IssueClickOrder(cx, cy, null);
        Assert.That(session.Player.Order, Is.Not.Null);
        Assert.That(session.Player.Order!.Kind, Is.EqualTo(PlayerOrderKind.MoveTo));

        double startX = rabbit.X;
        session.Player.Intents.MoveX = 1;
        session.Creatures.SyncGrid();
        session.Creatures.StepCreature(rabbit, 0.25);

        Assert.That(rabbit.X, Is.GreaterThan(startX), "WASD steering should move the creature");
        Assert.That(session.Player.Order, Is.Null, "WASD must cancel the active order");
    }

    [Test]
    public void Wasd_WaterBlocksNonSwimmer()
    {
        var session = NewSession();
        var rabbit = SpawnOnLand(session, "rabbit");
        session.Player.Possess(rabbit);

        int sx = (int)Math.Round(rabbit.X);
        int sy = (int)Math.Round(rabbit.Y);
        // Wall of water directly east of the rabbit (rabbits can't swim).
        for (int dy = -2; dy <= 2; dy++)
        {
            for (int dx = 1; dx <= 3; dx++)
            {
                int ti = (sy + dy) * session.State.W + (sx + dx);
                if (ti >= 0 && ti < session.State.Biome.Length)
                {
                    session.State.Biome[ti] = (byte)Biome.Lake;
                }
            }
        }

        session.Player.Intents.MoveX = 1;
        for (int i = 0; i < 30; i++)
        {
            session.Creatures.SyncGrid();
            session.Creatures.StepCreature(rabbit, 0.25);
        }

        Assert.That(rabbit.X, Is.LessThan(sx + 0.6), "non-swimmer must be blocked at the water edge");
    }

    [Test]
    public void Sprint_MovesFasterAndDrainsMoreEnergy()
    {
        var session = NewSession();
        var rabbit = SpawnOnLand(session, "rabbit");
        session.Player.Possess(rabbit);

        // Clear a grass corridor east so neither run is blocked by terrain.
        int sy = (int)Math.Round(rabbit.Y);
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = 0; dx <= 24; dx++)
            {
                int tx = (int)Math.Round(rabbit.X) + dx;
                int ti = (sy + dy) * session.State.W + tx;
                if (ti >= 0 && ti < session.State.Biome.Length)
                {
                    session.State.Biome[ti] = (byte)Biome.Grass;
                }
            }
        }

        double RunEast(bool sprint)
        {
            rabbit.Energy = 80;
            double startX = rabbit.X;
            session.Player.Intents.MoveX = 1;
            session.Player.Intents.SprintHeld = sprint;
            for (int i = 0; i < 6; i++)
            {
                session.Creatures.SyncGrid();
                session.Creatures.StepCreature(rabbit, 0.25);
            }
            session.Player.Intents.SprintHeld = false;
            return rabbit.X - startX;
        }

        double walkDist = RunEast(sprint: false);
        double walkEnergy = 80 - rabbit.Energy;
        double sprintDist = RunEast(sprint: true);
        double sprintEnergy = 80 - rabbit.Energy;

        Assert.That(sprintDist, Is.GreaterThan(walkDist * 1.3), "sprinting should be substantially faster");
        Assert.That(sprintEnergy, Is.GreaterThan(walkEnergy * 1.5), "sprinting should cost substantially more energy");
    }

    [Test]
    public void ClickGround_PathfindsAndArrives()
    {
        var session = NewSession();
        var rabbit = SpawnOnLand(session, "rabbit");
        session.Player.Possess(rabbit);

        var goal = Navigation.PickRandomWalkableTile(session.State, rabbit.X, rabbit.Y, 4, false);
        session.Player.IssueClickOrder(goal.X, goal.Y, null);
        double startDist = Math.Sqrt(Math.Pow(goal.X - rabbit.X, 2) + Math.Pow(goal.Y - rabbit.Y, 2));

        for (int i = 0; i < 400 && session.Player.Order != null; i++)
        {
            session.Creatures.SyncGrid();
            session.Creatures.StepCreature(rabbit, 0.25);
        }

        double endDist = Math.Sqrt(Math.Pow(goal.X - rabbit.X, 2) + Math.Pow(goal.Y - rabbit.Y, 2));
        Assert.That(session.Player.Order, Is.Null, "move order should clear on arrival");
        Assert.That(endDist, Is.LessThan(Math.Max(0.6, startDist)), "creature should reach the clicked goal");
    }

    [Test]
    public void ClickWater_TravelsToShoreAndDrinks()
    {
        var session = NewSession();
        var rabbit = SpawnOnLand(session, "rabbit");
        session.Player.Possess(rabbit);
        rabbit.Thirst = 40;
        rabbit.Hunger = 95;

        // Start at a real shoreline so the walk is trivial, then click the adjacent water.
        var edge = FindWaterEdgeTile(session);
        rabbit.X = edge.LandX + 0.5;
        rabbit.Y = edge.LandY + 0.5;
        session.Creatures.SyncGrid();

        session.Player.IssueClickOrder(edge.WaterX + 0.5, edge.WaterY + 0.5, null);
        Assert.That(session.Player.Order, Is.Not.Null);
        Assert.That(session.Player.Order!.Kind, Is.EqualTo(PlayerOrderKind.DrinkAt));

        for (int i = 0; i < 300 && session.Player.Order != null; i++)
        {
            session.Creatures.SyncGrid();
            session.Creatures.StepCreature(rabbit, 0.25);
        }

        Assert.That(rabbit.Thirst, Is.GreaterThan(90), "drink order should refill thirst");
        Assert.That(session.Player.Order, Is.Null, "drink order should complete when full");
    }

    [Test]
    public void Carnivore_ClickPrey_HuntsKillsAndFeeds()
    {
        var session = NewSession();
        var fox = SpawnOnLand(session, "fox");
        var rabbit = session.Creatures.MakeCreature("rabbit", fox.X + 3, fox.Y);
        fox.Genome.Agg = 1.0;
        fox.Hunger = 40;
        rabbit.Hp = 10;
        session.Player.Possess(fox);

        session.Player.IssueClickOrder(rabbit.X, rabbit.Y, rabbit);
        Assert.That(session.Player.Order!.Kind, Is.EqualTo(PlayerOrderKind.Hunt));

        for (int i = 0; i < 300 && !rabbit.Dead; i++)
        {
            // Hold the prey in place so the chase is deterministic.
            rabbit.X = fox.X + Math.Min(3, Math.Abs(rabbit.X - fox.X));
            rabbit.Y = fox.Y;
            session.Creatures.SyncGrid();
            session.Creatures.StepCreature(fox, 0.25);
        }

        Assert.That(rabbit.Dead, Is.True, "hunt order should kill the clicked prey");
        Assert.That(rabbit.KilledById, Is.EqualTo(fox.Id));
        Assert.That(fox.Hunger, Is.GreaterThan(40), "the kill should feed the hunter");
        Assert.That(session.Player.Order, Is.Null, "hunt order should complete on the kill");
    }

    [Test]
    public void Herbivore_ClickVegetation_TravelsAndGrazes()
    {
        var session = NewSession();
        var deer = SpawnOnLand(session, "deer");
        session.Player.Possess(deer);
        deer.Hunger = 40;
        deer.Thirst = 95;

        int gx = (int)Math.Round(deer.X) + 3, gy = (int)Math.Round(deer.Y);
        int ti = gy * session.State.W + gx;
        session.State.Biome[ti] = (byte)Biome.Grass;
        session.State.VegCap[ti] = 1f;
        session.State.Veg[ti] = 1f;

        session.Player.IssueClickOrder(gx, gy, null);
        Assert.That(session.Player.Order!.Kind, Is.EqualTo(PlayerOrderKind.GrazeAt));

        double hunger = deer.Hunger;
        for (int i = 0; i < 200 && session.Player.Order != null; i++)
        {
            session.Creatures.SyncGrid();
            session.Creatures.StepCreature(deer, 0.25);
        }

        Assert.That(deer.Hunger, Is.GreaterThan(hunger), "graze order should feed the creature");
    }

    [Test]
    public void ClickThreat_FleesFromIt()
    {
        var session = NewSession();
        var rabbit = SpawnOnLand(session, "rabbit");
        var wolf = session.Creatures.MakeCreature("wolf", rabbit.X + 2, rabbit.Y);
        session.Player.Possess(rabbit);

        session.Player.IssueClickOrder(wolf.X, wolf.Y, wolf);
        Assert.That(session.Player.Order!.Kind, Is.EqualTo(PlayerOrderKind.FleeFrom));

        double startDist = Math.Sqrt(Math.Pow(wolf.X - rabbit.X, 2) + Math.Pow(wolf.Y - rabbit.Y, 2));
        session.Creatures.SyncGrid();
        session.Creatures.StepCreature(rabbit, 0.25);
        Assert.That(rabbit.State, Is.EqualTo("flee"), "flee order should set the flee state while active");

        for (int i = 0; i < 40 && session.Player.Order != null; i++)
        {
            session.Creatures.SyncGrid();
            session.Creatures.StepCreature(rabbit, 0.25);
        }

        double endDist = Math.Sqrt(Math.Pow(wolf.X - rabbit.X, 2) + Math.Pow(wolf.Y - rabbit.Y, 2));
        Assert.That(endDist, Is.GreaterThan(startDist), "flee order should gain distance from the threat");
        Assert.That(session.Player.Order, Is.Null, "flee order should complete once safely away");
    }

    [Test]
    public void ClickMate_ApproachesAndConsummates()
    {
        var session = NewSession();
        var t = session.Creatures.FindSpawnTile("rabbit")!;
        var male = session.Creatures.MakeCreature("rabbit", t.Value.X, t.Value.Y, sex: "male");
        var female = session.Creatures.MakeCreature("rabbit", t.Value.X + 2.5, t.Value.Y, sex: "female");
        foreach (var c in new[] { male, female })
        {
            c.Age = c.Genome.Lifespan * 0.5;
            c.MateCd = 0;
            c.Pregnant = 0;
            c.Energy = 90;
        }

        session.Player.Possess(male);
        session.Player.IssueClickOrder(female.X, female.Y, female);
        Assert.That(session.Player.Order!.Kind, Is.EqualTo(PlayerOrderKind.MateWith));

        for (int i = 0; i < 100 && session.Player.Order != null; i++)
        {
            session.Creatures.SyncGrid();
            session.Creatures.StepCreature(male, 0.25);
        }

        Assert.That(female.Pregnant, Is.GreaterThan(0), "mate order should consummate on contact");
        Assert.That(session.Player.Order, Is.Null);
    }

    [Test]
    public void ClickNeutralAnimal_JustMovesToward()
    {
        var session = NewSession();
        var rabbit = SpawnOnLand(session, "rabbit");
        var deer = session.Creatures.MakeCreature("deer", rabbit.X + 4, rabbit.Y);
        session.Player.Possess(rabbit);

        // Deer neither hunts rabbits nor is hunted by them.
        session.Player.IssueClickOrder(deer.X, deer.Y, deer);
        Assert.That(session.Player.Order!.Kind, Is.EqualTo(PlayerOrderKind.MoveTo));
    }

    [Test]
    public void AttackKey_DamagesAndKillsPrey_RecordingKiller()
    {
        var session = NewSession();
        var fox = SpawnOnLand(session, "fox");
        var rabbit = session.Creatures.MakeCreature("rabbit", fox.X + 0.4, fox.Y);
        fox.Genome.Agg = 1.0;
        rabbit.Hp = 10;
        session.Player.Possess(fox);

        for (int i = 0; i < 100 && !rabbit.Dead; i++)
        {
            rabbit.X = fox.X + 0.4;
            rabbit.Y = fox.Y;
            session.Creatures.SyncGrid();
            session.Player.Intents.AttackPressed = true;
            session.Creatures.StepCreature(fox, 0.25);
        }

        Assert.That(rabbit.Dead, Is.True, "player attack should kill weakened adjacent prey");
        Assert.That(rabbit.Cause, Is.EqualTo("predation"));
        Assert.That(rabbit.KilledById, Is.EqualTo(fox.Id), "killer must be recorded");
    }

    [Test]
    public void AttackKey_IsNoOpForHerbivore()
    {
        var session = NewSession();
        var rabbit = SpawnOnLand(session, "rabbit");
        var mouse = session.Creatures.MakeCreature("mouse", rabbit.X + 0.3, rabbit.Y);
        mouse.Hp = 100;
        session.Player.Possess(rabbit);

        for (int i = 0; i < 20; i++)
        {
            session.Creatures.SyncGrid();
            session.Player.Intents.AttackPressed = true;
            session.Creatures.StepCreature(rabbit, 0.25);
        }

        Assert.That(mouse.Hp, Is.EqualTo(100), "herbivores have no attack");
    }

    [TestCase("male")]
    [TestCase("female")]
    public void MateKey_ConsummatesFromEitherSex(string playerSex)
    {
        var session = NewSession();
        var t = session.Creatures.FindSpawnTile("rabbit")!;
        var player = session.Creatures.MakeCreature("rabbit", t.Value.X, t.Value.Y, sex: playerSex);
        var partner = session.Creatures.MakeCreature(
            "rabbit", t.Value.X + 0.4, t.Value.Y, sex: playerSex == "male" ? "female" : "male");
        foreach (var c in new[] { player, partner })
        {
            c.Age = c.Genome.Lifespan * 0.5;
            c.MateCd = 0;
            c.Pregnant = 0;
            c.Energy = 90;
        }

        session.Player.Possess(player);
        session.Creatures.SyncGrid();
        session.Player.Intents.MatePressed = true;
        session.Creatures.StepCreature(player, 0.25);

        var female = player.Sex == "female" ? player : partner;
        Assert.That(female.Pregnant, Is.GreaterThan(0), "mating should start a pregnancy");
        Assert.That(female.MatePartnerId, Is.EqualTo(player.Sex == "male" ? player.Id : partner.Id));
    }

    [Test]
    public void AutoActions_DrinkGrazeRest_TriggerContextually()
    {
        var session = NewSession();
        var rabbit = SpawnOnLand(session, "rabbit");
        session.Player.Possess(rabbit);

        // Rest: low energy, no input.
        rabbit.Energy = 20;
        rabbit.Hunger = 90;
        rabbit.Thirst = 90;
        double energy = rabbit.Energy;
        session.Creatures.SyncGrid();
        session.Creatures.StepCreature(rabbit, 0.5);
        Assert.That(rabbit.State, Is.EqualTo("rest"));
        Assert.That(rabbit.Energy, Is.GreaterThan(energy - 0.5), "resting should offset energy drain");

        // Graze: hungry on an edible tile.
        int ti = (int)Math.Round(rabbit.Y) * session.State.W + (int)Math.Round(rabbit.X);
        session.State.Biome[ti] = (byte)Biome.Grass;
        session.State.VegCap[ti] = 1f;
        session.State.Veg[ti] = 1f;
        rabbit.Hunger = 40;
        rabbit.Energy = 80;
        double hunger = rabbit.Hunger;
        session.Creatures.StepCreature(rabbit, 0.5);
        Assert.That(rabbit.State, Is.EqualTo("graze"));
        Assert.That(rabbit.Hunger, Is.GreaterThan(hunger), "grazing should feed the creature");
    }
}

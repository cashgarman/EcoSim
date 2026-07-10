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
    public void Wasd_MovesCreature_AndCancelsClickGoal()
    {
        var session = NewSession();
        var rabbit = SpawnOnLand(session, "rabbit");
        session.Player.Possess(rabbit);

        session.Player.Intents.ClickGoalX = rabbit.X + 5;
        session.Player.Intents.ClickGoalY = rabbit.Y;
        Assert.That(session.Player.Intents.HasClickGoal, Is.True);

        double startX = rabbit.X;
        session.Player.Intents.MoveX = 1;
        session.Creatures.SyncGrid();
        session.Creatures.StepCreature(rabbit, 0.25);

        Assert.That(rabbit.X, Is.GreaterThan(startX), "WASD steering should move the creature");
        Assert.That(session.Player.Intents.HasClickGoal, Is.False, "WASD must cancel the click path");
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
    public void ClickGoal_PathfindsAndArrives()
    {
        var session = NewSession();
        var rabbit = SpawnOnLand(session, "rabbit");
        session.Player.Possess(rabbit);

        var goal = Navigation.PickRandomWalkableTile(session.State, rabbit.X, rabbit.Y, 4, false);
        session.Player.Intents.ClickGoalX = goal.X;
        session.Player.Intents.ClickGoalY = goal.Y;
        double startDist = Math.Sqrt(Math.Pow(goal.X - rabbit.X, 2) + Math.Pow(goal.Y - rabbit.Y, 2));

        for (int i = 0; i < 400 && session.Player.Intents.HasClickGoal; i++)
        {
            session.Creatures.SyncGrid();
            session.Creatures.StepCreature(rabbit, 0.25);
        }

        double endDist = Math.Sqrt(Math.Pow(goal.X - rabbit.X, 2) + Math.Pow(goal.Y - rabbit.Y, 2));
        Assert.That(session.Player.Intents.HasClickGoal, Is.False, "click goal should clear on arrival");
        Assert.That(endDist, Is.LessThan(Math.Max(0.6, startDist)), "creature should reach the clicked goal");
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

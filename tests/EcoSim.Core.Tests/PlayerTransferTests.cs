using EcoSim.Core.Data;
using EcoSim.Core.Sim;
using NUnit.Framework;

namespace EcoSim.Core.Tests;

[TestFixture]
public class PlayerTransferTests
{
    private string _repoRoot = "";

    [OneTimeSetUp]
    public void SetUp()
    {
        _repoRoot = TestPaths.FindRepoRoot();
        DataPaths.SetDataRoot(_repoRoot);
    }

    private SimSession NewSession(uint seed = 11)
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

    private static T ExpectEvent<T>(SimSession session) where T : PlayerEvent
    {
        while (session.Player.PendingEvents.Count > 0)
        {
            if (session.Player.PendingEvents.Dequeue() is T match) return match;
        }
        Assert.Fail($"expected a {typeof(T).Name} in the player event queue");
        throw new InvalidOperationException();
    }

    [Test]
    public void KilledByPredator_TransfersControlToKiller()
    {
        var session = NewSession();
        var rabbit = SpawnOnLand(session, "rabbit");
        var wolf = session.Creatures.MakeCreature("wolf", rabbit.X + 0.3, rabbit.Y);
        wolf.Genome.Agg = 1.0;
        rabbit.Hp = 5;
        session.Player.Possess(rabbit);
        session.Player.PendingEvents.Clear();
        session.Creatures.SyncGrid();

        for (int i = 0; i < 200 && !rabbit.Dead; i++)
        {
            CreatureActions.TryHuntStrike(session.Creatures, wolf, rabbit);
        }

        Assert.That(rabbit.Dead, Is.True);
        Assert.That(session.Player.ControlledId, Is.EqualTo(wolf.Id), "control must pass to the killer");
        var ev = ExpectEvent<TransferEvent>(session);
        Assert.That(ev.Reason, Is.EqualTo("killer"));
        Assert.That(ev.To.Id, Is.EqualTo(wolf.Id));
    }

    [Test]
    public void NaturalDeath_PrefersLivingSibling()
    {
        var session = NewSession();
        var player = SpawnOnLand(session, "rabbit");
        var sibling = session.Creatures.MakeCreature("rabbit", player.X + 1, player.Y);
        var unrelated = session.Creatures.MakeCreature("rabbit", player.X + 2, player.Y);
        player.ParentIds.Add(9999);
        sibling.ParentIds.Add(9999);
        unrelated.ParentIds.Add(8888);

        session.Player.Possess(player);
        session.Player.PendingEvents.Clear();
        session.Creatures.Die(player, "old age");

        Assert.That(session.Player.ControlledId, Is.EqualTo(sibling.Id), "sibling must be preferred");
        Assert.That(ExpectEvent<TransferEvent>(session).Reason, Is.EqualTo("sibling"));
    }

    [Test]
    public void NaturalDeath_NoSibling_FallsBackToSameSpecies()
    {
        var session = NewSession();
        session.State.Creatures.RemoveAll(c => c.Sp == "rabbit");
        session.Creatures.RebuildGrid();
        var player = SpawnOnLand(session, "rabbit");
        var other = session.Creatures.MakeCreature("rabbit", player.X + 2, player.Y);
        player.ParentIds.Add(9999);

        session.Player.Possess(player);
        session.Player.PendingEvents.Clear();
        session.Creatures.Die(player, "starvation");

        Assert.That(session.Player.ControlledId, Is.EqualTo(other.Id));
        Assert.That(ExpectEvent<TransferEvent>(session).Reason, Is.EqualTo("species"));
    }

    [Test]
    public void LastOfSpeciesDies_TriggersGameOver()
    {
        var session = NewSession();
        session.State.Creatures.RemoveAll(c => c.Sp == "rabbit");
        session.Creatures.RebuildGrid();
        var player = SpawnOnLand(session, "rabbit");

        session.Player.Possess(player);
        session.Player.PendingEvents.Clear();
        session.Creatures.Die(player, "old age");

        Assert.That(session.Player.ControlledId, Is.Null, "no body left to inhabit");
        var over = ExpectEvent<GameOverEvent>(session);
        Assert.That(over.Species, Is.EqualTo("rabbit"));
    }

    [Test]
    public void Transfer_SurvivesFullSimulationTick()
    {
        var session = NewSession();
        var player = SpawnOnLand(session, "rabbit");
        var sibling = session.Creatures.MakeCreature("rabbit", player.X + 1, player.Y);
        player.ParentIds.Add(7777);
        sibling.ParentIds.Add(7777);
        sibling.Hunger = 90;
        sibling.Thirst = 90;
        // Player dies of dehydration during StepNeeds inside the tick.
        player.Hp = 0.01;
        player.Thirst = 0;
        player.Hunger = 50;

        session.Player.Possess(player);
        session.Player.PendingEvents.Clear();
        session.Simulation.Tick(0.5);

        Assert.That(player.Dead, Is.True);
        Assert.That(session.Player.ControlledId, Is.EqualTo(sibling.Id),
            "transfer must resolve before PruneDead in the same tick");
        Assert.That(session.Player.Controlled, Is.Not.Null, "new body must survive PruneDead");
    }

    [TestCase("female")]
    [TestCase("male")]
    public void Birth_AwardsPointAndQueuesChoice_ForEitherParent(string playerSex)
    {
        var session = NewSession();
        var t = session.Creatures.FindSpawnTile("rabbit")!;
        var mother = session.Creatures.MakeCreature("rabbit", t.Value.X, t.Value.Y, sex: "female");
        var father = session.Creatures.MakeCreature("rabbit", t.Value.X + 0.4, t.Value.Y, sex: "male");
        var player = playerSex == "female" ? mother : father;

        mother.Age = mother.Genome.Lifespan * 0.5;
        mother.MatePartner = father.Genome;
        mother.MatePartnerId = father.Id;
        mother.LitterQ = 2;

        session.Player.Possess(player);
        session.Player.PendingEvents.Clear();
        int born = session.Creatures.GiveBirth(mother);

        Assert.That(born, Is.GreaterThan(0));
        Assert.That(session.Progress.Points("rabbit"), Is.EqualTo(1), "one point per litter");
        var ev = ExpectEvent<BirthChoiceEvent>(session);
        Assert.That(ev.Species, Is.EqualTo("rabbit"));
        Assert.That(ev.NewbornIds, Has.Count.EqualTo(born));

        // The player can possess a newborn from the event.
        var newborn = session.Creatures.GetById(ev.NewbornIds[0]);
        Assert.That(newborn, Is.Not.Null);
        session.Player.Possess(newborn!);
        Assert.That(session.Player.ControlledId, Is.EqualTo(newborn!.Id));
    }

    [Test]
    public void AiBirth_DoesNotAwardPlayerPoints()
    {
        var session = NewSession();
        var t = session.Creatures.FindSpawnTile("rabbit")!;
        var mother = session.Creatures.MakeCreature("rabbit", t.Value.X, t.Value.Y, sex: "female");
        var bystander = session.Creatures.MakeCreature("rabbit", t.Value.X + 3, t.Value.Y);
        mother.LitterQ = 1;

        session.Player.Possess(bystander);
        session.Player.PendingEvents.Clear();
        session.Creatures.GiveBirth(mother);

        Assert.That(session.Progress.Points("rabbit"), Is.EqualTo(0));
        Assert.That(session.Player.PendingEvents, Is.Empty);
    }
}

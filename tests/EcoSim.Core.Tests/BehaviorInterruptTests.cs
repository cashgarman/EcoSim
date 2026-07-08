using System.Text.Json.Nodes;
using EcoSim.Core.Behavior;
using EcoSim.Core.Data;
using EcoSim.Core.Sim;
using NUnit.Framework;

namespace EcoSim.Core.Tests;

[TestFixture]
public class BehaviorInterruptTests
{
  private string _repoRoot = "";

  [OneTimeSetUp]
  public void SetUp()
  {
    _repoRoot = FindRepoRoot();
    DataPaths.SetDataRoot(_repoRoot);
  }

  [Test]
  public void Mate_InterruptedByHunger_SwitchesToGraze()
  {
    var session = SimSession.Create(_repoRoot, 7);
    session.State.Cfg.Size = "s";
    session.GenerateWorld();

    var rabbit = session.State.Creatures.First(c => c.Sp == "rabbit" && !c.Dead);
    var mateAction = session.Species.Get("rabbit").BehaviorConfig!.Actions["Mate"];

    int ti = GridHelpers.Idx(session.State, (int)Math.Round(rabbit.X), (int)Math.Round(rabbit.Y));
    session.State.Veg[ti] = Math.Max(session.State.Veg[ti], 0.8f);

    rabbit.State = "mate";
    rabbit.BtAction = mateAction;
    rabbit.BtNodeId = "Mate";
    rabbit.BtBranchUid = "rabbit/herbivore_prey/sel/mate_branch/Mate";
    rabbit.StateCommittedSince = session.State.TGlobal;
    rabbit.Hunger = 30;
    rabbit.Thirst = 80;
    rabbit.Energy = 80;

    session.BehaviorTree.Tick(rabbit, 0.5, session.Creatures, executeActions: true);

    Assert.That(rabbit.State, Is.EqualTo("graze"));
  }

  [Test]
  public void Wander_InterruptedByThirst_SwitchesImmediately()
  {
    var session = SimSession.Create(_repoRoot, 11);
    session.State.Cfg.Size = "s";
    session.GenerateWorld();

    var rabbit = session.State.Creatures.First(c => c.Sp == "rabbit" && !c.Dead);
    var wanderAction = session.Species.Get("rabbit").BehaviorConfig!.Actions["Wander"];

    rabbit.State = "wander";
    rabbit.BtAction = wanderAction;
    rabbit.BtNodeId = "Wander";
    rabbit.StateCommittedSince = session.State.TGlobal;
    rabbit.Hunger = 80;
    rabbit.Thirst = 25;
    rabbit.Energy = 80;

    session.BehaviorTree.Tick(rabbit, 0.5, session.Creatures, executeActions: true);

    Assert.That(rabbit.State, Is.EqualTo("thirst"));
  }

  [Test]
  public void Graze_Hysteresis_KeepsGrazingUntilExit()
  {
    var session = SimSession.Create(_repoRoot, 13);
    session.State.Cfg.Size = "s";
    session.GenerateWorld();

    var rabbit = session.State.Creatures.First(c => c.Sp == "rabbit" && !c.Dead);
    var grazeAction = session.Species.Get("rabbit").BehaviorConfig!.Actions["Graze"];

    int ti = GridHelpers.Idx(session.State, (int)Math.Round(rabbit.X), (int)Math.Round(rabbit.Y));
    session.State.Veg[ti] = Math.Max(session.State.Veg[ti], 0.8f);

    rabbit.State = "graze";
    rabbit.BtAction = grazeAction;
    rabbit.BtNodeId = "Graze";
    rabbit.StateCommittedSince = session.State.TGlobal - 5;
    rabbit.Hunger = 58;
    rabbit.Thirst = 80;
    rabbit.Energy = 80;
    rabbit.Target = null;

    foreach (var other in session.State.Creatures.Where(c => c.Sp is "fox" or "wolf" or "hawk" or "owl" or "bear"))
    {
      other.X = 0;
      other.Y = 0;
    }
    session.Creatures.RebuildGrid();

    session.BehaviorTree.Tick(rabbit, 0.5, session.Creatures, executeActions: true);

    Assert.That(rabbit.State, Is.EqualTo("graze"));
  }

  [Test]
  public void Stalk_InterruptedByUrgentEnergy_SwitchesToRest()
  {
    var session = SimSession.Create(_repoRoot, 17);
    session.State.Cfg.Size = "s";
    session.GenerateWorld();

    var wolf = session.State.Creatures.First(c => c.Sp == "wolf" && !c.Dead);
    var stalkAction = session.Species.Get("wolf").BehaviorConfig!.Actions["StalkPrey"];

    wolf.State = "huntSearch";
    wolf.BtAction = stalkAction;
    wolf.BtNodeId = "StalkPrey";
    wolf.StateCommittedSince = session.State.TGlobal;
    wolf.Hunger = 80;
    wolf.Thirst = 80;
    wolf.Energy = 10;

    session.BehaviorTree.Tick(wolf, 0.5, session.Creatures, executeActions: true);

    Assert.That(wolf.State, Is.EqualTo("rest"));
  }

  [Test]
  public void Flee_AlwaysWins_OverCommittedMate()
  {
    var session = SimSession.Create(_repoRoot, 19);
    session.State.Cfg.Size = "s";
    session.GenerateWorld();

    var rabbit = session.State.Creatures.First(c => c.Sp == "rabbit" && !c.Dead);
    var predator = session.State.Creatures.First(c =>
      c.Sp == "fox" && !c.Dead && c.Id != rabbit.Id);
    var mateAction = session.Species.Get("rabbit").BehaviorConfig!.Actions["Mate"];

    session.Creatures.RebuildGrid();
    var pos = session.Creatures.SimPos(rabbit);
    predator.X = pos.X + 0.5;
    predator.Y = pos.Y + 0.5;
    session.Creatures.RebuildGrid();

    rabbit.State = "mate";
    rabbit.BtAction = mateAction;
    rabbit.BtNodeId = "Mate";
    rabbit.StateCommittedSince = session.State.TGlobal;
    rabbit.Hunger = 80;
    rabbit.Thirst = 80;
    rabbit.Energy = 80;

    session.BehaviorTree.Tick(rabbit, 0.5, session.Creatures, executeActions: true);

    Assert.That(rabbit.State, Is.EqualTo("flee"));
  }

  [Test]
  public void SameTierWander_BlocksRapidReselectionWithinDwell()
  {
    var session = SimSession.Create(_repoRoot, 23);
    session.State.Cfg.Size = "s";
    session.GenerateWorld();

    var rabbit = session.State.Creatures.First(c => c.Sp == "rabbit" && !c.Dead);
    var wanderAction = session.Species.Get("rabbit").BehaviorConfig!.Actions["Wander"];

    rabbit.State = "wander";
    rabbit.BtAction = wanderAction;
    rabbit.BtNodeId = "Wander";
    rabbit.Tx = 12.5;
    rabbit.Ty = 14.5;
    rabbit.StateCommittedSince = session.State.TGlobal;
    session.State.TGlobal += 0.1;

    var decision = session.BehaviorTree.Decide(rabbit, session.Creatures);
    Assert.That(decision, Is.Not.Null);
    Assert.That(decision!.Action["state"]?.GetValue<string>(), Is.EqualTo("wander"));

    bool apply = InvokeShouldApply(session, rabbit, decision);
    Assert.That(apply, Is.False, "same-tier wander should respect min commit dwell");
  }

  private static bool InvokeShouldApply(SimSession session, Creature creature, BehaviorDecision proposed)
  {
    var method = typeof(BehaviorTree).GetMethod("ShouldApplyDecision",
      System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    Assert.That(method, Is.Not.Null);
    return (bool)method!.Invoke(session.BehaviorTree, [creature, proposed])!;
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

using EcoSim.Core;
using EcoSim.Core.Behavior;
using EcoSim.Core.Data;
using EcoSim.Core.Sim;
using NUnit.Framework;

namespace EcoSim.Core.Tests;

[TestFixture]
public class BehaviorEvaluatorTraceTests
{
  private string _repoRoot = "";

  [OneTimeSetUp]
  public void SetUp()
  {
    _repoRoot = FindRepoRoot();
    DataPaths.SetDataRoot(_repoRoot);
  }

  [Test]
  public void ThirstBranch_PassesWhenThirsty_AndSelectsSeekWater()
  {
    var session = SimSession.Create(_repoRoot, 21);
    session.State.Cfg.Size = "s";
    session.GenerateWorld();

    var rabbit = session.State.Creatures.First(c => c.Sp == "rabbit" && !c.Dead);
  foreach (var predator in session.State.Creatures.Where(c => c.Sp is "fox" or "wolf" or "hawk" or "owl" or "bear"))
    {
      predator.X = 0;
      predator.Y = 0;
    }
    session.Creatures.RebuildGrid();

    rabbit.Hunger = 80;
    rabbit.Thirst = 20;
    rabbit.Energy = 80;
    rabbit.State = "wander";

    var trace = session.BehaviorTree.EvaluateWithTrace(rabbit, session.Creatures);

    Assert.That(trace.Winner, Is.Not.Null);
    Assert.That(trace.Winner!.NodeId, Is.EqualTo("SeekWater"));
    Assert.That(trace.Steps.Any(s => s.Id == "Thirsty" && s.Outcome == TraceOutcome.Passed), Is.True);
    Assert.That(trace.Steps.Any(s => s.Id == "SeekWater" && s.Outcome == TraceOutcome.Selected), Is.True);
    Assert.That(trace.Steps.Any(s => s.Id == "Thirsty" && s.Detail != null && s.Detail.Contains("thirst=20")), Is.True);
  }

  [Test]
  public void ThirstBranch_FailsWhenHydrated()
  {
    var session = SimSession.Create(_repoRoot, 23);
    session.State.Cfg.Size = "s";
    session.GenerateWorld();

    var rabbit = session.State.Creatures.First(c => c.Sp == "rabbit" && !c.Dead);
  foreach (var predator in session.State.Creatures.Where(c => c.Sp is "fox" or "wolf" or "hawk" or "owl" or "bear"))
    {
      predator.X = 0;
      predator.Y = 0;
    }
    session.Creatures.RebuildGrid();

    rabbit.Hunger = 80;
    rabbit.Thirst = 90;
    rabbit.Energy = 80;
    rabbit.State = "wander";

    var cfg = session.Species.Get("rabbit").BehaviorConfig!;
    var ctx = BehaviorContextBuilder.Build(rabbit, session.Creatures, session.State, session.Species);
    var trace = BehaviorEvaluator.EvaluateTreeWithTrace(session.State, cfg.Root, ctx);

    Assert.That(trace.Steps.Any(s => s.Id == "Thirsty" && s.Outcome == TraceOutcome.Failed), Is.True);
    Assert.That(trace.Winner?.NodeId, Is.Not.EqualTo("SeekWater"));
  }

  [Test]
  public void Selector_RecordsFailedSiblingsBeforeWinner()
  {
    var session = SimSession.Create(_repoRoot, 25);
    session.State.Cfg.Size = "s";
    session.GenerateWorld();

    var rabbit = session.State.Creatures.First(c => c.Sp == "rabbit" && !c.Dead);
    rabbit.Hunger = 80;
    rabbit.Thirst = 20;
    rabbit.Energy = 80;
    rabbit.State = "wander";

    foreach (var predator in session.State.Creatures.Where(c => c.Sp is "fox" or "wolf" or "hawk" or "owl" or "bear"))
    {
      predator.X = 0;
      predator.Y = 0;
    }
    session.Creatures.RebuildGrid();

    var trace = session.BehaviorTree.EvaluateWithTrace(rabbit, session.Creatures);

    Assert.That(trace.Winner!.NodeId, Is.EqualTo("SeekWater"));
    Assert.That(trace.Steps.Any(s => s.Type == "selector" && s.Outcome == TraceOutcome.Passed), Is.True);
  }

  [Test]
  public void PeekDecision_ShowsBlockedMateCommit()
  {
    var session = SimSession.Create(_repoRoot, 27);
    session.State.Cfg.Size = "s";
    session.GenerateWorld();

    var rabbit = session.State.Creatures.First(c => c.Sp == "rabbit" && !c.Dead);
    var mateAction = session.Species.Get("rabbit").BehaviorConfig!.Actions["Mate"];

  foreach (var predator in session.State.Creatures.Where(c => c.Sp is "fox" or "wolf" or "hawk" or "owl" or "bear"))
    {
      predator.X = 0;
      predator.Y = 0;
    }
    session.Creatures.RebuildGrid();

    rabbit.State = "mate";
    rabbit.BtAction = mateAction;
    rabbit.BtNodeId = "Mate";
    rabbit.StateCommittedSince = session.State.TGlobal;
    rabbit.Hunger = 30;
    rabbit.Thirst = 80;
    rabbit.Energy = 80;

    var (proposed, wouldApply) = session.BehaviorTree.PeekDecision(rabbit, session.Creatures);

    Assert.That(proposed, Is.Not.Null);
    Assert.That(proposed!.Action["state"]?.GetValue<string>(), Is.EqualTo("graze"));
    Assert.That(wouldApply, Is.True);
  }

  [Test]
  public void GraphLayout_AssignsPositionsWhenUnset()
  {
    var session = SimSession.Create(_repoRoot, 29);
    var cfg = session.Species.Get("rabbit").BehaviorConfig!;
    var doc = BehaviorGraphAdapter.ToFlatDocument(cfg);

    Assert.That(doc.Nodes.All(n => n.X == 0 && n.Y == 0), Is.True);
    BehaviorGraphLayout.ApplyAutoLayout(doc);
    Assert.That(doc.Nodes.Any(n => n.Y > 0), Is.True);
    Assert.That(doc.Nodes.Select(n => n.X).Distinct().Count(), Is.GreaterThan(1));
  }

  private static string FindRepoRoot() => TestPaths.FindRepoRoot();
}

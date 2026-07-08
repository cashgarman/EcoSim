using System.Text.Json.Nodes;
using EcoSim.Core.Sim;

namespace EcoSim.Core.Behavior;

public sealed class BehaviorDecision
{
  public string? NodeId { get; init; }
  public string? BranchUid { get; init; }
  public required JsonObject Action { get; init; }
  public required BehaviorContext Ctx { get; init; }
}

public sealed class TreeEvalResult
{
  public string? NodeId { get; init; }
  public string? BranchUid { get; init; }
  public JsonObject? Action { get; init; }
  public bool ConditionPassed { get; init; }
}

public enum TraceOutcome
{
  Passed,
  Failed,
  Selected,
  Skipped,
}

public sealed class BehaviorTraceStep
{
  public required string Uid { get; init; }
  public required string Type { get; init; }
  public string? Id { get; init; }
  public TraceOutcome Outcome { get; init; }
  public string? Detail { get; init; }
}

public sealed class BehaviorEvalTrace
{
  public TreeEvalResult? Winner { get; init; }
  public List<BehaviorTraceStep> Steps { get; init; } = [];
}

public static class BehaviorEvaluator
{
  public static TreeEvalResult? EvaluateTree(SimState state, BehaviorTreeNode? node, BehaviorContext ctx)
  {
    if (node == null) return null;

    if (node.Type == BehaviorNodeType.Action && node.Action != null)
    {
      return new TreeEvalResult
      {
        NodeId = node.Id,
        BranchUid = node.Uid,
        Action = node.Action,
      };
    }

    if (node.Type == BehaviorNodeType.Condition && node.Condition != null)
    {
      bool ok = ConditionEvaluator.Evaluate(state, node.Id ?? "", node.Condition, ctx);
      return ok ? new TreeEvalResult { NodeId = node.Id, BranchUid = node.Uid, ConditionPassed = true } : null;
    }

    if (node.Type == BehaviorNodeType.Sequence)
    {
      TreeEvalResult? lastAction = null;
      foreach (var child in node.Children)
      {
        var result = EvaluateTree(state, child, ctx);
        if (result == null) return null;
        if (result.Action != null) lastAction = result;
      }
      return lastAction;
    }

    if (node.Type == BehaviorNodeType.Selector)
    {
      foreach (var child in node.Children)
      {
        var result = EvaluateTree(state, child, ctx);
        if (result?.Action != null) return result;
      }
      return null;
    }

    return null;
  }

  public static BehaviorEvalTrace EvaluateTreeWithTrace(SimState state, BehaviorTreeNode? node, BehaviorContext ctx)
  {
    var steps = new List<BehaviorTraceStep>();
    var winner = EvaluateTreeWithTraceInner(state, node, ctx, steps);
    return new BehaviorEvalTrace
    {
      Winner = winner,
      Steps = steps,
    };
  }

  private static TreeEvalResult? EvaluateTreeWithTraceInner(
    SimState state,
    BehaviorTreeNode? node,
    BehaviorContext ctx,
    List<BehaviorTraceStep> steps)
  {
    if (node == null) return null;

    if (node.Type == BehaviorNodeType.Action && node.Action != null)
    {
      steps.Add(new BehaviorTraceStep
      {
        Uid = node.Uid,
        Type = "actionRef",
        Id = node.Id,
        Outcome = TraceOutcome.Selected,
        Detail = node.Action["state"]?.GetValue<string>(),
      });
      return new TreeEvalResult
      {
        NodeId = node.Id,
        BranchUid = node.Uid,
        Action = node.Action,
      };
    }

    if (node.Type == BehaviorNodeType.Condition && node.Condition != null)
    {
      bool ok = ConditionEvaluator.Evaluate(state, node.Id ?? "", node.Condition, ctx);
      string detail = ConditionEvaluator.Describe(state, node.Id ?? "", node.Condition, ctx, ok);
      steps.Add(new BehaviorTraceStep
      {
        Uid = node.Uid,
        Type = "conditionRef",
        Id = node.Id,
        Outcome = ok ? TraceOutcome.Passed : TraceOutcome.Failed,
        Detail = detail,
      });
      return ok
        ? new TreeEvalResult { NodeId = node.Id, BranchUid = node.Uid, ConditionPassed = true }
        : null;
    }

    if (node.Type == BehaviorNodeType.Sequence)
    {
      TreeEvalResult? lastAction = null;
      for (int i = 0; i < node.Children.Count; i++)
      {
        var result = EvaluateTreeWithTraceInner(state, node.Children[i], ctx, steps);
        if (result == null)
        {
          steps.Add(new BehaviorTraceStep
          {
            Uid = node.Uid,
            Type = "sequence",
            Id = node.Id,
            Outcome = TraceOutcome.Failed,
          });
          return null;
        }

        if (result.Action != null)
        {
          lastAction = result;
          if (i < node.Children.Count - 1)
          {
            DemoteSelectedToPassed(steps, result.BranchUid);
          }
        }
      }

      steps.Add(new BehaviorTraceStep
      {
        Uid = node.Uid,
        Type = "sequence",
        Id = node.Id,
        Outcome = TraceOutcome.Passed,
      });
      return lastAction;
    }

    if (node.Type == BehaviorNodeType.Selector)
    {
      foreach (var child in node.Children)
      {
        var result = EvaluateTreeWithTraceInner(state, child, ctx, steps);
        if (result?.Action != null)
        {
          steps.Add(new BehaviorTraceStep
          {
            Uid = node.Uid,
            Type = "selector",
            Id = node.Id,
            Outcome = TraceOutcome.Passed,
          });
          return result;
        }
      }

      steps.Add(new BehaviorTraceStep
      {
        Uid = node.Uid,
        Type = "selector",
        Id = node.Id,
        Outcome = TraceOutcome.Failed,
      });
      return null;
    }

    return null;
  }

  private static void DemoteSelectedToPassed(List<BehaviorTraceStep> steps, string? branchUid)
  {
    if (string.IsNullOrEmpty(branchUid)) return;
    for (int i = steps.Count - 1; i >= 0; i--)
    {
      if (steps[i].Uid != branchUid) continue;
      if (steps[i].Outcome != TraceOutcome.Selected) return;
      steps[i] = new BehaviorTraceStep
      {
        Uid = steps[i].Uid,
        Type = steps[i].Type,
        Id = steps[i].Id,
        Outcome = TraceOutcome.Passed,
        Detail = steps[i].Detail,
      };
      return;
    }
  }
}

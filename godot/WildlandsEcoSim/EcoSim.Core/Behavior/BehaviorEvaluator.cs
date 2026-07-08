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
}

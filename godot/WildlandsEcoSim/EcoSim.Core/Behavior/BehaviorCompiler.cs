using System.Text.Json.Nodes;
using EcoSim.Core.Util;

namespace EcoSim.Core.Behavior;

public static class BehaviorCompiler
{
  public sealed class MergedBehaviorData
  {
    public Dictionary<string, double> Thresholds { get; set; } = new();
    public Dictionary<string, JsonObject> Conditions { get; set; } = new();
    public Dictionary<string, JsonObject> Actions { get; set; } = new();
  }

  public static BehaviorConfig Compile(
    string behaviorKey,
    BehaviorSpeciesFile speciesFile,
    BehaviorLibraryRoot library)
  {
    var merged = new MergedBehaviorData
    {
      Thresholds = MergeThresholds(library.Thresholds, speciesFile.Thresholds),
      Conditions = MergeJsonDict(library.Conditions, speciesFile.Conditions),
      Actions = MergeJsonDict(library.Actions, speciesFile.Actions),
    };

    string templateName = speciesFile.Extends;
    if (!library.Trees.TryGetValue(templateName, out var templateNode))
    {
      throw new InvalidOperationException($"Behavior {behaviorKey}: unknown tree template \"{templateName}\"");
    }

    var sourceTree = templateNode.DeepClone();
    var root = ResolveTreeNode(templateNode, merged, $"{behaviorKey}/{templateName}", new HashSet<string>(StringComparer.Ordinal));

    if (speciesFile.Tree != null &&
        (speciesFile.Tree.Remove?.Length > 0 ||
         speciesFile.Tree.InsertBefore?.Count > 0 ||
         speciesFile.Tree.InsertAfter?.Count > 0))
    {
      root.Children = ApplyTreePatches(root.Children, speciesFile.Tree, merged);
    }

    return new BehaviorConfig
    {
      BehaviorKey = behaviorKey,
      TemplateName = templateName,
      Thresholds = merged.Thresholds,
      Actions = merged.Actions,
      Conditions = merged.Conditions,
      Root = root,
      SourceTree = sourceTree,
    };
  }

  public static Dictionary<string, double> MergeThresholds(
    Dictionary<string, double> library,
    Dictionary<string, double>? file)
  {
    var result = new Dictionary<string, double>(library);
    if (file == null) return result;
    foreach (var (k, v) in file) result[k] = v;
    return result;
  }

  public static Dictionary<string, JsonObject> MergeJsonDict(
    Dictionary<string, JsonObject> library,
    Dictionary<string, JsonObject>? file)
  {
    var result = new Dictionary<string, JsonObject>(library, StringComparer.Ordinal);
    if (file == null) return result;
    foreach (var (k, v) in file)
    {
      if (result.TryGetValue(k, out var existing))
      {
        result[k] = DeepMerge.Merge(existing, v)!.AsObject();
      }
      else
      {
        result[k] = v;
      }
    }
    return result;
  }

  private static BehaviorTreeNode ResolveTreeNode(
    JsonNode node,
    MergedBehaviorData merged,
    string pathPrefix,
    HashSet<string> usedUids)
  {
    if (node is JsonValue value && value.TryGetValue<string>(out var id))
    {
      string uid = $"{pathPrefix}/{id}";
      EnsureUniqueUid(uid, usedUids);
      if (merged.Actions.TryGetValue(id, out var action))
      {
        return new BehaviorTreeNode
        {
          Type = BehaviorNodeType.Action,
          Id = id,
          Uid = uid,
          Action = action,
        };
      }

      if (merged.Conditions.TryGetValue(id, out var condition))
      {
        return new BehaviorTreeNode
        {
          Type = BehaviorNodeType.Condition,
          Id = id,
          Uid = uid,
          Condition = condition,
        };
      }

      throw new InvalidOperationException($"Unknown behavior node: {id}");
    }

    if (node is JsonObject obj)
    {
      string type = obj.TryGetPropertyValue("type", out var typeNode)
        ? typeNode!.GetValue<string>()
        : "selector";
      string typeTag = type == "sequence" ? "seq" : "sel";
      string? branchId = obj.TryGetPropertyValue("id", out var idNode) ? idNode?.GetValue<string>() : null;
      string uid = obj.TryGetPropertyValue("uid", out var uidNode)
        ? uidNode!.GetValue<string>()
        : $"{pathPrefix}/{typeTag}";
      EnsureUniqueUid(uid, usedUids);

      var children = new List<BehaviorTreeNode>();
      if (obj.TryGetPropertyValue("children", out var childrenNode) && childrenNode is JsonArray arr)
      {
        for (int i = 0; i < arr.Count; i++)
        {
          var child = arr[i];
          if (child == null) continue;
          string childPath = $"{uid}/{typeTag}:{i}";
          children.Add(ResolveTreeNode(child, merged, childPath, usedUids));
        }
      }

      return new BehaviorTreeNode
      {
        Type = type == "sequence" ? BehaviorNodeType.Sequence : BehaviorNodeType.Selector,
        Id = branchId,
        Uid = uid,
        Children = children,
      };
    }

    throw new InvalidOperationException("Invalid behavior tree node");
  }

  private static void EnsureUniqueUid(string uid, HashSet<string> usedUids)
  {
    if (!usedUids.Add(uid))
    {
      throw new InvalidOperationException($"Duplicate behavior node uid: {uid}");
    }
  }

  private static List<BehaviorTreeNode> ApplyTreePatches(
    List<BehaviorTreeNode> baseChildren,
    BehaviorTreePatch patch,
    MergedBehaviorData merged)
  {
    var children = new List<BehaviorTreeNode>(baseChildren);

    if (patch.Remove != null)
    {
      var removeSet = new HashSet<string>(patch.Remove, StringComparer.Ordinal);
      children.RemoveAll(ch => MatchesAnchor(ch, removeSet));
    }

    if (patch.InsertBefore != null)
    {
      foreach (var (anchor, nodeId) in patch.InsertBefore)
      {
        int idx = children.FindIndex(ch => MatchesAnchor(ch, anchor));
        var node = ResolveTreeNode(JsonValue.Create(nodeId)!, merged, $"patch/before/{anchor}", new HashSet<string>(StringComparer.Ordinal));
        if (idx >= 0) children.Insert(idx, node);
      }
    }

    if (patch.InsertAfter != null)
    {
      foreach (var (anchor, nodeId) in patch.InsertAfter)
      {
        int idx = children.FindIndex(ch => MatchesAnchor(ch, anchor));
        var node = ResolveTreeNode(JsonValue.Create(nodeId)!, merged, $"patch/after/{anchor}", new HashSet<string>(StringComparer.Ordinal));
        if (idx >= 0) children.Insert(idx + 1, node);
      }
    }

    return children;
  }

  private static bool MatchesAnchor(BehaviorTreeNode node, string anchor)
  {
    return node.Id == anchor || node.Uid == anchor || node.Id != null && node.Id == anchor;
  }

  private static bool MatchesAnchor(BehaviorTreeNode node, HashSet<string> anchors)
  {
    if (node.Id != null && anchors.Contains(node.Id)) return true;
    if (node.Uid != null && anchors.Contains(node.Uid)) return true;
    return false;
  }
}

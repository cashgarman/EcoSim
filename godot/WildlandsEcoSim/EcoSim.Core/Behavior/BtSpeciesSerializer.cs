using System.Text.Json.Nodes;

namespace EcoSim.Core.Behavior;

/// <summary>
/// Serializes a behavior tree into a self-contained species behavior file
/// (<c>{ "root": ..., "thresholds": ..., "conditions": ..., "actions": ... }</c>).
/// Used by the visual BT editor when saving edits to <c>data/behaviors/{species}.json</c>.
/// </summary>
public static class BtSpeciesSerializer
{
  public static JsonObject ToSpeciesJson(BehaviorConfig config)
    => ToSpeciesJson(config.Root, config.Thresholds, config.Conditions, config.Actions);

  public static JsonObject ToSpeciesJson(BtEditorDocument doc)
  {
    var (root, conditions, actions) = doc.BuildTree();
    return ToSpeciesJson(root, doc.Thresholds, conditions, actions);
  }

  public static JsonObject ToSpeciesJson(
    BehaviorTreeNode root,
    IReadOnlyDictionary<string, double> thresholds,
    IReadOnlyDictionary<string, JsonObject> conditions,
    IReadOnlyDictionary<string, JsonObject> actions)
  {
    var referencedActions = new HashSet<string>(StringComparer.Ordinal);
    var referencedConditions = new HashSet<string>(StringComparer.Ordinal);
    var rootJson = NodeToJson(root, referencedActions, referencedConditions);

    var obj = new JsonObject
    {
      ["root"] = rootJson,
    };

    if (thresholds.Count > 0)
    {
      var th = new JsonObject();
      foreach (var (k, v) in thresholds.OrderBy(kv => kv.Key, StringComparer.Ordinal))
      {
        th[k] = v;
      }
      obj["thresholds"] = th;
    }

    if (referencedConditions.Count > 0)
    {
      var cond = new JsonObject();
      foreach (string id in referencedConditions.OrderBy(x => x, StringComparer.Ordinal))
      {
        if (conditions.TryGetValue(id, out var def) && def != null)
        {
          cond[id] = def.DeepClone();
        }
      }
      obj["conditions"] = cond;
    }

    if (referencedActions.Count > 0)
    {
      var act = new JsonObject();
      foreach (string id in referencedActions.OrderBy(x => x, StringComparer.Ordinal))
      {
        if (actions.TryGetValue(id, out var def) && def != null)
        {
          act[id] = def.DeepClone();
        }
      }
      obj["actions"] = act;
    }

    return obj;
  }

  private static JsonNode NodeToJson(
    BehaviorTreeNode node,
    HashSet<string> refActions,
    HashSet<string> refConditions)
  {
    switch (node.Type)
    {
      case BehaviorNodeType.Action:
        if (!string.IsNullOrEmpty(node.Id)) refActions.Add(node.Id);
        return JsonValue.Create(node.Id ?? "");

      case BehaviorNodeType.Condition:
        if (!string.IsNullOrEmpty(node.Id)) refConditions.Add(node.Id);
        return JsonValue.Create(node.Id ?? "");

      default:
      {
        var obj = new JsonObject
        {
          ["type"] = node.Type == BehaviorNodeType.Sequence ? "sequence" : "selector",
        };
        if (!string.IsNullOrEmpty(node.Id))
        {
          obj["id"] = node.Id;
        }

        var children = new JsonArray();
        foreach (var child in node.Children)
        {
          children.Add(NodeToJson(child, refActions, refConditions));
        }
        obj["children"] = children;
        return obj;
      }
    }
  }
}

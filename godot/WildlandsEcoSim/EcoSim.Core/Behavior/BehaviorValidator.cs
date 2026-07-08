using System.Text.Json.Nodes;
using EcoSim.Core.Util;

namespace EcoSim.Core.Behavior;

public static class BehaviorValidator
{
  public static IReadOnlyList<BehaviorValidationError> Validate(
    string behaviorKey,
    BehaviorSpeciesFile speciesFile,
    BehaviorLibraryRoot library,
    BehaviorSchema? schema = null)
  {
    var errors = new List<BehaviorValidationError>();
    schema ??= BehaviorSchema.Load();

    if (string.IsNullOrEmpty(speciesFile.Extends))
    {
      errors.Add(new BehaviorValidationError
      {
        Path = $"{behaviorKey}.extends",
        Code = "missing_extends",
        Message = "Species behavior file must declare extends template",
      });
      return errors;
    }

    if (!library.Trees.ContainsKey(speciesFile.Extends))
    {
      errors.Add(new BehaviorValidationError
      {
        Path = $"{behaviorKey}.extends",
        Code = "unknown_template",
        Message = $"Unknown tree template \"{speciesFile.Extends}\"",
      });
    }

    var mergedConditions = MergeJsonDict(library.Conditions, speciesFile.Conditions);
    var mergedActions = MergeJsonDict(library.Actions, speciesFile.Actions);

    ValidateConditions($"{behaviorKey}.conditions", mergedConditions, schema.KnownConditionOps, errors);
    ValidateActions($"{behaviorKey}.actions", mergedActions, errors);

    if (library.Trees.TryGetValue(speciesFile.Extends, out var templateNode))
    {
      ValidateTreeNode($"{behaviorKey}.tree", templateNode, mergedConditions, mergedActions, errors);
    }

    if (speciesFile.Tree != null && library.Trees.TryGetValue(speciesFile.Extends, out var template))
    {
      var anchors = CollectAnchors(template);
      ValidatePatchAnchors($"{behaviorKey}.tree.patch", speciesFile.Tree, anchors, errors);
    }

    return errors;
  }

  private static void ValidateConditions(
    string path,
    Dictionary<string, JsonObject> defs,
    IReadOnlySet<string> knownOps,
    List<BehaviorValidationError> errors)
  {
    foreach (var (name, def) in defs)
    {
      string op = def["op"]?.GetValue<string>() ?? "";
      if (string.IsNullOrEmpty(op) || !knownOps.Contains(op))
      {
        errors.Add(new BehaviorValidationError
        {
          Path = $"{path}.{name}",
          Code = "unknown_condition_op",
          Message = $"Condition \"{name}\" has unknown op \"{op}\"",
        });
      }
    }
  }

  private static void ValidateActions(string path, Dictionary<string, JsonObject> defs, List<BehaviorValidationError> errors)
  {
    foreach (var (name, def) in defs)
    {
      if (!def.ContainsKey("state") || !def.ContainsKey("goal") || !def.ContainsKey("label"))
      {
        errors.Add(new BehaviorValidationError
        {
          Path = $"{path}.{name}",
          Code = "missing_action_field",
          Message = $"Action \"{name}\" missing required label/state/goal",
        });
      }
    }
  }

  private static void ValidateTreeNode(
    string path,
    JsonNode? node,
    Dictionary<string, JsonObject> conditions,
    Dictionary<string, JsonObject> actions,
    List<BehaviorValidationError> errors)
  {
    if (node is JsonValue value && value.TryGetValue<string>(out var id))
    {
      if (!actions.ContainsKey(id) && !conditions.ContainsKey(id))
      {
        errors.Add(new BehaviorValidationError
        {
          Path = path,
          Code = "unknown_node_ref",
          Message = $"Unknown behavior node reference \"{id}\"",
        });
      }
      return;
    }

    if (node is JsonObject obj &&
        obj.TryGetPropertyValue("children", out var childrenNode) &&
        childrenNode is JsonArray arr)
    {
      if (arr.Count == 0)
      {
        errors.Add(new BehaviorValidationError
        {
          Path = path,
          Code = "empty_composite",
          Message = "Composite node has no children",
        });
      }

      for (int i = 0; i < arr.Count; i++)
      {
        ValidateTreeNode($"{path}.children[{i}]", arr[i], conditions, actions, errors);
      }
    }
  }

  private static HashSet<string> CollectAnchors(JsonNode template)
  {
    var anchors = new HashSet<string>(StringComparer.Ordinal);
  Collect(template);
    return anchors;

    void Collect(JsonNode? node)
    {
      if (node is JsonValue value && value.TryGetValue<string>(out var leafId))
      {
        anchors.Add(leafId);
        return;
      }

      if (node is JsonObject obj)
      {
        if (obj.TryGetPropertyValue("id", out var idNode))
        {
          string? branchId = idNode?.GetValue<string>();
          if (!string.IsNullOrEmpty(branchId)) anchors.Add(branchId);
        }

        if (obj.TryGetPropertyValue("children", out var childrenNode) && childrenNode is JsonArray arr)
        {
          foreach (var child in arr) Collect(child);
        }
      }
    }
  }

  private static void ValidatePatchAnchors(
    string path,
    BehaviorTreePatch patch,
    HashSet<string> anchors,
    List<BehaviorValidationError> errors)
  {
    void CheckAnchor(string anchor, string op)
    {
      if (!anchors.Contains(anchor))
      {
        errors.Add(new BehaviorValidationError
        {
          Path = path,
          Code = "patch_anchor_miss",
          Message = $"Patch {op} anchor \"{anchor}\" not found in template",
        });
      }
    }

    if (patch.Remove != null)
    {
      foreach (string anchor in patch.Remove) CheckAnchor(anchor, "remove");
    }

    if (patch.InsertBefore != null)
    {
      foreach (string anchor in patch.InsertBefore.Keys) CheckAnchor(anchor, "insertBefore");
    }

    if (patch.InsertAfter != null)
    {
      foreach (string anchor in patch.InsertAfter.Keys) CheckAnchor(anchor, "insertAfter");
    }
  }

  private static Dictionary<string, JsonObject> MergeJsonDict(
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
}

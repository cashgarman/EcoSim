using System.Text.Json;
using System.Text.Json.Nodes;
using EcoSim.Core.Data;

namespace EcoSim.Core.Behavior;

public sealed class BehaviorSchema
{
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
  };

  private JsonObject? _root;

  public static BehaviorSchema Load(string? path = null)
  {
    path ??= DataPaths.BehaviorSchemaJson;
    var schema = new BehaviorSchema();
    if (File.Exists(path))
    {
      string json = File.ReadAllText(path);
      schema._root = JsonNode.Parse(json)?.AsObject();
    }
    return schema;
  }

  public IReadOnlySet<string> KnownConditionOps
  {
    get
    {
      var ops = new HashSet<string>(StringComparer.Ordinal);
      if (_root?.TryGetPropertyValue("conditionOps", out var arr) == true && arr is JsonArray nodes)
      {
        foreach (var node in nodes)
        {
          string? op = node?["op"]?.GetValue<string>();
          if (!string.IsNullOrEmpty(op)) ops.Add(op);
        }
      }
      return ops;
    }
  }

  public IReadOnlySet<string> KnownActionFields
  {
    get
    {
      var fields = new HashSet<string>(StringComparer.Ordinal);
      if (_root?.TryGetPropertyValue("actionFields", out var arr) == true && arr is JsonArray nodes)
      {
        foreach (var node in nodes)
        {
          string? name = node?["name"]?.GetValue<string>();
          if (!string.IsNullOrEmpty(name)) fields.Add(name);
        }
      }
      return fields;
    }
  }

  public IReadOnlySet<string> KnownThresholdKeys
  {
    get
    {
      var keys = new HashSet<string>(StringComparer.Ordinal);
      if (_root?.TryGetPropertyValue("thresholdKeys", out var arr) == true && arr is JsonArray nodes)
      {
        foreach (var node in nodes)
        {
          string? key = node?["key"]?.GetValue<string>();
          if (!string.IsNullOrEmpty(key)) keys.Add(key);
        }
      }
      return keys;
    }
  }
}

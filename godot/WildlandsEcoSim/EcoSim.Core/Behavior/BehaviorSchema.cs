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

  // ── Typed accessors for the editor palette / inspector ─────────────────────

  public IReadOnlyList<SchemaNodeType> NodeTypes => ReadList("nodeTypes", n => new SchemaNodeType
  {
    Id = n["id"]?.GetValue<string>() ?? "",
    Label = n["label"]?.GetValue<string>() ?? "",
    Description = n["description"]?.GetValue<string>() ?? "",
  });

  public IReadOnlyList<SchemaInterruptTier> InterruptTiers => ReadList("interruptTiers", n => new SchemaInterruptTier
  {
    Tier = n["tier"]?.GetValue<int>() ?? 0,
    Label = n["label"]?.GetValue<string>() ?? "",
    Description = n["description"]?.GetValue<string>() ?? "",
  });

  public IReadOnlyList<SchemaThresholdKey> ThresholdKeySpecs => ReadList("thresholdKeys", n => new SchemaThresholdKey
  {
    Key = n["key"]?.GetValue<string>() ?? "",
    Label = n["label"]?.GetValue<string>() ?? "",
    Default = n["default"]?.GetValue<double>() ?? 0,
  });

  public IReadOnlyList<SchemaActionField> ActionFields => ReadList("actionFields", n => new SchemaActionField
  {
    Name = n["name"]?.GetValue<string>() ?? "",
    ParamType = n["type"]?.GetValue<string>() ?? "string",
    Required = n["required"]?.GetValue<bool>() ?? false,
  });

  public IReadOnlyList<SchemaConditionOp> ConditionOpSpecs => ReadList("conditionOps", n =>
  {
    var op = new SchemaConditionOp { Op = n["op"]?.GetValue<string>() ?? "" };
    if (n["params"] is JsonArray pars)
    {
      foreach (var p in pars)
      {
        if (p == null) continue;
        op.Params.Add(new SchemaParam
        {
          Name = p["name"]?.GetValue<string>() ?? "",
          ParamType = p["type"]?.GetValue<string>() ?? "string",
          Default = p["default"]?.DeepClone(),
          Optional = p["optional"]?.GetValue<bool>() ?? false,
        });
      }
    }
    return op;
  });

  private List<T> ReadList<T>(string key, Func<JsonObject, T> map)
  {
    var list = new List<T>();
    if (_root?.TryGetPropertyValue(key, out var arr) == true && arr is JsonArray nodes)
    {
      foreach (var node in nodes)
      {
        if (node is JsonObject obj) list.Add(map(obj));
      }
    }
    return list;
  }
}

public sealed class SchemaNodeType
{
  public string Id { get; init; } = "";
  public string Label { get; init; } = "";
  public string Description { get; init; } = "";
}

public sealed class SchemaInterruptTier
{
  public int Tier { get; init; }
  public string Label { get; init; } = "";
  public string Description { get; init; } = "";
}

public sealed class SchemaThresholdKey
{
  public string Key { get; init; } = "";
  public string Label { get; init; } = "";
  public double Default { get; init; }
}

public sealed class SchemaActionField
{
  public string Name { get; init; } = "";
  public string ParamType { get; init; } = "string";
  public bool Required { get; init; }
}

public sealed class SchemaConditionOp
{
  public string Op { get; init; } = "";
  public List<SchemaParam> Params { get; } = [];
}

public sealed class SchemaParam
{
  public string Name { get; init; } = "";
  public string ParamType { get; init; } = "string";
  public JsonNode? Default { get; init; }
  public bool Optional { get; init; }
}

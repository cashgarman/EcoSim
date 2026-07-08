using System.Text.Json;
using System.Text.Json.Nodes;

namespace EcoSim.Core.Behavior;

public enum BehaviorNodeType
{
  Selector,
  Sequence,
  Action,
  Condition,
}

public sealed class BehaviorTreeNode
{
  public BehaviorNodeType Type { get; set; }
  public string? Id { get; set; }
  public string Uid { get; set; } = "";
  public List<BehaviorTreeNode> Children { get; set; } = [];
  public JsonObject? Action { get; set; }
  public JsonObject? Condition { get; set; }
}

public sealed class BehaviorConfig
{
  public string BehaviorKey { get; set; } = "";
  public string TemplateName { get; set; } = "";
  public Dictionary<string, double> Thresholds { get; set; } = new();
  public Dictionary<string, JsonObject> Actions { get; set; } = new();
  public Dictionary<string, JsonObject> Conditions { get; set; } = new();
  public BehaviorTreeNode Root { get; set; } = new();
  public JsonNode? SourceTree { get; set; }
}

public sealed class BehaviorLibraryRoot
{
  public Dictionary<string, double> Thresholds { get; set; } = new();
  public Dictionary<string, JsonObject> Conditions { get; set; } = new();
  public Dictionary<string, JsonObject> Actions { get; set; } = new();
  public Dictionary<string, JsonNode> Trees { get; set; } = new();
}

public sealed class BehaviorSpeciesFile
{
  public string Extends { get; set; } = "";
  public Dictionary<string, double>? Thresholds { get; set; }
  public Dictionary<string, JsonObject>? Conditions { get; set; }
  public Dictionary<string, JsonObject>? Actions { get; set; }
  public BehaviorTreePatch? Tree { get; set; }
}

public sealed class BehaviorTreePatch
{
  public string[]? Remove { get; set; }
  public Dictionary<string, string>? InsertBefore { get; set; }
  public Dictionary<string, string>? InsertAfter { get; set; }
}

public sealed class BehaviorValidationError
{
  public required string Path { get; init; }
  public required string Code { get; init; }
  public required string Message { get; init; }
}

public sealed class BehaviorFlatNode
{
  public required string Uid { get; init; }
  public required string Type { get; init; }
  public string? Id { get; init; }
  public string? Ref { get; init; }
  public double X { get; set; }
  public double Y { get; set; }
  public bool Collapsed { get; set; }
  public string? Comment { get; set; }
}

public sealed class BehaviorFlatEdge
{
  public required string From { get; init; }
  public required string To { get; init; }
  public int Order { get; init; }
}

public sealed class BehaviorFlatDocument
{
  public string BehaviorKey { get; set; } = "";
  public string Extends { get; set; } = "";
  public List<BehaviorFlatNode> Nodes { get; set; } = [];
  public List<BehaviorFlatEdge> Edges { get; set; } = [];
}

public sealed class BehaviorLayoutSidecar
{
  public Dictionary<string, BehaviorLayoutNode> Nodes { get; set; } = new(StringComparer.Ordinal);
}

public sealed class BehaviorLayoutNode
{
  public double X { get; set; }
  public double Y { get; set; }
  public bool Collapsed { get; set; }
  public string? Comment { get; set; }
}

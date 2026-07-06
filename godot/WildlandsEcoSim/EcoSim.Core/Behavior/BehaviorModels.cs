using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

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
    public List<BehaviorTreeNode> Children { get; set; } = [];
    public JsonObject? Action { get; set; }
    public JsonObject? Condition { get; set; }
}

public sealed class BehaviorConfig
{
    public string BehaviorKey { get; set; } = "";
    public Dictionary<string, double> Thresholds { get; set; } = new();
    public Dictionary<string, JsonObject> Actions { get; set; } = new();
    public Dictionary<string, JsonObject> Conditions { get; set; } = new();
    public BehaviorTreeNode Root { get; set; } = new();
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

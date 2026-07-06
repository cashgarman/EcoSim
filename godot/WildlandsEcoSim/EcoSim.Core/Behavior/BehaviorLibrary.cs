using System.Text.Json;
using System.Text.Json.Nodes;
using EcoSim.Core.Data;
using EcoSim.Core.Util;

namespace EcoSim.Core.Behavior;

public sealed class BehaviorLibrary
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private BehaviorLibraryRoot? _baseLibrary;
    private BehaviorLibraryRoot? _library;
    private readonly Dictionary<string, JsonObject> _baseBehaviorFiles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, JsonObject> _behaviorFiles = new(StringComparer.Ordinal);
    private BehaviorOverrides _overrides = new();

    public IReadOnlyList<string> ThresholdKeys { get; } =
    [
        "thirstUrgent", "thirstExit", "hungerGraze", "hungerHunt",
        "restEnergy", "nightWanderRestEnergy",
        "mateHungerMin", "mateThirstMin", "mateEnergyMin",
    ];

    public IReadOnlyList<string> FuzzActionKeys { get; } = ["Flee", "HuntNearby", "Wander"];

    public BehaviorLibraryRoot? Library => _library;

    public void Load(SpeciesCatalog catalog, string? libraryPath = null)
    {
        libraryPath ??= DataPaths.BehaviorLibraryJson;
        string json = File.ReadAllText(libraryPath);
        _library = DeserializeLibrary(json);
        _baseLibrary = DeserializeLibrary(json);
        _overrides = new BehaviorOverrides();
        _behaviorFiles.Clear();
        _baseBehaviorFiles.Clear();

        foreach (string sp in catalog.SpeciesKeys)
        {
            var def = catalog.Get(sp);
            string behaviorKey = string.IsNullOrEmpty(def.Behavior) ? sp : def.Behavior;
            JsonObject fileData = LoadBehaviorFile(behaviorKey);
            var config = CompileBehaviorFile(behaviorKey, fileData, _library);
            catalog.AttachBehaviorConfig(sp, config);
        }
    }

    public void SetOverrides(BehaviorOverrides overrides)
    {
        _overrides = overrides;
    }

    public void RecompileAll(SpeciesCatalog catalog)
    {
        var lib = EffectiveLibrary();
        if (lib == null) return;
        _library = lib;

        foreach (string sp in catalog.SpeciesKeys)
        {
            var def = catalog.Get(sp);
            string behaviorKey = string.IsNullOrEmpty(def.Behavior) ? sp : def.Behavior;
            JsonObject baseFile = _baseBehaviorFiles.TryGetValue(behaviorKey, out var bf)
                ? bf
                : _behaviorFiles[behaviorKey];
            JsonObject fileData = DeepMerge.Merge(baseFile, _overrides.Species.GetValueOrDefault(sp))!.AsObject();
            var config = CompileBehaviorFile(behaviorKey, fileData, lib);
            catalog.AttachBehaviorConfig(sp, config);
        }
    }

    public BehaviorConfig? GetSpeciesBehavior(SpeciesCatalog catalog, string sp)
    {
        return catalog.TryGet(sp, out var def) ? def?.BehaviorConfig : null;
    }

    private BehaviorLibraryRoot EffectiveLibrary()
    {
        if (_baseLibrary == null) return _library!;
        var merged = DeepMerge.Merge(
            JsonSerializer.SerializeToNode(_baseLibrary),
            JsonSerializer.SerializeToNode(_overrides.Library))!;
        return merged.Deserialize<BehaviorLibraryRoot>(JsonOptions)!;
    }

    private JsonObject LoadBehaviorFile(string stem)
    {
        if (_behaviorFiles.TryGetValue(stem, out var cached))
        {
            return cached;
        }

        string path = DataPaths.BehaviorFile(stem);
        string json = File.ReadAllText(path);
        var node = JsonNode.Parse(json)!.AsObject();
        _behaviorFiles[stem] = node;
        _baseBehaviorFiles[stem] = node.DeepClone().AsObject();
        return node;
    }

    private static BehaviorLibraryRoot DeserializeLibrary(string json)
    {
        return JsonSerializer.Deserialize<BehaviorLibraryRoot>(json, JsonOptions)
            ?? throw new InvalidOperationException("Invalid behavior library JSON");
    }

    private BehaviorConfig CompileBehaviorFile(string behaviorKey, JsonObject fileData, BehaviorLibraryRoot library)
    {
        var speciesFile = fileData.Deserialize<BehaviorSpeciesFile>(JsonOptions)
            ?? throw new InvalidOperationException($"Invalid behavior file: {behaviorKey}");

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

        var root = ResolveTreeNode(templateNode, merged);

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
            Thresholds = merged.Thresholds,
            Actions = merged.Actions,
            Conditions = merged.Conditions,
            Root = root,
        };
    }

    private sealed class MergedBehaviorData
    {
        public Dictionary<string, double> Thresholds { get; set; } = new();
        public Dictionary<string, JsonObject> Conditions { get; set; } = new();
        public Dictionary<string, JsonObject> Actions { get; set; } = new();
    }

    private static Dictionary<string, double> MergeThresholds(
        Dictionary<string, double> library,
        Dictionary<string, double>? file)
    {
        var result = new Dictionary<string, double>(library);
        if (file == null) return result;
        foreach (var (k, v) in file) result[k] = v;
        return result;
    }

    private static Dictionary<string, JsonObject> MergeJsonDict(
        Dictionary<string, JsonObject> library,
        Dictionary<string, JsonObject>? file)
    {
        var result = new Dictionary<string, JsonObject>(library, StringComparer.Ordinal);
        if (file == null) return result;
        foreach (var (k, v) in file) result[k] = v;
        return result;
    }

    private BehaviorTreeNode ResolveTreeNode(JsonNode node, MergedBehaviorData merged)
    {
        if (node is JsonValue value && value.TryGetValue<string>(out var id))
        {
            if (merged.Actions.TryGetValue(id, out var action))
            {
                return new BehaviorTreeNode
                {
                    Type = BehaviorNodeType.Action,
                    Id = id,
                    Action = action,
                };
            }

            if (merged.Conditions.TryGetValue(id, out var condition))
            {
                return new BehaviorTreeNode
                {
                    Type = BehaviorNodeType.Condition,
                    Id = id,
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
            var children = new List<BehaviorTreeNode>();
            if (obj.TryGetPropertyValue("children", out var childrenNode) && childrenNode is JsonArray arr)
            {
                foreach (var child in arr)
                {
                    if (child != null) children.Add(ResolveTreeNode(child, merged));
                }
            }

            return new BehaviorTreeNode
            {
                Type = type == "sequence" ? BehaviorNodeType.Sequence : BehaviorNodeType.Selector,
                Children = children,
            };
        }

        throw new InvalidOperationException("Invalid behavior tree node");
    }

    private List<BehaviorTreeNode> ApplyTreePatches(
        List<BehaviorTreeNode> baseChildren,
        BehaviorTreePatch patch,
        MergedBehaviorData merged)
    {
        var children = new List<BehaviorTreeNode>(baseChildren);

        if (patch.Remove != null)
        {
            var removeSet = new HashSet<string>(patch.Remove, StringComparer.Ordinal);
            children.RemoveAll(ch => ch.Id != null && removeSet.Contains(ch.Id));
        }

        if (patch.InsertBefore != null)
        {
            foreach (var (anchor, nodeId) in patch.InsertBefore)
            {
                int idx = children.FindIndex(ch => ch.Id == anchor);
                var node = ResolveTreeNode(JsonValue.Create(nodeId)!, merged);
                if (idx >= 0) children.Insert(idx, node);
            }
        }

        if (patch.InsertAfter != null)
        {
            foreach (var (anchor, nodeId) in patch.InsertAfter)
            {
                int idx = children.FindIndex(ch => ch.Id == anchor);
                var node = ResolveTreeNode(JsonValue.Create(nodeId)!, merged);
                if (idx >= 0) children.Insert(idx + 1, node);
            }
        }

        return children;
    }
}

public sealed class BehaviorOverrides
{
    public Dictionary<string, JsonObject> Library { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, JsonObject> Species { get; set; } = new(StringComparer.Ordinal);
}

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
  private BehaviorSchema _schema = BehaviorSchema.Load();

  public IReadOnlyList<string> ThresholdKeys { get; } =
  [
    "thirstUrgent", "thirstExit",
    "hungerUrgent", "hungerGraze", "hungerExit", "hungerHunt",
    "energyUrgent", "restEnergy", "energyExit",
    "nightWanderRestEnergy",
    "mateHungerMin", "mateThirstMin", "mateEnergyMin",
  ];

  public IReadOnlyList<string> FuzzActionKeys { get; } = ["Flee", "HuntNearby", "Wander"];

  public BehaviorLibraryRoot? Library => _library;
  public BehaviorSchema Schema => _schema;

  public void Load(SpeciesCatalog catalog, string? libraryPath = null)
  {
    libraryPath ??= DataPaths.BehaviorLibraryJson;
    string json = File.ReadAllText(libraryPath);
    _library = DeserializeLibrary(json);
    _baseLibrary = DeserializeLibrary(json);
    _overrides = new BehaviorOverrides();
    _behaviorFiles.Clear();
    _baseBehaviorFiles.Clear();
    _schema = BehaviorSchema.Load();

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

  public IReadOnlyList<BehaviorValidationError> ValidateBehaviorFile(string behaviorKey, JsonObject fileData)
  {
    var speciesFile = fileData.Deserialize<BehaviorSpeciesFile>(JsonOptions)
      ?? throw new InvalidOperationException($"Invalid behavior file: {behaviorKey}");
    return BehaviorValidator.Validate(behaviorKey, speciesFile, _library ?? _baseLibrary!, _schema);
  }

  /// <summary>Re-reads a single behavior file from disk into the caches (used after an editor save).</summary>
  public void ReloadBehaviorFile(string stem)
  {
    string path = DataPaths.BehaviorFile(stem);
    string json = File.ReadAllText(path);
    var node = JsonNode.Parse(json)!.AsObject();
    _behaviorFiles[stem] = node;
    _baseBehaviorFiles[stem] = node.DeepClone().AsObject();
  }

  /// <summary>Recompiles the given behavior file and reattaches it to every species that uses it.</summary>
  public BehaviorConfig RecompileSpecies(SpeciesCatalog catalog, string behaviorKey)
  {
    var lib = _library ?? _baseLibrary
      ?? throw new InvalidOperationException("Behavior library not loaded");
    JsonObject fileData = _behaviorFiles.TryGetValue(behaviorKey, out var bf)
      ? bf
      : LoadBehaviorFile(behaviorKey);
    var config = CompileBehaviorFile(behaviorKey, fileData, lib);

    foreach (string sp in catalog.SpeciesKeys)
    {
      var def = catalog.Get(sp);
      string key = string.IsNullOrEmpty(def.Behavior) ? sp : def.Behavior;
      if (key == behaviorKey)
      {
        catalog.AttachBehaviorConfig(sp, config);
      }
    }
    return config;
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

    var errors = BehaviorValidator.Validate(behaviorKey, speciesFile, library, _schema);
    if (errors.Count > 0)
    {
      throw new InvalidOperationException(
        $"Behavior validation failed for {behaviorKey}: {errors[0].Path} — {errors[0].Message}");
    }

    return BehaviorCompiler.Compile(behaviorKey, speciesFile, library);
  }
}

public sealed class BehaviorOverrides
{
  public Dictionary<string, JsonObject> Library { get; set; } = new(StringComparer.Ordinal);
  public Dictionary<string, JsonObject> Species { get; set; } = new(StringComparer.Ordinal);
}

using System.Text.Json;
using EcoSim.Core.Rng;
using EcoSim.Core.Util;

namespace EcoSim.Core.Data;

public sealed class SpeciesCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private Dictionary<string, SpeciesDefinition> _baseSpecies = new();
    private Dictionary<string, SpeciesDefinition> _species = new();
    private Dictionary<string, Dictionary<string, object>>? _rawOverrides;

    public IReadOnlyList<string> SpeciesKeys { get; private set; } = [];
    public IReadOnlyDictionary<string, int> SpeciesIndex { get; private set; } = new Dictionary<string, int>();
    public string[] GeneKeys { get; private set; } = [];
    public Dictionary<string, double[]> GeneRange { get; private set; } = new();
    public Dictionary<string, string> GeneLabel { get; private set; } = new();

    public IReadOnlyDictionary<string, SpeciesDefinition> Species => _species;

    public static SpeciesCatalog LoadFromFile(string? path = null)
    {
        path ??= DataPaths.SpeciesJson;
        string json = File.ReadAllText(path);
        var root = JsonSerializer.Deserialize<SpeciesFileRoot>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Failed to parse species data: {path}");
        var catalog = new SpeciesCatalog();
        catalog.Initialize(root);
        return catalog;
    }

    private void Initialize(SpeciesFileRoot root)
    {
        GeneKeys = root.GeneKeys;
        GeneRange = root.GeneRange;
        GeneLabel = root.GeneLabel;
        _baseSpecies = CloneSpeciesMap(root.Species);
        _species = CloneSpeciesMap(root.Species);
        RebuildIndices();
        AttachSpeciesMasks();
    }

    public void ApplyOverrides(Dictionary<string, Dictionary<string, object>>? overrides)
    {
        _rawOverrides = overrides;
        _species = CloneSpeciesMap(_baseSpecies);
        if (overrides == null) return;

        foreach (var (sp, patch) in overrides)
        {
            if (!_species.TryGetValue(sp, out var existing)) continue;
            var merged = DeepMerge.MergeObjects(existing, DictionaryToSpeciesPatch(patch));
            _species[sp] = merged;
        }

        AttachSpeciesMasks();
    }

    public void AttachBehaviorConfig(string speciesKey, Behavior.BehaviorConfig config)
    {
        if (_species.TryGetValue(speciesKey, out var def))
        {
            def.BehaviorConfig = config;
        }
    }

    public SpeciesDefinition Get(string key) => _species[key];

    public bool TryGet(string key, out SpeciesDefinition? def) => _species.TryGetValue(key, out def);

    public double SampleGestation(string sp)
    {
        var range = _species[sp].GestationSec;
        return GlobalRng.Rf(range[0], range[1]);
    }

    public double SampleMateCooldown(string sp)
    {
        var range = _species[sp].MateCooldownSec;
        return GlobalRng.Rf(range[0], range[1]);
    }

    public static string SexSymbol(string sex) => sex == "male" ? "♂" : "♀";

    public static string SexLabel(string sex) => sex == "male" ? "Male" : "Female";

    public static bool SpeciesCanSwim(SpeciesDefinition s)
    {
        return s.CanSwim || s.Shape == "bird";
    }

    public uint SpeciesMask(IEnumerable<string>? speciesList)
    {
        uint mask = 0;
        if (speciesList == null) return mask;
        foreach (string sp in speciesList)
        {
            if (SpeciesIndex.TryGetValue(sp, out int bit) && bit >= 0 && bit < 30)
            {
                mask |= 1u << bit;
            }
        }
        return mask;
    }

    private void RebuildIndices()
    {
        SpeciesKeys = _species.Keys.ToList();
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < SpeciesKeys.Count; i++)
        {
            index[SpeciesKeys[i]] = i;
        }
        SpeciesIndex = index;
    }

    private void AttachSpeciesMasks()
    {
        foreach (var sp in SpeciesKeys)
        {
            var s = _species[sp];
            s.HuntsMask = SpeciesMask(s.Hunts);
            s.PreyMask = SpeciesMask(s.PreyOf);
        }
    }

    private static Dictionary<string, SpeciesDefinition> CloneSpeciesMap(Dictionary<string, SpeciesDefinition> source)
    {
        string json = JsonSerializer.Serialize(source, JsonOptions);
        return JsonSerializer.Deserialize<Dictionary<string, SpeciesDefinition>>(json, JsonOptions)!;
    }

    private static SpeciesDefinition DictionaryToSpeciesPatch(Dictionary<string, object> patch)
    {
        string json = JsonSerializer.Serialize(patch, JsonOptions);
        return JsonSerializer.Deserialize<SpeciesDefinition>(json, JsonOptions)!;
    }
}

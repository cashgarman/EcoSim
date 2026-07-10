using System.Text.Json;

namespace EcoSim.Core.Data;

/// <summary>
/// Loads and validates the hand-crafted per-species evolution trees from
/// <c>data/evolutions/{species}.json</c>. Species without a file simply have no tree.
/// </summary>
public sealed class EvolutionCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly string[] AbilityKeys = ["canSwim"];

    private readonly Dictionary<string, EvolutionTreeFile> _trees = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, EvolutionTreeFile> Trees => _trees;

    public EvolutionTreeFile? TreeFor(string sp) => _trees.GetValueOrDefault(sp);

    public EvolutionNode? NodeFor(string sp, string nodeId) =>
        TreeFor(sp)?.Nodes.FirstOrDefault(n => n.Id == nodeId);

    public static EvolutionCatalog Load(SpeciesCatalog species, string? directory = null)
    {
        directory ??= DataPaths.EvolutionsDirectory;
        var catalog = new EvolutionCatalog();
        if (!Directory.Exists(directory)) return catalog;

        foreach (string sp in species.SpeciesKeys)
        {
            string path = Path.Combine(directory, sp + ".json");
            if (!File.Exists(path)) continue;
            string json = File.ReadAllText(path);
            var tree = JsonSerializer.Deserialize<EvolutionTreeFile>(json, JsonOptions)
                ?? throw new InvalidOperationException($"Invalid evolution tree JSON: {path}");
            Validate(sp, tree, species);
            catalog._trees[sp] = tree;
        }
        return catalog;
    }

    public static void Validate(string sp, EvolutionTreeFile tree, SpeciesCatalog species)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in tree.Nodes)
        {
            if (string.IsNullOrEmpty(node.Id))
                throw new InvalidOperationException($"Evolution tree {sp}: node with empty id");
            if (!ids.Add(node.Id))
                throw new InvalidOperationException($"Evolution tree {sp}: duplicate node id '{node.Id}'");
            if (node.Cost < 1)
                throw new InvalidOperationException($"Evolution tree {sp}: node '{node.Id}' has cost < 1");

            if (node.Effects.Genes != null)
            {
                foreach (string key in node.Effects.Genes.Keys)
                {
                    if (!species.GeneKeys.Contains(key))
                        throw new InvalidOperationException(
                            $"Evolution tree {sp}: node '{node.Id}' references unknown gene '{key}'");
                }
            }
            if (node.Effects.Abilities != null)
            {
                foreach (string key in node.Effects.Abilities.Keys)
                {
                    if (!AbilityKeys.Contains(key))
                        throw new InvalidOperationException(
                            $"Evolution tree {sp}: node '{node.Id}' references unknown ability '{key}'");
                }
            }
        }

        foreach (var node in tree.Nodes)
        {
            foreach (string req in node.Requires)
            {
                if (!ids.Contains(req))
                    throw new InvalidOperationException(
                        $"Evolution tree {sp}: node '{node.Id}' requires unknown node '{req}'");
            }
        }

        // Cycle check: repeatedly peel nodes whose prerequisites are all peeled.
        var resolved = new HashSet<string>(StringComparer.Ordinal);
        int lastCount = -1;
        while (resolved.Count != lastCount)
        {
            lastCount = resolved.Count;
            foreach (var node in tree.Nodes)
            {
                if (resolved.Contains(node.Id)) continue;
                if (node.Requires.All(resolved.Contains)) resolved.Add(node.Id);
            }
        }
        if (resolved.Count != tree.Nodes.Count)
            throw new InvalidOperationException($"Evolution tree {sp}: cyclic prerequisites detected");
    }
}

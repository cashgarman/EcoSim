using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace WildlandsEcoSim.Render;

public sealed class CreatureSpriteDef
{
    public required Texture2D Texture { get; init; }
    /// <summary>Subtle per-species size tier (genome size still applies via draw size).</summary>
    public float Scale { get; init; } = 1f;
    public Vector2 Anchor { get; init; } = new(0.5f, 0.85f);
    /// <summary>Opaque pixel bounds inside the texture (for normalized draw size).</summary>
    public Rect2 ContentRegion { get; init; }
}

/// <summary>Loads species creature sprites from res://assets/creatures/manifest.json.</summary>
public static class CreatureSpriteCatalog
{
    private const string ManifestPath = "res://assets/creatures/manifest.json";

    private static bool _initialized;
    private static readonly Dictionary<string, CreatureSpriteDef> SpeciesSprites = new();
    private static readonly Dictionary<string, CreatureSpriteDef> ExtraSprites = new();

    public static void EnsureLoaded()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        LoadManifest();
    }

    public static bool TryGetSpeciesSprite(string speciesKey, out CreatureSpriteDef def)
    {
        EnsureLoaded();
        return SpeciesSprites.TryGetValue(speciesKey, out def!);
    }

    public static bool TryGetExtraSprite(string extraKey, out CreatureSpriteDef def)
    {
        EnsureLoaded();
        return ExtraSprites.TryGetValue(extraKey, out def!);
    }

    private static void LoadManifest()
    {
        using var file = Godot.FileAccess.Open(ManifestPath, Godot.FileAccess.ModeFlags.Read);
        if (file == null)
        {
            GD.PushWarning($"Creature sprite manifest missing: {ManifestPath}");
            return;
        }

        using var doc = JsonDocument.Parse(file.GetAsText());
        var root = doc.RootElement;

        if (root.TryGetProperty("species", out var species))
        {
            LoadSection(species, SpeciesSprites, "species");
        }

        if (root.TryGetProperty("extras", out var extras))
        {
            LoadSection(extras, ExtraSprites, "extras");
        }
    }

    private static void LoadSection(
        JsonElement section,
        Dictionary<string, CreatureSpriteDef> target,
        string label)
    {
        foreach (var entry in section.EnumerateObject())
        {
            string key = entry.Name;
            var node = entry.Value;
            string fileName = node.GetProperty("file").GetString()
                ?? throw new InvalidOperationException($"Creature {label} entry '{key}' is missing file.");
            float scale = node.TryGetProperty("scale", out var scaleNode) ? scaleNode.GetSingle() : 1f;
            Vector2 anchor = ReadAnchor(node);

            string path = $"res://assets/creatures/{fileName}";
            var texture = GD.Load<Texture2D>(path);
            if (texture == null)
            {
                GD.PushWarning($"Failed to load creature sprite '{path}' for {label} '{key}'.");
                continue;
            }

            Rect2 contentRegion = ReadContentRegion(node, texture);
            target[key] = new CreatureSpriteDef
            {
                Texture = texture,
                Scale = scale,
                Anchor = anchor,
                ContentRegion = contentRegion,
            };
        }
    }

    private static Vector2 ReadAnchor(JsonElement node)
    {
        if (!node.TryGetProperty("anchor", out var anchorNode) || anchorNode.GetArrayLength() < 2)
        {
            return new Vector2(0.5f, 0.85f);
        }

        return new Vector2(anchorNode[0].GetSingle(), anchorNode[1].GetSingle());
    }

    private static Rect2 ReadContentRegion(JsonElement node, Texture2D? texture)
    {
        if (node.TryGetProperty("content", out var contentNode) && contentNode.GetArrayLength() >= 4)
        {
            return new Rect2(
                contentNode[0].GetSingle(),
                contentNode[1].GetSingle(),
                contentNode[2].GetSingle(),
                contentNode[3].GetSingle());
        }

        if (texture == null)
        {
            return new Rect2(0f, 0f, 0f, 0f);
        }

        Vector2 texSize = texture.GetSize();
        return new Rect2(0f, 0f, texSize.X, texSize.Y);
    }
}

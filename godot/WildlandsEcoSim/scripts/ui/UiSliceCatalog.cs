using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace WildlandsEcoSim.UI;

/// <summary>Loads shared 9-slice UI textures from res://assets/ui/ui-slices.json.</summary>
public static class UiSliceCatalog
{
    private const string ManifestPath = "res://assets/ui/ui-slices.json";

    private static JsonDocument? _manifest;
    private static readonly Dictionary<string, Texture2D> Textures = new();

    private static JsonDocument Manifest
    {
        get
        {
            if (_manifest != null)
            {
                return _manifest;
            }

            using var file = Godot.FileAccess.Open(ManifestPath, Godot.FileAccess.ModeFlags.Read);
            if (file == null)
            {
                throw new InvalidOperationException($"Missing UI slice manifest: {ManifestPath}");
            }

            _manifest = JsonDocument.Parse(file.GetAsText());
            return _manifest;
        }
    }

    public static StyleBoxTexture MakeSlice(string key)
    {
        var entry = Manifest.RootElement.GetProperty("slices").GetProperty(key);
        var margin = entry.GetProperty("margin");
        var style = new StyleBoxTexture
        {
            Texture = LoadTexture(key),
            TextureMarginLeft = margin.GetProperty("left").GetInt32(),
            TextureMarginTop = margin.GetProperty("top").GetInt32(),
            TextureMarginRight = margin.GetProperty("right").GetInt32(),
            TextureMarginBottom = margin.GetProperty("bottom").GetInt32(),
            AxisStretchHorizontal = StyleBoxTexture.AxisStretchMode.Stretch,
            AxisStretchVertical = StyleBoxTexture.AxisStretchMode.Stretch,
        };

        if (entry.TryGetProperty("content_margin", out var content))
        {
            style.ContentMarginLeft = content.GetProperty("left").GetInt32();
            style.ContentMarginTop = content.GetProperty("top").GetInt32();
            style.ContentMarginRight = content.GetProperty("right").GetInt32();
            style.ContentMarginBottom = content.GetProperty("bottom").GetInt32();
        }

        return style;
    }

    public static StyleBoxTexture MakeStonePanel() => MakeSlice("panel_stone");

    public static StyleBoxTexture MakeFlatPanel() => MakeSlice("panel_flat");

    public static StyleBoxTexture MakeInsetPanel() => MakeSlice("panel_inset");

    public static StyleBoxTexture MakeHeaderStrip() => MakeSlice("panel_header");

    public static StyleBoxTexture MakeButtonNormal() => MakeSlice("button_normal");

    public static StyleBoxTexture MakeButtonGold() => MakeSlice("button_gold");

    private static Texture2D LoadTexture(string key)
    {
        if (Textures.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var fileName = Manifest.RootElement.GetProperty("slices").GetProperty(key).GetProperty("file").GetString()
            ?? throw new InvalidOperationException($"Slice '{key}' is missing a file entry.");
        var texture = GD.Load<Texture2D>($"res://assets/ui/{fileName}")
            ?? throw new InvalidOperationException($"Failed to load UI texture for '{key}'.");
        Textures[key] = texture;
        return texture;
    }
}

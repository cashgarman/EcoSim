using EcoSim.Core.Data;
using EcoSim.Core.Sim;
using Godot;

namespace WildlandsEcoSim.Render;

public static class CreatureDrawUtil
{
    public static Color CreatureColor(SpeciesDefinition def, Genome genome)
    {
        var hueCol = HslToRgb(genome.Hue / 360.0, 0.5, 0.55);
        int r = (int)(def.Col[0] * 0.7 + hueCol.R * 255 * 0.3);
        int g = (int)(def.Col[1] * 0.7 + hueCol.G * 255 * 0.3);
        int b = (int)(def.Col[2] * 0.7 + hueCol.B * 255 * 0.3);
        return new Color(r / 255f, g / 255f, b / 255f);
    }

    /// <summary>Vivid per-species colour for zoomed-out map circles.</summary>
    public static Color SpeciesMapColor(string speciesKey, SpeciesDefinition def)
    {
        if (SpeciesMarkerPalette.TryGetValue(speciesKey, out Color palette))
        {
            return palette;
        }

        return BoostSpeciesColor(def);
    }

    public static float MapMarkerBrightness(double lightLevel) =>
        (float)(0.9 + 0.1 * lightLevel);

    public static float MapCircleRadiusTiles(float camZoom, float genomeSize, float eSize)
    {
        float zoom = Math.Max(0.15f, camZoom);
        float baseRadius = 0.35f + genomeSize * 0.12f;
        const float minScreenPx = 6f;
        float minTiles = minScreenPx / (WorldRenderer.TilePixels * zoom);
        float sizeBoost = Math.Max(0.85f, eSize);
        float zoomBoost = zoom < 1.8f ? Mathf.Lerp(2.8f, 1f, zoom / 1.8f) : 1f;
        return Math.Max(baseRadius * sizeBoost * zoomBoost, minTiles);
    }

    public static void DrawMapCircle(
        CanvasItem canvas,
        Vector2 pos,
        float radiusTiles,
        Color color,
        float brightness,
        float camZoom)
    {
        Color fill = ApplyBrightness(color, brightness);
        canvas.DrawCircle(pos, radiusTiles, fill);

        float outlineW = Math.Max(0.03f, 1.25f / (WorldRenderer.TilePixels * Math.Max(0.15f, camZoom)));
        canvas.DrawArc(pos, radiusTiles, 0, Mathf.Tau, 24, new Color(0.06f, 0.08f, 0.05f, 0.7f), outlineW);
    }

    private static Color BoostSpeciesColor(SpeciesDefinition def)
    {
        if (def.Col.Length < 3)
        {
            return Colors.Gray;
        }

        var c = new Color(def.Col[0] / 255f, def.Col[1] / 255f, def.Col[2] / 255f);
        c = c.Lightened(0.18f);
        return c with { S = Math.Min(1f, c.S * 1.45f) };
    }

    private static readonly Dictionary<string, Color> SpeciesMarkerPalette = new(StringComparer.Ordinal)
    {
        ["rabbit"] = new Color("#f5e6a8"),
        ["mouse"] = new Color("#b8b4c8"),
        ["deer"] = new Color("#e8943a"),
        ["elk"] = new Color("#c87830"),
        ["beaver"] = new Color("#8a5a38"),
        ["boar"] = new Color("#d86a58"),
        ["fox"] = new Color("#f06828"),
        ["wolf"] = new Color("#6a9ac8"),
        ["hawk"] = new Color("#d84820"),
        ["owl"] = new Color("#9a68b8"),
        ["bear"] = new Color("#5a4030"),
    };

    public static float CreatureBrightness(Creature c, Creature? selected, double lightLevel)
    {
        if (IsPedigreeLit(c, selected))
        {
            return 1f;
        }

        // Dim less than terrain at night; stay fairly visible (JS parity: lerp floor, raised).
        return (float)(0.78 + 0.22 * lightLevel);
    }

    public static Color ApplyBrightness(Color color, float brightness)
    {
        return new Color(color.R * brightness, color.G * brightness, color.B * brightness, color.A);
    }

    public static bool IsPedigreeLit(Creature c, Creature? selected)
    {
        if (selected == null || selected.Dead) return false;
        if (c.Id == selected.Id) return true;
        if (selected.ParentIds.Contains(c.Id)) return true;
        if (selected.OffspringIds.Contains(c.Id)) return true;
        return false;
    }

    public static float EffectiveSize(CreatureSystem creatures, Creature c) =>
        (float)(creatures.ESize(c));

    public static Vector2 DisplayPos(Creature c) => new((float)c.Rx, (float)c.Ry);

    public static void DrawMarker(CanvasItem canvas, Vector2 pos, float size, Color color, float brightness)
    {
        Color col = ApplyBrightness(color, brightness);

        float m = Math.Max(0.25f, size * 0.55f);
        canvas.DrawRect(new Rect2(pos.X - m * 0.5f, pos.Y - m * 0.5f, m, m), col);
    }

    public static void DrawBodyRect(CanvasItem canvas, Vector2 pos, float size, Color color, float brightness)
    {
        Color col = ApplyBrightness(color, brightness);

        float body = Math.Max(0.25f, size * 0.65f);
        canvas.DrawRect(new Rect2(pos.X - body * 0.5f, pos.Y - body * 0.4f, body, body * 0.8f), col);
    }

    private const float SpriteMaxWidthToHeight = 1.22f;
    private const float HighlightRadiusPadding = 1.08f;

    public readonly struct CreatureHighlightBounds
    {
        public Vector2 CenterOffset { get; init; }
        public float RadiusTiles { get; init; }
    }

    public static float CreatureDrawSizeTiles(Creature c, CreatureSystem creatures, float camZoom, int detailTier)
    {
        float eSize = EffectiveSize(creatures, c);
        float baseRadius = 0.35f + (float)c.Genome.Size * 0.12f;
        if (detailTier >= 2 && camZoom > 4.2f)
        {
            return Math.Max(baseRadius, camZoom * 0.22f * eSize);
        }

        return baseRadius;
    }

    public static CreatureHighlightBounds GetHighlightBounds(
        Creature c,
        CreatureSystem creatures,
        float camZoom,
        int detailTier,
        string shape,
        CreatureSpriteDef? spriteDef)
    {
        if (camZoom < 3.5f || detailTier <= 0)
        {
            float mapR = MapCircleRadiusTiles(camZoom, (float)c.Genome.Size, EffectiveSize(creatures, c)) * 1.22f;
            return new CreatureHighlightBounds { CenterOffset = Vector2.Zero, RadiusTiles = mapR };
        }

        float sizeTiles = CreatureDrawSizeTiles(c, creatures, camZoom, detailTier);

        if (detailTier >= 2 && camZoom > 4.2f)
        {
            if (spriteDef != null)
            {
                return SpriteHighlightBounds(c, sizeTiles, spriteDef);
            }

            return ProceduralSpriteHighlightBounds(sizeTiles, shape);
        }

        float body = Math.Max(0.25f, sizeTiles * 0.65f);
        float bodyH = body * 0.8f;
        float radius = Math.Max(body * 0.5f, bodyH * 0.5f) * 1.12f;
        return new CreatureHighlightBounds
        {
            CenterOffset = new Vector2(0f, -body * 0.4f + bodyH * 0.5f),
            RadiusTiles = radius,
        };
    }

    public static float HighlightRadiusTiles(
        Creature c,
        CreatureSystem creatures,
        float camZoom,
        int detailTier,
        string shape,
        CreatureSpriteDef? spriteDef) =>
        GetHighlightBounds(c, creatures, camZoom, detailTier, shape, spriteDef).RadiusTiles;

    /// <summary>Sprite/highlight center in tile space — use as line anchor for the creature body.</summary>
    public static Vector2 GetVisualCenter(
        Creature c,
        CreatureSystem creatures,
        float camZoom,
        int detailTier,
        SpeciesDefinition def,
        CreatureSpriteDef? spriteDef)
    {
        var bounds = GetHighlightBounds(c, creatures, camZoom, detailTier, def.Shape, spriteDef);
        return DisplayPos(c) + bounds.CenterOffset;
    }

    private static CreatureHighlightBounds SpriteHighlightBounds(Creature c, float sizeTiles, CreatureSpriteDef def)
    {
        Rect2 src = def.ContentRegion;
        if (src.Size.X <= 0f || src.Size.Y <= 0f)
        {
            Vector2 texSize = def.Texture.GetSize();
            src = new Rect2(0f, 0f, texSize.X, texSize.Y);
        }

        float contentW = Math.Max(1f, src.Size.X);
        float contentH = Math.Max(1f, src.Size.Y);
        float heightTiles = Math.Max(0.35f, sizeTiles * 1.2f * def.Scale);
        float widthRatio = Math.Min(contentW / contentH, SpriteMaxWidthToHeight);
        float widthTiles = heightTiles * widthRatio;

        bool moving = Math.Sqrt(c.Vx * c.Vx + c.Vy * c.Vy) > 0.02;
        float bob = moving ? Math.Abs((float)Math.Sin(c.Walk)) * sizeTiles * 0.06f : 0f;
        float centerYOffset = heightTiles * (0.5f - def.Anchor.Y) - bob;
        float halfW = widthTiles * 0.5f;
        float halfH = heightTiles * 0.5f;
        float radius = Mathf.Sqrt(halfW * halfW + halfH * halfH) * HighlightRadiusPadding;

        return new CreatureHighlightBounds
        {
            CenterOffset = new Vector2(0f, centerYOffset),
            RadiusTiles = radius,
        };
    }

    private static CreatureHighlightBounds ProceduralSpriteHighlightBounds(float sizeTiles, string shape)
    {
        float bl = shape == "tall" ? 0.78f : shape == "stocky" ? 0.95f : 0.7f;
        float bh = shape == "tall" ? 0.5f : shape == "stocky" ? 0.62f : 0.5f;
        if (shape == "bird")
        {
            bl = 0.56f;
            bh = 0.5f;
        }

        float w = sizeTiles * bl;
        float h = sizeTiles * (bh + 0.42f);
        float radius = Mathf.Sqrt((w * 0.5f) * (w * 0.5f) + (h * 0.5f) * (h * 0.5f)) * HighlightRadiusPadding;
        return new CreatureHighlightBounds
        {
            CenterOffset = new Vector2(0f, -h * 0.28f),
            RadiusTiles = radius,
        };
    }

    public static void DrawTexturedSprite(
        CanvasItem canvas,
        Texture2D texture,
        Vector2 pos,
        float sizeTiles,
        int dir,
        bool moving,
        double walk,
        float brightness,
        Vector2 anchor,
        float speciesScale,
        Rect2 contentRegion)
    {
        float bob = moving ? Math.Abs((float)Math.Sin(walk)) * sizeTiles * 0.06f : 0f;
        Vector2 drawPos = pos with { Y = pos.Y - bob };

        Vector2 texSize = texture.GetSize();
        if (texSize.X <= 0f || texSize.Y <= 0f)
        {
            return;
        }

        Rect2 src = contentRegion.Size.X > 0f && contentRegion.Size.Y > 0f
            ? contentRegion
            : new Rect2(0f, 0f, texSize.X, texSize.Y);

        float contentW = Math.Max(1f, src.Size.X);
        float contentH = Math.Max(1f, src.Size.Y);
        float heightTiles = Math.Max(0.35f, sizeTiles * 1.2f * speciesScale);
        float widthRatio = Math.Min(contentW / contentH, SpriteMaxWidthToHeight);
        float widthTiles = heightTiles * widthRatio;
        Vector2 size = new(widthTiles, heightTiles);
        float flip = dir >= 0 ? 1f : -1f;
        Vector2 localOrigin = new(-size.X * anchor.X, -size.Y * anchor.Y);
        Color modulate = ApplyBrightness(Colors.White, brightness);

        canvas.DrawSetTransform(drawPos, 0f, new Vector2(flip, 1f));
        canvas.DrawTextureRectRegion(texture, new Rect2(localOrigin.X, localOrigin.Y, size.X, size.Y), src, modulate);
        canvas.DrawSetTransform(Vector2.Zero);
    }

    public static void DrawSprite(
        CanvasItem canvas,
        Vector2 pos,
        float size,
        int dir,
        string shape,
        Color rgb,
        Color dk,
        bool moving,
        double walk,
        bool juvenile,
        float brightness)
    {
        rgb = ApplyBrightness(rgb, brightness);
        dk = ApplyBrightness(dk, brightness);

        float legSw = moving ? (float)(Math.Sin(walk) * size * 0.28) : 0f;
        float bob = moving ? Math.Abs((float)Math.Sin(walk)) * size * 0.06f : 0f;
        float d = dir >= 0 ? 1f : -1f;
        Vector2 origin = pos;

        if (shape == "bird")
        {
            float flap = (float)(Math.Sin(walk * 1.4) * size * 0.5);
            canvas.DrawRect(new Rect2(origin.X + d * -size * 0.1f, origin.Y - flap - size * 0.1f, size * 0.9f, size * 0.22f), dk);
            canvas.DrawRect(new Rect2(origin.X + d * -size * 0.8f, origin.Y + flap - size * 0.1f, size * 0.9f, size * 0.22f), dk);
            canvas.DrawRect(new Rect2(origin.X + d * -size * 0.28f, origin.Y - size * 0.25f - bob, size * 0.56f, size * 0.5f), rgb);
            canvas.DrawRect(new Rect2(origin.X + d * size * 0.28f, origin.Y - size * 0.12f - bob, size * 0.2f, size * 0.14f), new Color("#f2c23a"));
            return;
        }

        canvas.DrawRect(new Rect2(origin.X + d * -size * 0.32f, origin.Y + size * 0.1f - legSw, size * 0.16f, size * 0.4f + legSw), dk);
        canvas.DrawRect(new Rect2(origin.X + d * size * 0.16f, origin.Y + size * 0.1f + legSw, size * 0.16f, size * 0.4f - legSw), dk);
        if (shape is "tall" or "stocky")
        {
            canvas.DrawRect(new Rect2(origin.X + d * -size * 0.1f, origin.Y + size * 0.1f + legSw * 0.6f, size * 0.14f, size * 0.4f - legSw * 0.6f), dk);
            canvas.DrawRect(new Rect2(origin.X + d * size * 0.02f, origin.Y + size * 0.1f - legSw * 0.6f, size * 0.14f, size * 0.4f + legSw * 0.6f), dk);
        }

        float bl = shape == "tall" ? 0.78f : shape == "stocky" ? 0.95f : 0.7f;
        float bh = shape == "tall" ? 0.5f : shape == "stocky" ? 0.62f : 0.5f;
        canvas.DrawRect(new Rect2(origin.X + d * -size * bl / 2f, origin.Y - size * bh / 2f - bob, size * bl, size * bh), rgb);
        canvas.DrawRect(new Rect2(origin.X + d * (size * bl / 2f - size * 0.12f), origin.Y - size * 0.5f - bob, size * 0.42f, size * 0.42f), rgb);
        if (shape == "small")
        {
            canvas.DrawRect(new Rect2(origin.X + d * (size * bl / 2f + size * 0.05f), origin.Y - size * 0.85f - bob, size * 0.12f, size * 0.4f), dk);
            canvas.DrawRect(new Rect2(origin.X + d * (size * bl / 2f + size * 0.22f), origin.Y - size * 0.85f - bob, size * 0.12f, size * 0.4f), dk);
        }

        canvas.DrawRect(new Rect2(origin.X + d * -size * (bl / 2f + 0.15f), origin.Y - size * 0.3f - bob, size * 0.18f, size * 0.3f), dk);
        canvas.DrawRect(new Rect2(origin.X + d * (size * bl / 2f + size * 0.14f), origin.Y - size * 0.4f - bob, size * 0.08f, size * 0.08f), new Color("#111111"));
        if (juvenile)
        {
            DrawJuvenileCap(canvas, origin, size, dir, moving, walk, brightness);
        }
    }

    public static void DrawJuvenileCap(
        CanvasItem canvas,
        Vector2 pos,
        float size,
        int dir,
        bool moving,
        double walk,
        float brightness)
    {
        float bob = moving ? Math.Abs((float)Math.Sin(walk)) * size * 0.06f : 0f;
        float d = dir >= 0 ? 1f : -1f;
        Vector2 origin = pos;
        Color col = ApplyBrightness(new Color(1, 1, 1, 0.6f), brightness);
        canvas.DrawRect(new Rect2(origin.X + d * -size * 0.05f, origin.Y - size * 0.9f - bob, size * 0.1f, size * 0.1f), col);
    }

    public static string? StateEmoji(string state) => state switch
    {
        "flee" => "❗",
        "thirst" => "💧",
        "graze" => "🌱",
        "hunt" => "🎯",
        "mate" => "❤️",
        "rest" => "💤",
        _ => null,
    };

    /// <summary>Draw crisp pixel-art behavior icon above a creature (JS screen-px sizing, snapped cells).</summary>
    public static void DrawStateIcon(
        CanvasItem canvas,
        Vector2 tilePos,
        string state,
        float eSize,
        float camZoom,
        float tilePixels,
        float creatureSizeTiles)
    {
        if (!TryGetStateIconGrid(state, out string[] grid)) return;

        int gridSize = grid.Length;
        float worldScale = tilePixels * Math.Max(camZoom, 0.01f);
        float sScreen = Math.Max(2.5f, camZoom * 0.9f * eSize);

        // ~8–10 screen px; grows gently with zoom, stays smaller than the creature.
        float iconScreen = Math.Clamp(sScreen * 0.45f, 8f, 10f);
        float iconSize = iconScreen / worldScale;
        float cellLocal = iconSize / gridSize;

        // Snap each grid cell to whole screen pixels so rects stay sharp when scaled.
        float cellScreen = Math.Max(1f, Mathf.Round(cellLocal * worldScale));
        cellLocal = cellScreen / worldScale;
        iconSize = cellLocal * gridSize;

        float liftScreen = Math.Max(sScreen * 0.75f, creatureSizeTiles * worldScale * 0.5f + 3f);
        float liftTiles = liftScreen / worldScale;
        Vector2 origin = new(
            tilePos.X - iconSize * 0.5f,
            tilePos.Y - liftTiles - iconSize);

        for (int y = 0; y < gridSize; y++)
        {
            string row = grid[y];
            for (int x = 0; x < gridSize && x < row.Length; x++)
            {
                if (!TryIconColor(row[x], out Color col)) continue;
                canvas.DrawRect(new Rect2(
                    origin.X + x * cellLocal,
                    origin.Y + y * cellLocal,
                    cellLocal,
                    cellLocal), col);
            }
        }
    }

    /// <summary>Pixel heart sprite for mating FX (MateIcon grid).</summary>
    public static void DrawHeartSprite(
        CanvasItem canvas,
        Vector2 center,
        float iconSizeTiles,
        float alpha,
        float camZoom)
    {
        DrawIconGrid(canvas, MateIcon, center, iconSizeTiles, alpha, camZoom);
    }

    /// <summary>Rising pixel hearts between a mating pair (wall-clock animated).</summary>
    public static void DrawMatingHeartFx(
        CanvasItem canvas,
        Vector2 anchor,
        int pairSeed,
        double animTimeSec,
        float camZoom)
    {
        float worldScale = WorldRenderer.TilePixels * Math.Max(camZoom, 0.01f);
        const float heartScreenPx = 16f;
        float baseSize = heartScreenPx / worldScale;

        for (int i = 0; i < 5; i++)
        {
            float phase = pairSeed * 0.41f + i * 0.87f;
            float cycle = (float)((animTimeSec * 1.4 + phase) % 1.6);
            float t = cycle / 1.6f;
            float alpha = (1f - t) * (1f - t);
            if (alpha < 0.04f) continue;

            float rise = t * 2.2f;
            float spread = (i - 2f) * 0.12f;
            float wobble = MathF.Sin((float)animTimeSec * 5.5f + phase * 2f) * 0.1f;
            Vector2 pos = anchor + new Vector2(spread + wobble, -0.15f - rise);
            float pulse = 0.88f + 0.12f * MathF.Sin((float)animTimeSec * 11f + phase);
            DrawHeartSprite(canvas, pos, baseSize * pulse, alpha, camZoom);
        }
    }

    private static void DrawIconGrid(
        CanvasItem canvas,
        string[] grid,
        Vector2 center,
        float iconSizeTiles,
        float alpha,
        float camZoom)
    {
        int gridSize = grid.Length;
        float worldScale = WorldRenderer.TilePixels * Math.Max(camZoom, 0.01f);
        float cellLocal = iconSizeTiles / gridSize;
        float cellScreen = Math.Max(1f, Mathf.Round(cellLocal * worldScale));
        cellLocal = cellScreen / worldScale;
        iconSizeTiles = cellLocal * gridSize;
        Vector2 origin = center - new Vector2(iconSizeTiles * 0.5f, iconSizeTiles * 0.5f);

        for (int y = 0; y < gridSize; y++)
        {
            string row = grid[y];
            for (int x = 0; x < gridSize && x < row.Length; x++)
            {
                if (!TryIconColor(row[x], out Color col)) continue;
                col.A = alpha;
                canvas.DrawRect(new Rect2(
                    origin.X + x * cellLocal,
                    origin.Y + y * cellLocal,
                    cellLocal,
                    cellLocal), col);
            }
        }
    }

    private static bool TryGetStateIconGrid(string state, out string[] grid)
    {
        grid = state switch
        {
            "flee" => FleeIcon,
            "thirst" => ThirstIcon,
            "graze" => GrazeIcon,
            "hunt" => HuntIcon,
            "mate" => MateIcon,
            "rest" => RestIcon,
            _ => [],
        };
        return grid.Length > 0;
    }

    private static bool TryIconColor(char key, out Color color)
    {
        color = key switch
        {
            'R' => new Color("#e04a3a"),
            'W' => new Color("#f0ece0"),
            'B' => new Color("#3aa8d8"),
            'G' => new Color("#4fd455"),
            'Y' => new Color("#d8c23a"),
            'K' => new Color("#141810"),
            'P' => new Color("#e84a6a"),
            'L' => new Color("#9ad8ff"),
            _ => default,
        };
        return key != '.';
    }

    // 8x8 pixel grids — each cell snaps to a whole screen pixel when drawn.
    private static readonly string[] FleeIcon =
    [
        "........",
        "...R....",
        "...R....",
        "...R....",
        "...R....",
        "........",
        "...R....",
        "........",
    ];

    private static readonly string[] ThirstIcon =
    [
        "........",
        "...B....",
        "..BBB...",
        ".BBBBB..",
        "..BBB...",
        "...B....",
        "........",
        "........",
    ];

    private static readonly string[] GrazeIcon =
    [
        "........",
        "G.....G.",
        ".G...G..",
        "..G.G...",
        "...G....",
        "..GGG...",
        ".GGGGG..",
        "........",
    ];

    private static readonly string[] HuntIcon =
    [
        ".WWWWWW.",
        "WRRRRRRW",
        "WRWWWRW",
        "WRWKWRW",
        "WRWWWRW",
        "WRRRRRRW",
        ".WWWWWW.",
    ];

    private static readonly string[] MateIcon =
    [
        "........",
        ".PP..PP.",
        "PPPPPPP.",
        ".PPPPP..",
        "..PPP...",
        "...P....",
        "........",
        "........",
    ];

    private static readonly string[] RestIcon =
    [
        "........",
        "....LL..",
        "...LL...",
        "..LL....",
        "LL......",
        ".LL.....",
        "..LLL...",
        "........",
    ];

    private static Color HslToRgb(double h, double s, double l)
    {
        if (s <= 0) return new Color((float)l, (float)l, (float)l);
        double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        double p = 2 * l - q;
        return new Color(
            (float)HueToRgb(p, q, h + 1.0 / 3.0),
            (float)HueToRgb(p, q, h),
            (float)HueToRgb(p, q, h - 1.0 / 3.0));
    }

    private static double HueToRgb(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1.0 / 6.0) return p + (q - p) * 6 * t;
        if (t < 1.0 / 2.0) return q;
        if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6;
        return p;
    }
}

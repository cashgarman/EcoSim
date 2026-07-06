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

    public static float CreatureBrightness(Creature c, Creature? selected, double lightLevel)
    {
        if (IsPedigreeLit(c, selected))
        {
            return 1f;
        }

        return (float)(0.62 + 0.38 * lightLevel);
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
        Color col = color;
        if (brightness < 0.999f)
        {
            col = col.Lightened((brightness - 0.62f) / 0.38f * 0.2f);
        }

        float m = Math.Max(0.25f, size * 0.55f);
        canvas.DrawRect(new Rect2(pos.X - m * 0.5f, pos.Y - m * 0.5f, m, m), col);
    }

    public static void DrawBodyRect(CanvasItem canvas, Vector2 pos, float size, Color color, float brightness)
    {
        Color col = color;
        if (brightness < 0.999f)
        {
            col = col.Lightened((brightness - 0.62f) / 0.38f * 0.2f);
        }

        float body = Math.Max(0.25f, size * 0.65f);
        canvas.DrawRect(new Rect2(pos.X - body * 0.5f, pos.Y - body * 0.4f, body, body * 0.8f), col);
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
        if (brightness < 0.999f)
        {
            rgb = rgb.Lightened((brightness - 0.62f) / 0.38f * 0.2f);
            dk = dk.Lightened((brightness - 0.62f) / 0.38f * 0.15f);
        }

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
            canvas.DrawRect(new Rect2(origin.X + d * -size * 0.05f, origin.Y - size * 0.9f - bob, size * 0.1f, size * 0.1f),
                new Color(1, 1, 1, 0.6f));
        }
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

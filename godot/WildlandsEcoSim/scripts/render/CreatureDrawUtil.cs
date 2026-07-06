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

    /// <summary>Draw behavior emoji above a creature (JS: screen px, converted to tile-local draw units).</summary>
    public static void DrawStateEmoji(
        CanvasItem canvas,
        Vector2 tilePos,
        string emoji,
        float camZoom,
        float eSize,
        float tilePixels)
    {
        float sScreen = Math.Max(2.5f, camZoom * 0.9f * eSize);
        int fontScreen = Math.Max(6, (int)(sScreen * 0.9f));
        float worldScale = tilePixels * Math.Max(camZoom, 0.01f);
        int fontLocal = Math.Max(1, (int)Math.Ceiling(fontScreen / worldScale));
        Vector2 offset = new(-sScreen * 0.4f / worldScale, -sScreen * 0.9f / worldScale);
        canvas.DrawString(ThemeDB.FallbackFont, tilePos + offset, emoji,
            HorizontalAlignment.Left, -1, fontLocal);
    }

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

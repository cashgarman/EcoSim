using EcoSim.Core.Data;
using EcoSim.Core.Numerics;
using EcoSim.Core.Sim;
using Godot;

namespace WildlandsEcoSim.Render;

public static class TerrainBaker
{
    public const int Tx = 8;

    public static Color BackdropOceanColor(SimState state)
    {
        double sumR = 0;
        double sumG = 0;
        double sumB = 0;
        int count = 0;

        void SampleTile(int x, int y)
        {
            if (x < 0 || x >= state.W || y < 0 || y >= state.H)
            {
                return;
            }

            int i = GridHelpers.Idx(state, x, y);
            byte biomeId = state.Biome[i];
            if (biomeId != (byte)Biome.Ocean && biomeId != (byte)Biome.Deep && biomeId != (byte)Biome.Lake)
            {
                return;
            }

            float tx = x + 0.5f;
            float ty = y + 0.5f;
            var c = SampleTerrainColor(state, tx, ty, tx * Tx, ty * Tx);
            sumR += c.R;
            sumG += c.G;
            sumB += c.B;
            count++;
        }

        for (int x = 0; x < state.W; x++)
        {
            SampleTile(x, 0);
            SampleTile(x, state.H - 1);
        }

        for (int y = 1; y < state.H - 1; y++)
        {
            SampleTile(0, y);
            SampleTile(state.W - 1, y);
        }

        if (count > 0)
        {
            return new Color(
                (float)(sumR / count),
                (float)(sumG / count),
                (float)(sumB / count));
        }

        var rgb = BiomeData.Info[Biome.Ocean].ColorRgb;
        return new Color(rgb[0] / 255f, rgb[1] / 255f, rgb[2] / 255f);
    }

    public static Image BakeTerrainImage(SimState state)
    {
        int w = state.W * Tx;
        int h = state.H * Tx;
        byte[] data = new byte[w * h * 4];

        for (int py = 0; py < h; py++)
        {
            for (int px = 0; px < w; px++)
            {
                float tx = (px + 0.5f) / Tx;
                float ty = (py + 0.5f) / Tx;
                WriteColor(data, (py * w + px) * 4, SampleTerrainColor(state, tx, ty, px, py));
            }
        }

        return Image.CreateFromData(w, h, false, Image.Format.Rgba8, data);
    }

    public static Image BakeBiomeImage(SimState state) => BakeTerrainImage(state);

    public static Image BakeVegImage(SimState state)
    {
        int w = state.W * Tx;
        int h = state.H * Tx;
        byte[] data = new byte[w * h * 4];

        for (int py = 0; py < h; py++)
        {
            for (int px = 0; px < w; px++)
            {
                float tx = (px + 0.5f) / Tx;
                float ty = (py + 0.5f) / Tx;
                float alpha = SampleVegAlpha(state, tx, ty);
                int o = (py * w + px) * 4;
                if (alpha <= 0.001f)
                {
                    continue;
                }

                WriteColor(data, o, new Color(0.18f, 0.72f, 0.22f, alpha));
            }
        }

        return Image.CreateFromData(w, h, false, Image.Format.Rgba8, data);
    }

    public static Image BakeWaterMaskImage(SimState state)
    {
        int w = state.W * Tx;
        int h = state.H * Tx;
        byte[] data = new byte[w * h * 4];

        for (int py = 0; py < h; py++)
        {
            for (int px = 0; px < w; px++)
            {
                float tx = (px + 0.5f) / Tx;
                float ty = (py + 0.5f) / Tx;
                float coverage = SampleWaterCoverage(state, tx, ty);
                int o = (py * w + px) * 4;
                if (coverage <= 0.001f)
                {
                    continue;
                }

                WriteColor(data, o, new Color(1f, 1f, 1f, coverage));
            }
        }

        return Image.CreateFromData(w, h, false, Image.Format.Rgba8, data);
    }

    private static void WriteColor(byte[] data, int o, Color c)
    {
        data[o] = (byte)c.R8;
        data[o + 1] = (byte)c.G8;
        data[o + 2] = (byte)c.B8;
        data[o + 3] = (byte)c.A8;
    }

    private static Color SampleTerrainColor(SimState state, float tx, float ty, float worldPx, float worldPy)
    {
        GetTileCorners(state, tx, ty, out int x0, out int y0, out int x1, out int y1, out float u, out float v);
        Color c00 = TileColor(state, x0, y0, worldPx, worldPy);
        Color c10 = TileColor(state, x1, y0, worldPx, worldPy);
        Color c01 = TileColor(state, x0, y1, worldPx, worldPy);
        Color c11 = TileColor(state, x1, y1, worldPx, worldPy);
        return BilinearColor(c00, c10, c01, c11, u, v);
    }

    private static float SampleVegAlpha(SimState state, float tx, float ty)
    {
        GetTileCorners(state, tx, ty, out int x0, out int y0, out int x1, out int y1, out float u, out float v);
        float a00 = TileVegAlpha(state, x0, y0);
        float a10 = TileVegAlpha(state, x1, y0);
        float a01 = TileVegAlpha(state, x0, y1);
        float a11 = TileVegAlpha(state, x1, y1);
        return Bilinear(a00, a10, a01, a11, u, v) * 0.85f;
    }

    private static float SampleWaterCoverage(SimState state, float tx, float ty)
    {
        GetTileCorners(state, tx, ty, out int x0, out int y0, out int x1, out int y1, out float u, out float v);
        float w00 = TileWaterWeight(state, x0, y0);
        float w10 = TileWaterWeight(state, x1, y0);
        float w01 = TileWaterWeight(state, x0, y1);
        float w11 = TileWaterWeight(state, x1, y1);
        float coverage = Bilinear(w00, w10, w01, w11, u, v);
        return SmoothStep(coverage);
    }

    private static void GetTileCorners(
        SimState state,
        float tx,
        float ty,
        out int x0,
        out int y0,
        out int x1,
        out int y1,
        out float u,
        out float v)
    {
        x0 = Math.Clamp((int)MathF.Floor(tx), 0, state.W - 1);
        y0 = Math.Clamp((int)MathF.Floor(ty), 0, state.H - 1);
        x1 = Math.Clamp(x0 + 1, 0, state.W - 1);
        y1 = Math.Clamp(y0 + 1, 0, state.H - 1);
        u = SmoothStep(tx - x0);
        v = SmoothStep(ty - y0);
    }

    private static Color TileColor(SimState state, int x, int y, float worldPx, float worldPy)
    {
        int i = GridHelpers.Idx(state, x, y);
        byte biomeId = state.Biome[i];
        var info = BiomeData.Info[(Biome)biomeId];
        float e = state.Elev[i];
        double hN = EcoSim.Core.Numerics.Noise.HashN((int)worldPx, (int)worldPy, 7);
        float shd = 0.9f + (e - 0.5f) * 0.28f + ((float)hN - 0.5f) * 0.14f;
        return Shade(info.ColorRgb, shd);
    }

    private static float TileVegAlpha(SimState state, int x, int y)
    {
        int i = GridHelpers.Idx(state, x, y);
        float cap = state.VegCap[i];
        if (cap <= 0.001f || BiomeData.IsWater(state.Biome[i])) return 0f;
        return Math.Clamp(state.Veg[i] / cap, 0f, 1f);
    }

    private static float TileWaterWeight(SimState state, int x, int y)
    {
        int i = GridHelpers.Idx(state, x, y);
        return BiomeData.IsWater(state.Biome[i]) ? 1f : 0f;
    }

    private static float Bilinear(float v00, float v10, float v01, float v11, float u, float v)
    {
        float top = Mathf.Lerp(v00, v10, u);
        float bot = Mathf.Lerp(v01, v11, u);
        return Mathf.Lerp(top, bot, v);
    }

    private static Color BilinearColor(Color c00, Color c10, Color c01, Color c11, float u, float v)
    {
        return new Color(
            Bilinear(c00.R, c10.R, c01.R, c11.R, u, v),
            Bilinear(c00.G, c10.G, c01.G, c11.G, u, v),
            Bilinear(c00.B, c10.B, c01.B, c11.B, u, v),
            1f);
    }

    private static float SmoothStep(float t) => t * t * (3f - 2f * t);

    private static Color Shade(byte[] rgb, float mult)
    {
        return new Color(
            Math.Clamp(rgb[0] / 255f * mult, 0f, 1f),
            Math.Clamp(rgb[1] / 255f * mult, 0f, 1f),
            Math.Clamp(rgb[2] / 255f * mult, 0f, 1f));
    }

    /// <summary>Deep-ocean terrain pixel outside the grid — elevation clamped to nearest in-bounds tile.</summary>
    public static void WriteOceanTerrainPixel(byte[] data, int o, int tx, int ty, SimState state)
    {
        float sampleTx = tx + 0.5f;
        float sampleTy = ty + 0.5f;
        float worldPx = sampleTx * Tx;
        float worldPy = sampleTy * Tx;
        var c = SampleTerrainColor(state, sampleTx, sampleTy, worldPx, worldPy);
        WriteColor(data, o, new Color(c.R, c.G, c.B, 1f));
    }
}

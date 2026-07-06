using EcoSim.Core.Data;
using EcoSim.Core.Numerics;
using EcoSim.Core.Sim;
using Godot;

namespace WildlandsEcoSim.Render;

public static class TerrainBaker
{
    public const int Tx = 4;

    public static Image BakeTerrainImage(SimState state)
    {
        int w = state.W * Tx;
        int h = state.H * Tx;
        var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);

        for (int y = 0; y < state.H; y++)
        {
            for (int x = 0; x < state.W; x++)
            {
                int i = GridHelpers.Idx(state, x, y);
                byte biomeId = state.Biome[i];
                var info = BiomeData.Info[(Biome)biomeId];
                float e = state.Elev[i];

                for (int sy = 0; sy < Tx; sy++)
                {
                    for (int sx = 0; sx < Tx; sx++)
                    {
                        int px = x * Tx + sx;
                        int py = y * Tx + sy;
                        double hN = EcoSim.Core.Numerics.Noise.HashN(px, py, 7);
                        float shd = 0.9f + (e - 0.5f) * 0.28f + ((float)hN - 0.5f) * 0.14f;
                        var c = Shade(info.ColorRgb, shd);
                        img.SetPixel(px, py, c);
                    }
                }
            }
        }
        return img;
    }

    public static Image BakeBiomeImage(SimState state) => BakeTerrainImage(state);

    public static Image BakeVegImage(SimState state)
    {
        var img = Image.CreateEmpty(state.W, state.H, false, Image.Format.Rgba8);
        for (int y = 0; y < state.H; y++)
        {
            for (int x = 0; x < state.W; x++)
            {
                int i = GridHelpers.Idx(state, x, y);
                float cap = state.VegCap[i];
                if (cap <= 0.001f)
                {
                    img.SetPixel(x, y, Colors.Transparent);
                    continue;
                }

                float alpha = Math.Clamp(state.Veg[i] / cap, 0f, 1f) * 0.85f;
                img.SetPixel(x, y, new Color(0.18f, 0.72f, 0.22f, alpha));
            }
        }
        return img;
    }

    public static Image BakeWaterImage(SimState state, double animPhase)
    {
        var img = Image.CreateEmpty(state.W, state.H, false, Image.Format.Rgba8);
        for (int y = 0; y < state.H; y++)
        {
            for (int x = 0; x < state.W; x++)
            {
                int i = GridHelpers.Idx(state, x, y);
                if (!BiomeData.IsWater(state.Biome[i]))
                {
                    img.SetPixel(x, y, Colors.Transparent);
                    continue;
                }

                double n = EcoSim.Core.Numerics.Noise.HashN(x, y, 13);
                float shimmer = 0.15f + (float)(Math.Sin((x + y) * 0.3 + animPhase) * 0.5 + 0.5) * 0.2f;
                float alpha = (float)(0.25 + n * 0.15) + shimmer * 0.3f;
                img.SetPixel(x, y, new Color(0.2f, 0.55f, 0.9f, Math.Clamp(alpha, 0f, 0.65f)));
            }
        }
        return img;
    }

    private static Color Shade(byte[] rgb, float mult)
    {
        return new Color(
            Math.Clamp(rgb[0] / 255f * mult, 0f, 1f),
            Math.Clamp(rgb[1] / 255f * mult, 0f, 1f),
            Math.Clamp(rgb[2] / 255f * mult, 0f, 1f));
    }
}

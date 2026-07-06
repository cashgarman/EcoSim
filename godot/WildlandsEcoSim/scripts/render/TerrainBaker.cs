using EcoSim.Core.Data;
using EcoSim.Core.Sim;
using Godot;

namespace WildlandsEcoSim.Render;

public static class TerrainBaker
{
    public static Image BakeBiomeImage(SimState state)
    {
        var img = Image.CreateEmpty(state.W, state.H, false, Image.Format.Rgba8);
        for (int y = 0; y < state.H; y++)
        {
            for (int x = 0; x < state.W; x++)
            {
                int i = GridHelpers.Idx(state, x, y);
                byte biomeId = state.Biome[i];
                var info = BiomeData.Info[(Biome)biomeId];
                img.SetPixel(x, y, new Color(
                    info.ColorRgb[0] / 255f,
                    info.ColorRgb[1] / 255f,
                    info.ColorRgb[2] / 255f));
            }
        }
        return img;
    }

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
}

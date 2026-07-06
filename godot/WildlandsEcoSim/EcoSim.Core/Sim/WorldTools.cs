using EcoSim.Core.Data;

namespace EcoSim.Core.Sim;

public static class WorldTools
{
    public static void ApplyRain(SimState state, int cx, int cy, int radius = 4)
    {
        for (int dy = -radius; dy <= radius; dy++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                int nx = cx + dx, ny = cy + dy;
                if (!GridHelpers.InBounds(state, nx, ny)) continue;
                int i = GridHelpers.Idx(state, nx, ny);
                if (BiomeData.IsWater(state.Biome[i])) continue;
                state.Veg[i] = state.VegCap[i];
            }
        }
        state.VegDirty = true;
    }

    public static void ApplyDrought(SimState state, int cx, int cy, int radius = 4)
    {
        for (int dy = -radius; dy <= radius; dy++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                int nx = cx + dx, ny = cy + dy;
                if (!GridHelpers.InBounds(state, nx, ny)) continue;
                state.Veg[GridHelpers.Idx(state, nx, ny)] = 0;
            }
        }
        state.VegDirty = true;
    }

    public static void ApplyMeteor(SimState state, CreatureSystem creatures, double wx, double wy, int radius = 3)
    {
        foreach (var c in state.Creatures)
        {
            if (c.Dead) continue;
            double d = Math.Sqrt((c.X - wx) * (c.X - wx) + (c.Y - wy) * (c.Y - wy));
            if (d < radius) creatures.Die(c, "meteor");
        }

        int cx = (int)Math.Round(wx), cy = (int)Math.Round(wy);
        for (int dy = -radius; dy < radius; dy++)
        {
            for (int dx = -radius; dx < radius; dx++)
            {
                if (dx * dx + dy * dy >= radius * radius) continue;
                int nx = cx + dx, ny = cy + dy;
                if (!GridHelpers.InBounds(state, nx, ny)) continue;
                int i = GridHelpers.Idx(state, nx, ny);
                if (BiomeData.IsWater(state.Biome[i])) continue;
                state.Veg[i] = 0;
            }
        }
        state.VegDirty = true;
    }

    public static void ApplyCull(SimState state, CreatureSystem creatures, double wx, double wy, double radius = 1.5)
    {
        foreach (var c in state.Creatures)
        {
            if (c.Dead) continue;
            double d = Math.Sqrt((c.X - wx) * (c.X - wx) + (c.Y - wy) * (c.Y - wy));
            if (d < radius) creatures.Die(c, "removed");
        }
    }
}

using EcoSim.Core.Data;
using EcoSim.Core.Numerics;
using EcoSim.Core.Rng;

namespace EcoSim.Core.Sim;

public sealed class WorldGenerator
{
    private readonly SimState _state;

    public WorldGenerator(SimState state)
    {
        _state = state;
    }

    public Biome BiomeAt(double e, double t, double m)
    {
        var cfg = _state.Cfg;
        if (e < cfg.Sea - 0.09) return Biome.Deep;
        if (e < cfg.Sea) return Biome.Ocean;
        if (e < cfg.Sea + 0.015) return Biome.Beach;
        if (e > 0.86) return e > 0.93 ? Biome.Peak : (t < 0.35 ? Biome.Snow : Biome.Mountain);
        if (t < 0.22) return m < 0.5 ? Biome.Tundra : Biome.Snow;
        if (t < 0.42) return m < 0.35 ? Biome.Tundra : Biome.Taiga;
        if (t < 0.72)
        {
            if (m < 0.22) return Biome.Grass;
            if (m < 0.5) return Biome.Grass;
            if (m < 0.78) return Biome.Forest;
            return Biome.Swamp;
        }
        if (m < 0.25) return Biome.Desert;
        if (m < 0.5) return Biome.Savanna;
        if (m < 0.78) return Biome.Forest;
        return Biome.Rainforest;
    }

    public void ComputeLandBounds()
    {
        int minX = _state.W, minY = _state.H, maxX = 0, maxY = 0;
        bool found = false;
        for (int y = 0; y < _state.H; y++)
        {
            for (int x = 0; x < _state.W; x++)
            {
                if (BiomeData.IsWater(_state.Biome[GridHelpers.Idx(_state, x, y)])) continue;
                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
                found = true;
            }
        }

        if (!found)
        {
            minX = 0; minY = 0; maxX = _state.W; maxY = _state.H;
        }
        else
        {
            maxX += 1; maxY += 1;
        }

        _state.LandBounds = new LandBounds { MinX = minX, MinY = minY, MaxX = maxX, MaxY = maxY };
    }

    public void Generate()
    {
        GlobalRng.SetSeed(_state.Seed);
        var sz = WorldSizePresets.Get(_state.Cfg.Size);
        _state.WorldAreaKm2 = sz.AreaKm2;
        int sideTiles = (int)Math.Round(sz.SideKm * sz.TilesPerKm);
        _state.W = sideTiles;
        _state.H = sideTiles;
        _state.WorldKmPerTile = sz.SideKm / sideTiles;
        int area = _state.W * _state.H;
        _state.GrowStride = (int)SimMath.Clamp(_state.H / 40.0, 4, 12);

        _state.Elev = new float[area];
        _state.Temp = new float[area];
        _state.Moist = new float[area];
        _state.Biome = new byte[area];
        _state.Veg = new float[area];
        _state.VegCap = new float[area];
        _state.PassMask = new byte[area];

        int eS = GlobalRng.Ri(1, 9999);
        int mS = GlobalRng.Ri(1, 9999);
        int tS = GlobalRng.Ri(1, 9999);
        int lS = GlobalRng.Ri(1, 9999);
        double relief = _state.Cfg.Relief;
        var cfg = _state.Cfg;

        for (int y = 0; y < _state.H; y++)
        {
            for (int x = 0; x < _state.W; x++)
            {
                double e = Noise.Fbm(x, y, eS, 5, 0.028, 0.5);
                double cx = x / (double)_state.W - 0.5;
                double cy = y / (double)_state.H - 0.5;
                double radial = Math.Sqrt(cx * cx + cy * cy) * 1.9;
                double falloff = Math.Pow(SimMath.Clamp(radial, 0, 1), 2.2) * 0.34;
                e = e * (0.7 + relief * 0.5) + 0.34 - falloff;
                e += (Noise.Fbm(x, y, eS + 31, 3, 0.09, 0.5) - 0.5) * 0.15 * relief;
                e = SimMath.Clamp(e, 0, 1);

                double moist = Noise.Fbm(x, y, mS, 4, 0.04, 0.55) * 0.7 + cfg.Moist * 0.4;
                double lat = 1 - Math.Abs(y / (double)_state.H - 0.5) * 2;
                double temp = lat * 0.7 + cfg.Temp * 0.5 - Math.Max(0, e - cfg.Sea) * 0.55
                    + (Noise.Fbm(x, y, tS, 3, 0.05, 0.5) - 0.5) * 0.25;
                temp = SimMath.Clamp(temp, 0, 1);
                moist = SimMath.Clamp(moist, 0, 1);

                int ti = GridHelpers.Idx(_state, x, y);
                _state.Elev[ti] = (float)e;
                _state.Temp[ti] = (float)temp;
                _state.Moist[ti] = (float)moist;

                Biome b = BiomeAt(e, temp, moist);
                if (!BiomeData.IsWater(b) && e < 0.62)
                {
                    double lake = Noise.Fbm(x, y, lS, 3, 0.07, 0.5);
                    if (lake > 0.72) b = Biome.Lake;
                }

                _state.Biome[ti] = (byte)b;
                double cap = BiomeData.Info[b].VegCap;
                _state.VegCap[ti] = (float)cap;
                _state.Veg[ti] = (float)(cap * GlobalRng.Rf(0.4, 1.0));
            }
        }

        ComputeLandBounds();
        Navigation.BuildWaterDistanceField(_state);
        _state.VegDirty = true;
        _state.GrowRow = 0;
    }

    public void GrowVegetation(double dt)
    {
        int y = _state.GrowRow;
        for (int x = 0; x < _state.W; x++)
        {
            int ti = GridHelpers.Idx(_state, x, y);
            float cap = _state.VegCap[ti];
            if (cap > 0.02f && _state.Veg[ti] < cap)
            {
                _state.Veg[ti] = Math.Min(cap,
                    _state.Veg[ti] + (float)(cap * 0.22 * dt * _state.GrowStride * (0.6 + _state.Moist[ti])));
            }
        }

        _state.GrowRow = (_state.GrowRow + 1) % _state.H;
        if (_state.GrowRow == 0) _state.VegDirty = true;
    }
}

namespace EcoSim.Core.Data;

public enum Biome : byte
{
    Deep = 0,
    Ocean = 1,
    Lake = 2,
    Beach = 3,
    Desert = 4,
    Savanna = 5,
    Grass = 6,
    Shrub = 7,
    Forest = 8,
    Rainforest = 9,
    Swamp = 10,
    Taiga = 11,
    Tundra = 12,
    Snow = 13,
    Mountain = 14,
    Peak = 15,
}

public sealed record BiomeInfo(string Name, byte[] ColorRgb, bool Water, bool Passable, double VegCap);

public static class BiomeData
{
    public static readonly IReadOnlyDictionary<Biome, BiomeInfo> Info = new Dictionary<Biome, BiomeInfo>
    {
        [Biome.Deep] = new("Deep Ocean", [30, 72, 120], true, false, 0),
        [Biome.Ocean] = new("Ocean", [46, 108, 168], true, false, 0),
        [Biome.Lake] = new("Lake", [62, 132, 200], true, false, 0),
        [Biome.Beach] = new("Beach", [222, 206, 150], false, true, 0.08),
        [Biome.Desert] = new("Desert", [224, 200, 132], false, true, 0.06),
        [Biome.Savanna] = new("Savanna", [196, 182, 98], false, true, 0.5),
        [Biome.Grass] = new("Grassland", [126, 178, 84], false, true, 0.75),
        [Biome.Shrub] = new("Shrubland", [154, 170, 98], false, true, 0.45),
        [Biome.Forest] = new("Forest", [70, 132, 64], false, true, 0.85),
        [Biome.Rainforest] = new("Rainforest", [44, 112, 58], false, true, 1.0),
        [Biome.Swamp] = new("Swamp", [84, 112, 74], false, true, 0.7),
        [Biome.Taiga] = new("Taiga", [92, 130, 102], false, true, 0.4),
        [Biome.Tundra] = new("Tundra", [158, 162, 142], false, true, 0.2),
        [Biome.Snow] = new("Snow", [236, 240, 244], false, true, 0.04),
        [Biome.Mountain] = new("Mountains", [132, 128, 122], false, true, 0.1),
        [Biome.Peak] = new("Peak", [226, 228, 232], false, false, 0.02),
    };

    public static bool IsWater(Biome biome)
    {
        return biome <= Biome.Lake;
    }

    public static bool IsWater(byte biomeId)
    {
        return biomeId <= (byte)Biome.Lake;
    }
}

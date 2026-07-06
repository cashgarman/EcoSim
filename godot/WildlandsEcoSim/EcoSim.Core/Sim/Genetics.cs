using EcoSim.Core.Data;
using EcoSim.Core.Numerics;
using EcoSim.Core.Rng;

namespace EcoSim.Core.Sim;

public static class Genetics
{
    public static Genome NewGenome(SpeciesCatalog catalog, string sp)
    {
        var b = catalog.Get(sp).Base;
        var g = new Genome
        {
            Size = ClampGene(catalog, "size", b.Size * (1 + GlobalRng.Gauss() * 0.12)),
            Speed = ClampGene(catalog, "speed", b.Speed * (1 + GlobalRng.Gauss() * 0.12)),
            Sense = ClampGene(catalog, "sense", b.Sense * (1 + GlobalRng.Gauss() * 0.12)),
            Metab = ClampGene(catalog, "metab", b.Metab * (1 + GlobalRng.Gauss() * 0.12)),
            Litter = ClampGene(catalog, "litter", b.Litter * (1 + GlobalRng.Gauss() * 0.12)),
            Lifespan = ClampGene(catalog, "lifespan", b.Lifespan * (1 + GlobalRng.Gauss() * 0.12)),
            Temp = ClampGene(catalog, "temp", b.Temp * (1 + GlobalRng.Gauss() * 0.12)),
            Tol = ClampGene(catalog, "tol", b.Tol * (1 + GlobalRng.Gauss() * 0.12)),
            Hue = ClampGene(catalog, "hue", b.Hue + GlobalRng.Gauss() * 20),
            Agg = ClampGene(catalog, "agg", b.Agg * (1 + GlobalRng.Gauss() * 0.12)),
        };
        return g;
    }

    public static Genome BreedGenome(SpeciesCatalog catalog, Genome a, Genome b)
    {
        double Mut(string key, double va, double vb)
        {
            catalog.GeneRange.TryGetValue(key, out var range);
            double min = range?[0] ?? 0;
            double max = range?[1] ?? 1;
            double v = (va + vb) / 2 + GlobalRng.Gauss() * (max - min) * 0.05;
            if (GlobalRng.Next() < 0.02) v += GlobalRng.Gauss() * (max - min) * 0.18;
            return SimMath.Clamp(v, min, max);
        }

        return new Genome
        {
            Size = Mut("size", a.Size, b.Size),
            Speed = Mut("speed", a.Speed, b.Speed),
            Sense = Mut("sense", a.Sense, b.Sense),
            Metab = Mut("metab", a.Metab, b.Metab),
            Litter = Mut("litter", a.Litter, b.Litter),
            Lifespan = Mut("lifespan", a.Lifespan, b.Lifespan),
            Temp = Mut("temp", a.Temp, b.Temp),
            Tol = Mut("tol", a.Tol, b.Tol),
            Hue = Mut("hue", a.Hue, b.Hue),
            Agg = Mut("agg", a.Agg, b.Agg),
        };
    }

    private static double ClampGene(SpeciesCatalog catalog, string key, double value)
    {
        if (!catalog.GeneRange.TryGetValue(key, out var range)) return value;
        return SimMath.Clamp(value, range[0], range[1]);
    }
}

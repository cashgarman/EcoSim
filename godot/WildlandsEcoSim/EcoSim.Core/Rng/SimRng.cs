namespace EcoSim.Core.Rng;

/// <summary>
/// Seeded LCG RNG — must match <c>js/utils.js</c> for deterministic parity.
/// </summary>
public sealed class SimRng
{
    private uint _state;

    public SimRng(uint seed)
    {
        _state = seed;
    }

    public uint Seed
    {
        get => _state;
        set => _state = value;
    }

    public double Next()
    {
        _state = unchecked(_state * 1664525u + 1013904223u);
        return _state / 4294967296.0;
    }

    public int Ri(int a, int b)
    {
        return a + (int)Math.Floor(Next() * (b - a + 1));
    }

    public double Rf(double a, double b)
    {
        return a + Next() * (b - a);
    }

    public T Pick<T>(IReadOnlyList<T> arr)
    {
        return arr[(int)Math.Floor(Next() * arr.Count)];
    }

    public double Gauss()
    {
        return (Next() + Next() + Next() + Next() - 2.0) / 2.0;
    }
}

/// <summary>Global RNG instance mirroring <c>setRngSeed</c> / <c>rng()</c> in JS.</summary>
public static class GlobalRng
{
    private static SimRng _rng = new(1);

    public static void SetSeed(uint seed)
    {
        _rng = new SimRng(seed);
    }

    public static SimRng Instance => _rng;

    public static double Next() => _rng.Next();

    public static int Ri(int a, int b) => _rng.Ri(a, b);

    public static double Rf(double a, double b) => _rng.Rf(a, b);

    public static double Gauss() => _rng.Gauss();
}

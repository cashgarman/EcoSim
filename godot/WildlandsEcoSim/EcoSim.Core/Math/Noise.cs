namespace EcoSim.Core.Math;

/// <summary>Procedural noise — ports <c>hashN</c>, <c>vnoise</c>, <c>fbm</c> from <c>js/utils.js</c>.</summary>
public static class Noise
{
    public static double HashN(int x, int y, int seed)
    {
        uint h = unchecked((uint)(x * 374761393 + y * 668265263 + seed * 40499));
        h = unchecked((h ^ (h >> 13)) * 1274126177);
        h = h ^ (h >> 16);
        return h / 4294967296.0;
    }

    public static double VNoise(double x, double y, int seed)
    {
        int xi = (int)Math.Floor(x);
        int yi = (int)Math.Floor(y);
        double xf = x - xi;
        double yf = y - yi;
        double u = xf * xf * (3.0 - 2.0 * xf);
        double v = yf * yf * (3.0 - 2.0 * yf);
        double a = HashN(xi, yi, seed);
        double b = HashN(xi + 1, yi, seed);
        double c = HashN(xi, yi + 1, seed);
        double d = HashN(xi + 1, yi + 1, seed);
        return a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v;
    }

    public static double Fbm(double x, double y, int seed, int octaves, double frequency, double persistence)
    {
        double amplitude = 1.0;
        double freq = frequency;
        double sum = 0.0;
        double norm = 0.0;

        for (int octave = 0; octave < octaves; octave++)
        {
            sum += amplitude * VNoise(x * freq, y * freq, seed + octave * 97);
            norm += amplitude;
            amplitude *= persistence;
            freq *= 2.0;
        }

        return sum / norm;
    }
}

namespace EcoSim.Core.Numerics;

/// <summary>Procedural noise — ports <c>hashN</c>, <c>vnoise</c>, <c>fbm</c> from <c>js/utils.js</c>.</summary>
public static class Noise
{
    public static double HashN(int x, int y, int seed)
    {
        // JS bitwise ops use ToInt32; >>> 0 uses ToUint32 on the numeric result
        uint h = JsToUint32(x * 374761393.0 + y * 668265263.0 + seed * 40499.0);
        int signed = JsToInt32(h);
        h = JsToUint32((signed ^ (signed >> 13)) * 1274126177.0);
        signed = JsToInt32(h);
        h = JsToUint32(signed ^ (signed >> 16));
        return h / 4294967296.0;
    }

    private static int JsToInt32(uint value)
    {
        return unchecked((int)value);
    }

    private static uint JsToUint32(double value)
    {
        double pos = Math.Sign(value) * Math.Floor(Math.Abs(value));
        pos %= 4294967296.0;
        return (uint)pos;
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

namespace EcoSim.Core.Numerics;

using EcoSim.Core.Sim;

public static class SimMath
{
    public static double Clamp(double v, double min, double max)
    {
        if (v < min) return min;
        return v > max ? max : v;
    }

    public static float Clamp(float v, float min, float max)
    {
        if (v < min) return min;
        return v > max ? max : v;
    }

    public static double Lerp(double a, double b, double t)
    {
        return a + (b - a) * t;
    }

    public static double Hypot(double x, double y)
    {
        return Math.Sqrt(x * x + y * y);
    }

    public static double ExpSmoothT(double rate, double dt)
    {
        return 1.0 - Math.Exp(-rate * dt);
    }

    public static double LightLevelFromTimeOfDay(double timeOfDay)
    {
        double sun = Math.Sin((timeOfDay - 0.25) * Math.PI * 2.0);
        return Clamp(Math.Pow(sun * 0.5 + 0.5, 0.9), 0.08, 1.0);
    }

    /// <summary>Maps global sim time to day-phase (matches js/timeline-viewport.js).</summary>
    public static double TimeOfDayAtSimT(double t, double origin = 0.3)
    {
        return ((origin + t / SimConstants.SimDaySeconds) % 1.0 + 1.0) % 1.0;
    }

    public static (string Phase, string Icon, string Label) DayPhaseFromTimeOfDay(double timeOfDay)
    {
        double frac = ((timeOfDay % 1.0) + 1.0) % 1.0;
        double light = LightLevelFromTimeOfDay(frac);
        if (light < 0.28) return ("night", "🌙", "Night");
        if (light < 0.55)
        {
            return frac < 0.5
                ? ("dawn", "🌅", "Dawn")
                : ("dusk", "🌇", "Dusk");
        }
        return ("day", "☀️", "Day");
    }

    public static string FormatTimeOfDay12(double timeOfDay)
    {
        double frac = ((timeOfDay % 1.0) + 1.0) % 1.0;
        int totalMinutes = (int)Math.Round(frac * 24.0 * 60.0) % (24 * 60);
        int hours24 = totalMinutes / 60;
        int minutes = totalMinutes % 60;
        string ampm = hours24 >= 12 ? "PM" : "AM";
        int hours12 = hours24 % 12;
        if (hours12 == 0) hours12 = 12;
        string minStr = minutes < 10 ? "0" + minutes : minutes.ToString();
        return $"{hours12}:{minStr} {ampm}";
    }
}

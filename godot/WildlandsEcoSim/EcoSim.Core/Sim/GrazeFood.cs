namespace EcoSim.Core.Sim;

/// <summary>Shared vegetation edibility checks for graze goal selection and eating.</summary>
public static class GrazeFood
{
    public const double AbsoluteMin = 0.01;

    public static double MinEdibleAmount(float vegCap)
    {
        if (vegCap <= 0) return double.PositiveInfinity;
        return Math.Max(AbsoluteMin, vegCap * 0.04);
    }

    public static bool IsEdible(float veg, float vegCap) => vegCap > 0 && veg > MinEdibleAmount(vegCap);

    public static bool IsEdible(SimState state, int tileIndex)
        => IsEdible(state.Veg[tileIndex], state.VegCap[tileIndex]);
}

namespace EcoSim.Core.Sim;

public static class GridHelpers
{
    public static int Idx(SimState state, int x, int y) => y * state.W + x;

    public static bool InBounds(SimState state, int x, int y)
    {
        return x >= 0 && y >= 0 && x < state.W && y < state.H;
    }

    public static long Gkey(int cx, int cy) => cx * 100000L + cy;
}

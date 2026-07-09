namespace EcoSim.Core.Sim;

using EcoSim.Core.Numerics;

public static class GridHelpers
{
    public static int Idx(SimState state, int x, int y) => y * state.W + x;

    public static bool InBounds(SimState state, int x, int y)
    {
        return x >= 0 && y >= 0 && x < state.W && y < state.H;
    }

    public static long Gkey(int cx, int cy) => cx * 100000L + cy;

    /// <summary>Tile index for world coords where entities live at tile centers (x+0.5).</summary>
    public static int TileX(SimState state, double x)
        => (int)SimMath.Clamp(Math.Floor(x), 0, state.W - 1);

    public static int TileY(SimState state, double y)
        => (int)SimMath.Clamp(Math.Floor(y), 0, state.H - 1);

    public static int TileIdx(SimState state, double x, double y)
        => Idx(state, TileX(state, x), TileY(state, y));
}

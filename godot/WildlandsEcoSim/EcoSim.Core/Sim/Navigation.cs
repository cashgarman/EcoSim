using EcoSim.Core.Data;
using EcoSim.Core.Numerics;
using EcoSim.Core.Rng;

namespace EcoSim.Core.Sim;

public static class Navigation
{
    private static readonly (int Dx, int Dy, int Cost)[] NavDirs =
    [
        (0, 1, 10), (1, 0, 10), (0, -1, 10), (-1, 0, 10),
        (1, 1, 14), (1, -1, 14), (-1, 1, 14), (-1, -1, 14),
    ];

    private static readonly (int Dx, int Dy)[] ShoreNeighbors =
    [
        (-1, -1), (0, -1), (1, -1),
        (-1, 0), (1, 0),
        (-1, 1), (0, 1), (1, 1),
    ];

    private static int[]? _astarGScore;
    private static int[]? _astarFScore;
    private static int[]? _astarParent;
    private static byte[]? _astarClosed;
    private static int[]? _astarHeapIdx;
    private static int[]? _astarHeapVal;

    public static bool IsTileWalkable(SimState state, int tx, int ty, bool canSwim)
    {
        if (!GridHelpers.InBounds(state, tx, ty)) return false;
        int ti = GridHelpers.Idx(state, tx, ty);
        var b = (Biome)state.Biome[ti];
        if (b == Biome.Peak) return false;
        if (BiomeData.IsWater(b)) return canSwim;
        if ((state.PassMask[ti] & SimConstants.PassGroundBlocked) != 0) return false;
        return true;
    }

    public static bool AtWaterEdge(SimState state, double x, double y)
    {
        int ix = (int)Math.Round(x);
        int iy = (int)Math.Round(y);
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = ix + dx, ny = iy + dy;
                if (GridHelpers.InBounds(state, nx, ny) && BiomeData.IsWater(state.Biome[GridHelpers.Idx(state, nx, ny)]))
                {
                    return true;
                }
            }
        }
        return false;
    }

    public static bool LineOfSightClear(SimState state, double x0, double y0, double x1, double y1, bool canSwim)
    {
        int x = (int)Math.Round(x0), y = (int)Math.Round(y0);
        int tx = (int)Math.Round(x1), ty = (int)Math.Round(y1);
        int dx = Math.Abs(tx - x), dy = Math.Abs(ty - y);
        int sx = x < tx ? 1 : -1, sy = y < ty ? 1 : -1;
        int err = dx - dy;
        while (true)
        {
            if (!IsTileWalkable(state, x, y, canSwim)) return false;
            if (x == tx && y == ty) return true;
            int e2 = err * 2;
            if (e2 > -dy) { err -= dy; x += sx; }
            if (e2 < dx) { err += dx; y += sy; }
        }
    }

    public static (int X, int Y)? SnapWalkableGoal(SimState state, int gx, int gy, bool canSwim, int radius = 8)
    {
        if (IsTileWalkable(state, gx, gy, canSwim)) return (gx, gy);
        int? bestX = null, bestY = null;
        int bd = radius * radius + 1;
        for (int dy = -radius; dy <= radius; dy++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                int lx = gx + dx, ly = gy + dy;
                if (!GridHelpers.InBounds(state, lx, ly) || !IsTileWalkable(state, lx, ly, canSwim)) continue;
                int d = dx * dx + dy * dy;
                if (d < bd) { bd = d; bestX = lx; bestY = ly; }
            }
        }
        return bestX.HasValue ? (bestX.Value, bestY!.Value) : null;
    }

    public static (double X, double Y) UnsnappedWalkableGoal(SimState state, double gx, double gy, bool canSwim)
    {
        int tx = (int)Math.Round(gx);
        int ty = (int)Math.Round(gy);
        if (IsTileWalkable(state, tx, ty, canSwim)) return (gx, gy);
        var sn = SnapWalkableGoal(state, tx, ty, canSwim, 8);
        return sn.HasValue ? (sn.Value.X + 0.5, sn.Value.Y + 0.5) : (gx, gy);
    }

    public static (double X, double Y)? ResolveMovementTarget(
        SimState state, double px, double py, double goalX, double goalY, bool canSwim,
        int radius = 48, bool direct = false, double directRadius = SimConstants.DirectPursuitRadius)
    {
        double dist = SimMath.Hypot(goalX - px, goalY - py);
        if (direct && dist <= directRadius) return (goalX, goalY);

        int fx = (int)Math.Round(px), fy = (int)Math.Round(py);
        int gx = (int)Math.Round(goalX), gy = (int)Math.Round(goalY);
        if (LineOfSightClear(state, fx, fy, gx, gy, canSwim)) return (goalX, goalY);

        return PlanGridStep(state, px, py, goalX, goalY, canSwim, radius);
    }

    public static void BuildWaterDistanceField(SimState state)
    {
        int n = state.W * state.H;
        if (state.WaterDist.Length != n) state.WaterDist = new float[n];
        Array.Fill(state.WaterDist, (float)SimConstants.WaterDistUnreachable);

        var queue = new Queue<int>();
        for (int y = 0; y < state.H; y++)
        {
            for (int x = 0; x < state.W; x++)
            {
                if (!IsShoreWalkableTile(state, x, y)) continue;
                int ti = GridHelpers.Idx(state, x, y);
                state.WaterDist[ti] = 0;
                queue.Enqueue(ti);
            }
        }

        while (queue.Count > 0)
        {
            int ti = queue.Dequeue();
            int cx = ti % state.W;
            int cy = ti / state.W;
            float nextDist = state.WaterDist[ti] + 1;
            foreach (var (ddx, ddy) in ShoreNeighbors)
            {
                int nx = cx + ddx, ny = cy + ddy;
                if (!GridHelpers.InBounds(state, nx, ny)) continue;
                if (!IsTileWalkable(state, nx, ny, false)) continue;
                int ni = GridHelpers.Idx(state, nx, ny);
                if (state.WaterDist[ni] <= nextDist) continue;
                state.WaterDist[ni] = nextDist;
                queue.Enqueue(ni);
            }
        }
    }

    public static (double X, double Y)? WaterEdgeGoalFromField(SimState state, double x, double y, int maxSteps)
    {
        int cx = (int)Math.Round(x);
        int cy = (int)Math.Round(y);
        if (!GridHelpers.InBounds(state, cx, cy)) return null;

        int ti = GridHelpers.Idx(state, cx, cy);
        float dist = state.WaterDist[ti];
        if (dist >= SimConstants.WaterDistUnreachable) return null;
        if (dist <= 0) return (cx + 0.5, cy + 0.5);

        int stepLimit = Math.Max(1, Math.Min(maxSteps, (int)dist));
        for (int step = 0; step < stepLimit; step++)
        {
            int bestNx = cx, bestNy = cy;
            float bestDist = dist;
            foreach (var (ddx, ddy) in ShoreNeighbors)
            {
                int nx = cx + ddx, ny = cy + ddy;
                if (!GridHelpers.InBounds(state, nx, ny)) continue;
                if (!IsTileWalkable(state, nx, ny, false)) continue;
                float nd = state.WaterDist[GridHelpers.Idx(state, nx, ny)];
                if (nd < bestDist) { bestDist = nd; bestNx = nx; bestNy = ny; }
            }
            if (bestDist >= dist) break;
            cx = bestNx; cy = bestNy; dist = bestDist;
            if (dist <= 0) break;
        }
        return (cx + 0.5, cy + 0.5);
    }

    public static int WaterSeekRadius(double senseR) => Math.Max((int)(senseR + 6), SimConstants.WaterSeekRadiusMin);

    public static (double X, double Y)? NearestWaterEdgeTarget(SimState state, double x, double y, int r)
    {
        double? bestX = null, bestY = null;
        double bd = r * r;
        int ix = (int)Math.Round(x), iy = (int)Math.Round(y);
        for (int dy = -r; dy <= r; dy++)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                int lx = ix + dx, ly = iy + dy;
                if (!GridHelpers.InBounds(state, lx, ly)) continue;
                if (!IsTileWalkable(state, lx, ly, false)) continue;
                bool nearWater = false;
                for (int wy = -1; wy <= 1 && !nearWater; wy++)
                {
                    for (int wx = -1; wx <= 1; wx++)
                    {
                        int nx = lx + wx, ny = ly + wy;
                        if (GridHelpers.InBounds(state, nx, ny) && BiomeData.IsWater(state.Biome[GridHelpers.Idx(state, nx, ny)]))
                        {
                            nearWater = true;
                            break;
                        }
                    }
                }
                if (!nearWater) continue;
                double d = dx * dx + dy * dy;
                if (d < bd) { bd = d; bestX = lx + 0.5; bestY = ly + 0.5; }
            }
        }
        return bestX.HasValue ? (bestX.Value, bestY!.Value) : null;
    }

    public static (double X, double Y) PickRandomWalkableTile(SimState state, double cx, double cy, int spread, bool canSwim)
    {
        for (int tries = 0; tries < 12; tries++)
        {
            int lx = (int)Math.Round(cx + (GlobalRng.Next() * 2 - 1) * spread);
            int ly = (int)Math.Round(cy + (GlobalRng.Next() * 2 - 1) * spread);
            if (GridHelpers.InBounds(state, lx, ly) && IsTileWalkable(state, lx, ly, canSwim))
            {
                return (lx + 0.5, ly + 0.5);
            }
        }
        return (cx, cy);
    }

    public static (double X, double Y)? PlanGridStep(SimState state, double x, double y, double goalX, double goalY, bool canSwim, int radius = 48)
    {
        int R = Math.Max(8, Math.Min(64, radius));
        int gx = (int)Math.Round(goalX), gy = (int)Math.Round(goalY);
        var snapped = SnapWalkableGoal(state, gx, gy, canSwim, 8);
        if (snapped.HasValue) { gx = snapped.Value.X; gy = snapped.Value.Y; }
        else return null;

        int fx = (int)Math.Round(x), fy = (int)Math.Round(y);
        if (fx == gx && fy == gy) return (gx + 0.5, gy + 0.5);
        if (SimMath.Hypot(gx - fx, gy - fy) < 1.5) return (gx + 0.5, gy + 0.5);
        if (LineOfSightClear(state, fx, fy, gx, gy, canSwim)) return (goalX, goalY);

        int side = R * 2 + 1;
        int ox = ClampWindowOrigin((int)Math.Floor((fx + gx) * 0.5) - R, state.W - side);
        int oy = ClampWindowOrigin((int)Math.Floor((fy + gy) * 0.5) - R, state.H - side);
        int glx = gx - ox, gly = gy - oy;
        int flx = fx - ox, fly = fy - oy;
        if (glx < 0 || gly < 0 || glx >= side || gly >= side) return (gx + 0.5, gy + 0.5);
        if (flx < 0 || fly < 0 || flx >= side || fly >= side) return (gx + 0.5, gy + 0.5);

        int cells = side * side;
        EnsureAstarBuffers(cells);
        Array.Fill(_astarGScore!, int.MaxValue, 0, cells);
        Array.Fill(_astarFScore!, int.MaxValue, 0, cells);
        Array.Fill(_astarParent!, -1, 0, cells);
        Array.Fill(_astarClosed!, (byte)0, 0, cells);

        int start = fly * side + flx;
        int goal = gly * side + glx;
        _astarGScore![start] = 0;
        _astarFScore![start] = OctileHeuristic(flx, fly, glx, gly);
        int heapSize = HeapPush(0, start, _astarFScore[start]);

        while (heapSize > 0)
        {
            var popped = HeapPop(heapSize);
            heapSize = popped.HeapSize;
            int ci = popped.CellIdx;
            if (_astarClosed![ci] != 0) continue;
            _astarClosed[ci] = 1;

            if (ci == goal)
            {
                int step = ci;
                int prev = _astarParent![step];
                while (prev >= 0 && prev != start)
                {
                    step = prev;
                    prev = _astarParent[step];
                }
                int sx = step % side;
                int sy = step / side;
                return (ox + sx + 0.5, oy + sy + 0.5);
            }

            int cx = ci % side;
            int cy = ci / side;
            int cg = _astarGScore[ci];
            if (cg >= R * 14) continue;

            foreach (var (ddx, ddy, cost) in NavDirs)
            {
                int nx = cx + ddx, ny = cy + ddy;
                if (nx < 0 || ny < 0 || nx >= side || ny >= side) continue;
                int wx = ox + nx, wy = oy + ny;
                if (!GridHelpers.InBounds(state, wx, wy) || !IsTileWalkable(state, wx, wy, canSwim)) continue;
                if (DiagonalBlocked(state, cx, cy, ddx, ddy, ox, oy, canSwim)) continue;

                int ni = ny * side + nx;
                if (_astarClosed[ni] != 0) continue;
                int tg = cg + cost;
                if (tg >= _astarGScore[ni]) continue;

                _astarGScore[ni] = tg;
                _astarParent![ni] = ci;
                int tf = tg + OctileHeuristic(nx, ny, glx, gly);
                _astarFScore[ni] = tf;
                heapSize = HeapPush(heapSize, ni, tf);
            }
        }

        (double X, double Y)? fallback = null;
        int fbScore = int.MaxValue;
        for (int cy2 = 0; cy2 < side; cy2++)
        {
            for (int cx2 = 0; cx2 < side; cx2++)
            {
                int ni = cy2 * side + cx2;
                int g = _astarGScore![ni];
                if (g >= int.MaxValue) continue;
                int md = Math.Abs(gx - (ox + cx2)) + Math.Abs(gy - (oy + cy2));
                int score = g * 1000 + md;
                if (score < fbScore)
                {
                    fbScore = score;
                    fallback = (ox + cx2 + 0.5, oy + cy2 + 0.5);
                }
            }
        }
        return fallback;
    }

    private static bool IsShoreWalkableTile(SimState state, int tx, int ty)
    {
        if (!IsTileWalkable(state, tx, ty, false)) return false;
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = tx + dx, ny = ty + dy;
                if (GridHelpers.InBounds(state, nx, ny) && BiomeData.IsWater(state.Biome[GridHelpers.Idx(state, nx, ny)]))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static bool DiagonalBlocked(SimState state, int cx, int cy, int ddx, int ddy, int ox, int oy, bool canSwim)
    {
        if (Math.Abs(ddx) + Math.Abs(ddy) != 2) return false;
        int wx1 = ox + cx + ddx, wy1 = oy + cy + ddy;
        int wx2 = ox + cx, wy2 = oy + cy;
        if (!IsTileWalkable(state, wx1, wy2, canSwim)) return true;
        if (!IsTileWalkable(state, wx2, wy1, canSwim)) return true;
        return false;
    }

    private static int ClampWindowOrigin(int origin, int maxOrigin)
    {
        if (origin < 0) return 0;
        if (origin > maxOrigin) return Math.Max(0, maxOrigin);
        return origin;
    }

    private static int OctileHeuristic(int ax, int ay, int bx, int by)
    {
        int dx = Math.Abs(ax - bx);
        int dy = Math.Abs(ay - by);
        int mn = Math.Min(dx, dy);
        int mx = Math.Max(dx, dy);
        return 14 * mn + 10 * (mx - mn);
    }

    private static void EnsureAstarBuffers(int cells)
    {
        if (_astarGScore == null || _astarGScore.Length < cells)
        {
            _astarGScore = new int[cells];
            _astarFScore = new int[cells];
            _astarParent = new int[cells];
            _astarClosed = new byte[cells];
            _astarHeapIdx = new int[cells];
            _astarHeapVal = new int[cells];
        }
    }

    private static int HeapPush(int heapSize, int cellIdx, int fScore)
    {
        int i = heapSize;
        _astarHeapIdx![i] = cellIdx;
        _astarHeapVal![i] = fScore;
        while (i > 0)
        {
            int p = (i - 1) >> 1;
            if (_astarHeapVal[p] <= _astarHeapVal[i]) break;
            (_astarHeapIdx[p], _astarHeapIdx[i]) = (_astarHeapIdx[i], _astarHeapIdx[p]);
            (_astarHeapVal[p], _astarHeapVal[i]) = (_astarHeapVal[i], _astarHeapVal[p]);
            i = p;
        }
        return heapSize + 1;
    }

    private static (int CellIdx, int HeapSize) HeapPop(int heapSize)
    {
        int cellIdx = _astarHeapIdx![0];
        int last = heapSize - 1;
        _astarHeapIdx[0] = _astarHeapIdx[last];
        _astarHeapVal![0] = _astarHeapVal[last];
        int i = 0;
        while (true)
        {
            int l = i * 2 + 1;
            int r = l + 1;
            int smallest = i;
            if (l < last && _astarHeapVal[l] < _astarHeapVal[smallest]) smallest = l;
            if (r < last && _astarHeapVal[r] < _astarHeapVal[smallest]) smallest = r;
            if (smallest == i) break;
            (_astarHeapIdx[i], _astarHeapIdx[smallest]) = (_astarHeapIdx[smallest], _astarHeapIdx[i]);
            (_astarHeapVal[i], _astarHeapVal[smallest]) = (_astarHeapVal[smallest], _astarHeapVal[i]);
            i = smallest;
        }
        return (cellIdx, last);
    }
}

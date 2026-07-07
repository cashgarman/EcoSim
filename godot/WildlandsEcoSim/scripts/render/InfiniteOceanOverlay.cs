using EcoSim.Core.Data;
using EcoSim.Core.Numerics;
using Godot;
using WildlandsEcoSim;
using WildlandsEcoSim.UI;

namespace WildlandsEcoSim.Render;

/// <summary>
/// Cached deep-ocean backdrop outside the sim grid (JS drawInfiniteOcean parity).
/// Rebakes a tile texture when the camera viewport or water frame changes — not every frame.
/// </summary>
public partial class InfiniteOceanOverlay : Node2D
{
    private const int PadTiles = 6;
    private const int MaxBakeTiles = 2048;

    private WorldCamera? _camera;
    private int _worldW;
    private int _worldH;
    private double _waterAnim;

    private Sprite2D _sprite = null!;
    private string _cacheKey = "";
    private Vector2 _lastCamPos = new(float.NaN, float.NaN);
    private float _lastZoom = float.NaN;
    private int _lastWaterFrame = -1;

    private static readonly byte[] DeepRgb = BiomeData.Info[Biome.Deep].ColorRgb;

    public override void _Ready()
    {
        ZIndex = 0;
        _sprite = new Sprite2D
        {
            Centered = false,
            TextureFilter = TextureFilterEnum.Nearest,
        };
        AddChild(_sprite);
    }

    public void Bind(WorldCamera camera, int worldW, int worldH)
    {
        _camera = camera;
        _worldW = worldW;
        _worldH = worldH;
        _cacheKey = "";
        RebuildIfNeeded(force: true);
    }

    public void SetWaterAnim(double anim) => _waterAnim = anim;

    public void Invalidate() => _cacheKey = "";

    public override void _Process(double delta)
    {
        if (_camera == null || _worldW <= 0 || _worldH <= 0) return;

        int waterFrame = (int)(_waterAnim * 4);
        bool camMoved = _lastCamPos.DistanceSquaredTo(_camera.Position) > 0.0001f
            || Math.Abs(_lastZoom - _camera.Zoom.X) > 0.0001f;
        bool waterChanged = waterFrame != _lastWaterFrame;

        if (camMoved || waterChanged)
        {
            RebuildIfNeeded(force: waterChanged);
        }
    }

    private void RebuildIfNeeded(bool force)
    {
        if (_camera == null) return;

        var (x0, y0, iw, ih) = VisibleTileBounds();
        if (iw <= 0 || ih <= 0) return;

        int waterFrame = (int)(_waterAnim * 4);
        string key = $"{x0},{y0},{iw},{ih},{waterFrame},{_worldW},{_worldH}";
        if (!force && key == _cacheKey) return;

        PerfProfiler.Instance.Timed("render.oceanBake", () =>
        {
        var img = Image.CreateEmpty(iw, ih, false, Image.Format.Rgba8);
        for (int j = 0; j < ih; j++)
        {
            int wy = y0 + j;
            for (int i = 0; i < iw; i++)
            {
                int wx = x0 + i;
                if (wx >= 0 && wx < _worldW && wy >= 0 && wy < _worldH)
                {
                    img.SetPixel(i, j, Colors.Transparent);
                    continue;
                }

                img.SetPixel(i, j, SampleDeepOceanTile(wx, wy, waterFrame));
            }
        }

        _sprite.Texture = ImageTexture.CreateFromImage(img);
        _sprite.Position = new Vector2(x0, y0);
        _cacheKey = key;
        _lastCamPos = _camera.Position;
        _lastZoom = _camera.Zoom.X;
        _lastWaterFrame = waterFrame;
        });
    }

    private (int x0, int y0, int iw, int ih) VisibleTileBounds()
    {
        Vector2 vp = GetViewport().GetVisibleRect().Size;
        float tileScale = GetParent<Node2D>().Scale.X;
        float zoom = Math.Max(0.05f, _camera!.Zoom.X);
        float vw = vp.X / (zoom * tileScale);
        float vh = vp.Y / (zoom * tileScale);

        int x0 = (int)Math.Floor(_camera.Position.X - vw * 0.5f) - PadTiles;
        int y0 = (int)Math.Floor(_camera.Position.Y - vh * 0.5f) - PadTiles;
        int x1 = (int)Math.Ceiling(_camera.Position.X + vw * 0.5f) + PadTiles;
        int y1 = (int)Math.Ceiling(_camera.Position.Y + vh * 0.5f) + PadTiles;

        int iw = Math.Max(1, x1 - x0);
        int ih = Math.Max(1, y1 - y0);
        if (iw > MaxBakeTiles)
        {
            int cx = (x0 + x1) / 2;
            iw = MaxBakeTiles;
            x0 = cx - iw / 2;
        }

        if (ih > MaxBakeTiles)
        {
            int cy = (y0 + y1) / 2;
            ih = MaxBakeTiles;
            y0 = cy - ih / 2;
        }

        return (x0, y0, iw, ih);
    }

    private static Color SampleDeepOceanTile(int tx, int ty, int waterFrame)
    {
        double h = EcoSim.Core.Numerics.Noise.HashN(tx * TerrainBaker.Tx, ty * TerrainBaker.Tx, 7);
        float shade = 0.9f + (float)((h - 0.5) * 0.14);

        float phase = waterFrame * 0.25f;
        float shimmer = 0.12f + (float)(Math.Sin((tx + ty) * 0.3 + phase) * 0.5 + 0.5) * 0.1f;
        shimmer += (float)EcoSim.Core.Numerics.Noise.HashN(tx, ty, 17) * 0.06f;

        float r = Math.Clamp((DeepRgb[0] + 38) / 255f * shade + shimmer * 0.08f, 0f, 1f);
        float g = Math.Clamp((DeepRgb[1] + 34) / 255f * shade + shimmer * 0.1f, 0f, 1f);
        float b = Math.Clamp((DeepRgb[2] + 28) / 255f * shade + shimmer * 0.12f, 0f, 1f);

        return new Color(r, g, b);
    }
}

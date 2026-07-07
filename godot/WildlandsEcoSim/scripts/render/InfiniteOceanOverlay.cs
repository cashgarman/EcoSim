using EcoSim.Core.Sim;
using Godot;
using WildlandsEcoSim;
using WildlandsEcoSim.UI;

namespace WildlandsEcoSim.Render;

/// <summary>
/// Deep-ocean backdrop outside the sim grid — baked terrain base + GPU-animated water overlay.
/// </summary>
public partial class InfiniteOceanOverlay : Node2D
{
    private const int PadTiles = 6;
    private const int BoundsSnapTiles = 8;
    private const int MaxBakeTiles = 2048;

    private static readonly StringName AnimPhaseParam = "anim_phase";
    private static readonly StringName TileOriginParam = "tile_origin";

    private WorldCamera? _camera;
    private SimState? _state;
    private int _worldW;
    private int _worldH;

    private Sprite2D _terrainSprite = null!;
    private Sprite2D _waterSprite = null!;
    private ShaderMaterial _waterMaterial = null!;
    private string _cacheKey = "";
    private (int x0, int y0, int iw, int ih) _lastBounds = (0, 0, 0, 0);
    private float _lastZoomSample = float.NaN;
    private int _zoomStableFrames;

    public override void _Ready()
    {
        ZIndex = -1;
        _terrainSprite = new Sprite2D
        {
            Centered = false,
            TextureFilter = TextureFilterEnum.Nearest,
        };
        var waterShader = GD.Load<Shader>("res://shaders/water_overlay.gdshader");
        _waterMaterial = new ShaderMaterial { Shader = waterShader };
        _waterSprite = new Sprite2D
        {
            Centered = false,
            TextureFilter = TextureFilterEnum.Nearest,
            Material = _waterMaterial,
        };
        AddChild(_terrainSprite);
        AddChild(_waterSprite);
    }

    public void Bind(WorldCamera camera, SimState state, int worldW, int worldH)
    {
        _camera = camera;
        _state = state;
        _worldW = worldW;
        _worldH = worldH;
        _cacheKey = "";
        _lastBounds = (0, 0, 0, 0);
        _lastZoomSample = float.NaN;
        _zoomStableFrames = 0;
        RebuildIfNeeded(VisibleTileBounds(), force: true);
        SyncWaterShader();
    }

    public void SetAnimPhase(float phase)
    {
        if (_waterMaterial == null) return;
        _waterMaterial.SetShaderParameter(AnimPhaseParam, phase);
    }

    public void Invalidate() => _cacheKey = "";

    public override void _Process(double delta)
    {
        if (_camera == null || _worldW <= 0 || _worldH <= 0) return;

        float zoom = _camera.Zoom.X;
        if (float.IsNaN(_lastZoomSample) || Math.Abs(zoom - _lastZoomSample) > 0.01f)
        {
            _lastZoomSample = zoom;
            _zoomStableFrames = 0;
        }
        else
        {
            _zoomStableFrames++;
        }

        var bounds = VisibleTileBounds();
        if (bounds.iw <= 0 || bounds.ih <= 0) return;
        if (bounds == _lastBounds) return;
        if (_zoomStableFrames < 2) return;

        RebuildIfNeeded(bounds);
    }

    private void SyncWaterShader()
    {
        if (_waterMaterial == null) return;
        _waterMaterial.SetShaderParameter(TileOriginParam, _terrainSprite.Position);
    }

    private void RebuildIfNeeded((int x0, int y0, int iw, int ih) bounds, bool force = false)
    {
        if (_camera == null || _state == null) return;

        var (x0, y0, iw, ih) = bounds;

        string key = $"{x0},{y0},{iw},{ih},{_worldW},{_worldH}";
        if (!force && key == _cacheKey) return;

        PerfProfiler.Instance.Timed("render.oceanBake", () =>
        {
            int stride = iw * 4;
            byte[] terrainData = new byte[stride * ih];
            byte[] maskData = new byte[stride * ih];
            for (int j = 0; j < ih; j++)
            {
                int wy = y0 + j;
                int row = j * stride;
                for (int i = 0; i < iw; i++)
                {
                    int wx = x0 + i;
                    int o = row + i * 4;
                    if (wx >= 0 && wx < _worldW && wy >= 0 && wy < _worldH) continue;

                    TerrainBaker.WriteOceanTerrainPixel(terrainData, o, wx, wy, _state);
                    maskData[o] = 255;
                    maskData[o + 1] = 255;
                    maskData[o + 2] = 255;
                    maskData[o + 3] = 255;
                }
            }

            var terrainImg = Image.CreateFromData(iw, ih, false, Image.Format.Rgba8, terrainData);
            var maskImg = Image.CreateFromData(iw, ih, false, Image.Format.Rgba8, maskData);
            var pos = new Vector2(x0, y0);
            _terrainSprite.Texture = ImageTexture.CreateFromImage(terrainImg);
            _terrainSprite.Position = pos;
            _waterSprite.Texture = ImageTexture.CreateFromImage(maskImg);
            _waterSprite.Position = pos;
            _cacheKey = key;
            _lastBounds = bounds;
            SyncWaterShader();
        });
    }

    private (int x0, int y0, int iw, int ih) VisibleTileBounds()
    {
        Vector2 vp = GetViewport().GetVisibleRect().Size;
        float tileScale = GetParent<Node2D>().Scale.X;
        float zoom = Math.Max(0.05f, _camera!.Zoom.X);
        float vw = vp.X / (zoom * tileScale);
        float vh = vp.Y / (zoom * tileScale);

        int x0 = SnapDown((int)Math.Floor(_camera.Position.X - vw * 0.5f) - PadTiles);
        int y0 = SnapDown((int)Math.Floor(_camera.Position.Y - vh * 0.5f) - PadTiles);
        int x1 = SnapUp((int)Math.Ceiling(_camera.Position.X + vw * 0.5f) + PadTiles);
        int y1 = SnapUp((int)Math.Ceiling(_camera.Position.Y + vh * 0.5f) + PadTiles);

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

    private static int SnapDown(int v) =>
        (int)Math.Floor(v / (double)BoundsSnapTiles) * BoundsSnapTiles;

    private static int SnapUp(int v) =>
        (int)Math.Ceiling(v / (double)BoundsSnapTiles) * BoundsSnapTiles;
}

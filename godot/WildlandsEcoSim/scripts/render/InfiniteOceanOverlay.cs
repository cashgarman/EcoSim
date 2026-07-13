using EcoSim.Core.Data;
using EcoSim.Core.Sim;
using Godot;
using WildlandsEcoSim;
using WildlandsEcoSim.UI;

namespace WildlandsEcoSim.Render;

/// <summary>
/// Deep-ocean backdrop outside the sim grid — solid fill + GPU-animated water overlay.
/// </summary>
public partial class InfiniteOceanOverlay : Node2D
{
    private const int PadTiles = 6;
    private const int BoundsSnapTiles = 8;
    private const int MaxBakeTiles = 2048;
    /// <summary>Subpixel bleed at sim-grid edges to hide 1px seams vs main water.</summary>
    private const int SeamBleedSubpixels = 3;

    private static readonly StringName AnimPhaseParam = "anim_phase";
    private static readonly StringName TileOriginParam = "tile_origin";

    private WorldCamera? _camera;
    private SimState? _state;
    private int _worldW;
    private int _worldH;

    private ColorRect _baseFill = null!;
    private Sprite2D _waterSprite = null!;
    private ShaderMaterial _waterMaterial = null!;
    private string _cacheKey = "";
    private (int x0, int y0, int iw, int ih) _lastBounds = (0, 0, 0, 0);
    private float _lastZoomSample = float.NaN;
    private int _zoomStableFrames;

    public override void _Ready()
    {
        ZIndex = -1;
        _baseFill = new ColorRect
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        var waterShader = GD.Load<Shader>("res://shaders/water_overlay.gdshader");
        _waterMaterial = new ShaderMaterial { Shader = waterShader };
        _waterSprite = new Sprite2D
        {
            Centered = false,
            TextureFilter = TextureFilterEnum.Nearest,
            Material = _waterMaterial,
            Scale = new Vector2(1f / TerrainBaker.Tx, 1f / TerrainBaker.Tx),
        };
        AddChild(_baseFill);
        AddChild(_waterSprite);
    }

    public void Bind(WorldCamera camera, SimState state, int worldW, int worldH)
    {
        _camera = camera;
        _state = state;
        _worldW = worldW;
        _worldH = worldH;
        ResetCache();
    }

    public void SetAnimPhase(float phase)
    {
        if (_waterMaterial == null) return;
        _waterMaterial.SetShaderParameter(AnimPhaseParam, phase);
    }

    public void Invalidate() => ResetCache();

    /// <summary>Rebuild immediately after camera jumps (e.g. CenterOnWorld).</summary>
    public void ForceSyncAfterCamera()
    {
        if (_camera == null || _state == null) return;
        ResetCache();
        _lastZoomSample = _camera.Zoom.X;
        _zoomStableFrames = 2;
        RebuildIfNeeded(VisibleTileBounds(), force: true);
    }

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

    private void ResetCache()
    {
        _cacheKey = "";
        _lastBounds = (0, 0, 0, 0);
        _lastZoomSample = float.NaN;
        _zoomStableFrames = 0;
    }

    private void SyncWaterShader(Vector2 originTiles)
    {
        if (_waterMaterial == null) return;
        float tx = TerrainBaker.Tx;
        _waterMaterial.SetShaderParameter(TileOriginParam, new Vector2(originTiles.X * tx, originTiles.Y * tx));
    }

    private float TileScale()
    {
        Node? node = this;
        while (node != null)
        {
            if (node is WorldRenderer renderer)
            {
                return renderer.Scale.X;
            }

            node = node.GetParent();
        }

        return WorldRenderer.TilePixels;
    }

    private void RebuildIfNeeded((int x0, int y0, int iw, int ih) bounds, bool force = false)
    {
        if (_camera == null || _state == null) return;

        var (x0, y0, iw, ih) = bounds;

        string key = $"{x0},{y0},{iw},{ih},{_worldW},{_worldH}";
        if (!force && key == _cacheKey) return;

        PerfProfiler.Instance.Timed("render.oceanBake", () =>
        {
            int tx = TerrainBaker.Tx;
            int tw = iw * tx;
            int th = ih * tx;
            byte[] maskData = new byte[tw * th * 4];

            for (int py = 0; py < th; py++)
            {
                for (int px = 0; px < tw; px++)
                {
                    float worldTileX = x0 + (px + 0.5f) / tx;
                    float worldTileY = y0 + (py + 0.5f) / tx;
                    int o = (py * tw + px) * 4;

                    bool insideWorld = worldTileX >= 0 && worldTileX < _worldW &&
                                       worldTileY >= 0 && worldTileY < _worldH;
                    if (insideWorld)
                    {
                        float bleed = SeamBleedSubpixels / (float)tx;
                        bool nearAabb = worldTileX < bleed || worldTileX >= _worldW - bleed ||
                                        worldTileY < bleed || worldTileY >= _worldH - bleed;
                        if (!nearAabb)
                        {
                            continue;
                        }
                    }

                    maskData[o] = 255;
                    maskData[o + 1] = 255;
                    maskData[o + 2] = 255;
                    maskData[o + 3] = 255;
                }
            }

            var maskImg = Image.CreateFromData(tw, th, false, Image.Format.Rgba8, maskData);
            var pos = new Vector2(x0, y0);
            Color backdrop = TerrainBaker.BackdropOceanColor(_state);
            _baseFill.Color = backdrop;
            _baseFill.Position = pos;
            _baseFill.Size = new Vector2(iw, ih);
            _waterSprite.Texture = ImageTexture.CreateFromImage(maskImg);
            _waterSprite.Position = pos;
            _cacheKey = key;
            _lastBounds = bounds;
            SyncWaterShader(pos);
        });
    }

    private (int x0, int y0, int iw, int ih) VisibleTileBounds()
    {
        Vector2 vp = GetViewport().GetVisibleRect().Size;
        float tileScale = TileScale();
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
            iw = MaxBakeTiles;
            x1 = x0 + iw;
        }

        if (ih > MaxBakeTiles)
        {
            ih = MaxBakeTiles;
            y1 = y0 + ih;
        }

        return (x0, y0, iw, ih);
    }

    private static int SnapDown(int v) =>
        (int)Math.Floor(v / (double)BoundsSnapTiles) * BoundsSnapTiles;

    private static int SnapUp(int v) =>
        (int)Math.Ceiling(v / (double)BoundsSnapTiles) * BoundsSnapTiles;
}

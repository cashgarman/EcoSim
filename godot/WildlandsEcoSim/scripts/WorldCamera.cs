using EcoSim.Core.Sim;
using Godot;
using WildlandsEcoSim.Render;

namespace WildlandsEcoSim;

public partial class WorldCamera : Camera2D
{
    private static readonly Vector2 MinZoom = new(0.25f, 0.25f);
    private static readonly Vector2 MaxZoom = new(8f, 8f);
    /// <summary>Extra tiles of pan room beyond the grid edge (infinite ocean backdrop).</summary>
    private const float OceanPanMarginTiles = 40f;

    private EcoSimHost _host = null!;
    private bool _panning;
    private MouseButton _panButton;
    private Vector2 _lastPanMouse;
    private Creature? _followTarget;
    private bool _followEnabled;

    public bool FollowEnabled
    {
        get => _followEnabled;
        set => _followEnabled = value;
    }

    public override void _Ready()
    {
        _host = GetNode<EcoSimHost>("/root/EcoSimHost");
    }

    private static bool IsPossessionLocked(SimSession session) => session.Player.IsControlling;

    /// <summary>World-viewport mouse input (wheel, RMB/MMB pan). Returns true if consumed.</summary>
    public bool HandleWorldInput(InputEvent @event)
    {
        var session = _host.Session;
        bool possessionLock = session != null && IsPossessionLocked(session);

        if (@event is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex is MouseButton.Middle or MouseButton.Right)
            {
                if (possessionLock)
                {
                    return true;
                }

                if (mb.Pressed)
                {
                    _panning = true;
                    _panButton = mb.ButtonIndex;
                    _lastPanMouse = mb.Position;
                    _followEnabled = false;
                }
                else if (_panning && mb.ButtonIndex == _panButton)
                {
                    _panning = false;
                }

                return true;
            }

            if (mb.Pressed)
            {
                if (mb.ButtonIndex == MouseButton.WheelUp)
                {
                    ApplyZoom(1.12f, mb.Position);
                    return true;
                }

                if (mb.ButtonIndex == MouseButton.WheelDown)
                {
                    ApplyZoom(1f / 1.12f, mb.Position);
                    return true;
                }
            }
        }
        else if (@event is InputEventMouseMotion mm && _panning)
        {
            Vector2 delta = mm.Position - _lastPanMouse;
            Position -= delta / (Zoom * ParentScale());
            _lastPanMouse = mm.Position;
            ClampToLand();
            return true;
        }

        return false;
    }

    public override void _Process(double delta)
    {
        if (_panning && !Input.IsMouseButtonPressed(_panButton))
        {
            _panning = false;
        }

        if (_panning) return;

        var session = _host.Session;
        if (session == null || !session.State.Ready) return;

        bool possessionLock = IsPossessionLocked(session);
        if (possessionLock)
        {
            _followEnabled = true;
            var controlled = session.Player.Controlled;
            if (controlled != null)
            {
                _followTarget = controlled;
            }
        }
        else if (_followEnabled && session.State.Selected != null && !session.State.Selected.Dead)
        {
            _followTarget = session.State.Selected;
        }

        if (_followEnabled && _followTarget != null && !_followTarget.Dead)
        {
            Vector2 target = CreatureDrawUtil.DisplayPos(_followTarget);
            float followRate = possessionLock ? 10f : 4f;
            Position = Position.Lerp(target, (float)Math.Min(1, delta * followRate));
            ClampToLand();
        }
    }

    public void FocusCreature(Creature c, bool ensureMinZoom = true)
    {
        if (c.Dead) return;

        _followTarget = c;
        Position = CreatureDrawUtil.DisplayPos(c);

        if (ensureMinZoom)
        {
            float minZoom = ComputeMinFollowZoom();
            if (Zoom.X < minZoom)
            {
                Zoom = new Vector2(minZoom, minZoom);
            }
        }

        ClampToLand();
    }

    private float ComputeMinFollowZoom()
    {
        var session = _host.Session;
        if (session == null) return 3f;

        var bounds = session.State.LandBounds;
        float landW = Math.Max(1f, bounds.MaxX - bounds.MinX);
        float landH = Math.Max(1f, bounds.MaxY - bounds.MinY);
        Vector2 vp = GetViewport().GetVisibleRect().Size;
        float scale = ParentScale();
        float zToPanX = vp.X / (landW * 0.92f * scale);
        float zToPanY = vp.Y / (landH * 0.92f * scale);
        return Math.Clamp(Math.Max(Math.Max(zToPanX, zToPanY), MinZoom.X * 1.25f), MinZoom.X, MaxZoom.X);
    }

    public void CenterOnWorld()
    {
        var session = _host.Session;
        if (session == null) return;

        var bounds = session.State.LandBounds;
        Position = new Vector2(
            (bounds.MinX + bounds.MaxX) * 0.5f,
            (bounds.MinY + bounds.MaxY) * 0.5f);

        float landW = bounds.MaxX - bounds.MinX;
        float landH = bounds.MaxY - bounds.MinY;
        Vector2 vp = GetViewport().GetVisibleRect().Size;
        float scale = ParentScale();
        float fit = Math.Min(vp.X / (landW * scale), vp.Y / (landH * scale)) * 0.9f;
        Zoom = new Vector2(fit, fit).Clamp(MinZoom, MaxZoom);
        ClampToLand();
    }

    private void ApplyZoom(float factor, Vector2 viewportMouse)
    {
        Vector2 worldBefore = ViewportMouseToWorld(viewportMouse);
        Vector2 newZoom = (Zoom * factor).Clamp(MinZoom, MaxZoom);
        if (newZoom == Zoom) return;

        Zoom = newZoom;
        Vector2 vpCenter = GetViewport().GetVisibleRect().Size * 0.5f;
        float scale = ParentScale();
        Position = worldBefore - (viewportMouse - vpCenter) / (Zoom * scale);
        ClampToLand();
    }

    private Vector2 ViewportMouseToWorld(Vector2 viewportMouse)
    {
        Vector2 vpCenter = GetViewport().GetVisibleRect().Size * 0.5f;
        float scale = ParentScale();
        return Position + (viewportMouse - vpCenter) / (Zoom * scale);
    }

    private float ParentScale()
    {
        Node? parent = GetParent();
        if (parent is Node2D n2d)
        {
            return n2d.Scale.X;
        }

        return 1f;
    }

    private void ClampToLand()
    {
        var session = _host.Session;
        if (session == null) return;

        var state = session.State;
        float minX = -OceanPanMarginTiles;
        float minY = -OceanPanMarginTiles;
        float maxX = state.W + OceanPanMarginTiles;
        float maxY = state.H + OceanPanMarginTiles;

        Vector2 vp = GetViewport().GetVisibleRect().Size;
        float scale = ParentScale();
        float vw = vp.X / (Zoom.X * scale);
        float vh = vp.Y / (Zoom.Y * scale);
        float boundsW = maxX - minX;
        float boundsH = maxY - minY;

        if (vw >= boundsW)
        {
            Position = new Vector2((minX + maxX) * 0.5f, Position.Y);
        }
        else
        {
            float loX = minX + vw * 0.5f;
            float hiX = maxX - vw * 0.5f;
            Position = new Vector2(Math.Clamp(Position.X, loX, hiX), Position.Y);
        }

        if (vh >= boundsH)
        {
            Position = new Vector2(Position.X, (minY + maxY) * 0.5f);
        }
        else
        {
            float loY = minY + vh * 0.5f;
            float hiY = maxY - vh * 0.5f;
            Position = new Vector2(Position.X, Math.Clamp(Position.Y, loY, hiY));
        }
    }
}

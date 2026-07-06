using EcoSim.Core.Sim;
using Godot;
using WildlandsEcoSim.Render;

namespace WildlandsEcoSim;

public partial class WorldCamera : Camera2D
{
  private EcoSimHost _host = null!;
  private bool _dragging;
  private Vector2 _dragStart;
  private Vector2 _camStart;
  private Creature? _followTarget;

  public override void _Ready()
  {
    _host = GetNode<EcoSimHost>("/root/EcoSimHost");
  }

  public override void _UnhandledInput(InputEvent @event)
  {
    if (@event is InputEventMouseButton mb)
    {
      if (mb.ButtonIndex == MouseButton.WheelUp && mb.Pressed)
      {
        Zoom *= 1.12f;
        Zoom = Zoom.Clamp(new Vector2(0.25f, 0.25f), new Vector2(8f, 8f));
      }
      else if (mb.ButtonIndex == MouseButton.WheelDown && mb.Pressed)
      {
        Zoom /= 1.12f;
        Zoom = Zoom.Clamp(new Vector2(0.25f, 0.25f), new Vector2(8f, 8f));
      }
      else if (mb.ButtonIndex is MouseButton.Middle or MouseButton.Right)
      {
        _dragging = mb.Pressed;
        _dragStart = mb.Position;
        _camStart = Position;
      }
    }
    else if (@event is InputEventMouseMotion mm && _dragging)
    {
      Vector2 delta = (mm.Position - _dragStart) / Zoom;
      Position = _camStart - delta;
      ClampToLand();
    }
  }

  public override void _Process(double delta)
  {
    var session = _host.Session;
    if (session == null || !session.State.Ready) return;

    if (session.State.Selected != null && !session.State.Selected.Dead)
    {
      _followTarget = session.State.Selected;
    }

    if (_followTarget != null && !_followTarget.Dead)
    {
      var target = new Vector2(
        (float)_followTarget.X * WorldRenderer.TilePixels,
        (float)_followTarget.Y * WorldRenderer.TilePixels);
      Position = Position.Lerp(target, (float)Math.Min(1, delta * 4));
      ClampToLand();
    }
  }

  public void CenterOnWorld()
  {
    var session = _host.Session;
    if (session == null) return;

    var bounds = session.State.LandBounds;
    float cx = (bounds.MinX + bounds.MaxX) * 0.5f * WorldRenderer.TilePixels;
    float cy = (bounds.MinY + bounds.MaxY) * 0.5f * WorldRenderer.TilePixels;
    Position = new Vector2(cx, cy);
    Zoom = new Vector2(1.5f, 1.5f);
    ClampToLand();
  }

  private void ClampToLand()
  {
    var session = _host.Session;
    if (session == null) return;

    var b = session.State.LandBounds;
    float margin = 32f;
    float minX = b.MinX * WorldRenderer.TilePixels - margin;
    float minY = b.MinY * WorldRenderer.TilePixels - margin;
    float maxX = b.MaxX * WorldRenderer.TilePixels + margin;
    float maxY = b.MaxY * WorldRenderer.TilePixels + margin;
    Position = new Vector2(
      Math.Clamp(Position.X, minX, maxX),
      Math.Clamp(Position.Y, minY, maxY));
  }
}

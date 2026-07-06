using EcoSim.Core.Numerics;
using EcoSim.Core.Sim;
using Godot;

namespace WildlandsEcoSim.UI;

public partial class TimelineStrip : Control
{
    private readonly List<(double T, int Day)> _snapshots = [];
    private double _currentT;
    private double _baselineT;
    private bool _paused;
    private bool _dragging;

    [Signal]
    public delegate void SeekRequestedEventHandler(double targetT);

    [Signal]
    public delegate void PresentRequestedEventHandler();

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(200, 22);
        MouseDefaultCursorShape = CursorShape.Cross;
    }

    public void SetSnapshots(IReadOnlyList<(double T, int Day)> snaps, double currentT, double baselineT, bool paused)
    {
        _snapshots.Clear();
        _snapshots.AddRange(snaps);
        _currentT = currentT;
        _baselineT = baselineT;
        _paused = paused;
        QueueRedraw();
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (_snapshots.Count == 0) return;
        double minT = _snapshots[0].T;
        double maxT = _snapshots[^1].T;
        if (maxT <= minT) maxT = minT + 1;

        if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            _dragging = mb.Pressed;
            if (mb.Pressed)
            {
                EmitSeek(mb.Position.X, minT, maxT);
            }
        }
        else if (@event is InputEventMouseMotion mm && _dragging)
        {
            EmitSeek(mm.Position.X, minT, maxT);
        }
    }

    private void EmitSeek(float localX, double minT, double maxT)
    {
        float frac = Math.Clamp(localX / Size.X, 0f, 1f);
        double t = minT + (maxT - minT) * frac;
        EmitSignal(SignalName.SeekRequested, t);
    }

    public override void _Draw()
    {
        var rect = GetRect();
        DrawStyleBox(EcoSimThemeBuilder.MakeFlat(EcoSimThemeBuilder.PanelDarker, EcoSimThemeBuilder.Edge), rect);

        if (_snapshots.Count == 0) return;

        double minT = _snapshots[0].T;
        double maxT = Math.Max(_snapshots[^1].T, _baselineT);
        if (maxT <= minT) maxT = minT + 1;

        foreach (var (t, day) in _snapshots)
        {
            float x = (float)((t - minT) / (maxT - minT) * rect.Size.X);
            DrawLine(new Vector2(rect.Position.X + x, rect.Position.Y),
                new Vector2(rect.Position.X + x, rect.Position.Y + rect.Size.Y),
                new Color(1, 1, 1, 0.25f));
        }

        var font = ThemeDB.FallbackFont;
        for (int d = 0; d <= (int)Math.Ceiling(maxT / SimConstants.SimDaySeconds); d++)
        {
            double dayT = d * SimConstants.SimDaySeconds;
            if (dayT < minT || dayT > maxT) continue;
            float x = (float)((dayT - minT) / (maxT - minT) * rect.Size.X);
            DrawLine(new Vector2(rect.Position.X + x, rect.Position.Y),
                new Vector2(rect.Position.X + x, rect.Position.Y + rect.Size.Y),
                new Color(1, 0.77f, 0.38f, 0.5f));
            DrawString(font, new Vector2(rect.Position.X + x + 2, rect.Position.Y + rect.Size.Y - 2),
                $"Day {d}", HorizontalAlignment.Left, -1, 8, new Color(1, 0.88f, 0.66f, 0.9f));
        }

        float playX = (float)((_currentT - minT) / (maxT - minT) * rect.Size.X);
        DrawLine(new Vector2(rect.Position.X + playX, rect.Position.Y),
            new Vector2(rect.Position.X + playX, rect.Position.Y + rect.Size.Y),
            EcoSimThemeBuilder.Gold, 2f);

        string icon = _paused ? "❚❚" : "▶";
        DrawString(font, new Vector2(rect.Position.X + rect.Size.X - 18, rect.Position.Y + 14),
            icon, HorizontalAlignment.Left, -1, 10, EcoSimThemeBuilder.Text);
    }
}

using EcoSim.Core.Numerics;
using EcoSim.Core.Sim;
using Godot;

namespace WildlandsEcoSim.UI;

public partial class TimelineStrip : Control
{
    private readonly List<(double T, int Day)> _snapshots = [];
    private double _currentT;
    private double _baselineT;
    private double _originTimeOfDay = 0.3;
    private bool _paused;
    private bool _dragging;

    [Signal]
    public delegate void SeekRequestedEventHandler(double targetT);

    [Signal]
    public delegate void PresentRequestedEventHandler();

    [Signal]
    public delegate void ScrubDragStartedEventHandler();

    [Signal]
    public delegate void ScrubDragEndedEventHandler();

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(200, 22);
        MouseDefaultCursorShape = CursorShape.Cross;
    }

    public void SetSnapshots(
        IReadOnlyList<(double T, int Day)> snaps,
        double currentT,
        double baselineT,
        bool paused,
        double originTimeOfDay = 0.3)
    {
        _snapshots.Clear();
        _snapshots.AddRange(snaps);
        _currentT = currentT;
        _baselineT = baselineT;
        _originTimeOfDay = originTimeOfDay;
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
            if (mb.Pressed)
            {
                _dragging = true;
                EmitSignal(SignalName.ScrubDragStarted);
                EmitSeek(mb.Position.X, minT, maxT);
                AcceptEvent();
            }
            else if (_dragging)
            {
                EndScrubDrag();
                AcceptEvent();
            }
        }
        else if (@event is InputEventMouseMotion mm && _dragging)
        {
            EmitSeek(mm.Position.X, minT, maxT);
            AcceptEvent();
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (!_dragging) return;
        if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left && !mb.Pressed)
        {
            EndScrubDrag();
        }
    }

    private void EndScrubDrag()
    {
        if (!_dragging) return;
        _dragging = false;
        EmitSignal(SignalName.ScrubDragEnded);
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
        DrawStyleBox(UiSliceCatalog.MakeInsetPanel(), rect);

        if (_snapshots.Count == 0) return;

        double minT = _snapshots[0].T;
        double maxT = Math.Max(_snapshots[^1].T, _baselineT);
        if (maxT <= minT) maxT = minT + 1;

        DrawDayNightGradient(rect, minT, maxT);

        foreach (var (t, _) in _snapshots)
        {
            float x = TimeToX(t, minT, maxT, rect);
            DrawLine(new Vector2(rect.Position.X + x, rect.Position.Y),
                new Vector2(rect.Position.X + x, rect.Position.Y + rect.Size.Y),
                new Color(1, 1, 1, 0.25f));
        }

        var font = EcoSimFonts.GetFont();
        for (int d = 0; d <= (int)Math.Ceiling(maxT / SimConstants.SimDaySeconds); d++)
        {
            double dayT = d * SimConstants.SimDaySeconds;
            if (dayT < minT || dayT > maxT) continue;
            float x = TimeToX(dayT, minT, maxT, rect);
            DrawLine(new Vector2(rect.Position.X + x, rect.Position.Y),
                new Vector2(rect.Position.X + x, rect.Position.Y + rect.Size.Y),
                new Color(1, 0.77f, 0.38f, 0.62f));
            DrawString(font, new Vector2(rect.Position.X + x + 2, rect.Position.Y + rect.Size.Y - 2),
                $"Day {d}", HorizontalAlignment.Left, -1, EcoSimFonts.ScrubDayLabel,
                new Color(1, 0.88f, 0.66f, 0.92f));
        }

        float playX = TimeToX(_currentT, minT, maxT, rect);
        DrawLine(new Vector2(rect.Position.X + playX, rect.Position.Y),
            new Vector2(rect.Position.X + playX, rect.Position.Y + rect.Size.Y),
            EcoSimThemeBuilder.Gold, 2f);

        var playBg = new Rect2(rect.Position.X + rect.Size.X - 24, rect.Position.Y + 2, 20, rect.Size.Y - 4);
        DrawStyleBox(EcoSimThemeBuilder.MakeFlat(
            _paused ? EcoSimThemeBuilder.Gold : new Color(0, 0, 0, 0.35f),
            EcoSimThemeBuilder.Edge, 1), playBg);
        string icon = _paused ? "❚❚" : "▶";
        Color playColor = _paused ? new Color("0d100b") : EcoSimThemeBuilder.Text;
        DrawString(font, new Vector2(playBg.Position.X + 5, playBg.Position.Y + playBg.Size.Y - 4),
            icon, HorizontalAlignment.Left, -1, EcoSimFonts.ScrubPlayIcon, playColor);
    }

    private static float TimeToX(double t, double minT, double maxT, Rect2 rect)
    {
        return (float)((t - minT) / (maxT - minT) * rect.Size.X);
    }

    private void DrawDayNightGradient(Rect2 rect, double minT, double maxT)
    {
        int width = Math.Max(1, (int)rect.Size.X);
        for (int x = 0; x < width; x++)
        {
            float ratio = width > 1 ? x / (float)(width - 1) : 0f;
            double t = minT + ratio * (maxT - minT);
            double tod = SimMath.TimeOfDayAtSimT(t, _originTimeOfDay);
            float light = (float)SimMath.LightLevelFromTimeOfDay(tod);
            var col = EcoSimThemeBuilder.TimelineNight.Lerp(EcoSimThemeBuilder.TimelineDay, light);
            DrawLine(
                new Vector2(rect.Position.X + x, rect.Position.Y + 2),
                new Vector2(rect.Position.X + x, rect.Position.Y + rect.Size.Y - 2),
                col);
        }
    }
}

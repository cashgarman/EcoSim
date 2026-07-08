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

    private Image? _gradientImage;
    private ImageTexture? _gradientTexture;
    private double _gradientMinT = double.NaN;
    private double _gradientMaxT = double.NaN;
    private int _gradientWidth = -1;
    private int _gradientHeight = -1;

    private PlayheadOverlay _playheadOverlay = null!;
    private float _playheadX = -1f;

    private sealed partial class PlayheadOverlay : Control
    {
        public float PlayheadX { get; set; } = -1f;

        public override void _Draw()
        {
            if (PlayheadX < 0f) return;
            var rect = GetRect();
            DrawLine(
                new Vector2(PlayheadX, rect.Position.Y),
                new Vector2(PlayheadX, rect.Position.Y + rect.Size.Y),
                EcoSimThemeBuilder.Gold, 2f);
        }
    }

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

        _playheadOverlay = new PlayheadOverlay
        {
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _playheadOverlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_playheadOverlay);
    }

    public void SetSnapshots(
        IReadOnlyList<(double T, int Day)> snaps,
        double currentT,
        double baselineT,
        bool paused,
        double originTimeOfDay = 0.3)
    {
        bool rangeChanged = _snapshots.Count == 0 && snaps.Count > 0;
        if (_snapshots.Count > 0 && snaps.Count > 0)
        {
            rangeChanged = snaps[0].T != _snapshots[0].T
                || snaps[^1].T != _snapshots[^1].T
                || baselineT != _baselineT;
        }

        _snapshots.Clear();
        _snapshots.AddRange(snaps);
        _currentT = currentT;
        _baselineT = baselineT;
        _originTimeOfDay = originTimeOfDay;
        _paused = paused;
        if (rangeChanged) InvalidateGradientCache();
        UpdatePlayheadX();
        QueueRedraw();
        _playheadOverlay.PlayheadX = _playheadX;
        _playheadOverlay.QueueRedraw();
    }

    public void SetPlayheadPreview(double currentT)
    {
        _currentT = currentT;
        UpdatePlayheadX();
        _playheadOverlay.PlayheadX = _playheadX;
        _playheadOverlay.QueueRedraw();
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

        var playBg = new Rect2(rect.Position.X + rect.Size.X - 24, rect.Position.Y + 2, 20, rect.Size.Y - 4);
        DrawStyleBox(EcoSimThemeBuilder.MakeFlat(
            _paused ? EcoSimThemeBuilder.Gold : new Color(0, 0, 0, 0.35f),
            EcoSimThemeBuilder.Edge, 1), playBg);
        string icon = _paused ? "❚❚" : "▶";
        Color playColor = _paused ? new Color("0d100b") : EcoSimThemeBuilder.Text;
        DrawString(font, new Vector2(playBg.Position.X + 5, playBg.Position.Y + playBg.Size.Y - 4),
            icon, HorizontalAlignment.Left, -1, EcoSimFonts.ScrubPlayIcon, playColor);
    }

    private void UpdatePlayheadX()
    {
        if (_snapshots.Count == 0)
        {
            _playheadX = -1f;
            return;
        }

        var rect = GetRect();
        double minT = _snapshots[0].T;
        double maxT = Math.Max(_snapshots[^1].T, _baselineT);
        if (maxT <= minT) maxT = minT + 1;
        _playheadX = TimeToX(_currentT, minT, maxT, rect);
    }

    private static float TimeToX(double t, double minT, double maxT, Rect2 rect)
    {
        return (float)((t - minT) / (maxT - minT) * rect.Size.X);
    }

    private void InvalidateGradientCache()
    {
        _gradientImage = null;
        _gradientTexture = null;
        _gradientMinT = double.NaN;
        _gradientMaxT = double.NaN;
        _gradientWidth = -1;
        _gradientHeight = -1;
    }

    private void DrawDayNightGradient(Rect2 rect, double minT, double maxT)
    {
        int width = Math.Max(1, (int)rect.Size.X);
        int height = Math.Max(1, (int)rect.Size.Y - 4);
        EnsureGradientCache(width, height, minT, maxT);
        if (_gradientTexture == null) return;

        DrawTextureRect(
            _gradientTexture,
            new Rect2(rect.Position.X, rect.Position.Y + 2, rect.Size.X, rect.Size.Y - 4),
            false);
    }

    private void EnsureGradientCache(int width, int height, double minT, double maxT)
    {
        if (_gradientTexture != null
            && _gradientImage != null
            && _gradientWidth == width
            && _gradientHeight == height
            && Math.Abs(_gradientMinT - minT) < 1e-6
            && Math.Abs(_gradientMaxT - maxT) < 1e-6)
        {
            return;
        }

        _gradientWidth = width;
        _gradientHeight = height;
        _gradientMinT = minT;
        _gradientMaxT = maxT;
        _gradientImage = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
        for (int x = 0; x < width; x++)
        {
            float ratio = width > 1 ? x / (float)(width - 1) : 0f;
            double t = minT + ratio * (maxT - minT);
            double tod = SimMath.TimeOfDayAtSimT(t, _originTimeOfDay);
            float light = (float)SimMath.LightLevelFromTimeOfDay(tod);
            var col = EcoSimThemeBuilder.TimelineNight.Lerp(EcoSimThemeBuilder.TimelineDay, light);
            for (int y = 0; y < height; y++)
            {
                _gradientImage.SetPixel(x, y, col);
            }
        }

        _gradientTexture ??= new ImageTexture();
        _gradientTexture.SetImage(_gradientImage);
    }
}

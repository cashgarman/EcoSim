using Godot;

namespace WildlandsEcoSim.UI;

/// <summary>Compact 0–10× sim speed slider with track notches and per-step labels.</summary>
public partial class SpeedSliderControl : VBoxContainer
{
    public const int MinSpeed = 0;
    public const int MaxSpeed = 10;
    public const float ControlWidth = 225f;

    private HSlider _slider = null!;
    private SpeedNotchOverlay _overlay = null!;
    private readonly Label[] _notchLabels = new Label[MaxSpeed + 1];

    [Signal]
    public delegate void SpeedChangedEventHandler(double value);

    public double Value => _slider.Value;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(ControlWidth, 0);
        SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        AddThemeConstantOverride("separation", 1);

        var trackRow = new Control
        {
            CustomMinimumSize = new Vector2(ControlWidth, 18),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };

        _slider = new HSlider
        {
            MinValue = MinSpeed,
            MaxValue = MaxSpeed,
            Step = 1,
            Value = 1,
            TickCount = MaxSpeed + 1,
            TicksOnBorders = true,
        };
        _slider.SetAnchorsPreset(LayoutPreset.FullRect);
        _slider.OffsetRight = 0;
        _slider.OffsetBottom = 0;

        _overlay = new SpeedNotchOverlay();
        _overlay.SetAnchorsPreset(LayoutPreset.FullRect);
        _overlay.OffsetRight = 0;
        _overlay.OffsetBottom = 0;
        _overlay.MouseFilter = MouseFilterEnum.Ignore;

        trackRow.AddChild(_slider);
        trackRow.AddChild(_overlay);

        var labelRow = new HBoxContainer();
        labelRow.AddThemeConstantOverride("separation", 0);
        for (int i = MinSpeed; i <= MaxSpeed; i++)
        {
            var lbl = new Label
            {
                Text = $"{i}×",
                HorizontalAlignment = HorizontalAlignment.Center,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                ClipText = true,
            };
            EcoSimFonts.ApplyFont(lbl, EcoSimFonts.ScrubDayLabel, EcoSimThemeBuilder.Dim);
            _notchLabels[i] = lbl;
            labelRow.AddChild(lbl);
        }

        AddChild(trackRow);
        AddChild(labelRow);

        _slider.ValueChanged += OnSliderChanged;
        UpdateActiveNotch();
    }

    public void SetValueNoSignal(double value)
    {
        _slider.SetValueNoSignal(value);
        UpdateActiveNotch();
    }

    private void OnSliderChanged(double value)
    {
        UpdateActiveNotch();
        EmitSignal(SignalName.SpeedChanged, value);
    }

    private void UpdateActiveNotch()
    {
        int active = (int)Math.Round(_slider.Value);
        active = Math.Clamp(active, MinSpeed, MaxSpeed);
        for (int i = MinSpeed; i <= MaxSpeed; i++)
        {
            Color col = i == active ? EcoSimThemeBuilder.Gold : EcoSimThemeBuilder.Dim;
            _notchLabels[i].AddThemeColorOverride("font_color", col);
        }
    }

    private sealed partial class SpeedNotchOverlay : Control
    {
        public override void _Notification(int what)
        {
            if (what == NotificationResized)
            {
                QueueRedraw();
            }
        }

        public override void _Draw()
        {
            Rect2 rect = GetRect();
            if (rect.Size.X < 8f) return;

            float trackY = rect.Size.Y * 0.58f;
            float tickTop = trackY - 4f;
            float tickBot = trackY + 4f;
            float margin = 7f;
            float width = Math.Max(1f, rect.Size.X - margin * 2f);

            for (int i = MinSpeed; i <= MaxSpeed; i++)
            {
                float frac = MaxSpeed > 0 ? i / (float)MaxSpeed : 0f;
                float x = margin + width * frac;
                Color col = i == 0 || i == MaxSpeed
                    ? EcoSimThemeBuilder.Edge
                    : new Color(EcoSimThemeBuilder.Edge, 0.75f);
                DrawLine(new Vector2(x, tickTop), new Vector2(x, tickBot), col, 1f);
            }
        }
    }
}

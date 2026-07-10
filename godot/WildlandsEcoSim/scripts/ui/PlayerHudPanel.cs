using EcoSim.Core.Data;
using EcoSim.Core.Sim;
using Godot;

namespace WildlandsEcoSim.UI;

/// <summary>Bottom-center HUD while possessing: needs bars, species, generation points, key hints.</summary>
public partial class PlayerHudPanel : PanelContainer
{
    private Label _title = null!;
    private Label _stateLabel = null!;
    private Label _hintLabel = null!;
    private ProgressBar _hp = null!;
    private ProgressBar _hunger = null!;
    private ProgressBar _thirst = null!;
    private ProgressBar _energy = null!;

    public override void _Ready()
    {
        Visible = false;
        MouseFilter = MouseFilterEnum.Ignore;
        TopLevel = true;
        ZIndex = 90;

        AddThemeStyleboxOverride("panel", UiSliceCatalog.MakeStonePanel());
        AddThemeConstantOverride("margin_left", 12);
        AddThemeConstantOverride("margin_right", 12);
        AddThemeConstantOverride("margin_top", 8);
        AddThemeConstantOverride("margin_bottom", 8);
        CustomMinimumSize = new Vector2(340, 0);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 4);

        _title = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        EcoSimFonts.ApplyFont(_title, EcoSimFonts.Scaled7, EcoSimThemeBuilder.Gold);
        vbox.AddChild(_title);

        var bars = new GridContainer { Columns = 2 };
        bars.AddThemeConstantOverride("h_separation", 10);
        bars.AddThemeConstantOverride("v_separation", 3);
        _hp = AddBar(bars, "HP", EcoSimThemeBuilder.Hp);
        _hunger = AddBar(bars, "Food", EcoSimThemeBuilder.Hunger);
        _thirst = AddBar(bars, "Water", EcoSimThemeBuilder.Thirst);
        _energy = AddBar(bars, "Rest", EcoSimThemeBuilder.Energy);
        vbox.AddChild(bars);

        _stateLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        EcoSimFonts.ApplyFont(_stateLabel, EcoSimFonts.Small, EcoSimThemeBuilder.Text);
        vbox.AddChild(_stateLabel);

        _hintLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        EcoSimFonts.ApplyFont(_hintLabel, EcoSimFonts.Small, EcoSimThemeBuilder.Dim);
        vbox.AddChild(_hintLabel);

        AddChild(vbox);
        GetViewport().SizeChanged += Reposition;
        Reposition();
    }

    private static ProgressBar AddBar(GridContainer parent, string name, Color fill)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        var label = new Label { Text = name, CustomMinimumSize = new Vector2(44, 0) };
        EcoSimFonts.ApplyFont(label, EcoSimFonts.Small, EcoSimThemeBuilder.Dim);
        var bar = new ProgressBar
        {
            MinValue = 0,
            MaxValue = 100,
            ShowPercentage = false,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };
        EcoSimThemeBuilder.StyleNeedBar(bar, fill);
        // StyleNeedBar resets CustomMinimumSize; give the bar its width afterwards.
        bar.CustomMinimumSize = new Vector2(110, 10);
        row.AddChild(label);
        row.AddChild(bar);
        parent.AddChild(row);
        return bar;
    }

    private void Reposition()
    {
        var rect = GetViewport().GetVisibleRect();
        ResetSize();
        Vector2 size = GetMinimumSize();
        Position = new Vector2(rect.Size.X * 0.5f - size.X * 0.5f, rect.Size.Y - size.Y - 54);
    }

    public void Refresh(SimSession session)
    {
        var c = session.Player.Controlled;
        if (c == null)
        {
            Visible = false;
            return;
        }

        var def = session.Species.Get(c.Sp);
        int points = session.Progress.Points(c.Sp);
        string sex = CreatureNotify.SexSymbol(c.Sex);
        _title.Text = $"{def.Emoji} {def.Label} {sex} #{c.Id}   🧬 {points} pt{(points == 1 ? "" : "s")}";
        _hp.Value = c.Hp;
        _hunger.Value = c.Hunger;
        _thirst.Value = c.Thirst;
        _energy.Value = c.Energy;
        _stateLabel.Text = StatusLine(session, c);
        bool hunter = def.HuntsMask != 0;
        _hintLabel.Text = hunter
            ? "WASD/click move · E attack · R mate · T evolve · P release"
            : "WASD/click move · R mate · T evolve · P release";
        Visible = true;
        Reposition();
    }

    private static string StatusLine(SimSession session, EcoSim.Core.Sim.Creature c)
    {
        string state = c.State switch
        {
            "graze" => "eating",
            "thirst" => "drinking",
            "rest" => "resting",
            "mate" => "mating",
            _ => session.Player.Intents.HasClickGoal ? "traveling" : "",
        };
        string extra = c.Pregnant > 0 ? " · pregnant" : "";
        string age = $"age {c.Age:F1}/{c.Genome.Lifespan:F0}";
        return string.IsNullOrEmpty(state) ? age + extra : $"{state} · {age}{extra}";
    }
}

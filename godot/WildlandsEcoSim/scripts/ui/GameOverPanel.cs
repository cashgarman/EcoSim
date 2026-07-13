using EcoSim.Core.Data;
using EcoSim.Core.Sim;
using Godot;

namespace WildlandsEcoSim.UI;

/// <summary>
/// Full-screen game-over screen shown when the player's species goes extinct with them.
/// Shows per-species world statistics and offers restart or possessing another species.
/// </summary>
public partial class GameOverPanel : Control
{
    [Signal]
    public delegate void RestartRequestedEventHandler();

    [Signal]
    public delegate void PossessSpeciesRequestedEventHandler(string speciesKey);

    private Label _subtitle = null!;
    private GridContainer _statsGrid = null!;
    private OptionButton _speciesPicker = null!;
    private Button _possessBtn = null!;
    private readonly List<string> _pickerKeys = [];

    public bool IsOpen => Visible;

    public override void _Ready()
    {
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        TopLevel = true;
        ZIndex = 300;

        var dim = new ColorRect
        {
            Color = new Color(0, 0, 0, 0.7f),
            MouseFilter = MouseFilterEnum.Stop,
        };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(dim);

        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Stop };
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(640, 0) };
        panel.AddThemeStyleboxOverride("panel", UiSliceCatalog.MakeStonePanel());
        panel.AddThemeConstantOverride("margin_left", 18);
        panel.AddThemeConstantOverride("margin_right", 18);
        panel.AddThemeConstantOverride("margin_top", 16);
        panel.AddThemeConstantOverride("margin_bottom", 16);
        center.AddChild(panel);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 12);
        panel.AddChild(vbox);

        var title = new Label
        {
            Text = "EXTINCTION",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        EcoSimFonts.StylePanelTitle(title, 14);
        vbox.AddChild(title);

        _subtitle = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        EcoSimFonts.ApplyFont(_subtitle, EcoSimFonts.Body, EcoSimThemeBuilder.Text);
        vbox.AddChild(_subtitle);

        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0, 280),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        _statsGrid = new GridContainer { Columns = 5, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _statsGrid.AddThemeConstantOverride("h_separation", 12);
        _statsGrid.AddThemeConstantOverride("v_separation", 7);
        scroll.AddChild(_statsGrid);
        vbox.AddChild(scroll);

        var pickRow = new HBoxContainer();
        pickRow.AddThemeConstantOverride("separation", 8);
        pickRow.Alignment = BoxContainer.AlignmentMode.Center;
        _speciesPicker = new OptionButton { CustomMinimumSize = new Vector2(200, 0) };
        _possessBtn = new Button { Text = "Possess" };
        EcoSimThemeBuilder.StylePrimaryButton(_possessBtn);
        _possessBtn.Pressed += OnPossessPressed;
        pickRow.AddChild(_speciesPicker);
        pickRow.AddChild(_possessBtn);
        vbox.AddChild(pickRow);

        var restart = new Button
        {
            Text = "Start Over (new world)",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 44),
        };
        StylePaddedDangerButton(restart);
        restart.Pressed += () =>
        {
            Visible = false;
            EmitSignal(SignalName.RestartRequested);
        };
        vbox.AddChild(restart);

        GetViewport().SizeChanged += FitToViewport;
        FitToViewport();
    }

    public void ShowGameOver(SimSession session, GameOverEvent ev)
    {
        var def = session.Species.Get(ev.Species);
        var progress = session.Progress;
        _subtitle.Text =
            $"The last {def.Emoji} {def.Label} has died. The species is extinct.\n" +
            $"Run: {progress.BodiesInhabited} bodies inhabited · {progress.TotalPointsEarned} points earned · " +
            $"killed {progress.TimesKilled}× · {progress.NaturalDeaths} natural deaths";

        RebuildStats(session);
        RebuildPicker(session);
        FitToViewport();
        Visible = true;
    }

    public void HidePanel() => Visible = false;

    private void RebuildStats(SimSession session)
    {
        foreach (var child in _statsGrid.GetChildren())
        {
            child.QueueFree();
        }

        AddHeader("Species");
        AddHeader("Alive");
        AddHeader("Born");
        AddHeader("Died");
        AddHeader("Top cause of death");

        var aliveBySpecies = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var c in session.State.Creatures)
        {
            if (!c.Dead) aliveBySpecies[c.Sp] = aliveBySpecies.GetValueOrDefault(c.Sp) + 1;
        }

        foreach (string sp in session.Species.SpeciesKeys)
        {
            var def = session.Species.Get(sp);
            var stats = session.SpeciesStats.Get(sp);
            int alive = aliveBySpecies.GetValueOrDefault(sp);
            string topCause = "—";
            int topCount = 0;
            foreach (var (cause, n) in stats.DeathsByKey)
            {
                if (n > topCount) { topCount = n; topCause = cause; }
            }

            AddCell($"{def.Emoji} {def.Label}", alive > 0 ? EcoSimThemeBuilder.Text : EcoSimThemeBuilder.PopDeltaDown);
            AddCell(alive.ToString(), alive > 0 ? EcoSimThemeBuilder.Gold : EcoSimThemeBuilder.PopDeltaDown);
            AddCell(stats.TotalBorn.ToString(), EcoSimThemeBuilder.Dim);
            AddCell(stats.TotalDied.ToString(), EcoSimThemeBuilder.Dim);
            AddCell(topCause, EcoSimThemeBuilder.Dim);
        }
    }

    private void AddHeader(string text)
    {
        var label = new Label { Text = text };
        EcoSimFonts.ApplyFont(label, EcoSimFonts.Scaled8, EcoSimThemeBuilder.Gold);
        _statsGrid.AddChild(label);
    }

    private void AddCell(string text, Color color)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        EcoSimFonts.ApplyFont(label, EcoSimFonts.Scaled7, color);
        _statsGrid.AddChild(label);
    }

    private static void StylePaddedDangerButton(Button button)
    {
        EcoSimThemeBuilder.StyleDangerButton(button);
        ApplyButtonPadding(button, 12, 16);
    }

    private static void ApplyButtonPadding(Button button, int vertical, int horizontal)
    {
        foreach (string state in new[] { "normal", "hover", "pressed" })
        {
            var style = button.GetThemeStylebox(state);
            if (style == null) continue;
            var padded = (StyleBoxFlat)style.Duplicate();
            padded.ContentMarginTop = vertical;
            padded.ContentMarginBottom = vertical;
            padded.ContentMarginLeft = horizontal;
            padded.ContentMarginRight = horizontal;
            button.AddThemeStyleboxOverride(state, padded);
        }
    }

    private void RebuildPicker(SimSession session)
    {
        _speciesPicker.Clear();
        _pickerKeys.Clear();
        var alive = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in session.State.Creatures)
        {
            if (!c.Dead) alive.Add(c.Sp);
        }

        foreach (string sp in session.Species.SpeciesKeys)
        {
            if (!alive.Contains(sp)) continue;
            var def = session.Species.Get(sp);
            _speciesPicker.AddItem($"{def.Emoji} {def.Label}", _pickerKeys.Count);
            _pickerKeys.Add(sp);
        }

        bool any = _pickerKeys.Count > 0;
        _speciesPicker.Visible = any;
        _possessBtn.Visible = any;
        if (any) _speciesPicker.Selected = 0;
    }

    private void OnPossessPressed()
    {
        int idx = _speciesPicker.Selected;
        if (idx < 0 || idx >= _pickerKeys.Count) return;
        Visible = false;
        EmitSignal(SignalName.PossessSpeciesRequested, _pickerKeys[idx]);
    }

    private void FitToViewport()
    {
        var rect = GetViewport().GetVisibleRect();
        Position = Vector2.Zero;
        Size = rect.Size;
    }
}

using EcoSim.Core.Data;
using EcoSim.Core.Sim;
using Godot;

namespace WildlandsEcoSim.UI;

/// <summary>
/// Shown when a new world is generated: pick a species to possess (a random living
/// individual is chosen automatically), or dismiss to observe the simulation.
/// </summary>
public partial class SpeciesSelectPanel : Control
{
    [Signal]
    public delegate void SpeciesChosenEventHandler(string speciesKey);

    [Signal]
    public delegate void ObserveChosenEventHandler();

    private VBoxContainer _cardList = null!;
    private ScrollContainer _scroll = null!;

    public bool IsOpen => Visible;

    public override void _Ready()
    {
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        TopLevel = true;
        ZIndex = 260;

        var dim = new ColorRect
        {
            Color = new Color(0, 0, 0, 0.55f),
            MouseFilter = MouseFilterEnum.Stop,
        };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(dim);

        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Stop };
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(720, 0) };
        panel.AddThemeStyleboxOverride("panel", UiSliceCatalog.MakeStonePanel());
        panel.AddThemeConstantOverride("margin_left", 22);
        panel.AddThemeConstantOverride("margin_right", 22);
        panel.AddThemeConstantOverride("margin_top", 20);
        panel.AddThemeConstantOverride("margin_bottom", 20);
        center.AddChild(panel);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 12);
        panel.AddChild(vbox);

        var title = new Label
        {
            Text = "Choose your animal",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        EcoSimFonts.StylePanelTitle(title, 16);
        vbox.AddChild(title);

        var subtitle = new Label
        {
            Text = "Survive. Breed. Evolve.",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        EcoSimFonts.ApplyFont(subtitle, EcoSimFonts.Body, EcoSimThemeBuilder.Dim);
        vbox.AddChild(subtitle);

        _scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0, 480),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        _cardList = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _cardList.AddThemeConstantOverride("separation", 8);
        _scroll.AddChild(_cardList);
        vbox.AddChild(_scroll);

        var observe = new Button { Text = "Just observe", FocusMode = FocusModeEnum.None };
        EcoSimFonts.ApplyFont(observe, EcoSimFonts.Body, EcoSimThemeBuilder.Dim);
        observe.Flat = true;
        observe.Pressed += () =>
        {
            Visible = false;
            EmitSignal(SignalName.ObserveChosen);
        };
        vbox.AddChild(observe);

        GetViewport().SizeChanged += FitToViewport;
        FitToViewport();
    }

    public void Open(SimSession session)
    {
        RebuildCards(session);
        FitToViewport();
        Visible = true;
        // Deferred: layout must settle before the scroll offset can be reset.
        _scroll.SetDeferred(ScrollContainer.PropertyName.ScrollVertical, 0);
    }

    private void RebuildCards(SimSession session)
    {
        foreach (var child in _cardList.GetChildren())
        {
            child.QueueFree();
        }

        var aliveBySpecies = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var c in session.State.Creatures)
        {
            if (!c.Dead) aliveBySpecies[c.Sp] = aliveBySpecies.GetValueOrDefault(c.Sp) + 1;
        }

        foreach (string sp in session.Species.SpeciesKeys)
        {
            int alive = aliveBySpecies.GetValueOrDefault(sp);
            if (alive == 0) continue;
            _cardList.AddChild(BuildCard(session.Species, sp, alive));
        }
    }

    private Control BuildCard(SpeciesCatalog catalog, string sp, int alive)
    {
        var def = catalog.Get(sp);

        var card = new Button
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            FocusMode = FocusModeEnum.All,
        };
        card.AddThemeStyleboxOverride("normal", EcoSimThemeBuilder.MakeFlat(
            EcoSimThemeBuilder.PanelDarker, EcoSimThemeBuilder.Edge, 1));
        card.AddThemeStyleboxOverride("hover", EcoSimThemeBuilder.MakeFlat(
            EcoSimThemeBuilder.PanelDark, EcoSimThemeBuilder.Gold, 1));
        card.AddThemeStyleboxOverride("pressed", EcoSimThemeBuilder.MakeFlat(
            EcoSimThemeBuilder.PanelDark, EcoSimThemeBuilder.Gold, 2));
        card.AddThemeStyleboxOverride("focus", EcoSimThemeBuilder.MakeFlat(
            EcoSimThemeBuilder.PanelDark, EcoSimThemeBuilder.Gold, 1));
        card.Pressed += () =>
        {
            Visible = false;
            EmitSignal(SignalName.SpeciesChosen, sp);
        };

        var content = new VBoxContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
        };
        content.AddThemeConstantOverride("separation", 4);

        var header = new Label { Text = $"{def.Emoji} {def.Label}   ·   {alive} alive" };
        EcoSimFonts.ApplyFont(header, EcoSimFonts.Scaled8, EcoSimThemeBuilder.Gold);

        var blurb = new Label
        {
            Text = def.Blurb,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        EcoSimFonts.ApplyFont(blurb, EcoSimFonts.Body, EcoSimThemeBuilder.Text);

        var stats = new Label
        {
            Text = StatLine(catalog, def),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        EcoSimFonts.ApplyFont(stats, EcoSimFonts.Medium, EcoSimThemeBuilder.Dim);

        content.AddChild(header);
        content.AddChild(blurb);
        content.AddChild(stats);

        // Let the content size the button; pad via a margin container.
        var margin = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_top", 10);
        margin.AddThemeConstantOverride("margin_bottom", 10);
        margin.AddChild(content);
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
        card.AddChild(margin);
        card.CustomMinimumSize = new Vector2(0, 112);

        return card;
    }

    private static string StatLine(SpeciesCatalog catalog, SpeciesDefinition def)
    {
        var parts = new List<string>
        {
            def.Diet switch { 1 => "Carnivore", 2 => "Omnivore", _ => "Herbivore" },
            $"{RangeAdjective(catalog, "speed", def.Base.Speed, "Slow", "Steady", "Fast")} · {RangeAdjective(catalog, "sense", def.Base.Sense, "Dim senses", "Alert", "Keen senses")}",
            $"Litter ~{Math.Max(1, Math.Round(def.Base.Litter))} · Lives ~{Math.Round(def.Base.Lifespan)}",
        };
        if (SpeciesCatalog.SpeciesCanSwim(def)) parts.Add("Can swim");
        string line = string.Join("  ·  ", parts);

        var extras = new List<string>();
        if (def.Hunts is { Length: > 0 })
        {
            extras.Add("Hunts: " + string.Join(", ", def.Hunts.Select(s => catalog.Get(s).Label.ToLower())));
        }
        if (def.PreyOf is { Length: > 0 })
        {
            extras.Add("Hunted by: " + string.Join(", ", def.PreyOf.Select(s => catalog.Get(s).Label.ToLower())));
        }
        else
        {
            extras.Add("No natural predators");
        }

        return line + "\n" + string.Join("  ·  ", extras);
    }

    private static string RangeAdjective(SpeciesCatalog catalog, string gene, double value, string low, string mid, string high)
    {
        if (!catalog.GeneRange.TryGetValue(gene, out var range)) return mid;
        double t = (value - range[0]) / Math.Max(1e-9, range[1] - range[0]);
        return t < 0.35 ? low : t < 0.6 ? mid : high;
    }

    private void FitToViewport()
    {
        var rect = GetViewport().GetVisibleRect();
        Position = Vector2.Zero;
        Size = rect.Size;
    }
}

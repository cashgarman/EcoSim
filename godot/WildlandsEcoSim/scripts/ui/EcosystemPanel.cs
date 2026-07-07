using EcoSim.Core.Data;
using EcoSim.Core.Sim;
using Godot;

namespace WildlandsEcoSim.UI;

public partial class EcosystemPanel : DraggablePanel
{
    [Signal]
    public delegate void SpeciesLockedEventHandler(string speciesKey);

    [Signal]
    public delegate void SpeciesHoveredEventHandler(string speciesKey);

    [Signal]
    public delegate void SpeciesFollowEventHandler(string speciesKey);

    [Signal]
    public delegate void SpeciesGodMenuEventHandler(string speciesKey, Vector2 globalPos);

    private PopGraph _graph = null!;
    private ScrollContainer _scroll = null!;
    private VBoxContainer _rows = null!;
    private VBoxContainer _panelBody = null!;
    private Button? _maxBtn;
    private PanelContainer? _graphTip;
    private Label? _graphTipLabel;
    private SpeciesCatalog? _catalog;
    private PopHistoryTracker? _popHistory;
    private string? _lockedSpecies;
    private string? _hoveredSpecies;
    private bool _maximized;
    private Vector2 _normalSize;
    private const float NormalWidth = 270f;
    private const double FlashMs = 900;
    private readonly Dictionary<string, SpeciesPopFlash> _flashes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Tween> _rowTweens = new(StringComparer.Ordinal);
    private string? _lastRowClickSp;
    private ulong _lastRowClickMs;

    private sealed class SpeciesPopFlash
    {
        public string Kind = "";
        public double UntilMs;
    }

    public string? LockedSpecies => _lockedSpecies;
    public string? HoveredSpecies => _hoveredSpecies;
    public bool IsMaximized => _maximized;

    public override void _Ready()
    {
        LayoutKey = "ecosystem";
        base._Ready();
        _graph = Req<PopGraph>("PopGraph");
        _scroll = Req<ScrollContainer>("Scroll");
        _rows = Req<VBoxContainer>("SpeciesRows");
        _panelBody = Req<VBoxContainer>("PanelBody");
        _rows.AddThemeConstantOverride("separation", 4);
        _panelBody.AddThemeConstantOverride("separation", 6);
        _scroll.CustomMinimumSize = new Vector2(0, 160);
        _scroll.VerticalScrollMode = ScrollContainer.ScrollMode.Auto;
        CustomMinimumSize = new Vector2(NormalWidth, 300);
        _normalSize = Size;

        SetupMaxButton();
        RefreshHeaderDrag();
        SetupGraphTooltip();
        BindGraphInput();
    }

    public void Bind(SpeciesCatalog catalog, PopHistoryTracker popHistory)
    {
        _catalog = catalog;
        _popHistory = popHistory;
        _graph.Bind(catalog, popHistory);
        _rows.GetChildren().ToList().ForEach(c => c.QueueFree());
        _flashes.Clear();

        foreach (string sp in catalog.SpeciesKeys)
        {
            var def = catalog.Get(sp);
            _rows.AddChild(BuildSpeciesRow(sp, def));
        }

        Callable.From(RefreshRowsLayout).CallDeferred();
    }

    public void Reset()
    {
        _lockedSpecies = null;
        _hoveredSpecies = null;
        _flashes.Clear();
        _popHistory?.Clear();
        _graph.SetFocus(null, null);
        _graph.QueueRedraw();
        UpdateRowStyles();
    }

    public void ClearSpeciesLock()
    {
        _lockedSpecies = null;
        _hoveredSpecies = null;
        UpdateRowStyles();
        _graph.SetFocus(null, null);
        EmitSignal(SignalName.SpeciesLocked, "");
        EmitSignal(SignalName.SpeciesHovered, "");
    }

    public void FlashSpeciesPop(string sp, string kind)
    {
        if (string.IsNullOrEmpty(sp)) return;
        _flashes[sp] = new SpeciesPopFlash
        {
            Kind = kind,
            UntilMs = Time.GetTicksMsec() + FlashMs,
        };
        AnimateRowFlash(sp, kind);
    }

    public void Refresh(SimSession session)
    {
        if (_catalog == null) return;

        double now = Time.GetTicksMsec();
        foreach (string sp in _flashes.Keys.ToList())
        {
            if (_flashes[sp].UntilMs <= now)
            {
                _flashes.Remove(sp);
            }
        }

        _graph.SetFocus(_lockedSpecies, _hoveredSpecies);
        _graph.QueueRedraw();

        foreach (Node child in _rows.GetChildren())
        {
            if (child is not PanelContainer row) continue;
            string sp = row.GetMeta("species").AsString();
            int count = session.State.Creatures.Count(c => c.Sp == sp && !c.Dead);
            var def = _catalog.Get(sp);

            if (row.GetChild(0) is not HBoxContainer hbox) continue;

            if (hbox.GetChild(1) is Label name)
            {
                name.Text = $"{def.Emoji} {def.Label}";
            }

            if (hbox.GetChild(2) is not HBoxContainer countWrap || countWrap.GetChildCount() < 2) continue;

            var delta = countWrap.GetChild(0) as Label;
            var countLabel = countWrap.GetChild(1) as Label;
            if (countLabel != null)
            {
                countLabel.Text = count.ToString();
            }

            bool active = sp == _lockedSpecies;
            bool hovered = sp == _hoveredSpecies;
            Color countColor = active ? EcoSimThemeBuilder.Gold
                : hovered ? EcoSimThemeBuilder.Blue
                : EcoSimThemeBuilder.Gold;
            if (countLabel != null)
            {
                EcoSimFonts.ApplyFont(countLabel, EcoSimFonts.Scaled8, countColor);
            }

            if (delta != null)
            {
                var flash = _flashes.GetValueOrDefault(sp);
                if (flash != null && flash.UntilMs > now)
                {
                    delta.Text = flash.Kind == "born" ? "▲" : "▼";
                    EcoSimFonts.ApplyFont(delta, EcoSimFonts.Scaled8,
                        flash.Kind == "born" ? EcoSimThemeBuilder.PopDeltaUp : EcoSimThemeBuilder.PopDeltaDown);
                }
                else
                {
                    delta.Text = "";
                }
            }

            row.AddThemeStyleboxOverride("panel", EcoSimThemeBuilder.MakeSpeciesRowStyle(active, hovered));
        }
    }

    protected override void OnCollapseToggled(bool collapsed)
    {
        if (collapsed && _maximized)
        {
            SetMaximized(false);
        }
    }

    private void SetupMaxButton()
    {
        var headHBox = FindChild("PanelHead", true, false) as HBoxContainer;
        if (headHBox == null) return;

        _maxBtn = new Button { Name = "MaxBtn", Text = "□", TooltipText = "Maximize panel" };
        EcoSimThemeBuilder.StyleCollapseButton(_maxBtn);
        _maxBtn.Pressed += () => SetMaximized(!_maximized);
        headHBox.AddChild(_maxBtn);
    }

    private void SetupGraphTooltip()
    {
        _graphTip = new PanelContainer
        {
            Name = "GraphTip",
            Visible = false,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _graphTip.AddThemeStyleboxOverride("panel", UiSliceCatalog.MakeInsetPanel());
        _graphTip.SetAnchorsPreset(LayoutPreset.TopLeft);

        _graphTipLabel = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        EcoSimFonts.ApplyFont(_graphTipLabel, EcoSimFonts.Scaled6);
        _graphTip.AddChild(_graphTipLabel);
        _panelBody.AddChild(_graphTip);
        _panelBody.MoveChild(_graphTip, _panelBody.GetChildCount() - 1);
    }

    private void BindGraphInput()
    {
        _graph.MouseEntered += UpdateGraphTooltip;
        _graph.MouseExited += () =>
        {
            _graph.SetHoverIndex(-1, false);
            if (_graphTip != null) _graphTip.Visible = false;
        };
        _graph.GuiInput += e =>
        {
            if (e is InputEventMouseMotion && _maximized)
            {
                int idx = _graph.HoverIndexFromMouse(((InputEventMouseMotion)e).Position);
                _graph.SetHoverIndex(idx, true);
                UpdateGraphTooltip();
            }
        };
    }

    private void UpdateGraphTooltip()
    {
        if (_graphTip == null || _graphTipLabel == null || _catalog == null || _popHistory == null || !_maximized)
        {
            if (_graphTip != null) _graphTip.Visible = false;
            return;
        }

        Vector2 local = _graph.GetLocalMousePosition();
        if (local.X < 0 || local.Y < 0 || local.X > _graph.Size.X || local.Y > _graph.Size.Y)
        {
            _graphTip.Visible = false;
            return;
        }

        int idx = _graph.HoverIndexFromMouse(local);
        _graph.SetHoverIndex(idx, true);

        var refSeries = _popHistory.GetSeries(_catalog.SpeciesKeys[0]);
        if (refSeries.Count == 0)
        {
            _graphTip.Visible = false;
            return;
        }

        idx = Mathf.Clamp(idx, 0, refSeries.Count - 1);
        var lines = new List<string> { $"Sample {idx + 1}" };
        foreach (string sp in _catalog.SpeciesKeys)
        {
            var def = _catalog.Get(sp);
            var series = _popHistory.GetSeries(sp);
            int v = idx < series.Count ? series[idx] : 0;
            lines.Add($"{def.Emoji} {def.Label}: {v}");
        }

        _graphTipLabel.Text = string.Join("\n", lines);
        _graphTip.Visible = true;

        float x = idx / (float)Math.Max(1, refSeries.Count - 1) * _graph.Size.X;
        float tipW = Math.Max(130, _graphTip.GetCombinedMinimumSize().X);
        float left = Mathf.Clamp(x - tipW * 0.5f, 4, _graph.Size.X - tipW - 4);
        _graphTip.Position = _graph.Position + new Vector2(left, 4);
        _graphTip.Size = new Vector2(tipW, _graphTip.GetCombinedMinimumSize().Y + 8);
    }

    private void SetMaximized(bool max)
    {
        _maximized = max;
        if (_maxBtn != null)
        {
            _maxBtn.Text = max ? "❐" : "□";
            EcoSimThemeBuilder.StyleActiveButton(_maxBtn, max);
        }

        if (max)
        {
            if (IsCollapsed)
            {
                SetCollapsed(false);
            }

            _normalSize = Size;
            float vpW = GetViewport().GetVisibleRect().Size.X;
            float w = Math.Min(680f, vpW * 0.72f);
            float expandedH = Math.Max(Size.Y, 420f);
            Size = new Vector2(w, expandedH);
            _graph.CustomMinimumSize = new Vector2(0, 220);
        }
        else
        {
            _graph.CustomMinimumSize = new Vector2(0, 70);
            Size = new Vector2(NormalWidth, _normalSize.Y > 1 ? _normalSize.Y : Size.Y);
        }

        _popHistory?.SyncCapacity(Math.Max(226, (int)_graph.Size.X));
        _graph.QueueRedraw();
        if (!max && _graphTip != null)
        {
            _graphTip.Visible = false;
            _graph.SetHoverIndex(-1, false);
        }
    }

    private void RefreshRowsLayout()
    {
        _rows.UpdateMinimumSize();
        _scroll.UpdateMinimumSize();
        QueueSort();
    }

    private PanelContainer BuildSpeciesRow(string sp, SpeciesDefinition def)
    {
        var row = new PanelContainer
        {
            MouseFilter = MouseFilterEnum.Stop,
        };
        row.AddThemeStyleboxOverride("panel", UiSliceCatalog.MakeInsetPanel());
        row.CustomMinimumSize = new Vector2(0, 24);

        var hbox = new HBoxContainer();
        hbox.AddThemeConstantOverride("separation", 6);

        var dotWrap = new PanelContainer();
        dotWrap.AddThemeStyleboxOverride("panel",
            EcoSimThemeBuilder.MakeFlat(EcoSimThemeBuilder.SpeciesMapColor(sp, def), EcoSimThemeBuilder.Edge, 1));
        dotWrap.CustomMinimumSize = new Vector2(10, 10);

        var name = new Label
        {
            Text = $"{def.Emoji} {def.Label}",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        EcoSimFonts.ApplyFont(name, EcoSimFonts.Scaled7);

        var countWrap = new HBoxContainer();
        countWrap.AddThemeConstantOverride("separation", 3);
        var delta = new Label { Text = "" };
        EcoSimFonts.ApplyFont(delta, EcoSimFonts.Scaled8);
        var count = new Label { Text = "0" };
        EcoSimFonts.ApplyFont(count, EcoSimFonts.Scaled8, EcoSimThemeBuilder.Gold);
        countWrap.AddChild(delta);
        countWrap.AddChild(count);

        hbox.AddChild(dotWrap);
        hbox.AddChild(name);
        hbox.AddChild(countWrap);
        row.AddChild(hbox);

        row.SetMeta("species", sp);
        row.MouseEntered += () =>
        {
            if (_hoveredSpecies == sp) return;
            _hoveredSpecies = sp;
            UpdateRowStyles();
            _graph.SetFocus(_lockedSpecies, _hoveredSpecies);
            EmitSignal(SignalName.SpeciesHovered, sp);
        };
        row.MouseExited += () =>
        {
            if (_hoveredSpecies != sp) return;
            _hoveredSpecies = null;
            UpdateRowStyles();
            _graph.SetFocus(_lockedSpecies, _hoveredSpecies);
            EmitSignal(SignalName.SpeciesHovered, "");
        };
        row.GuiInput += e => OnRowInput(e, sp, row);
        return row;
    }

    private void UpdateRowStyles()
    {
        foreach (Node child in _rows.GetChildren())
        {
            if (child is not PanelContainer row) continue;
            string sp = row.GetMeta("species").AsString();
            bool active = sp == _lockedSpecies;
            bool hovered = sp == _hoveredSpecies;
            row.AddThemeStyleboxOverride("panel", EcoSimThemeBuilder.MakeSpeciesRowStyle(active, hovered));

            if (row.GetChild(0) is HBoxContainer hbox
                && hbox.GetChild(2) is HBoxContainer countWrap
                && countWrap.GetChild(1) is Label countLabel)
            {
                Color countColor = active ? EcoSimThemeBuilder.Gold
                    : hovered ? EcoSimThemeBuilder.Blue
                    : EcoSimThemeBuilder.Gold;
                EcoSimFonts.ApplyFont(countLabel, EcoSimFonts.Scaled8, countColor);
            }
        }
    }

    private void AnimateRowFlash(string sp, string kind)
    {
        PanelContainer? row = FindSpeciesRow(sp);
        if (row == null) return;

        bool active = sp == _lockedSpecies;
        bool hovered = sp == _hoveredSpecies;
        var endStyle = EcoSimThemeBuilder.MakeSpeciesRowStyle(active, hovered);
        var flashStyle = (StyleBoxFlat)endStyle.Duplicate();
        flashStyle.BgColor = kind == "born"
            ? new Color(62f / 255f, 207f / 255f, 106f / 255f, 0.32f)
            : new Color(224f / 255f, 74f / 255f, 58f / 255f, 0.32f);
        row.AddThemeStyleboxOverride("panel", flashStyle);

        if (_rowTweens.TryGetValue(sp, out var existing))
        {
            existing.Kill();
        }

        var tween = CreateTween();
        _rowTweens[sp] = tween;
        tween.TweenCallback(Callable.From(() =>
        {
            row.AddThemeStyleboxOverride("panel", EcoSimThemeBuilder.MakeSpeciesRowStyle(
                sp == _lockedSpecies, sp == _hoveredSpecies));
            _rowTweens.Remove(sp);
        })).SetDelay(0.9);
    }

    private PanelContainer? FindSpeciesRow(string sp)
    {
        foreach (Node child in _rows.GetChildren())
        {
            if (child is PanelContainer row && row.GetMeta("species").AsString() == sp)
            {
                return row;
            }
        }

        return null;
    }

    private void OnRowInput(InputEvent e, string sp, PanelContainer row)
    {
        if (e is not InputEventMouseButton mb || !mb.Pressed) return;

        if (mb.ButtonIndex == MouseButton.Left)
        {
            ulong now = Time.GetTicksMsec();
            bool isDouble = (_lastRowClickSp == sp && now - _lastRowClickMs <= 350) || mb.DoubleClick;
            _lastRowClickSp = sp;
            _lastRowClickMs = now;

            _lockedSpecies = sp;
            _hoveredSpecies = null;
            EmitSignal(SignalName.SpeciesLocked, sp);
            EmitSignal(SignalName.SpeciesHovered, "");
            UpdateRowStyles();
            _graph.SetFocus(_lockedSpecies, _hoveredSpecies);

            if (isDouble)
            {
                EmitSignal(SignalName.SpeciesFollow, sp);
            }

            GetViewport().SetInputAsHandled();
        }
        else if (mb.ButtonIndex == MouseButton.Right)
        {
            _lockedSpecies = sp;
            _hoveredSpecies = null;
            EmitSignal(SignalName.SpeciesLocked, sp);
            EmitSignal(SignalName.SpeciesHovered, "");
            UpdateRowStyles();
            _graph.SetFocus(_lockedSpecies, _hoveredSpecies);
            EmitSignal(SignalName.SpeciesGodMenu, sp, row.GlobalPosition);
            GetViewport().SetInputAsHandled();
        }
    }

    private T Req<T>(string name) where T : Node
    {
        return FindChild(name, true, false) as T
            ?? throw new InvalidOperationException($"Missing node: {name}");
    }
}

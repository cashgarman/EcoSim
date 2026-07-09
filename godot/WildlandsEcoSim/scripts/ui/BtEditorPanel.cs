using EcoSim.Core.Behavior;
using EcoSim.Core.Sim;
using Godot;

namespace WildlandsEcoSim.UI;

/// <summary>
/// Three-pane visual Behavior Tree editor (Blackboard | Graph | Inspector).
/// Shows the full BT structure for a locked species, live trace for a selected creature,
/// and edits that can be saved to a self-contained per-species behavior file.
/// </summary>
public partial class BtEditorPanel : DraggablePanel
{
    private static readonly Vector2 DefaultSize = new(1060, 600);
    private static readonly Vector2 MinSize = new(720, 400);

    private Control _shell = null!;
    private Label _title = null!;
    private Label _empty = null!;
    private VBoxContainer _content = null!;
    private HBoxContainer _panes = null!;
    private BtBlackboardPane _blackboard = null!;
    private BtGraphCanvas _canvas = null!;
    private BtInspectorPane _inspector = null!;
    private Button _saveBtn = null!;
    private Button _revertBtn = null!;
    private Label _status = null!;
    private Control _resizeGrip = null!;
    private bool _resizing;
    private Vector2 _resizeStartMouse;
    private Vector2 _resizeStartSize;

    private SimSession? _session;
    private BtEditSaveService? _saveService;
    private BtEditorDocument? _doc;
    private string? _currentSpecies;
    private bool _dirty;
    private bool _schemaSet;

    public override void _Ready()
    {
        LayoutKey = "bteditor";
        Visible = false;
        CustomMinimumSize = MinSize;
        ClipContents = false;
        ZIndex = 55;

        _shell = new Control { MouseFilter = MouseFilterEnum.Ignore };
        _shell.SetAnchorsPreset(LayoutPreset.FullRect);
        _shell.SetOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_shell);

        var root = new VBoxContainer();
        root.SetAnchorsPreset(LayoutPreset.FullRect);
        root.SetOffsetsPreset(LayoutPreset.FullRect);
        _shell.AddChild(root);

        var head = new HBoxContainer { Name = "PanelHead" };
        _title = new Label { Text = "Behavior Tree", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        EcoSimFonts.StylePanelTitle(_title);
        head.AddChild(_title);
        root.AddChild(head);

        var body = new VBoxContainer
        {
            Name = "PanelBody",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        body.AddThemeConstantOverride("separation", 6);
        root.AddChild(body);

        base._Ready();

        var toolbar = new HBoxContainer();
        toolbar.AddThemeConstantOverride("separation", 6);
        _saveBtn = MakeToolButton("Save", OnSave);
        _revertBtn = MakeToolButton("Revert", OnRevert);
        toolbar.AddChild(_saveBtn);
        toolbar.AddChild(_revertBtn);
        toolbar.AddChild(MakeToolButton("Auto-layout", () => _canvas.AutoLayout()));
        toolbar.AddChild(MakeToolButton("Reset view", () => _canvas.ResetView()));
        _status = new Label { Text = "", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        EcoSimFonts.StyleDimLabel(_status);
        toolbar.AddChild(_status);
        body.AddChild(toolbar);

        _empty = new Label
        {
            Text = "Select a creature or lock a species to view its behavior tree.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        EcoSimFonts.ApplyFont(_empty, EcoSimFonts.Body, EcoSimThemeBuilder.Dim);
        body.AddChild(_empty);

        _content = new VBoxContainer
        {
            Visible = false,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        _content.AddThemeConstantOverride("separation", 0);
        body.AddChild(_content);

        _panes = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _panes.AddThemeConstantOverride("separation", 8);
        _content.AddChild(_panes);

        _blackboard = new BtBlackboardPane
        {
            SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        _canvas = new BtGraphCanvas
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        _inspector = new BtInspectorPane
        {
            SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        _panes.AddChild(_blackboard);
        _panes.AddChild(_canvas);
        _panes.AddChild(_inspector);

        _resizeGrip = new Control
        {
            CustomMinimumSize = new Vector2(20, 20),
            MouseFilter = MouseFilterEnum.Stop,
            MouseDefaultCursorShape = CursorShape.Bdiagsize,
            TooltipText = "Drag to resize",
            ZIndex = 1,
        };
        _resizeGrip.SetAnchorsPreset(LayoutPreset.BottomRight);
        _resizeGrip.OffsetLeft = -20;
        _resizeGrip.OffsetTop = -20;
        _resizeGrip.OffsetRight = 0;
        _resizeGrip.OffsetBottom = 0;
        _resizeGrip.GuiInput += OnResizeGripInput;
        _shell.AddChild(_resizeGrip);

        Callable.From(EnsureLayout).CallDeferred();

        _canvas.NodeSelected += id => _inspector.ShowNode(id);
        _canvas.StructureChanged += () => { MarkDirty(); _inspector.ShowNode(_canvas.SelectedId); };
        _canvas.Dirtied += MarkDirty;
        _inspector.Changed += () => { MarkDirty(); _canvas.QueueRedraw(); };
        _blackboard.ThresholdsChanged += MarkDirty;
    }

    public override void _Process(double delta)
    {
        base._Process(delta);

        if (!_resizing) return;

        if (!Input.IsMouseButtonPressed(MouseButton.Left))
        {
            _resizing = false;
            SaveLayout();
            _canvas.QueueRedraw();
            return;
        }

        Vector2 deltaSize = GetGlobalMousePosition() - _resizeStartMouse;
        Size = new Vector2(
            Mathf.Max(MinSize.X, _resizeStartSize.X + deltaSize.X),
            Mathf.Max(MinSize.Y, _resizeStartSize.Y + deltaSize.Y));
        PanelLayoutService.ClampToViewport(this);
        _canvas.QueueRedraw();
    }

    public override void _Draw()
    {
        base._Draw();
        if (_resizeGrip == null) return;

        Vector2 p = _resizeGrip.Position + new Vector2(6, 6);
        Color c = EcoSimThemeBuilder.Dim;
        for (int i = 0; i < 3; i++)
        {
            Vector2 o = new(i * 4, i * 4);
            DrawLine(p + o, p + o + new Vector2(8, 0), c, 1.5f);
            DrawLine(p + o, p + o + new Vector2(0, 8), c, 1.5f);
        }
    }

    public override void SaveLayout()
    {
        if (!string.IsNullOrEmpty(LayoutKey))
        {
            PanelLayoutService.SaveBounds(this, LayoutKey);
        }
    }

    private void EnsureLayout()
    {
        Vector2 defaultPos = GlobalPosition;
        if (defaultPos == Vector2.Zero)
        {
            var vp = GetViewportRect().Size;
            defaultPos = new Vector2((vp.X - DefaultSize.X) * 0.5f, 88);
        }

        if (!PanelLayoutService.ApplyBounds(this, LayoutKey, defaultPos, DefaultSize))
        {
            Size = DefaultSize;
        }

        PanelLayoutService.ClampToViewport(this);
    }

    private void OnResizeGripInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            if (mb.Pressed)
            {
                _resizing = true;
                _resizeStartMouse = GetGlobalMousePosition();
                _resizeStartSize = Size;
                AcceptEvent();
            }
        }
        else if (@event is InputEventMouseMotion && _resizing)
        {
            AcceptEvent();
        }
    }

    private Button MakeToolButton(string text, Action onPressed)
    {
        var btn = new Button { Text = text };
        EcoSimFonts.ApplyFont(btn, EcoSimFonts.PanelUiBtn);
        btn.FocusMode = FocusModeEnum.None;
        btn.Pressed += onPressed;
        return btn;
    }

    private void MarkDirty()
    {
        _dirty = true;
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        bool canWrite = _saveService?.CanWrite ?? false;
        _saveBtn.Disabled = !canWrite;
        _saveBtn.TooltipText = canWrite ? "" : "Behaviors folder not writable (exported build)";
        _status.Text = _dirty ? (canWrite ? "Unsaved changes" : "Unsaved (read-only build)") : "";
    }

    public void Refresh(SimSession? session, string? lockedSpecies)
    {
        if (_content == null) return;
        _session = session;

        if (session == null)
        {
            ShowEmpty();
            return;
        }

        _saveService ??= new BtEditSaveService(session.Behaviors, session.Species);
        if (!_schemaSet)
        {
            _blackboard.SetSchema(session.Behaviors.Schema);
            _inspector.SetSchema(session.Behaviors.Schema);
            _schemaSet = true;
        }

        var selected = session.State.Selected is { Dead: false } c ? c : null;
        string? targetSpecies = selected?.Sp ?? (string.IsNullOrEmpty(lockedSpecies) ? null : lockedSpecies);

        if (targetSpecies == null)
        {
            ShowEmpty();
            return;
        }

        _empty.Visible = false;
        _content.Visible = true;

        // (Re)build the editor doc when the target species changes and there are no unsaved edits.
        if (targetSpecies != _currentSpecies && !_dirty)
        {
            BuildDocFor(session, targetSpecies);
        }
        else if (_doc == null)
        {
            BuildDocFor(session, targetSpecies);
        }

        // Live trace only when a creature of the current doc's species is selected and edits are clean.
        bool liveMatches = selected != null && selected.Sp == _currentSpecies && !_dirty;
        if (liveMatches)
        {
            var trace = session.BehaviorTree.EvaluateWithTrace(selected!, session.Creatures);
            var (proposed, _) = session.BehaviorTree.PeekDecision(selected!, session.Creatures);
            _canvas.SetTrace(true, selected!.BtBranchUid, proposed?.BranchUid, trace.Steps);
        }
        else
        {
            _canvas.SetTrace(false, null, null, null);
        }

        _blackboard.UpdateLive(selected, session.Species, session.State);
        UpdateStatus();
    }

    private void BuildDocFor(SimSession session, string speciesKey)
    {
        var def = session.Species.Get(speciesKey);
        var cfg = def.BehaviorConfig;
        _currentSpecies = speciesKey;
        _dirty = false;

        _title.Text = $"{def.Emoji} {def.Label} BT";

        if (cfg == null)
        {
            _doc = null;
            _canvas.SetDocument(null, true);
            _blackboard.SetDocument(null);
            _inspector.SetContext(null);
            _inspector.ShowNode(null);
            return;
        }

        _doc = BehaviorGraphAdapter.ToEditorDocument(cfg);
        _canvas.SetAvailableDefs(cfg.Actions, cfg.Conditions);
        _canvas.SetDocument(_doc, true);
        _blackboard.SetDocument(_doc);
        _inspector.SetContext(_doc);
        _inspector.ShowNode(null);
    }

    private void OnSave()
    {
        if (_session == null || _saveService == null || _doc == null || _currentSpecies == null) return;

        var result = _saveService.Save(_currentSpecies, _doc);
        if (result.Success)
        {
            _dirty = false;
            if (result.Config != null)
            {
                BehaviorGraphAdapter.SyncUidsFromConfig(_doc, result.Config);
                _canvas.SetAvailableDefs(result.Config.Actions, result.Config.Conditions);
            }
            _status.Text = "Saved";
            UpdateStatus();
        }
        else
        {
            string detail = result.ValidationErrors.Count > 0
                ? result.ValidationErrors[0].Message
                : result.Error ?? "unknown error";
            _status.Text = $"Save failed: {detail}";
        }
    }

    private void OnRevert()
    {
        if (_session == null || _saveService == null || _currentSpecies == null) return;
        var cfg = _saveService.Revert(_currentSpecies) ?? _session.Species.Get(_currentSpecies).BehaviorConfig;
        _dirty = false;
        if (cfg != null)
        {
            _doc = BehaviorGraphAdapter.ToEditorDocument(cfg);
            _canvas.SetAvailableDefs(cfg.Actions, cfg.Conditions);
            _canvas.SetDocument(_doc, true);
            _blackboard.SetDocument(_doc);
            _inspector.SetContext(_doc);
            _inspector.ShowNode(null);
        }
        _status.Text = "Reverted";
        UpdateStatus();
    }

    private void ShowEmpty()
    {
        _empty.Visible = true;
        _content.Visible = false;
        _title.Text = "Behavior Tree";
        _currentSpecies = null;
        _dirty = false;
        _canvas.SetTrace(false, null, null, null);
    }
}

using EcoSim.Core.Sim;
using Godot;

namespace WildlandsEcoSim.UI;

public partial class TimelineDbPanel : DraggablePanel
{
    private OptionButton _storeSelect = null!;
    private VBoxContainer _rows = null!;
    private Button _prevBtn = null!;
    private Button _nextBtn = null!;
    private Label _pageLabel = null!;
    private TimelineDb? _db;
    private string _store = "world";
    private int _page;

    public override void _Ready()
    {
        LayoutKey = "timelinedb";
        Visible = false;

        var rootVBox = new VBoxContainer();
        AddChild(rootVBox);

        var head = new HBoxContainer { Name = "PanelHead" };
        var title = new Label { Text = "Timeline DB", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        EcoSimFonts.StylePanelTitle(title);
        head.AddChild(title);
        rootVBox.AddChild(head);

        var body = new VBoxContainer { Name = "PanelBody", SizeFlagsVertical = SizeFlags.ExpandFill };
        rootVBox.AddChild(body);

        base._Ready();

        _storeSelect = new OptionButton();
        _storeSelect.AddItem("World events", 0);
        _storeSelect.AddItem("Creature events", 1);
        _storeSelect.AddItem("Heartbeats", 2);
        _storeSelect.ItemSelected += idx =>
        {
            _store = idx switch { 1 => "creature", 2 => "heartbeat", _ => "world" };
            _page = 0;
            Refresh();
        };
        body.AddChild(_storeSelect);

        _rows = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddChild(_rows);

        var nav = new HBoxContainer();
        _prevBtn = new Button { Text = "◀" };
        _nextBtn = new Button { Text = "▶" };
        _pageLabel = new Label { Text = "Page 1" };
        _prevBtn.Pressed += () => { _page = Math.Max(0, _page - 1); Refresh(); };
        _nextBtn.Pressed += () => { _page++; Refresh(); };
        nav.AddChild(_prevBtn);
        nav.AddChild(_pageLabel);
        nav.AddChild(_nextBtn);
        body.AddChild(nav);
    }

    public void Bind(TimelineDb db)
    {
        _db = db;
        Refresh();
    }

    public void Refresh()
    {
        if (_db == null || _rows == null) return;
        _rows.GetChildren().ToList().ForEach(c => c.QueueFree());
        var rows = _db.ListRows(_store, _page * 20, 20);
        _pageLabel.Text = $"Page {_page + 1}";
        foreach (var row in rows)
        {
            var label = new Label
            {
                Text = $"t={row.T:F1} d{row.Day}: {row.Text}",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            };
            EcoSimFonts.ApplyFont(label, EcoSimFonts.Scaled6);
            _rows.AddChild(label);
        }
    }
}

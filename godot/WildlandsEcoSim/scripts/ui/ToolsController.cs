using EcoSim.Core.Data;
using EcoSim.Core.Sim;
using Godot;

namespace WildlandsEcoSim.UI;

public partial class ToolsController : HBoxContainer
{
    private string _activeTool = "inspect";
    private SpeciesCatalog? _catalog;

    public string ActiveTool => _activeTool;

    public override void _Ready()
    {
        BuildToolbar();
    }

    public void Bind(SpeciesCatalog catalog)
    {
        _catalog = catalog;
        BuildToolbar();
    }

    private void BuildToolbar()
    {
        GetChildren().ToList().ForEach(c => c.QueueFree());
        AddTool("inspect", "🔍", "Inspect creature");
        if (_catalog != null)
        {
            foreach (string sp in _catalog.SpeciesKeys)
            {
                var def = _catalog.Get(sp);
                AddTool($"spawn-{sp}", def.Emoji, $"Spawn {def.Label}");
            }
        }

        AddTool("rain", "🌧", "Rain — refill vegetation");
        AddTool("drought", "☀", "Drought — strip vegetation");
        AddTool("meteor", "☄", "Meteor — scorch area");
        AddTool("cull", "💀", "Cull — remove creatures");
        UpdateActiveStyles();
    }

    private void AddTool(string id, string label, string tooltip)
    {
        var btn = new Button { Text = label, TooltipText = tooltip };
        btn.SetMeta("tool_id", id);
        EcoSimFonts.StyleToolButton(btn);
        btn.Pressed += () =>
        {
            _activeTool = id;
            UpdateActiveStyles();
        };
        AddChild(btn);
    }

    private void UpdateActiveStyles()
    {
        foreach (Node child in GetChildren())
        {
            if (child is Button btn)
            {
                bool active = btn.GetMeta("tool_id").AsString() == _activeTool;
                btn.Modulate = active ? EcoSimThemeBuilder.Gold : Colors.White;
            }
        }
    }

    public bool IsPaintTool => _activeTool is "rain" or "drought" or "meteor" or "cull";

    public void ApplyAt(SimSession session, SpeciesCatalog catalog, double wx, double wy)
    {
        if (_activeTool == "inspect") return;

        if (_activeTool.StartsWith("spawn-", StringComparison.Ordinal))
        {
            string sp = _activeTool["spawn-".Length..];
            WorldTools.TrySpawn(session.Creatures, catalog, sp, wx, wy);
            return;
        }

        int cx = (int)Math.Round(wx), cy = (int)Math.Round(wy);
        switch (_activeTool)
        {
            case "rain":
                WorldTools.ApplyRain(session.State, cx, cy);
                break;
            case "drought":
                WorldTools.ApplyDrought(session.State, cx, cy);
                break;
            case "meteor":
                WorldTools.ApplyMeteor(session.State, session.Creatures, wx, wy);
                break;
            case "cull":
                WorldTools.ApplyCull(session.State, session.Creatures, wx, wy);
                break;
        }
    }
}

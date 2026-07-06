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
        AddTool("inspect", "🔍");
        if (_catalog != null)
        {
            foreach (string sp in _catalog.SpeciesKeys)
            {
                var def = _catalog.Get(sp);
                AddTool($"spawn-{sp}", def.Emoji);
            }
        }
        AddTool("rain", "🌧");
        AddTool("drought", "☀");
        AddTool("meteor", "☄");
        AddTool("cull", "💀");
        UpdateActiveStyles();
    }

    private void AddTool(string id, string label)
    {
        var btn = new Button { Text = label, TooltipText = id };
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
                bool active = btn.TooltipText == _activeTool;
                btn.Modulate = active ? EcoSimThemeBuilder.Gold : Colors.White;
            }
        }
    }

    public void ApplyAt(SimSession session, double wx, double wy)
    {
        if (_activeTool == "inspect") return;

        if (_activeTool.StartsWith("spawn-", StringComparison.Ordinal))
        {
            string sp = _activeTool[6..];
            int tx = (int)Math.Round(wx), ty = (int)Math.Round(wy);
            if (!GridHelpers.InBounds(session.State, tx, ty)) return;
            if (BiomeData.IsWater(session.State.Biome[GridHelpers.Idx(session.State, tx, ty)])) return;
            session.Creatures.MakeCreature(sp, wx, wy);
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

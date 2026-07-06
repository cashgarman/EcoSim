using EcoSim.Core.Sim;
using Godot;

namespace WildlandsEcoSim.UI;

/// <summary>Steward intervention tools — inspect + weather/disaster; no manual spawning.</summary>
public partial class ToolsController : HBoxContainer
{
    private string _activeTool = "inspect";

    public string ActiveTool => _activeTool;

    public override void _Ready()
    {
        BuildToolbar();
    }

    private void BuildToolbar()
    {
        GetChildren().ToList().ForEach(c => c.QueueFree());
        AddTool("inspect", "🔍", "Inspect creature");
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

    public void ApplyAt(SimSession session, double wx, double wy)
    {
        if (_activeTool == "inspect") return;

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

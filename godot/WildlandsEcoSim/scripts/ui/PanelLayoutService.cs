using Godot;

namespace WildlandsEcoSim.UI;

public static class PanelLayoutService
{
    private const string Path = "user://panel-layout.cfg";

    public static void ApplyPosition(Control panel, string key)
    {
        var cfg = new ConfigFile();
        if (cfg.Load(Path) != Error.Ok) return;
        if (!cfg.HasSectionKey(key, "x")) return;
        panel.Position = new Vector2(
            (float)cfg.GetValue(key, "x").AsDouble(),
            (float)cfg.GetValue(key, "y").AsDouble());
    }

    public static void SavePosition(Control panel, string key)
    {
        var cfg = new ConfigFile();
        cfg.Load(Path);
        cfg.SetValue(key, "x", panel.Position.X);
        cfg.SetValue(key, "y", panel.Position.Y);
        cfg.Save(Path);
    }

    public static void SaveAll(DraggablePanel[] panels)
    {
        foreach (var p in panels)
        {
            p.SaveLayout();
        }
    }
}

using Godot;

namespace WildlandsEcoSim.UI;

public static class PanelLayoutService
{
    private const string Path = "user://panel-layout.cfg";

    public static void ApplyPosition(Control panel, string key)
    {
        panel.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        panel.SetOffsetsPreset(Control.LayoutPreset.TopLeft);

        var cfg = new ConfigFile();
        if (cfg.Load(Path) != Error.Ok) return;
        if (!cfg.HasSectionKey(key, "x")) return;
        panel.GlobalPosition = new Vector2(
            (float)cfg.GetValue(key, "x").AsDouble(),
            (float)cfg.GetValue(key, "y").AsDouble());
    }

    public static void SavePosition(Control panel, string key)
    {
        var cfg = new ConfigFile();
        cfg.Load(Path);
        Vector2 pos = panel.GlobalPosition;
        cfg.SetValue(key, "x", pos.X);
        cfg.SetValue(key, "y", pos.Y);
        cfg.Save(Path);
    }

    public static void SaveAll(DraggablePanel[] panels)
    {
        foreach (var p in panels)
        {
            p.SaveLayout();
        }
    }

    public static void ClampAll(DraggablePanel[] panels)
    {
        foreach (var p in panels)
        {
            ClampToViewport(p);
        }
    }

    public static void ClampToViewport(Control panel)
    {
        Vector2 viewport = panel.GetViewport().GetVisibleRect().Size;
        Vector2 size = panel.Size;
        if (size.X < 1f || size.Y < 1f)
        {
            size = panel.GetCombinedMinimumSize();
        }

        Vector2 pos = panel.GlobalPosition;
        float maxX = Math.Max(0f, viewport.X - size.X);
        float maxY = Math.Max(0f, viewport.Y - size.Y);
        panel.GlobalPosition = new Vector2(
            Mathf.Clamp(pos.X, 0f, maxX),
            Mathf.Clamp(pos.Y, 0f, maxY));
    }
}

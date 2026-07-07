using Godot;

namespace WildlandsEcoSim.UI;

/// <summary>Shared header drag setup — whole strip drags; header buttons stay clickable.</summary>
public static class PanelHeaderDrag
{
    public static void ConfigureStrip(Control strip, HBoxContainer? headHBox, Control.GuiInputEventHandler onDragInput)
    {
        strip.MouseFilter = Control.MouseFilterEnum.Stop;
        strip.MouseDefaultCursorShape = Control.CursorShape.Move;
        strip.GuiInput -= onDragInput;
        strip.GuiInput += onDragInput;
        ConfigureHeadChildren(headHBox);
    }

    public static void ConfigureHeadChildren(HBoxContainer? headHBox)
    {
        if (headHBox == null) return;

        headHBox.MouseFilter = Control.MouseFilterEnum.Pass;
        foreach (Node child in headHBox.GetChildren())
        {
            if (child is Button btn)
            {
                btn.MouseFilter = Control.MouseFilterEnum.Stop;
            }
            else if (child is Label lbl)
            {
                lbl.MouseFilter = Control.MouseFilterEnum.Ignore;
            }
        }
    }
}

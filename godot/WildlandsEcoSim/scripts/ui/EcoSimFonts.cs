using Godot;

namespace WildlandsEcoSim.UI;

/// <summary>
/// Press Start 2P + pixel sizes from wildlands-ecosim.html.
/// Scaled* values match calc(Npx * 1.3) used on species/story rows.
/// </summary>
public static class EcoSimFonts
{
    public const string FontPath = "res://assets/fonts/PressStart2P-Regular.ttf";

    public const int Body = 8;
    public const int PanelTitle = 10;
    public const int PanelUiBtn = 10;
    public const int InspectorTitle = 9;
    public const int SpeciesStatsTitle = 8;
    public const int Small = 6;
    public const int Medium = 7;
    public const int DayIcon = 14;
    public const int ToolIcon = 16;
    public const int Scaled6 = 8;
    public const int Scaled7 = 9;
    public const int Scaled8 = 10;
    public const int ScrubDayLabel = 6;
    public const int ScrubPlayIcon = 8;

    private static FontFile? _fontFile;

    public static FontFile GetFont()
    {
        if (_fontFile != null)
        {
            return _fontFile;
        }

        _fontFile = GD.Load<FontFile>(FontPath)
            ?? throw new InvalidOperationException($"Missing UI font: {FontPath}");
        _fontFile.Antialiasing = TextServer.FontAntialiasing.None;
        _fontFile.Hinting = TextServer.Hinting.None;
        _fontFile.SubpixelPositioning = TextServer.SubpixelPositioning.Disabled;
        return _fontFile;
    }

    public static void ApplyThemeFonts(Theme theme)
    {
        var font = GetFont();
        string[] types = ["Label", "Button", "RichTextLabel", "LineEdit", "SpinBox", "CheckBox", "PopupMenu"];
        foreach (string type in types)
        {
            theme.SetFont("font", type, font);
        }

        theme.SetFontSize("font_size", "Label", Body);
        theme.SetFontSize("font_size", "Button", Body);
        theme.SetFontSize("font_size", "RichTextLabel", Body);
        theme.SetFontSize("font_size", "LineEdit", Body);
        theme.SetFontSize("font_size", "SpinBox", Body);
        theme.SetDefaultFont(font);
        theme.SetDefaultFontSize(Body);
    }

    public static void ApplyFont(Control control, int size, Color? color = null, bool textShadow = false)
    {
        control.AddThemeFontOverride("font", GetFont());
        control.AddThemeFontSizeOverride("font_size", size);
        if (color.HasValue)
        {
            control.AddThemeColorOverride("font_color", color.Value);
        }

        if (textShadow)
        {
            control.AddThemeConstantOverride("outline_size", 1);
            control.AddThemeColorOverride("font_outline_color", Colors.Black);
        }
    }

    public static void StylePanelTitle(Label label, int size = PanelTitle)
    {
        ApplyFont(label, size, EcoSimThemeBuilder.Gold, textShadow: true);
    }

    public static void StylePanelUiButton(Button button)
    {
        ApplyFont(button, PanelUiBtn);
    }

    public static void StyleDimLabel(Label label, int size = Small)
    {
        ApplyFont(label, size, EcoSimThemeBuilder.Dim);
    }

    public static void StyleBodyLabel(Label label, int size = Body, Color? color = null)
    {
        ApplyFont(label, size, color ?? EcoSimThemeBuilder.Text);
    }

    public static void StyleTabButton(Button button)
    {
        ApplyFont(button, Small);
    }

    public static void StyleToolButton(Button button)
    {
        ApplyFont(button, ToolIcon);
        button.CustomMinimumSize = new Vector2(38, 38);
    }

    public static void StyleStoryEntry(Label label)
    {
        ApplyFont(label, Scaled6);
    }

    public static void StyleSpeciesRow(Label name, Label count, Label? gen = null)
    {
        ApplyFont(name, Scaled7);
        ApplyFont(count, Scaled8, EcoSimThemeBuilder.Gold);
        if (gen != null)
        {
            ApplyFont(gen, Scaled6, EcoSimThemeBuilder.Dim);
        }
    }
}

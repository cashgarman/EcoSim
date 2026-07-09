using EcoSim.Core.Data;
using Godot;
using WildlandsEcoSim.Render;

namespace WildlandsEcoSim.UI;

public static class EcoSimThemeBuilder
{
    public static readonly Color Panel = new("3d4636");
    public static readonly Color PanelDark = new("2c3327");
    public static readonly Color PanelDarker = new("20261c");
    public static readonly Color Edge = new("141810");
    public static readonly Color Text = new("e8e4d0");
    public static readonly Color Gold = new("f2b53e");
    public static readonly Color Dim = new("9aa38a");
    public static readonly Color Blue = new("57b8e8");
    public static readonly Color Hp = new("4fd455");
    public static readonly Color NodeSequence = new("9a6cd6");
    public static readonly Color Hunger = new("d98a3a");
    public static readonly Color Thirst = new("3aa8d8");
    public static readonly Color Energy = new("d8c23a");
    public static readonly Color PageBg = new("1e4878");
    public static readonly Color UiShellBg = new("0c100a");
    public static readonly Color PopDeltaUp = new("3ecf6a");
    public static readonly Color PopDeltaDown = new("e04a3a");

    public static readonly Color BudgetSim = new("5a9e4a");
    public static readonly Color BudgetSnapshot = new("9e6b4a");
    public static readonly Color BudgetDisplay = new("6a5a9e");
    public static readonly Color BudgetRender = new("4a8a9e");
    public static readonly Color BudgetUi = new("9e9a4a");
    public static readonly Color BudgetOther = new("666666");

    public static readonly Color TimelineNight = new("142033");
    public static readonly Color TimelineDay = new("6a8ab0");

    public const float UiScale = 1.05f;

    public static Theme Build()
    {
        var theme = new Theme();
        EcoSimFonts.ApplyThemeFonts(theme);

        theme.SetStylebox("panel", "PanelContainer", UiSliceCatalog.MakeStonePanel());

        var btnNormal = UiSliceCatalog.MakeButtonNormal();
        var btnHover = MakeButtonStyle(PanelDark);
        theme.SetStylebox("normal", "Button", btnNormal);
        theme.SetStylebox("hover", "Button", btnHover);
        theme.SetStylebox("pressed", "Button", btnNormal);
        theme.SetStylebox("disabled", "Button", MakeButtonStyle(PanelDarker.Darkened(0.2f)));
        theme.SetColor("font_color", "Button", Text);
        theme.SetColor("font_disabled_color", "Button", Dim);
        theme.SetFontSize("font_size", "Button", EcoSimFonts.Body);

        theme.SetColor("font_color", "Label", Text);
        theme.SetColor("font_color", "RichTextLabel", Text);
        theme.SetFontSize("font_size", "Label", EcoSimFonts.Body);

        var sliderBg = UiSliceCatalog.MakeInsetPanel();
        var sliderFill = MakeFlat(Gold, Edge, 2);
        theme.SetStylebox("slider", "HSlider", sliderBg);
        theme.SetStylebox("grabber_area", "HSlider", sliderFill);
        theme.SetStylebox("grabber_area_highlight", "HSlider", sliderFill);

        theme.SetStylebox("background", "ProgressBar", UiSliceCatalog.MakeInsetPanel());
        theme.SetStylebox("fill", "ProgressBar", MakeFlat(Hp, Edge, 1));

        theme.SetStylebox("normal", "SpinBox", UiSliceCatalog.MakeInsetPanel());
        theme.SetStylebox("focus", "SpinBox", UiSliceCatalog.MakeInsetPanel());
        theme.SetStylebox("normal", "LineEdit", UiSliceCatalog.MakeInsetPanel());
        theme.SetStylebox("focus", "LineEdit", UiSliceCatalog.MakeInsetPanel());
        theme.SetColor("font_color", "LineEdit", Text);
        theme.SetStylebox("normal", "OptionButton", UiSliceCatalog.MakeInsetPanel());
        theme.SetStylebox("hover", "OptionButton", UiSliceCatalog.MakeInsetPanel());
        theme.SetStylebox("pressed", "OptionButton", UiSliceCatalog.MakeInsetPanel());
        theme.SetStylebox("focus", "OptionButton", UiSliceCatalog.MakeInsetPanel());
        theme.SetColor("font_color", "OptionButton", Text);
        theme.SetColor("font_color", "CheckBox", Text);
        theme.SetFontSize("font_size", "CheckBox", EcoSimFonts.Body);

        return theme;
    }

    public static void StyleDangerButton(Button button)
    {
        button.AddThemeStyleboxOverride("normal", MakeFlat(new Color("a64032"), new Color("4d1a14"), 2));
        button.AddThemeStyleboxOverride("hover", MakeFlat(new Color("b84838"), new Color("4d1a14"), 2));
        button.AddThemeStyleboxOverride("pressed", MakeFlat(new Color("963828"), new Color("4d1a14"), 2));
        button.AddThemeColorOverride("font_color", new Color("fff4e6"));
        EcoSimFonts.ApplyFont(button, EcoSimFonts.Body);
    }

    public static void StylePrimaryButton(Button button)
    {
        var gold = UiSliceCatalog.MakeButtonGold();
        button.AddThemeStyleboxOverride("normal", gold);
        button.AddThemeStyleboxOverride("hover", gold);
        button.AddThemeStyleboxOverride("pressed", gold);
        button.AddThemeColorOverride("font_color", new Color("2a2413"));
        EcoSimFonts.ApplyFont(button, EcoSimFonts.Body);
    }

    public static Color SpeciesColor(SpeciesDefinition def)
    {
        int[] col = def.Col;
        if (col.Length < 3)
        {
            return Colors.Gray;
        }

        return new Color(col[0] / 255f, col[1] / 255f, col[2] / 255f);
    }

    /// <summary>Per-species colour matching zoomed-out map circles.</summary>
    public static Color SpeciesMapColor(string speciesKey, SpeciesDefinition def) =>
        CreatureDrawUtil.SpeciesMapColor(speciesKey, def);

    public static void StyleActiveButton(Button button, bool active)
    {
        if (active)
        {
            button.AddThemeStyleboxOverride("normal", MakeOutlineStyle(Gold));
        }
        else
        {
            button.RemoveThemeStyleboxOverride("normal");
        }
    }

    public static void StyleCollapseButton(Button button)
    {
        button.AddThemeStyleboxOverride("normal", UiSliceCatalog.MakeInsetPanel());
        button.AddThemeStyleboxOverride("hover", UiSliceCatalog.MakeInsetPanel());
        button.AddThemeStyleboxOverride("pressed", UiSliceCatalog.MakeInsetPanel());
        button.CustomMinimumSize = new Vector2(20, 20);
        EcoSimFonts.StylePanelUiButton(button);
    }

    public static StyleBoxFlat MakeSpeciesRowStyle(bool active, bool hovered)
    {
        Color border = active ? Gold : hovered ? Blue : Edge;
        Color bg = active ? Panel : PanelDarker;
        return MakeFlat(bg, border, active || hovered ? 2 : 2);
    }

    public static StyleBoxFlat MakeOutlineStyle(Color outline)
    {
        var style = MakeButtonStyle(PanelDarker);
        style.BorderColor = outline;
        style.BorderWidthTop = 2;
        style.BorderWidthBottom = 2;
        style.BorderWidthLeft = 2;
        style.BorderWidthRight = 2;
        return style;
    }

    public static StyleBoxFlat MakeStoneStyle()
    {
        var s = new StyleBoxFlat
        {
            BgColor = Panel,
            BorderColor = Edge,
            BorderWidthTop = 3,
            BorderWidthBottom = 3,
            BorderWidthLeft = 3,
            BorderWidthRight = 3,
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6,
            ContentMarginLeft = 12,
            ContentMarginRight = 12,
            ContentMarginTop = 12,
            ContentMarginBottom = 12,
            ShadowColor = new Color(0, 0, 0, 0.5f),
            ShadowSize = 4,
            ShadowOffset = new Vector2(0, 4),
        };
        return s;
    }

    public static StyleBoxFlat MakeButtonStyle(Color bg, Color? border = null)
    {
        return new StyleBoxFlat
        {
            BgColor = bg,
            BorderColor = border ?? Edge,
            BorderWidthTop = 2,
            BorderWidthBottom = 2,
            BorderWidthLeft = 2,
            BorderWidthRight = 2,
            CornerRadiusTopLeft = 4,
            CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4,
            CornerRadiusBottomRight = 4,
            ContentMarginLeft = 8,
            ContentMarginRight = 8,
            ContentMarginTop = 6,
            ContentMarginBottom = 6,
        };
    }

    public static StyleBoxFlat MakeFlat(Color bg, Color border, int borderWidth = 2)
    {
        return new StyleBoxFlat
        {
            BgColor = bg,
            BorderColor = border,
            BorderWidthTop = borderWidth,
            BorderWidthBottom = borderWidth,
            BorderWidthLeft = borderWidth,
            BorderWidthRight = borderWidth,
            CornerRadiusTopLeft = 3,
            CornerRadiusTopRight = 3,
            CornerRadiusBottomLeft = 3,
            CornerRadiusBottomRight = 3,
        };
    }

    public static void StyleNeedBar(ProgressBar bar, Color fill)
    {
        bar.AddThemeStyleboxOverride("background", UiSliceCatalog.MakeInsetPanel());
        bar.AddThemeStyleboxOverride("fill", MakeFlat(fill, Edge, 1));
        bar.CustomMinimumSize = new Vector2(0, 10);
    }

    public static Label MakeGoldTitle(string text)
    {
        var label = new Label { Text = text };
        EcoSimFonts.StylePanelTitle(label);
        return label;
    }

    public static Label MakeDimLabel(string text)
    {
        var label = new Label { Text = text };
        EcoSimFonts.StyleDimLabel(label);
        return label;
    }
}

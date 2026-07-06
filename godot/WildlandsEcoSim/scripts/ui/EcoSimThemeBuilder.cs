using Godot;

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
    public static readonly Color Hp = new("4fd455");
    public static readonly Color Hunger = new("d98a3a");
    public static readonly Color Thirst = new("3aa8d8");
    public static readonly Color Energy = new("d8c23a");
    public static readonly Color PageBg = new("0c100a");

    public static Theme Build()
    {
        var theme = new Theme();

        var panelStyle = MakeStoneStyle();
        theme.SetStylebox("panel", "PanelContainer", panelStyle);

        var btnNormal = MakeButtonStyle(PanelDarker);
        var btnHover = MakeButtonStyle(PanelDark);
        var btnGold = MakeButtonStyle(Gold, Edge);
        theme.SetStylebox("normal", "Button", btnNormal);
        theme.SetStylebox("hover", "Button", btnHover);
        theme.SetStylebox("pressed", "Button", btnNormal);
        theme.SetStylebox("disabled", "Button", MakeButtonStyle(PanelDarker.Darkened(0.2f)));
        theme.SetColor("font_color", "Button", Text);
        theme.SetColor("font_disabled_color", "Button", Dim);
        theme.SetFontSize("font_size", "Button", 10);

        theme.SetColor("font_color", "Label", Text);
        theme.SetColor("font_color", "RichTextLabel", Text);
        theme.SetFontSize("font_size", "Label", 10);

        var sliderBg = MakeFlat(PanelDarker, Edge, 3);
        var sliderFill = MakeFlat(Gold, Edge, 2);
        theme.SetStylebox("slider", "HSlider", sliderBg);
        theme.SetStylebox("grabber_area", "HSlider", sliderFill);
        theme.SetStylebox("grabber_area_highlight", "HSlider", sliderFill);

        theme.SetStylebox("background", "ProgressBar", MakeFlat(PanelDarker, Edge, 2));
        theme.SetStylebox("fill", "ProgressBar", MakeFlat(Hp, Edge, 1));

        return theme;
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
        bar.AddThemeStyleboxOverride("background", MakeFlat(PanelDarker, Edge, 2));
        bar.AddThemeStyleboxOverride("fill", MakeFlat(fill, Edge, 1));
        bar.CustomMinimumSize = new Vector2(0, 10);
    }

    public static Label MakeGoldTitle(string text) => new()
    {
        Text = text,
        Modulate = Gold,
    };

    public static Label MakeDimLabel(string text) => new()
    {
        Text = text,
        Modulate = Dim,
    };
}

using Godot;

namespace WildlandsEcoSim.UI;

public static class BatchTestUiBuilder
{
    public static PanelContainer MakeStoneSection(string title, string? subtitle = null)
    {
        var panel = new PanelContainer();
        panel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        panel.SizeFlagsVertical = Control.SizeFlags.ExpandFill;

        var shell = new VBoxContainer();
        shell.AddThemeConstantOverride("separation", 0);

        var head = new PanelContainer();
        head.AddThemeStyleboxOverride("panel", UiSliceCatalog.MakeHeaderStrip());
        var headVBox = new VBoxContainer();
        headVBox.AddThemeConstantOverride("separation", 2);
        var titleLabel = EcoSimThemeBuilder.MakeGoldTitle(title);
        headVBox.AddChild(titleLabel);
        if (!string.IsNullOrEmpty(subtitle))
        {
            var sub = new Label { Text = subtitle };
            EcoSimFonts.ApplyFont(sub, EcoSimFonts.Scaled6, EcoSimThemeBuilder.Dim);
            headVBox.AddChild(sub);
        }

        head.AddChild(headVBox);
        shell.AddChild(head);
        panel.SetMeta("head", head);
        panel.SetMeta("body_slot", shell);
        panel.AddChild(shell);
        return panel;
    }

    public static VBoxContainer AttachScrollBody(PanelContainer section)
    {
        var scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        var body = new VBoxContainer();
        body.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        body.AddThemeConstantOverride("separation", 8);
        body.AddThemeConstantOverride("margin_left", 4);
        body.AddThemeConstantOverride("margin_right", 4);
        body.AddThemeConstantOverride("margin_top", 4);
        body.AddThemeConstantOverride("margin_bottom", 4);
        scroll.AddChild(body);
        (section.GetMeta("body_slot").AsGodotObject() as VBoxContainer)?.AddChild(scroll);
        return body;
    }

    public static VBoxContainer AttachFixedBody(PanelContainer section)
    {
        var body = new VBoxContainer();
        body.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        body.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        body.AddThemeConstantOverride("separation", 8);
        body.AddThemeConstantOverride("margin_left", 4);
        body.AddThemeConstantOverride("margin_right", 4);
        body.AddThemeConstantOverride("margin_top", 4);
        body.AddThemeConstantOverride("margin_bottom", 4);
        (section.GetMeta("body_slot").AsGodotObject() as VBoxContainer)?.AddChild(body);
        return body;
    }

    public static Label MakeFieldLabel(string text)
    {
        var label = new Label { Text = text };
        EcoSimFonts.ApplyFont(label, EcoSimFonts.Scaled6, EcoSimThemeBuilder.Dim);
        return label;
    }

    public static Label MakeHint(string text)
    {
        var label = new Label
        {
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        EcoSimFonts.ApplyFont(label, EcoSimFonts.Scaled6, EcoSimThemeBuilder.Dim);
        return label;
    }

    public static PanelContainer MakeStatusBox()
    {
        var box = new PanelContainer();
        box.AddThemeStyleboxOverride("panel", EcoSimThemeBuilder.MakeFlat(EcoSimThemeBuilder.PanelDarker, EcoSimThemeBuilder.Edge, 2));
        box.CustomMinimumSize = new Vector2(0, 36);
        var label = new Label
        {
            Name = "StatusLabel",
            Text = "Ready",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        EcoSimFonts.ApplyFont(label, EcoSimFonts.Scaled6, EcoSimThemeBuilder.Text);
        box.AddChild(label);
        return box;
    }

    public static HBoxContainer MakeTwoColumnRow(Control left, Control right)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        left.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        right.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(left);
        row.AddChild(right);
        return row;
    }

    public static OptionButton MakeSizeOption()
    {
        var opt = new OptionButton();
        opt.AddItem("s (25 km²)", 0);
        opt.AddItem("m (64 km²)", 1);
        opt.AddItem("l (100 km²)", 2);
        opt.AddItem("xl (400 km²)", 3);
        opt.AddItem("xxl (900 km²)", 4);
        opt.Select(1);
        return opt;
    }

    public static OptionButton MakeSimBackendOption()
    {
        var opt = new OptionButton();
        opt.AddItem("cpu", 0);
        opt.AddItem("gpu", 1);
        opt.Select(0);
        return opt;
    }

    public static OptionButton MakeFuzzProfileOption()
    {
        var opt = new OptionButton();
        opt.AddItem("fast (CPU)", 0);
        opt.AddItem("fast-gpu", 1);
        opt.AddItem("deep (CPU)", 2);
        opt.AddItem("deep-gpu", 3);
        opt.Select(0);
        return opt;
    }

    public static SpinBox MakeSpinBox(double value, double min, double max, double step = 1)
    {
        var spin = new SpinBox
        {
            MinValue = min,
            MaxValue = max,
            Step = step,
            Value = value,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 28),
            Alignment = HorizontalAlignment.Left,
        };
        return spin;
    }

    public static LineEdit MakeLineField(string value)
    {
        var edit = new LineEdit
        {
            Text = value,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 28),
        };
        return edit;
    }
}

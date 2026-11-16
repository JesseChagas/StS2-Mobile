using System;
using Godot;

namespace STS2Mobile.Launcher.Design;

public static class LauncherDesign
{
    public static Color Background => new(LauncherPreferences.Dark ? "#1b2838" : "#e0e5eb");
    public static Color Surface => new(LauncherPreferences.Dark ? "#171d25" : "#f5f7fa");
    public static Color Raised => new(LauncherPreferences.Dark ? "#32465a" : "#d1dce6");
    public static Color Text => new(LauncherPreferences.Dark ? "#dcdedf" : "#172635");
    public static Color Muted => new(LauncherPreferences.Dark ? "#9daab7" : "#526579");
    public static Color Border => new(LauncherPreferences.Dark ? "#486078" : "#9eafbf");
    public static Color Accent => new(LauncherPreferences.Dark ? "#66c0f4" : "#176797");
    public static Color AccentText => new(LauncherPreferences.Dark ? "#102332" : "#f5f7fa");
    public static Color Error => new(LauncherPreferences.Dark ? "#ef8c83" : "#a92c26");
    public static Color Success => new(LauncherPreferences.Dark ? "#a4d007" : "#446b0c");

    public static StyleBoxFlat Box(
        Color color,
        float scale,
        int radius = 8,
        bool border = false,
        int padding = 0
    )
    {
        var style = new StyleBoxFlat { BgColor = color, BorderColor = Border };
        style.SetCornerRadiusAll((int)(Math.Min(radius, 2) * scale));
        style.SetBorderWidthAll(border ? Math.Max(1, (int)scale) : 0);
        style.SetContentMarginAll((int)(padding * scale));
        return style;
    }

    public static void Button(Button button, float scale, bool primary = false, bool quiet = false)
    {
        var primaryColor = new Color(LauncherPreferences.Dark ? "#5c7e10" : "#527510");
        var normal =
            primary ? primaryColor
            : quiet ? Colors.Transparent
            : Raised;
        var hover = primary ? primaryColor.Lightened(0.16f) : Raised.Lightened(0.13f);
        var pressed = primary ? primaryColor.Darkened(0.12f) : Accent.Darkened(0.48f);
        button.AddThemeStyleboxOverride("normal", Box(normal, scale, 2, !primary && !quiet, 14));
        button.AddThemeStyleboxOverride("hover", Box(hover, scale, 2, !primary, 14));
        button.AddThemeStyleboxOverride("pressed", Box(pressed, scale, 2, !primary, 14));
        button.AddThemeStyleboxOverride(
            "hover_pressed",
            Box(pressed.Lightened(0.08f), scale, 2, !primary, 14)
        );
        button.AddThemeStyleboxOverride("disabled", Box(Surface, scale, 2, true, 14));
        var focus = Box(Colors.Transparent, scale, 2, true);
        focus.BorderColor = Accent;
        button.AddThemeStyleboxOverride("focus", focus);
        button.AddThemeColorOverride("font_color", primary ? Colors.White : Text);
        button.AddThemeColorOverride("font_hover_color", primary ? Colors.White : Text);
        button.AddThemeColorOverride("font_pressed_color", Colors.White);
        button.AddThemeColorOverride("font_hover_pressed_color", Colors.White);
        button.AddThemeColorOverride("font_disabled_color", Muted);
    }
}

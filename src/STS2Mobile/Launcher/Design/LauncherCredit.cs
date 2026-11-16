using Godot;

namespace STS2Mobile.Launcher.Design;

public static class LauncherCredit
{
    public static RichTextLabel Create(float scale, int fontSize = 16, bool includeVersion = false)
    {
        var label = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            CustomMinimumSize = new Vector2(0, 26 * scale),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        label.AddThemeFontSizeOverride("normal_font_size", (int)(fontSize * scale));
        label.Text =
            $"{LauncherPreferences.AppName} by [url={LauncherPreferences.CreatorUrl}]{LauncherPreferences.Creator}[/url]";
        if (includeVersion)
            label.Text += "   |   " + LauncherVersion.Text;
        label.MetaClicked += meta =>
        {
            if (meta.AsString() == LauncherPreferences.CreatorUrl)
                OS.ShellOpen(LauncherPreferences.CreatorUrl);
        };
        void Refresh() => label.AddThemeColorOverride("default_color", LauncherDesign.Muted);
        Refresh();
        LauncherPreferences.Changed += Refresh;
        label.TreeExiting += () => LauncherPreferences.Changed -= Refresh;
        return label;
    }
}

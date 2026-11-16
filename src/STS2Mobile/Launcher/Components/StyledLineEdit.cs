using Godot;
using STS2Mobile.Launcher.Design;

namespace STS2Mobile.Launcher.Components;

public partial class StyledLineEdit : LineEdit
{
    private readonly float _scale;
    private readonly string _placeholder;

    public StyledLineEdit(string placeholder, float scale, bool secret = false)
    {
        _scale = scale;
        _placeholder = placeholder;
        Secret = secret;
        CustomMinimumSize = new Vector2(0, (int)(52 * scale));
        AddThemeFontSizeOverride("font_size", (int)(19 * scale));
        ContextMenuEnabled = true;
        ShortcutKeysEnabled = true;
        SelectAllOnFocus = true;
        Refresh();
        LauncherPreferences.Changed += Refresh;
        TreeExiting += () => LauncherPreferences.Changed -= Refresh;
    }

    private void Refresh()
    {
        PlaceholderText = LauncherText.Get(_placeholder);
        AddThemeColorOverride("font_color", LauncherDesign.Text);
        AddThemeColorOverride("font_placeholder_color", LauncherDesign.Muted);
        AddThemeColorOverride("caret_color", LauncherDesign.Accent);
        AddThemeColorOverride("selection_color", LauncherDesign.Accent.Darkened(0.35f));
        AddThemeStyleboxOverride(
            "normal",
            LauncherDesign.Box(LauncherDesign.Background, _scale, 6, true, 12)
        );
        var focus = LauncherDesign.Box(Colors.Transparent, _scale, 6, true, 12);
        focus.BorderColor = LauncherDesign.Accent;
        AddThemeStyleboxOverride("focus", focus);
        AddThemeStyleboxOverride(
            "read_only",
            LauncherDesign.Box(LauncherDesign.Surface, _scale, 6, true, 12)
        );
    }
}

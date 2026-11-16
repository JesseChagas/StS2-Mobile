using Godot;
using STS2Mobile.Launcher.Design;

namespace STS2Mobile.Launcher.Components;

public partial class StyledLabel : Label
{
    private string _key;
    private object[] _args = System.Array.Empty<object>();
    private bool _muted;
    private bool _error;

    public StyledLabel(
        string text,
        float scale,
        int fontSize = 15,
        HorizontalAlignment align = HorizontalAlignment.Left
    )
    {
        _key = text;
        HorizontalAlignment = align;
        AddThemeFontSizeOverride("font_size", (int)(fontSize * scale));
        Refresh();
        LauncherPreferences.Changed += Refresh;
        TreeExiting += () => LauncherPreferences.Changed -= Refresh;
    }

    public void SetCopy(string key, params object[] args)
    {
        _key = key;
        _args = args;
        Refresh();
    }

    public void SetMuted(bool muted = true)
    {
        _muted = muted;
        Refresh();
    }

    public void SetError(bool error)
    {
        _error = error;
        Refresh();
    }

    private void Refresh()
    {
        Text = LauncherText.Get(_key, _args);
        AddThemeColorOverride(
            "font_color",
            _error ? LauncherDesign.Error
                : _muted ? LauncherDesign.Muted
                : LauncherDesign.Text
        );
    }
}

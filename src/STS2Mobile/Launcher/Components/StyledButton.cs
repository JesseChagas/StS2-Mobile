using Godot;
using STS2Mobile.Launcher.Design;

namespace STS2Mobile.Launcher.Components;

public partial class StyledButton : Button
{
    private readonly float _scale;
    private string _key;
    private object[] _args = System.Array.Empty<object>();
    private bool _primary;
    private bool _quiet;

    public StyledButton(string text, float scale, int fontSize = 18, int height = 48)
    {
        _scale = scale;
        _key = text;
        CustomMinimumSize = new Vector2(0, (int)(height * scale));
        AddThemeFontSizeOverride("font_size", (int)(fontSize * scale));
        MouseDefaultCursorShape = CursorShape.PointingHand;
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

    public void SetPrimary(bool primary)
    {
        _primary = primary;
        Refresh();
    }

    public void SetQuiet(bool quiet = true)
    {
        _quiet = quiet;
        Refresh();
    }

    private void Refresh()
    {
        Text = LauncherText.Get(_key, _args);
        LauncherDesign.Button(this, _scale, _primary, _quiet);
    }

    public static StyleBoxFlat MakeFilled(Color bg, int cornerRadius)
    {
        var style = new StyleBoxFlat { BgColor = bg };
        style.SetCornerRadiusAll(cornerRadius);
        return style;
    }

    public static StyleBoxFlat MakeOutline(Color color, int radius, int width)
    {
        var style = MakeFilled(Colors.Transparent, radius);
        style.BorderColor = color;
        style.SetBorderWidthAll(width);
        return style;
    }
}

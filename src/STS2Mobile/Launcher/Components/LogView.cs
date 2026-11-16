using System.Collections.Generic;
using Godot;
using STS2Mobile.Launcher.Design;

namespace STS2Mobile.Launcher.Components;

public partial class LogView : RichTextLabel
{
    private readonly float _scale;
    private readonly Queue<string> _lines = new();

    public LogView(float scale)
    {
        _scale = scale;
        CustomMinimumSize = new Vector2(0, (int)(100 * scale));
        ScrollFollowing = true;
        BbcodeEnabled = false;
        AddThemeFontSizeOverride("normal_font_size", (int)(11 * scale));
        Refresh();
        LauncherPreferences.Changed += Refresh;
        TreeExiting += () => LauncherPreferences.Changed -= Refresh;
    }

    private void Refresh()
    {
        AddThemeColorOverride("default_color", LauncherDesign.Muted);
        AddThemeStyleboxOverride(
            "normal",
            LauncherDesign.Box(LauncherDesign.Background, _scale, 4, false, 10)
        );
    }

    public void AppendLog(string msg)
    {
        if (string.IsNullOrWhiteSpace(msg))
            return;
        _lines.Enqueue(msg.Length > 500 ? msg[..500] : msg);
        while (_lines.Count > 80)
            _lines.Dequeue();
        Text = string.Join("\n", _lines);
    }

    public void AppendColoredLog(string msg, Color color) => AppendLog(msg);
}

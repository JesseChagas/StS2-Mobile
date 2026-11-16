using Godot;
using STS2Mobile.Launcher.Design;

namespace STS2Mobile.Launcher.Components;

public partial class StyledProgressBar : ProgressBar
{
    private readonly float _scale;

    public StyledProgressBar(float scale)
    {
        _scale = scale;
        CustomMinimumSize = new Vector2(0, (int)(12 * scale));
        ShowPercentage = false;
        Refresh();
        LauncherPreferences.Changed += Refresh;
        TreeExiting += () => LauncherPreferences.Changed -= Refresh;
    }

    private void Refresh()
    {
        AddThemeStyleboxOverride(
            "background",
            LauncherDesign.Box(LauncherDesign.Raised, _scale, 1)
        );
        AddThemeStyleboxOverride("fill", LauncherDesign.Box(LauncherDesign.Accent, _scale, 1));
    }
}

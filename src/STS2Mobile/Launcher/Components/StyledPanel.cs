using System;
using Godot;
using STS2Mobile.Launcher.Design;

namespace STS2Mobile.Launcher.Components;

public partial class StyledPanel : CenterContainer
{
    public VBoxContainer Content { get; }
    public PanelContainer Panel { get; }
    private readonly float _scale;
    private readonly float _widthRatio;

    public StyledPanel(float scale, float widthRatio = 0.7f)
    {
        _scale = scale;
        _widthRatio = widthRatio;
        SetAnchorsPreset(LayoutPreset.FullRect);
        Panel = new PanelContainer();
        AddChild(Panel);
        Content = new VBoxContainer();
        Content.AddThemeConstantOverride("separation", (int)(16 * scale));
        Panel.AddChild(Content);
        Refresh();
        LauncherPreferences.Changed += Refresh;
        TreeExiting += () => LauncherPreferences.Changed -= Refresh;
    }

    private void Refresh() =>
        Panel.AddThemeStyleboxOverride(
            "panel",
            LauncherDesign.Box(LauncherDesign.Surface, _scale, 10, true, 28)
        );

    public void UpdateSizeFromViewport(Vector2 size) =>
        Panel.CustomMinimumSize = new Vector2(Math.Min(size.X * _widthRatio, 780 * _scale), 0);
}

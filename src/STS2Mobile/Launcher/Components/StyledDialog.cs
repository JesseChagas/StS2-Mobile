using System;
using Godot;
using STS2Mobile.Launcher.Design;

namespace STS2Mobile.Launcher.Components;

public partial class StyledDialog : ColorRect
{
    public event Action Confirmed;
    public event Action Cancelled;
    public VBoxContainer Content { get; private set; }
    public HBoxContainer Buttons { get; private set; }

    public StyledDialog(string message, float scale, bool informational = false)
    {
        ZIndex = 200;
        SetAnchorsPreset(LayoutPreset.FullRect);
        Color = new Color(0, 0, 0, 0.65f);
        MouseFilter = MouseFilterEnum.Stop;
        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);
        var box = new PanelContainer();
        box.AddThemeStyleboxOverride(
            "panel",
            LauncherDesign.Box(LauncherDesign.Surface, scale, 10, true, 24)
        );
        center.AddChild(box);
        var content = new VBoxContainer();
        Content = content;
        content.AddThemeConstantOverride("separation", (int)(20 * scale));
        box.AddChild(content);
        var label = new StyledLabel(message, scale, 16)
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(420 * scale, 0),
        };
        content.AddChild(label);
        var row = new HBoxContainer();
        Buttons = row;
        row.AddThemeConstantOverride("separation", (int)(12 * scale));
        content.AddChild(row);
        if (!informational)
        {
            var cancel = new StyledButton("Cancel", scale)
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            cancel.Pressed += () =>
            {
                QueueFree();
                Cancelled?.Invoke();
            };
            row.AddChild(cancel);
        }
        var confirm = new StyledButton(informational ? "Close" : "Continue", scale)
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        confirm.SetPrimary(true);
        confirm.Pressed += () =>
        {
            QueueFree();
            Confirmed?.Invoke();
        };
        row.AddChild(confirm);
    }
}

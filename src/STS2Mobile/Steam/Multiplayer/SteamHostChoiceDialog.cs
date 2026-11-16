using Godot;
using STS2Mobile.Launcher.Design;

namespace STS2Mobile.Steam.Multiplayer;

internal partial class SteamHostChoiceDialog : ColorRect
{
    private readonly TaskCompletionSource<bool?> result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private SteamHostChoiceDialog(Vector2 viewport) {
        Name = "SteamHostChoice"; ZIndex = 500; Color = new Color(0, 0, 0, .9f);
        var en = LauncherPreferences.Language != "pt"; var scale = Mathf.Clamp(viewport.Y / 576f, .85f, 1.7f);
        var center = new CenterContainer(); AddChild(center); center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var box = new PanelContainer { CustomMinimumSize = new Vector2(Mathf.Min(viewport.X * .8f, 850 * scale), 330 * scale) }; center.AddChild(box);
        box.AddThemeStyleboxOverride("panel", LauncherDesign.Box(LauncherDesign.Surface, scale, 8, true, 24));
        var column = new VBoxContainer(); column.AddThemeConstantOverride("separation", (int)(16 * scale)); box.AddChild(column);
        var label = new Label { Text = en ? "Create a multiplayer lobby" : "Criar sala multiplayer" }; label.AddThemeFontSizeOverride("font_size", (int)(29 * scale)); column.AddChild(label);
        var steam = new Button { Text = en ? "Host on Steam · Internet" : "Hospedar na Steam · Internet", CustomMinimumSize = new Vector2(0, 72 * scale) };
        var lan = new Button { Text = en ? "Host on local network (LAN)" : "Hospedar na rede local (LAN)", CustomMinimumSize = new Vector2(0, 62 * scale) };
        var cancel = new Button { Text = en ? "Back" : "Voltar", CustomMinimumSize = new Vector2(0, 52 * scale) };
        foreach (var b in new[] { steam, lan, cancel }) { b.AddThemeFontSizeOverride("font_size", (int)(25 * scale)); column.AddChild(b); }
        steam.Pressed += () => result.TrySetResult(true); lan.Pressed += () => result.TrySetResult(false); cancel.Pressed += () => result.TrySetResult(null);
        TreeExiting += () => result.TrySetResult(null);
    }
    public static async Task<bool?> ShowAsync(Node parent) {
        if (parent.GetNodeOrNull("SteamHostChoice") != null) return null;
        var dialog = new SteamHostChoiceDialog(parent.GetViewport().GetVisibleRect().Size); parent.AddChild(dialog); dialog.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        try { return await dialog.result.Task; }
        finally { if (GodotObject.IsInstanceValid(dialog)) dialog.QueueFree(); }
    }
}

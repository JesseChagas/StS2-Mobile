using Godot;
using STS2Mobile.Launcher.Design;
namespace STS2Mobile.Graphics;

public partial class RenderScaleSettingsRow : VBoxContainer
{
    private readonly HSlider slider;
    private readonly Label value;
    private int change;
    public RenderScaleSettingsRow() {
        Name = "MobileRenderScaleSetting"; AddThemeConstantOverride("separation", 10);
        var en = LauncherPreferences.Language != "pt";
        var header = new HBoxContainer(); AddChild(header);
        var title = new Label { Text = en ? "Render resolution" : "Resolução de renderização", SizeFlagsHorizontal = SizeFlags.ExpandFill }; title.AddThemeFontSizeOverride("font_size", 28); header.AddChild(title);
        value = new Label { Text = MobileRenderScale.Percent + "%" }; value.AddThemeFontSizeOverride("font_size", 28); header.AddChild(value);
        slider = new HSlider { MinValue = 10, MaxValue = 100, Step = 5, Value = MobileRenderScale.Percent, CustomMinimumSize = new Vector2(0, 48), SizeFlagsHorizontal = SizeFlags.ExpandFill }; AddChild(slider);
        var hint = new Label { Text = en ? "100%: original · 10%: lighter, softer image. Smooth upscaling." : "100%: original · 10%: mais leve, menos nítido. Ampliação suave.", AutowrapMode = TextServer.AutowrapMode.WordSmart }; hint.AddThemeFontSizeOverride("font_size", 20); AddChild(hint);
        slider.ValueChanged += selected => { value.Text = ((int)selected) + "%"; ScheduleApply(++change); };
        MobileRenderScale.Changed += Refresh; TreeExiting += () => MobileRenderScale.Changed -= Refresh;
    }
    private async void ScheduleApply(int revision) {
        await ToSignal(GetTree().CreateTimer(.3), SceneTreeTimer.SignalName.Timeout);
        if (GodotObject.IsInstanceValid(this) && IsInsideTree() && revision == change) MobileRenderScale.SetPercent((int)slider.Value);
    }
    private void Refresh() { if (!GodotObject.IsInstanceValid(this)) return; slider.SetValueNoSignal(MobileRenderScale.Percent); value.Text = MobileRenderScale.Percent + "%"; }
}

using Godot;
using STS2Mobile.Launcher.Components;
using STS2Mobile.Launcher.Design;

namespace STS2Mobile.Launcher;

public partial class LoadingShell : Control
{
    public StyledLabel Status { get; }
    public StyledLabel Detail { get; }
    public StyledProgressBar Progress { get; }
    public StyledLabel Percentage { get; }

    public LoadingShell(float scale, string heading, bool indeterminate = false)
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(new ScreenBackground());
        var margins = new MarginContainer();
        margins.SetAnchorsPreset(LayoutPreset.FullRect);
        foreach (var side in new[] { "left", "right" })
            margins.AddThemeConstantOverride("margin_" + side, (int)(64 * scale));
        foreach (var side in new[] { "top", "bottom" })
            margins.AddThemeConstantOverride("margin_" + side, (int)(28 * scale));
        AddChild(margins);
        var page = new VBoxContainer();
        page.AddThemeConstantOverride("separation", (int)(24 * scale));
        margins.AddChild(page);
        var top = new VBoxContainer();
        top.AddThemeConstantOverride("separation", (int)(14 * scale));
        top.AddChild(LauncherCredit.Create(scale, 18));
        page.AddChild(top);
        var options = new HBoxContainer();
        options.AddThemeConstantOverride("separation", (int)(10 * scale));
        top.AddChild(options);
        var theme = new StyledButton("Light theme", scale, 18, 48);
        theme.Pressed += LauncherPreferences.ToggleTheme;
        options.AddChild(theme);
        var language = new StyledButton("EN / PT", scale, 18, 48);
        language.Pressed += LauncherPreferences.ToggleLanguage;
        options.AddChild(language);
        void UpdateControls()
        {
            theme.SetCopy(LauncherPreferences.Dark ? "Light theme" : "Dark theme");
            language.SetCopy(LauncherPreferences.Language == "pt" ? "PT / EN" : "EN / PT");
        }
        UpdateControls();
        LauncherPreferences.Changed += UpdateControls;
        TreeExiting += () => LauncherPreferences.Changed -= UpdateControls;
        var center = new CenterContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        page.AddChild(center);
        var content = new VBoxContainer { CustomMinimumSize = new Vector2(620 * scale, 0) };
        content.AddThemeConstantOverride("separation", (int)(20 * scale));
        var panel = new PanelContainer();
        center.AddChild(panel);
        panel.AddChild(content);
        void UpdatePanel() =>
            panel.AddThemeStyleboxOverride(
                "panel",
                LauncherDesign.Box(LauncherDesign.Surface, scale, 2, true, 24)
            );
        UpdatePanel();
        LauncherPreferences.Changed += UpdatePanel;
        TreeExiting += () => LauncherPreferences.Changed -= UpdatePanel;
        content.AddChild(
            new StyledLabel(heading, scale, 38) { AutowrapMode = TextServer.AutowrapMode.WordSmart }
        );
        Status = new StyledLabel(
            indeterminate ? "Starting game..." : "Scanning game resources...",
            scale,
            19
        );
        Status.SetMuted();
        content.AddChild(Status);
        Percentage = new StyledLabel("0%", scale, 30) { Visible = !indeterminate };
        content.AddChild(Percentage);
        Progress = new StyledProgressBar(scale) { Indeterminate = indeterminate };
        content.AddChild(Progress);
        Detail = new StyledLabel("This may take a few minutes. Keep the app open.", scale, 16)
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        Detail.SetMuted();
        content.AddChild(Detail);
        page.AddChild(LauncherCredit.Create(scale, includeVersion: true));
    }

    public void SetProgress(double value)
    {
        Progress.Value = value;
        Percentage.SetCopy($"{value:F0}%");
    }
}

using System;
using Godot;
using STS2Mobile.Launcher.Components;
using STS2Mobile.Launcher.Design;
using STS2Mobile.Launcher.Sections;

namespace STS2Mobile.Launcher;

// Identity and preferences stay on the right; the inset menu scrolls independently on the left.
public sealed class LauncherView
{
    public LoginSection Login { get; }
    public CodeSection Code { get; }
    public BranchSection Branch { get; }
    public DownloadSection Download { get; }
    public ActionSection Actions { get; }
    public LogView Log { get; }
    private readonly Control _parent;
    private readonly float _scale;
    private readonly MarginContainer _frame;
    private readonly PanelContainer _form;
    private readonly ScrollContainer _scroll;
    private readonly StyledLabel _heroTitle,
        _eyebrow,
        _formTitle,
        _statusLabel;
    private readonly StyledButton _themeButton,
        _languageButton,
        _detailsButton;
    private readonly RichTextLabel _header,
        _footer;
    private bool _authentication;
    private bool _detailsExpanded;
    private string _stage;

    public LauncherView(Control parent, float scale)
    {
        _parent = parent;
        _scale = scale;
        parent.GuiInput += DismissKeyboard;
        parent.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var size = parent.GetViewport()?.GetVisibleRect().Size ?? new Vector2(1920, 1080);
        var background = new ScreenBackground();
        background.GuiInput += DismissKeyboard;
        parent.AddChild(background);
        _frame = new MarginContainer();
        _frame.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _frame.AddThemeConstantOverride("margin_left", (int)(40 * scale));
        _frame.AddThemeConstantOverride("margin_right", (int)(40 * scale));
        _frame.AddThemeConstantOverride("margin_top", (int)(26 * scale));
        _frame.AddThemeConstantOverride("margin_bottom", (int)(22 * scale));
        parent.AddChild(_frame);
        var shell = new VBoxContainer();
        shell.AddThemeConstantOverride("separation", (int)(22 * scale));
        _frame.AddChild(shell);
        _scroll = new DragScrollContainer(scale)
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            FollowFocus = true,
        };
        ((DragScrollContainer)_scroll).GestureAllowed = () =>
        {
            foreach (var child in parent.GetChildren())
                if (child is StyledDialog { Visible: true })
                    return false;
            return true;
        };
        BoxContainer columns =
            size.X / Math.Max(1, size.Y) > 1.35f ? new HBoxContainer() : new VBoxContainer();
        columns.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        columns.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        columns.AddThemeConstantOverride("separation", (int)(36 * scale));
        shell.AddChild(columns);
        var heroCenter = new VBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 1f,
        };
        columns.AddChild(heroCenter);
        var hero = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(300 * scale, 0),
        };
        hero.AddThemeConstantOverride("separation", (int)(18 * scale));
        heroCenter.AddChild(hero);
        _eyebrow = new StyledLabel("Library", scale, 12);
        _eyebrow.SetMuted();
        _eyebrow.Visible = false;
        hero.AddChild(_eyebrow);
        _header = Credit(scale, 25);
        _header.CustomMinimumSize = new Vector2(340 * scale, 0);
        hero.AddChild(_header);
        _heroTitle = new StyledLabel("Slay the Spire 2", scale, 40)
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(350 * scale, 0),
        };
        hero.AddChild(_heroTitle);
        var appearance = new HBoxContainer();
        appearance.AddThemeConstantOverride("separation", (int)(10 * scale));
        hero.AddChild(appearance);
        _themeButton = new StyledButton("Light theme", scale, 18, 48);
        _themeButton.Pressed += LauncherPreferences.ToggleTheme;
        appearance.AddChild(_themeButton);
        _languageButton = new StyledButton("EN / PT", scale, 18, 48);
        _languageButton.Pressed += LauncherPreferences.ToggleLanguage;
        appearance.AddChild(_languageButton);
        var note = new StyledLabel(
            "Unofficial launcher. Not affiliated with Mega Crit or Valve.",
            scale,
            12
        )
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        note.SetMuted();
        hero.AddChild(note);
        var formCenter = new VBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        var menuMargin = new MarginContainer
        {
            CustomMinimumSize = new Vector2(Math.Min(650 * scale, size.X - 80 * scale), 0),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        menuMargin.AddThemeConstantOverride("margin_left", (int)(40 * scale));
        columns.AddChild(menuMargin);
        columns.MoveChild(menuMargin, 0);
        menuMargin.AddChild(_scroll);
        _scroll.AddChild(formCenter);
        _form = new PanelContainer
        {
            CustomMinimumSize = new Vector2(Math.Min(570 * scale, size.X - 120 * scale), 0),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
        };
        formCenter.AddChild(_form);
        var content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", (int)(14 * scale));
        _form.AddChild(content);
        var formTop = new HBoxContainer();
        content.AddChild(formTop);
        _formTitle = new StyledLabel("Getting ready", scale, 24)
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        formTop.AddChild(_formTitle);
        Branch = new BranchSection(scale);
        formTop.AddThemeConstantOverride("separation", (int)(16 * scale));
        formTop.AddChild(Branch);
        _statusLabel = new StyledLabel("Initializing...", scale, 17)
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(340 * scale, 0),
        };
        _statusLabel.SetMuted();
        content.AddChild(_statusLabel);
        Login = new LoginSection(scale);
        content.AddChild(Login);
        Code = new CodeSection(scale);
        content.AddChild(Code);
        Download = new DownloadSection(scale);
        content.AddChild(Download);
        Actions = new ActionSection(scale);
        Actions.AboutPressed += ShowAbout;
        content.AddChild(Actions);
        _detailsButton = new StyledButton("Connection details", scale, 17, 46) { Visible = false };
        _detailsButton.SetQuiet();
        _detailsButton.Pressed += () =>
        {
            _detailsExpanded = !_detailsExpanded;
            UpdateDetails();
        };
        content.AddChild(_detailsButton);
        Log = new LogView(scale) { Visible = false };
        content.AddChild(Log);
        var footerRow = new HBoxContainer();
        shell.AddChild(footerRow);
        _footer = Credit(scale, 16);
        _footer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        footerRow.AddChild(_footer);
        var about = new StyledButton("About & credits", scale, 17, 48);
        about.SetQuiet();
        about.Pressed += ShowAbout;
        footerRow.AddChild(about);
        Refresh();
        LauncherPreferences.Changed += Refresh;
        parent.TreeExiting += () => LauncherPreferences.Changed -= Refresh;
    }

    public static float ScaleFor(Vector2 size) =>
        Math.Clamp(Math.Min(size.X / 1280f, size.Y / 720f), 0.6f, 2.5f);

    private RichTextLabel Credit(float scale, int size)
    {
        var label = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            CustomMinimumSize = new Vector2(0, 28 * scale),
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        label.AddThemeFontSizeOverride("normal_font_size", (int)(size * scale));
        label.MetaClicked += meta =>
        {
            if (meta.AsString() == LauncherPreferences.CreatorUrl)
                OS.ShellOpen(LauncherPreferences.CreatorUrl);
        };
        return label;
    }

    private void Refresh()
    {
        var credit =
            $"{LauncherPreferences.AppName} by [url={LauncherPreferences.CreatorUrl}]{LauncherPreferences.Creator}[/url]";
        _header.Text = credit;
        _footer.Text = credit + "   |   " + LauncherVersion.Text;
        _header.AddThemeColorOverride("default_color", LauncherDesign.Text);
        _footer.AddThemeColorOverride("default_color", LauncherDesign.Muted);
        _form.AddThemeStyleboxOverride(
            "panel",
            LauncherDesign.Box(LauncherDesign.Surface, _scale, 2, true, 24)
        );
        _themeButton.SetCopy(LauncherPreferences.Dark ? "Light theme" : "Dark theme");
        _languageButton.SetCopy(LauncherPreferences.Language == "pt" ? "PT / EN" : "EN / PT");
    }

    public void CloseKeyboard()
    {
        _parent.GetViewport()?.GuiReleaseFocus();
        DisplayServer.VirtualKeyboardHide();
    }

    public void SetStatus(string key, params object[] args)
    {
        _statusLabel.SetError(false);
        _statusLabel.SetCopy(key, args);
    }

    public void SetErrorStatus(string key, params object[] args)
    {
        _statusLabel.SetCopy(key, args);
        _statusLabel.SetError(true);
    }

    public void SetAuthentication(bool active)
    {
        _authentication = active;
        if (!active)
            _detailsExpanded = false;
        UpdateDetails();
    }

    private void UpdateDetails()
    {
        _detailsButton.Visible = _authentication;
        Log.Visible = _authentication && _detailsExpanded;
        _detailsButton.SetCopy(_detailsExpanded ? "Hide connection details" : "Connection details");
    }

    public void AppendLog(string msg) => Log.AppendLog(msg);

    public void AppendColoredLog(string msg, Color color) => Log.AppendColoredLog(msg, color);

    public void HideAllSections()
    {
        Login.Visible = false;
        Code.Visible = false;
        Branch.Visible = false;
        Download.Visible = false;
        Actions.HideAll();
    }

    public void UpdateKeyboardOffset()
    {
        // Keep header/footer fixed and let the body scroll when a keyboard appears.
        var keyboard = DisplayServer.VirtualKeyboardGetHeight();
        var window = DisplayServer.WindowGetSize();
        var viewport = _parent.GetViewport()?.GetVisibleRect().Size ?? new Vector2(1920, 1080);
        _frame.OffsetBottom =
            keyboard > 0 && window.Y > 0
                ? -Math.Min(viewport.Y * 0.45f, keyboard * viewport.Y / window.Y)
                : 0;
        RefreshPresentation();
    }

    public void RefreshPresentation()
    {
        var stage =
            _authentication ? "login"
            : Download.Visible ? "download"
            : Actions.IsReady ? "ready"
            : "loading";
        if (_stage == stage)
            return;
        _stage = stage;
        _heroTitle.SetCopy(stage == "login" ? "Sign in." : "Slay the Spire 2");
        _eyebrow.SetCopy(stage == "login" ? "Your account" : "Library");
        _formTitle.SetCopy(
            stage == "login" ? "Your account"
            : stage == "download" ? "Game files"
            : stage == "ready" ? "Ready to play"
            : "Getting ready"
        );
    }

    public void ShowConfirmation(string message, Action confirmed)
    {
        var dialog = new StyledDialog(message, _scale);
        dialog.Confirmed += confirmed;
        _parent.AddChild(dialog);
    }

    public void ShowLogout(Action<bool> confirmed)
    {
        var dialog = new StyledDialog(
            "Sign out of this device? Local saves and backups will be kept. The app will restart.",
            _scale
        );
        var delete = new StyledButton("Delete downloaded game files: no", _scale, 17, 50)
        {
            ToggleMode = true,
        };
        delete.Toggled += value =>
            delete.SetCopy(
                value ? "Delete downloaded game files: yes" : "Delete downloaded game files: no"
            );
        dialog.Content.AddChild(delete);
        dialog.Content.MoveChild(delete, dialog.Buttons.GetIndex());
        dialog.Confirmed += () => confirmed(delete.ButtonPressed);
        _parent.AddChild(dialog);
    }

    private void ShowAbout()
    {
        var text =
            LauncherPreferences.AppLabel
            + "\n\n"
            + LauncherText.Get("Unofficial launcher. Not affiliated with Mega Crit or Valve.")
            + "\n\n"
            + LauncherText.Get("Original launcher")
            + ": Eky / Ekyso (MIT).\nGodot Engine (MIT), Harmony (MIT), SteamKit2 (LGPL 2.1).\nMade using FMOD Studio by Firelight Technologies Pty Ltd.\nSpine Runtime © Esoteric Software.\n\n"
            + LauncherPreferences.CreatorUrl;
        var dialog = new StyledDialog(text, _scale, true);
        _parent.AddChild(dialog);
    }

    private void DismissKeyboard(InputEvent input)
    {
        if (
            input
            is InputEventMouseButton { Pressed: true }
                or InputEventScreenTouch { Pressed: true }
        )
            _parent.GetViewport()?.GuiReleaseFocus();
    }
}

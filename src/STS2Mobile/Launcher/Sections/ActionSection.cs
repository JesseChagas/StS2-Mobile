using System;
using Godot;
using STS2Mobile.Launcher.Components;
using STS2Mobile.Launcher.Design;

namespace STS2Mobile.Launcher.Sections;

public partial class ActionSection : VBoxContainer
{
    public event Action LaunchPressed;
    public event Action RetryPressed;
    public event Action<bool> LocalBackupToggled;
    public event Action<bool> CloudSyncToggled;
    public event Action CloudPushPressed;
    public event Action CloudPullPressed;
    public event Action CheckForUpdatesPressed;
    public event Action VerifyFilesPressed;
    public event Action AboutPressed;
    public event Action LogoutPressed;
    private readonly StyledButton _launchButton,
        _retryButton,
        _localBackupToggle,
        _cloudSyncToggle,
        _pushButton,
        _pullMain,
        _pullMore,
        _updateButton,
        _verifyButton,
        _moreButton,
        _logoutButton;
    private readonly HBoxContainer _syncRow;
    private readonly VBoxContainer _extra;
    private bool _auto,
        _hasPulled,
        _cloudAvailable;
    public bool IsReady => Visible && _launchButton.Visible;
    public bool PullIsPrimary => _pullMain.Visible;

    public ActionSection(float scale)
    {
        Visible = false;
        AddThemeConstantOverride("separation", (int)(10 * scale));
        _syncRow = new HBoxContainer();
        _syncRow.AddThemeConstantOverride("separation", (int)(8 * scale));
        AddChild(_syncRow);
        _cloudSyncToggle = new StyledButton("Auto Sync: on", scale, 21, 54)
        {
            ToggleMode = true,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _cloudSyncToggle.Toggled += value =>
        {
            _auto = value;
            UpdateLabels();
            UpdateVisibility();
            CloudSyncToggled?.Invoke(value);
        };
        _syncRow.AddChild(_cloudSyncToggle);
        _pullMain = new StyledButton("Pull from cloud", scale, 21, 54)
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _pullMain.Pressed += () => CloudPullPressed?.Invoke();
        _syncRow.AddChild(_pullMain);
        _updateButton = new StyledButton("Check updates", scale, 23, 54);
        _updateButton.Pressed += () => CheckForUpdatesPressed?.Invoke();
        AddChild(_updateButton);
        _launchButton = new StyledButton("Play", scale, 26, 62);
        _launchButton.SetPrimary(true);
        _launchButton.Pressed += () => LaunchPressed?.Invoke();
        AddChild(_launchButton);
        _retryButton = new StyledButton("Try again", scale, 22, 54);
        _retryButton.SetPrimary(true);
        _retryButton.Pressed += () => RetryPressed?.Invoke();
        AddChild(_retryButton);
        _moreButton = new StyledButton("More options", scale, 22, 54);
        _moreButton.SetQuiet();
        _moreButton.Pressed += () =>
        {
            _extra.Visible = !_extra.Visible;
            _moreButton.SetCopy(_extra.Visible ? "Fewer options" : "More options");
        };
        AddChild(_moreButton);
        _extra = new VBoxContainer { Visible = false };
        _extra.AddThemeConstantOverride("separation", (int)(8 * scale));
        AddChild(_extra);
        _verifyButton = new StyledButton("Verify game files", scale, 22, 54);
        _verifyButton.Pressed += () => VerifyFilesPressed?.Invoke();
        _extra.AddChild(_verifyButton);
        _pullMore = new StyledButton("Pull from cloud", scale, 22, 54);
        _pullMore.Pressed += () => CloudPullPressed?.Invoke();
        _extra.AddChild(_pullMore);
        _pushButton = new StyledButton("Push to cloud", scale, 22, 54);
        _pushButton.Pressed += () => CloudPushPressed?.Invoke();
        _extra.AddChild(_pushButton);
        _localBackupToggle = new StyledButton("Local backup: off", scale, 22, 54)
        {
            ToggleMode = true,
        };
        _localBackupToggle.Toggled += value =>
        {
            _localBackupToggle.SetCopy(value ? "Local backup: on" : "Local backup: off");
            LocalBackupToggled?.Invoke(value);
        };
        _extra.AddChild(_localBackupToggle);
        var original = new StyledButton("Original project & licenses", scale, 22, 54);
        original.SetQuiet();
        original.Pressed += () => OS.ShellOpen("https://github.com/Ekyso/StS2-Launcher");
        _extra.AddChild(original);
        var about = new StyledButton("About & credits", scale, 22, 54);
        about.SetQuiet();
        about.Pressed += () => AboutPressed?.Invoke();
        _extra.AddChild(about);
        _logoutButton = new StyledButton("Sign out", scale, 22, 54);
        _logoutButton.Pressed += () => LogoutPressed?.Invoke();
        _extra.AddChild(_logoutButton);
    }

    public void SetLocalBackupChecked(bool value)
    {
        _localBackupToggle.SetPressedNoSignal(value);
        _localBackupToggle.SetCopy(value ? "Local backup: on" : "Local backup: off");
    }

    public void SetCloudSyncChecked(bool value)
    {
        _auto = value;
        _cloudSyncToggle.SetPressedNoSignal(value);
        UpdateLabels();
        UpdateVisibility();
    }

    public void SetCloudAlreadyPulled(bool value)
    {
        _hasPulled = value;
        UpdateVisibility();
    }

    private void UpdateLabels() =>
        _cloudSyncToggle.SetCopy(_auto ? "Auto Sync: on" : "Auto Sync: off");

    private void UpdateVisibility()
    {
        _syncRow.Visible = _cloudAvailable;
        _pullMain.Visible = _cloudAvailable && CloudPresentation.ShowPullInMain(_auto, _hasPulled);
        _pullMore.Visible = _cloudAvailable && !CloudPresentation.ShowPullInMain(_auto, _hasPulled);
        _pushButton.Visible = _cloudAvailable;
        _localBackupToggle.Visible = _cloudAvailable;
    }

    public void ShowLaunch(string text, bool showCloudSync, bool showUpdate)
    {
        Visible = true;
        _cloudAvailable = showCloudSync;
        _launchButton.SetCopy(text);
        _launchButton.Visible = true;
        _retryButton.Visible = false;
        _moreButton.Visible = true;
        _updateButton.Visible = showUpdate;
        _updateButton.Disabled = false;
        _updateButton.SetCopy("Check updates");
        UpdateVisibility();
    }

    public void ShowRetry()
    {
        Visible = true;
        _retryButton.Visible = true;
        _launchButton.Visible = false;
        _syncRow.Visible = false;
        _updateButton.Visible = false;
        _extra.Visible = false;
        _moreButton.Visible = false;
    }

    public void HideAll()
    {
        Visible = false;
        _extra.Visible = false;
        _moreButton.SetCopy("More options");
    }

    public void SetPushPullDisabled(bool disabled)
    {
        _logoutButton.Disabled = disabled;
        _pushButton.Disabled = disabled;
        _pullMain.Disabled = disabled;
        _pullMore.Disabled = disabled;
        _launchButton.Disabled = disabled;
        _updateButton.Disabled = disabled;
        _verifyButton.Disabled = disabled;
        _cloudSyncToggle.Disabled = disabled;
        _localBackupToggle.Disabled = disabled;
    }

    public void SetUpdateButtonText(string text) => _updateButton.SetCopy(text);

    public void SetUpdateButtonDisabled(bool disabled) => _updateButton.Disabled = disabled;
}

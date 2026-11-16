using System;
using System.Threading.Tasks;
using Godot;
using STS2Mobile.Launcher.Design;
using STS2Mobile.Patches;
using STS2Mobile.Steam;

namespace STS2Mobile.Launcher;

// Retains the existing authentication/download/cloud operations, with explicit
// user-facing states. Technical output stays in the optional sign-in console.
public sealed class LauncherController : IDisposable
{
    private readonly LauncherModel _model;
    private readonly LauncherView _view;
    private readonly Action<Action> _runOnMainThread;
    private volatile bool _checkingForUpdates;
    private bool _downloading;
    private volatile bool _disposed;
    private bool _signingOut;
    private Action<string> _cloudLogHandler;

    public LauncherController(
        LauncherModel model,
        LauncherView view,
        Action<Action> runOnMainThread
    )
    {
        _model = model;
        _view = view;
        _runOnMainThread = runOnMainThread;
    }

    private void Dispatch(Action action)
    {
        if (!_disposed)
            _runOnMainThread(() =>
            {
                if (!_disposed)
                    action();
            });
    }

    public void Start()
    {
        _model.SessionStateChanged += state => Dispatch(() => UpdateUI(state));
        _model.LogReceived += msg => Dispatch(() => _view.AppendLog(msg));
        _cloudLogHandler = msg =>
        {
            if (msg.StartsWith("[Cloud]"))
                Dispatch(() => _view.AppendLog(msg));
        };
        PatchHelper.LogEmitted += _cloudLogHandler;
        _model.AuthNoticeReceived += notice =>
            Dispatch(() =>
            {
                _view.SetAuthentication(true);
                switch (notice)
                {
                    case AuthNotice.ApproveDevice:
                        _view.SetStatus("Approve this sign-in in the Steam app on your phone.");
                        break;
                    case AuthNotice.EmailCode:
                        _view.SetStatus("Enter the code sent to your email.");
                        break;
                    case AuthNotice.DeviceCode:
                        _view.SetStatus("Enter Steam Guard code");
                        break;
                    case AuthNotice.InvalidCode:
                        _view.SetErrorStatus("That code was incorrect. Try the latest code.");
                        break;
                }
            });
        _model.CodeNeeded += incorrect =>
            Dispatch(() =>
            {
                _view.SetAuthentication(true);
                _view.Login.Visible = false;
                _view.Code.Show(incorrect);
            });
        _model.DownloadProgressChanged += progress =>
            Dispatch(() => {
                _view.Download.Visible = true;
                _view.Download.SetProgress(
                    progress.Percentage,
                    $"{LauncherModel.FormatSize(progress.DownloadedBytes)} / {LauncherModel.FormatSize(progress.TotalBytes)}"
                );
            });
        _model.DownloadLogReceived += msg => Dispatch(() => _view.AppendLog(msg));
        _model.DownloadCompleted += () =>
            Dispatch(() =>
            {
                _view.Download.Visible = false;
                _view.SetAuthentication(false);
                _view.Branch.Visible = true;
                _view.Branch.SetDisabled(false);
                _view.SetStatus(
                    "Download complete. Restart the app to play."
                );
                if (LauncherModel.GameFilesReady())
                    _view.Actions.ShowLaunch(PlayText, _model.InGameMode, _model.InGameMode);
                else
                    _view.Actions.ShowRetry();
            });
        _model.DownloadFailed += message =>
            Dispatch(() =>
            {
                _view.Branch.SetDisabled(false);
                _view.Download.Reset(message == null ? "Download game" : "Retry download");
                if (message != null)
                {
                    _view.SetErrorStatus("Download failed. Check your connection and try again.");
                    _view.AppendLog(message);
                }
            });
        _model.DownloadCancelled += () =>
            Dispatch(() =>
            {
                _view.SetStatus("Download cancelled");
                _view.Branch.SetDisabled(false);
                _view.Download.Reset();
            });
        _model.UpdateCheckCompleted += hasUpdate =>
            Dispatch(() =>
            {
                if (hasUpdate)
                {
                    _view.Actions.HideAll();
                    _view.Branch.Visible = true;
                    _view.Download.Visible = true;
                    _view.Download.Reset("Download update");
                    _view.SetStatus("Update available");
                }
                else
                {
                    _view.Download.Visible = false;
                    ShowReady();
                    _view.Actions.SetUpdateButtonText("Up to date");
                }
            });
        _model.UpdateCheckFailed += message =>
            Dispatch(() =>
            {
                _view.Actions.SetUpdateButtonText("Check failed");
                _view.SetErrorStatus("Could not check for updates. Try again.");
                _view.AppendLog(message);
            });
        _view.Login.LoginRequested += OnLoginPressed;
        _view.Code.CodeSubmitted += code =>
        {
            _view.CloseKeyboard();
            _view.SetStatus("Verifying code...");
            _model.SubmitCode(code);
        };
        _view.Download.DownloadRequested += OnDownloadPressed;
        _view.Actions.LaunchPressed += () => _model.Launch();
        _view.Actions.RetryPressed += () => HandleFastPath(_model.Retry());
        _view.Actions.LocalBackupToggled += value =>
        {
            if (value && !AppPaths.HasStoragePermission())
                AppPaths.RequestStoragePermission();
            if (value)
                AppPaths.EnsureExternalDirectories();
            LauncherModel.SaveLocalBackupPref(value);
            CloudSyncCoordinator.LocalBackupEnabled = value;
        };
        _view.Actions.CloudSyncToggled += value =>
        {
            LauncherModel.SaveCloudSyncPref(value);
            LauncherPatches.CloudSyncEnabled = value;
        };
        _view.Actions.CloudPushPressed += () =>
            _view.ShowConfirmation(
                "Send local saves to the cloud?\nYour cloud saves will be replaced.",
                () => RunCloudTransfer(false)
            );
        _view.Actions.CloudPullPressed += () =>
            _view.ShowConfirmation(
                "Pull cloud saves to this device?\nYour local saves will be replaced.",
                () => RunCloudTransfer(true)
            );
        _view.Actions.LogoutPressed += () => _view.ShowLogout(OnLogout);
        _view.Actions.CheckForUpdatesPressed += OnCheckForUpdatesPressed;
        _view.Actions.VerifyFilesPressed += OnDownloadPressed;
        _view.Branch.BranchChanged += branch =>
        {
            BranchSettings.Save(branch);
            if (LauncherModel.GameFilesReady())
                OnCheckForUpdatesPressed();
        };
        bool backup = LauncherModel.LoadLocalBackupPref();
        _view.Actions.SetLocalBackupChecked(backup);
        CloudSyncCoordinator.LocalBackupEnabled = backup;
        if (backup)
        {
            if (!AppPaths.HasStoragePermission())
                AppPaths.RequestStoragePermission();
            AppPaths.EnsureExternalDirectories();
        }
        _view.Actions.SetCloudSyncChecked(LauncherModel.LoadCloudSyncPref());
        HandleFastPath(_model.StartSession());
    }

    private async void OnLogout(bool deleteGame)
    {
        if (_signingOut)
            return;
        _signingOut = true;
        _view.Actions.SetPushPullDisabled(true);
        _view.Branch.SetDisabled(true);
        _view.SetStatus("Signing out...");
        try
        {
            await _model.LogoutAsync(deleteGame);
        }
        catch (Exception ex)
        {
            _signingOut = false;
            PatchHelper.Log($"[Logout] Failed: {ex}");
            _view.SetErrorStatus(
                "Could not sign out. Your local saves are safe. Please try again."
            );
            _view.Actions.SetPushPullDisabled(false);
            _view.Branch.SetDisabled(false);
        }
    }

    private string PlayText => _model.InGameMode && !_model.RequiresRestart ? "Play" : "Restart to play";

    private void ShowReady()
    {
        _view.SetAuthentication(false);
        _view.Branch.Visible = true;
        _view.Actions.ShowLaunch(PlayText, true, true);
        _view.Actions.SetCloudAlreadyPulled(
            CloudPresentation.HasPulled(OS.GetDataDir(), _model.AccountName)
        );
        _view.SetStatus("Welcome back, {0}", _model.AccountName);
    }

    private void HandleFastPath(FastPathResult result)
    {
        switch (result)
        {
            case FastPathResult.ReadyToLaunch:
                ShowReady();
                break;
            case FastPathResult.AutoConnect:
                _view.SetAuthentication(false);
                _model.Connect();
                StartConnectionTimeout();
                break;
            default:
                _view.SetAuthentication(true);
                _view.SetStatus("Enter your Steam credentials");
                _view.Login.Visible = true;
                _view.Login.SetDisabled(false);
                break;
        }
    }

    private async void StartConnectionTimeout()
    {
        await Task.Delay(10000);
        if (_disposed || _model.ConnectionResolved)
            return;
        if (
            _model.SessionState
            is SessionState.Connecting
                or SessionState.Authenticating
                or SessionState.VerifyingOwnership
        )
            Dispatch(() =>
            {
                if (_model.HasOwnershipMarker() && LauncherModel.GameFilesReady())
                {
                    _view.HideAllSections();
                    ShowReady();
                    _view.SetStatus("No connection. You can use your saved sign-in.");
                }
                else
                {
                    _view.SetErrorStatus("Internet is required for the first sign-in.");
                    _view.Actions.ShowRetry();
                }
            });
    }

    private void UpdateUI(SessionState state)
    {
        if (_signingOut)
            return;
        if (
            _checkingForUpdates
            || (
                _model.AwaitingCode
                && state
                    is SessionState.Connecting
                        or SessionState.WaitingForCredentials
                        or SessionState.Authenticating
            )
        )
            return;
        if (state == SessionState.Disconnected && _model.ConnectionResolved)
            return;
        _view.HideAllSections();
        switch (state)
        {
            case SessionState.Connecting:
                _view.SetStatus("Connecting to Steam...");
                break;
            case SessionState.WaitingForCredentials:
            case SessionState.Disconnected:
                _view.SetAuthentication(true);
                _view.SetStatus("Enter your Steam credentials");
                _view.Login.Visible = true;
                _view.Login.SetDisabled(false);
                break;
            case SessionState.Authenticating:
                _view.SetAuthentication(true);
                _view.SetStatus("Authenticating...");
                break;
            case SessionState.VerifyingOwnership:
                _view.SetAuthentication(false);
                _view.SetStatus("Verifying game ownership...");
                break;
            case SessionState.LoggedIn:
                _model.ConnectionResolved = true;
                _view.SetAuthentication(false);
                if (LauncherModel.GameFilesReady())
                    ShowReady();
                else
                {
                    _view.SetStatus("Logged in as {0}", _model.AccountName);
                    _view.Download.Visible = true;
                    _view.Download.Reset();
                    _view.Branch.Visible = true;
                }
                break;
            case SessionState.Failed:
                _model.ConnectionResolved = true;
                _view.SetAuthentication(true);
                _view.SetErrorStatus(LauncherText.FriendlyError(_model.FailReason));
                _view.AppendLog(_model.FailReason);
                _view.Login.Visible = true;
                _view.Login.SetDisabled(false);
                break;
        }
    }

    private async void OnLoginPressed(string username, string password)
    {
        _view.Login.SetDisabled(true);
        _view.CloseKeyboard();
        _view.Login.ClearPassword();
        await _model.LoginAsync(username, password);
    }

    private async void OnDownloadPressed()
    {
        if (_downloading || _checkingForUpdates) return;
        _downloading = true;
        _view.SetAuthentication(false);
        _view.Actions.SetPushPullDisabled(true);
        _view.Branch.SetDisabled(true);
        _view.Download.Visible = true;
        _view.Download.ShowProgress("Preparing download...");
        try { await _model.StartDownloadAsync(); }
        finally {
            _downloading = false;
            if (!_disposed) {
                _view.Actions.SetPushPullDisabled(false);
                _view.Branch.SetDisabled(false);
            }
        }
    }

    private async void OnCheckForUpdatesPressed()
    {
        if (_checkingForUpdates || _downloading)
            return;
        _checkingForUpdates = true;
        _view.Actions.SetPushPullDisabled(true);
        _view.Actions.SetUpdateButtonText("Checking...");
        _view.Branch.SetDisabled(true);
        try
        {
            var appTask = CheckAppUpdateAsync();
            await _model.CheckForUpdatesAsync();
            await appTask;
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[Launcher] Update check failed: {ex}");
            Dispatch(() => _view.SetErrorStatus("Could not check for updates. Try again."));
        }
        finally
        {
            _checkingForUpdates = false;
            Dispatch(() =>
            {
                _view.Actions.SetPushPullDisabled(false);
                _view.Branch.SetDisabled(false);
            });
        }
    }

    private async Task CheckAppUpdateAsync()
    {
        try
        {
            var result = await AppUpdateChecker.CheckAsync();
            if (result.HasUpdate)
                Dispatch(() =>
                    _view.SetStatus(
                        "An upstream launcher update is available. Review it in More options."
                    )
                );
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[Launcher] Upstream version check: {ex.Message}");
        }
    }

    private void RunCloudTransfer(bool pull)
    {
        _view.Actions.SetPushPullDisabled(true);
        _view.Branch.SetDisabled(true);
        _view.SetStatus(pull ? "Downloading cloud saves..." : "Queueing cloud upload...");
        _ = Task.Run(async () =>
        {
            try
            {
                var result = pull
                    ? await CloudSyncCoordinator.ManualPullAllAsync(
                        LauncherPatches.SavedAccountName,
                        LauncherPatches.SavedRefreshToken
                    )
                    : await CloudSyncCoordinator.ManualPushAllAsync(
                        LauncherPatches.SavedAccountName,
                        LauncherPatches.SavedRefreshToken
                    );
                if (pull && result.Successful > 0 && result.Failed == 0)
                    CloudPresentation.MarkPulled(OS.GetDataDir(), _model.AccountName);
                Dispatch(() =>
                {
                    if (result.Failed > 0)
                        _view.SetErrorStatus(
                            "Some saves could not be transferred. Please try again."
                        );
                    else if (pull && result.Successful == 0)
                        _view.SetStatus("No cloud saves were found.");
                    else
                        _view.SetStatus(
                            pull ? "Cloud saves downloaded." : "Saves queued for cloud upload."
                        );
                    _view.Actions.SetCloudAlreadyPulled(
                        CloudPresentation.HasPulled(OS.GetDataDir(), _model.AccountName)
                    );
                });
            }
            catch (Exception ex)
            {
                PatchHelper.Log($"[Cloud] Manual transfer failed: {ex}");
                Dispatch(() =>
                    _view.SetErrorStatus(
                        "Cloud transfer failed. Check your connection and try again."
                    )
                );
            }
            finally
            {
                Dispatch(() =>
                {
                    _view.Actions.SetPushPullDisabled(false);
                    _view.Branch.SetDisabled(false);
                });
            }
        });
    }

    public void Dispose()
    {
        _disposed = true;
        if (_cloudLogHandler != null)
            PatchHelper.LogEmitted -= _cloudLogHandler;
    }
}

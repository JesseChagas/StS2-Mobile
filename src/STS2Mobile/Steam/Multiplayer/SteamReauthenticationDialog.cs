using Godot;
using STS2Mobile.Launcher.Components;
using STS2Mobile.Launcher.Design;
using STS2Mobile.Launcher.Sections;

namespace STS2Mobile.Steam.Multiplayer;

// Renew only the selected account. No logout, game deletion, or save reset.
internal partial class SteamReauthenticationDialog : ColorRect
{
    private readonly TaskCompletionSource<bool> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource cancelled = new();
    private readonly string account;
    private readonly bool en;
    private readonly LoginSection login;
    private readonly CodeSection guard;
    private readonly Label status;
    private SteamAuth auth;
    private TaskCompletionSource<string> code;
    private bool working, closed;

    private SteamReauthenticationDialog(string username, Vector2 viewport)
    {
        account = username; en = LauncherPreferences.Language != "pt";
        Name = "SteamReauthentication"; ZIndex = 500; Color = new Color(0, 0, 0, .92f);
        var scale = Mathf.Clamp(viewport.Y / 800f, .55f, 1.5f);
        var center = new CenterContainer(); AddChild(center); center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var box = new PanelContainer { CustomMinimumSize = new Vector2(Mathf.Min(viewport.X * .87f, 820 * scale), viewport.Y * .86f) };
        box.AddThemeStyleboxOverride("panel", LauncherDesign.Box(LauncherDesign.Surface, scale, 8, true, 20)); center.AddChild(box);
        var scroll = new DragScrollContainer(scale) { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        box.AddChild(scroll);
        var content = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; content.AddThemeConstantOverride("separation", (int)(12 * scale)); scroll.AddChild(content);
        var title = new Label { Text = en ? "Reconnect your Steam account" : "Reconectar sua conta Steam" };
        title.AddThemeFontSizeOverride("font_size", (int)(27 * scale)); content.AddChild(title);
        status = new Label { Text = en ? "Steam rejected the saved credential. Sign in again to renew it." : "A Steam recusou a credencial salva. Entre novamente para renová-la.", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        status.AddThemeFontSizeOverride("font_size", (int)(18 * scale)); content.AddChild(status);
        login = new LoginSection(scale) { Visible = true }; content.AddChild(login);
        login.UsernameField.Text = account; login.UsernameField.Editable = false;
        guard = new CodeSection(scale); content.AddChild(guard);
        var cancel = new StyledButton("Cancel", scale, 20, 44); content.AddChild(cancel); cancel.Pressed += Cancel;
        login.LoginRequested += (username, password) => _ = AuthenticateAsync(password);
        guard.CodeSubmitted += value => code?.TrySetResult(value);
        TreeExiting += Cancel;
    }

    public static async Task<bool> ShowAsync(Node parent, string account)
    {
        if (parent.GetNodeOrNull("SteamReauthentication") != null) return false;
        var dialog = new SteamReauthenticationDialog(account, parent.GetViewport().GetVisibleRect().Size);
        parent.AddChild(dialog); dialog.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        try { return await dialog.completion.Task; }
        finally { if (GodotObject.IsInstanceValid(dialog)) dialog.QueueFree(); }
    }

    private void Dispatch(Action action) => Callable.From(() => { if (!closed && GodotObject.IsInstanceValid(this)) action(); }).CallDeferred();

    private async Task AuthenticateAsync(string password)
    {
        if (working || closed) return;
        working = true; login.SetDisabled(true); login.ClearPassword();
        ConnectionReport.Step("steam-reauth", "Renewing Steam authorization", "Renovando a autorização Steam");
        status.Text = en ? "Authenticating with Steam…" : "Autenticando na Steam…";
        using var attempt = new SteamAuth(OS.GetDataDir()); auth = attempt;
        attempt.NoticeReceived += notice => Dispatch(() => {
            if (notice == AuthNotice.ApproveDevice) {
                login.Visible = false;
                status.Text = en ? "Approve the sign-in in your Steam mobile app." : "Aprove o acesso no aplicativo Steam Guard.";
            }
        });
        attempt.CodeProvider = async incorrect => {
            code = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            Dispatch(() => { login.Visible = false; status.Text = en ? "Enter your Steam Guard code." : "Informe o código Steam Guard."; guard.Show(incorrect); });
            var supplied = await code.Task.WaitAsync(cancelled.Token);
            if (attempt.NeedsReconnectForAuth) await attempt.ReconnectForAuthAsync();
            return supplied;
        };
        try {
            var store = new SteamCredentialStore(OS.GetDataDir()); store.Load();
            var result = await attempt.LoginWithCredentialsAsync(account, password, store.GuardData, cancelled.Token);
            password = null;
            if (!string.Equals(result.AccountName, account, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The authenticated account does not match the current game's account.");
            await GameThread.Switch();
            if (closed) return;
            status.Text = en ? "Confirming the Steam session…" : "Confirmando a sessão Steam…";
            ConnectionReport.Step("steam-session-verify", "Confirming the renewed Steam session", "Confirmando a sessão Steam renovada");
            var connection = await Task.Run(() => SharedSteamConnection.UpdateCredentials(result.AccountName, result.RefreshToken), cancelled.Token);
            using var hold = connection.HoldOpen();
            await connection.GetAuthenticatedClientAsync(cancelled.Token);
            await GameThread.Switch();
            if (closed) return;
            var verifier = new OwnershipVerifier(OS.GetDataDir(), result.AccountName);
            if (!await verifier.VerifyAsync(connection)) throw new InvalidOperationException("This Steam account does not own Slay the Spire 2.");
            await GameThread.Switch();
            if (closed) return;
            // Persist only after an actual CM logon and ownership check succeed.
            store.Save(result.AccountName, result.RefreshToken, result.GuardData);
            attempt.ClearPending();
            STS2Mobile.Patches.LauncherPatches.SavedAccountName = result.AccountName;
            STS2Mobile.Patches.LauncherPatches.SavedRefreshToken = result.RefreshToken;
            SharedSteamConnection.KeepForGame(result.AccountName, result.RefreshToken);
            completion.TrySetResult(true);
        } catch (OperationCanceledException) when (cancelled.IsCancellationRequested) { }
        catch (Exception e) {
            await GameThread.Switch();
            if (closed) return;
            status.Text = e is SteamLoginRequiredException rejected
                ? (en ? $"Steam rejected the renewed session ({rejected.Result}). Close this dialog and copy the connection details."
                    : $"A Steam recusou a sessão renovada ({rejected.Result}). Feche esta janela e copie os detalhes da conexão.")
                : LauncherText.Get(LauncherText.FriendlyError(e.Message));
            ConnectionReport.Fail(e, false);
            login.Visible = true; guard.Visible = false;
            PatchHelper.Log("[SteamMP] Reauthentication failed: " + e.GetType().Name);
        } finally {
            auth = null; password = null;
            await GameThread.Switch();
            working = false;
            if (!closed && GodotObject.IsInstanceValid(login)) { login.SetDisabled(false); login.UsernameField.Editable = false; }
        }
    }

    public void Cancel()
    {
        if (closed) return;
        closed = true; cancelled.Cancel(); code?.TrySetCanceled(); auth?.ClearPending(); auth?.Dispose();
        completion.TrySetResult(false);
    }
}

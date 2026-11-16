using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Platform.Steam;
using STS2Mobile.Steam.Multiplayer;

namespace STS2Mobile.Patches;

public static class SteamMultiplayerPatches
{
    private const string LastHost = "user://steam_multiplayer_host.txt";
    private static bool applied;
    public static void Apply(Harmony harmony)
    {
        if (applied) return;
        applied = true;
        GameThread.Initialize();
        PatchHelper.Patch(harmony, typeof(MegaCrit.Sts2.Core.Platform.Null.NullPlatformUtilStrategy), "GetPlatformBranch",
            prefix: PatchHelper.Method(typeof(ReleaseMetadataPatches), nameof(ReleaseMetadataPatches.InstalledBranch)));
        PatchHelper.Patch(harmony, typeof(NJoinFriendScreen), "OnSubmenuClosed",
            postfix: PatchHelper.Method(typeof(SteamMultiplayerPatches), nameof(CancelReauthentication)));
        PatchHelper.Patch(harmony, typeof(NJoinFriendScreen), "_Ready",
            postfix: PatchHelper.Method(typeof(SteamMultiplayerPatches), nameof(AddSteamEntry)));
        // The main menu can instantiate this screen during GameStartup. In that
        // case _Ready has already run, so also attach on the first screen open.
        PatchHelper.Patch(harmony, typeof(NJoinFriendScreen), "OnSubmenuOpened",
            postfix: PatchHelper.Method(typeof(SteamMultiplayerPatches), nameof(AddSteamEntry)));
        // Patch instance methods of the Steam strategy. Patching PlatformUtil's
        // static methods would force its native-callback constructor too early.
        PatchHelper.Patch(harmony, typeof(SteamPlatformUtilStrategy), nameof(SteamPlatformUtilStrategy.GetLocalPlayerId),
            prefix: PatchHelper.Method(typeof(SteamMultiplayerPatches), nameof(LocalId)));
        PatchHelper.Patch(harmony, typeof(SteamPlatformUtilStrategy), nameof(SteamPlatformUtilStrategy.GetPlayerName),
            prefix: PatchHelper.Method(typeof(SteamMultiplayerPatches), nameof(PlayerName)));
        PatchHelper.Patch(harmony, typeof(NetClientGameService), nameof(NetClientGameService.HandshakeSucceeded),
            postfix: PatchHelper.Method(typeof(SteamMultiplayerPatches), nameof(HandshakeComplete)));
        PatchHelper.Patch(harmony, typeof(NetClientGameService), nameof(NetClientGameService.HandshakeFailed),
            postfix: PatchHelper.Method(typeof(SteamMultiplayerPatches), nameof(HandshakeRejected)));
        PatchHelper.Patch(harmony, typeof(NJoinFriendScreen), "JoinGameAsync",
            postfix: PatchHelper.Method(typeof(SteamMultiplayerPatches), nameof(HideOriginalOverlay)));
        PatchHelper.PatchGetter(harmony, typeof(NJoinFriendScreen), "InitialFocusedControl",
            PatchHelper.Method(typeof(SteamMultiplayerPatches), nameof(SteamFocus)));
        var popup = AccessTools.Method(typeof(MegaCrit.Sts2.Core.Nodes.CommonUi.NErrorPopup), "Create", new[] { typeof(NetErrorInfo) });
        harmony.Patch(popup, prefix: new HarmonyMethod(PatchHelper.Method(typeof(SteamMultiplayerPatches), nameof(AdapterErrorPopup))));
        try { SteamHostingPatches.Apply(harmony); } catch (Exception e) { PatchHelper.Log("[SteamHost] Registro indisponível: " + e); }
        PatchHelper.Log("[SteamMP] Integração Steam Android registrada [steam-p2p-v12]");
    }

    public static bool LocalId(ref ulong __result)
    {
        __result = MobileSteamSession.Current?.Self ?? MobileSteamHostSession.Current?.Self ?? 0;
        return false;
    }

    public static bool PlayerName(ulong playerId, ref string __result)
    {
        __result = MobileSteamSession.Current?.PlayerName(playerId) ?? MobileSteamHostSession.Current?.PlayerName(playerId);
        if (string.IsNullOrEmpty(__result)) __result = playerId.ToString();
        return false;
    }

    public static void HandshakeComplete(NetClientGameService __instance)
    {
        if (__instance.NetClient is MobileSteamNetClient) {
            ConnectionReport.Connected();
            PatchHelper.Log("[SteamMP] HANDSHAKE_STS2_APROVADO: SIM; protocolo original conectado.");
        }
    }

    public static void HandshakeRejected(NetClientGameService __instance, NetErrorInfo info)
    {
        if (__instance.NetClient is MobileSteamNetClient) {
            ConnectionReport.Note(HandshakeDiagnostics.Details(info));
            ConnectionReport.Note("selected-download-branch=" + STS2Mobile.Steam.BranchSettings.Load());
            ConnectionReport.Fail(new IOException(HandshakeDiagnostics.Describe(info,
                Launcher.Design.LauncherPreferences.Language != "pt")), false);
            PatchHelper.Log("[SteamMP] HANDSHAKE_STS2_RECUSADO: " + info.GetReason());
        }
    }

    public static void CancelReauthentication(NJoinFriendScreen __instance)
    {
        __instance.GetNodeOrNull<SteamReauthenticationDialog>("SteamReauthentication")?.Cancel();
    }

    public static void AddSteamEntry(NJoinFriendScreen __instance)
    {
        if (__instance.GetNodeOrNull<SteamMultiplayerPanel>("SteamInternetEntry") is { } existing) {
            ApplyViewMode(__instance, existing.LanMode);
            if (__instance.IsVisibleInTree() && !existing.LanMode) existing.RequestFriendsRefresh();
            return;
        }
        var en = STS2Mobile.Launcher.Design.LauncherPreferences.Language != "pt";
        var panel = new SteamMultiplayerPanel(en);
        __instance.AddChild(panel);
        panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        panel.Layout();
        if (Godot.FileAccess.FileExists(LastHost)) {
            using var file = Godot.FileAccess.Open(LastHost, Godot.FileAccess.ModeFlags.Read);
            if (file != null) panel.HostInput.Text = file.GetAsText().Trim();
        }
        bool joining = false;
        CancellationTokenSource friendSearch = null;
        panel.FriendsRefreshRequested += async () => {
            if (joining || panel.LanMode) return;
            friendSearch?.Cancel(); friendSearch?.Dispose();
            var search = friendSearch = new CancellationTokenSource();
            panel.SetFriendsLoading(true);
            try {
                var rooms = await SteamFriendLobbyBrowser.RefreshAsync(LauncherPatches.SavedAccountName,
                    LauncherPatches.SavedRefreshToken, search.Token);
                await GameThread.Switch();
                if (GodotObject.IsInstanceValid(panel) && !search.IsCancellationRequested) panel.SetFriendRooms(rooms);
            } catch (OperationCanceledException) when (search.IsCancellationRequested) { }
            catch (Exception e) {
                await GameThread.Switch();
                if (GodotObject.IsInstanceValid(panel)) {
                    panel.SetFriendRooms(Array.Empty<SteamHostRoom>(), en ? "Steam did not confirm friends' lobbies" : "A Steam não confirmou salas dos amigos");
                    if (e is STS2Mobile.Steam.SteamLoginRequiredException) {
                        panel.SetNeedsLogin(true);
                        panel.SetStatus(en ? "Reconnect Steam to load your friends." : "Reconecte a Steam para consultar os amigos.", true);
                    }
                }
                PatchHelper.Log("[SteamMP] Consulta de amigos: " + e.GetType().Name);
            } finally {
                await GameThread.Switch();
                if (GodotObject.IsInstanceValid(panel)) panel.SetFriendsLoading(false);
            }
        };
        __instance.VisibilityChanged += () => { if (!__instance.IsVisibleInTree()) friendSearch?.Cancel(); };
        panel.TreeExiting += () => { friendSearch?.Cancel(); friendSearch?.Dispose(); friendSearch = null; };
        void Refresh()
        {
            if (!GodotObject.IsInstanceValid(panel) || !panel.IsInsideTree()) return;
            var state = ConnectionReport.Snapshot;
            if (state.Stage == "idle") return;
            var message = state.Failed
                ? (en ? $"Failed at {state.Stage}: {state.Status}" : $"Falha em {state.Stage}: {state.Status}")
                : state.Status;
            panel.SetStatus(message, state.Failed, state.Failed);
            panel.SetNeedsLogin(state.RequiresLogin);
        }
        Action changed = () => Callable.From(Refresh).CallDeferred();
        ConnectionReport.Changed += changed;
        panel.TreeExiting += () => ConnectionReport.Changed -= changed;
        panel.LanSelected += mode => ApplyViewMode(__instance, mode);
        panel.CancelRequested += () => {
            var flow = AccessTools.Field(typeof(NJoinFriendScreen), "_currentJoinFlow")?.GetValue(__instance);
            var cancel = flow?.GetType().GetProperty("CancelToken")?.GetValue(flow) as CancellationTokenSource;
            cancel?.Cancel();
        };
        async Task Join(string text)
        {
            if (joining) return;
            if (!ulong.TryParse(text, out var host) || (host >> 56) != 1 || ((host >> 52) & 15) != 1) {
                panel.SetStatus(en ? "Enter a valid SteamID64." : "Informe um SteamID64 válido.", true);
                return;
            }
            if (string.IsNullOrEmpty(LauncherPatches.SavedRefreshToken)) {
                panel.SetStatus(en ? "Sign in to Steam in the launcher first." : "Entre na Steam pelo launcher primeiro.", true);
                return;
            }
            friendSearch?.Cancel();
            joining = true; panel.SetBusy(true); ConnectionReport.Begin(host);
            using (var file = Godot.FileAccess.Open(LastHost, Godot.FileAccess.ModeFlags.Write)) file?.StoreString(host.ToString());
            try { await __instance.JoinGameAsync(new MobileSteamInitializer(host)); }
            catch (OperationCanceledException) { ConnectionReport.Cancelled(); }
            catch (Exception e) { ConnectionReport.Fail(e, false); }
            finally {
                joining = false;
                if (GodotObject.IsInstanceValid(panel)) { panel.SetBusy(false); Refresh(); }
            }
        }
        panel.JoinRequested += async text => await Join(text);
        panel.ReconnectRequested += async () => {
            if (joining) return;
            panel.SetBusy(true);
            try {
                var renewed = await SteamReauthenticationDialog.ShowAsync(__instance, LauncherPatches.SavedAccountName);
                await GameThread.Switch();
                if (renewed && GodotObject.IsInstanceValid(panel) && __instance.IsVisibleInTree()) {
                    panel.SetNeedsLogin(false); panel.SetBusy(false);
                    await Join(panel.HostInput.Text.Trim());
                }
            } finally {
                await GameThread.Switch();
                if (GodotObject.IsInstanceValid(panel)) panel.SetBusy(false);
            }
        };
        ApplyViewMode(__instance, false);
        Refresh();
        if (__instance.IsVisibleInTree()) panel.RequestFriendsRefresh();
    }

    private static void ApplyViewMode(NJoinFriendScreen screen, bool lan)
    {
        var en = STS2Mobile.Launcher.Design.LauncherPreferences.Language != "pt";
        LanMultiplayerPatcher.SetSteamMode(!lan, screen);
        if (screen.GetNodeOrNull<Control>("LanIpEntry") is { } ip) {
            ip.Visible = lan;
            foreach (var child in ip.GetChildren()) {
                if (child is LineEdit edit) edit.PlaceholderText = en ? "Host IP address" : "Endereço IP do anfitrião";
                if (child is Button button) button.Text = en ? "JOIN" : "ENTRAR";
            }
        }
        foreach (var name in new[] { "_buttonContainer", "_refreshButton", "_noFriendsLabel" }) {
            if (AccessTools.Field(typeof(NJoinFriendScreen), name)?.GetValue(screen) is Control control) control.Visible = lan;
        }
        if (AccessTools.Field(typeof(NJoinFriendScreen), "_loadingFriendsIndicator")?.GetValue(screen) is Control indicator) indicator.Visible = false;
        var title = screen.GetNodeOrNull("TitleLabel");
        title?.GetType().GetMethod("SetTextAutoSize")?.Invoke(title, new object[] { "MULTIPLAYER" });
        var empty = AccessTools.Field(typeof(NJoinFriendScreen), "_noFriendsLabel")?.GetValue(screen);
        empty?.GetType().GetMethod("SetTextAutoSize")?.Invoke(empty,
            new object[] { en ? "Searching for LAN games…" : "Buscando partidas na rede local…" });
    }

    public static bool SteamFocus(NJoinFriendScreen __instance, ref Control __result)
    {
        if (__instance.GetNodeOrNull<SteamMultiplayerPanel>("SteamInternetEntry") is not { LanMode: false } panel) return true;
        __result = panel.InitialFocus;
        return false;
    }

    public static void HideOriginalOverlay(NJoinFriendScreen __instance,
        MegaCrit.Sts2.Core.Multiplayer.Connection.IClientConnectionInitializer connInitializer)
    {
        if (connInitializer is MobileSteamInitializer &&
            AccessTools.Field(typeof(NJoinFriendScreen), "_loadingOverlay")?.GetValue(__instance) is Control overlay)
            overlay.Visible = false;
    }

    public static bool AdapterErrorPopup(NetErrorInfo info, ref MegaCrit.Sts2.Core.Nodes.CommonUi.NErrorPopup __result)
    {
        if (info.JoinExtraInfo?.platform != PlatformType.Steam || !ConnectionReport.ConsumePopup(info.JoinExtraInfo.hostId)) return true;
        __result = null;
        return false;
    }
}

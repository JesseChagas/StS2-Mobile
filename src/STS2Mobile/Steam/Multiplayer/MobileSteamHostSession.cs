using System.Text.Json;
using SteamKit2;
using SteamKit2.Internal;
using Sts2.SteamClientPrototype;

namespace STS2Mobile.Steam.Multiplayer;

internal sealed class MobileSteamHostSession : IDisposable
{
    private static Task cleanup = Task.CompletedTask;
    public static Task WaitForCleanupAsync(CancellationToken token) => cleanup.WaitAsync(token);
    public static MobileSteamHostSession Current { get; private set; }
    private readonly SteamConnection connection;
    private readonly IDisposable hold;
    private readonly List<IDisposable> subscriptions = new();
    private readonly SemaphoreSlim availability = new(1, 1);
    private readonly List<(ulong Peer, byte[] Bytes, DateTimeOffset Expires)> pending = new();
    private readonly HashSet<ulong> accepted = new();
    private SteamHostSignaling signaling;
    private Task memberRefresh = Task.CompletedTask;
    private DateTimeOffset refreshAt;
    private volatile string failure;
    private bool disposed, context;
    private int capacity;
    public NativeGns Native { get; private set; }
    public ulong Self { get; private set; }
    public ulong LobbyId { get; private set; }
    public bool Alive => !disposed && connection.IsAuthenticated && failure == null;
    private SteamClient Client => connection.Client;
    private SteamMatchmaking Matchmaking => Client.GetHandler<SteamMatchmaking>();

    public MobileSteamHostSession(string account, string token) {
        connection = SharedSteamConnection.Get(account, token); hold = connection.HoldOpen();
    }
    public async Task OpenAsync(int players, CancellationToken token)
    {
        capacity = Math.Clamp(players, 2, 4);
        ConnectionReport.Step("host-login", "Preparing your Steam host session", "Preparando sua sessão para hospedar na Steam");
        await connection.GetAuthenticatedClientAsync(token); await connection.WaitForFriendsAsync(token);
        Self = Client.SteamID?.ConvertToUInt64() ?? 0;
        if (Self == 0) throw new IOException("A Steam não informou a identidade do anfitrião.");
        subscriptions.Add(connection.Subscribe<SteamUser.LoggedOffCallback>(x => { if (!disposed) failure = "Sessão Steam encerrada: " + x.Result; }));
        subscriptions.Add(connection.Subscribe<SteamUser.PlayingSessionStateCallback>(x => { if (!disposed && x.PlayingBlocked) failure = "A Steam bloqueou o jogo desta conta em outro dispositivo."; }));
        Client.GetHandler<SteamFriends>().SetPersonaState(EPersonaState.Online);
        await GameThread.Switch();
        Native = new NativeGns(Self, SteamFriendLobbyClient.AppId);
        if (Native.LocalAddressCount() <= 0) throw new IOException("Nenhum endereço de rede disponível para hospedar.");
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        ConnectionReport.Step("host-certificate-config", "Validating Steam host identity", "Validando a identidade Steam do anfitrião");
        var config = await http.GetStringAsync("https://api.steampowered.com/ISteamApps/GetSDRConfig/v1/?appid=2868840", token);
        using (var json = JsonDocument.Parse(config)) {
            if (!json.RootElement.TryGetProperty("success", out var success) || !success.GetBoolean()) throw new IOException("Configuração de certificados indisponível.");
            foreach (var cert in json.RootElement.GetProperty("certs").EnumerateArray()) Native.AddAuthority(cert.GetString());
            if (json.RootElement.TryGetProperty("revoked_keys", out var keys)) foreach (var key in keys.EnumerateArray())
                Native.RevokeAuthority(key.ValueKind == JsonValueKind.String ? ulong.Parse(key.GetString()) : key.GetUInt64());
        }
        var activity = new ClientMsgProtobuf<CMsgClientGamesPlayed>(EMsg.ClientGamesPlayed);
        activity.Body.games_played.Add(new CMsgClientGamesPlayed.GamePlayed { game_id = SteamFriendLobbyClient.AppId }); Client.Send(activity); context = true;
        ConnectionReport.Step("host-lobby", "Creating the Steam lobby", "Criando a sala Steam");
        var createJob = Matchmaking.CreateLobby(SteamFriendLobbyClient.AppId, ELobbyType.Private, capacity) ?? throw new IOException("A Steam ainda não disponibilizou os dados da conta.");
        createJob.Timeout = TimeSpan.FromSeconds(25);
        var create = createJob.ToTask();
        SteamMatchmaking.CreateLobbyCallback reply;
        try { reply = await create.WaitAsync(TimeSpan.FromSeconds(25), token); }
        catch {
            _ = LeaveLateLobbyAsync(create); throw;
        }
        if (reply.Result != EResult.OK || reply.AppID != SteamFriendLobbyClient.AppId) throw new IOException("Criação da sala Steam recusada: " + reply.Result);
        LobbyId = reply.LobbySteamID.ConvertToUInt64();
        // A create reply and SteamKit's synthetic owner/member cache are not an
        // entry acknowledgement. Complete the actual membership step before
        // exposing this lobby or accepting peers.
        var entered = await Matchmaking.JoinLobby(SteamFriendLobbyClient.AppId, reply.LobbySteamID)
            .ToTask().WaitAsync(TimeSpan.FromSeconds(12), token);
        if (entered.AppID != SteamFriendLobbyClient.AppId || entered.ChatRoomEnterResponse != EChatRoomEnterResponse.Success
            || entered.Lobby?.SteamID.ConvertToUInt64() != LobbyId
            || entered.Lobby.OwnerSteamID?.ConvertToUInt64() != Self
            || !entered.Lobby.Members.Any(x => x.SteamID.ConvertToUInt64() == Self))
            throw new IOException("A Steam não confirmou a entrada do anfitrião na própria sala.");
        ConnectionReport.Note($"host-lobby-entered={LobbyId}; owner={Self}; members={entered.Lobby.NumMembers}");
        var data = await Matchmaking.GetLobbyData(SteamFriendLobbyClient.AppId, reply.LobbySteamID).ToTask().WaitAsync(TimeSpan.FromSeconds(8), token);
        if (data.AppID != SteamFriendLobbyClient.AppId || data.Lobby.OwnerSteamID?.ConvertToUInt64() != Self) throw new IOException("A Steam não confirmou a propriedade da sala criada.");
        ConnectionReport.Step("host-certificate-request", "Requesting the host network certificate", "Solicitando certificado de rede do anfitrião");
        var certificate = await SteamNetworkJobs.RequestAsync(
            () => Client.GetHandler<SteamNetworking>().RequestNetworkingCertificate(SteamFriendLobbyClient.AppId, Native.PublicKey()),
            () => Alive, "host-network-certificate", token);
        if (certificate.Certificate is not { Length: > 0 } || certificate.CASignature is not { Length: 64 } || certificate.CAKeyID == 0) throw new IOException("Certificado Steam do host ausente ou inválido.");
        ConnectionReport.Step("host-certificate-install", "Validating the signed host certificate", "Validando o certificado assinado do anfitrião");
        Native.InstallCertificate(certificate.Certificate, certificate.CAKeyID, certificate.CASignature);
        if (!Alive) throw new IOException(failure ?? "Sessão Steam interrompida.");
        await GameThread.Switch(); Native.Listen(capacity - 1); signaling = new SteamHostSignaling(Client, Self);
        await SetClosedAsync(false, token);
        await GameThread.Switch(); Current = this;
        ConnectionReport.Step("hosting", "Steam lobby ready for friends", "Sala Steam pronta para os amigos");
        ConnectionReport.Note($"HOST_STEAM_ABERTO: lobby={LobbyId}; owner={Self}; slots={capacity}; native-revision={NativeGns.TransportRevision}");
    }
    private async Task LeaveLateLobbyAsync(Task<SteamMatchmaking.CreateLobbyCallback> create) {
        try { var result = await create; if (result.Result == EResult.OK && result.AppID == SteamFriendLobbyClient.AppId) await Matchmaking.LeaveLobby(SteamFriendLobbyClient.AppId, result.LobbySteamID).ToTask().WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (Exception e) { PatchHelper.Log("[SteamHost] Limpeza de criação tardia: " + e.GetType().Name); }
    }
    public async Task SetClosedAsync(bool closed, CancellationToken token = default) {
        await availability.WaitAsync(token);
        try {
            if (LobbyId == 0 || disposed) return;
            Native?.SetHostClosed(closed);
            var type = closed ? ELobbyType.Private : ELobbyType.FriendsOnly;
            var result = await Matchmaking.SetLobbyData(SteamFriendLobbyClient.AppId, new SteamID(LobbyId), type, capacity)
                .ToTask().WaitAsync(TimeSpan.FromSeconds(8), token);
            if (result.Result != EResult.OK) throw new IOException("A Steam não atualizou a disponibilidade da sala: " + result.Result);
            if (disposed) return;
            var published = await Matchmaking.GetLobbyData(SteamFriendLobbyClient.AppId, new SteamID(LobbyId))
                .ToTask().WaitAsync(TimeSpan.FromSeconds(8), token);
            if (disposed) return;
            var lobby = published.Lobby;
            if (published.AppID != SteamFriendLobbyClient.AppId || lobby?.SteamID.ConvertToUInt64() != LobbyId
                || lobby.OwnerSteamID?.ConvertToUInt64() != Self || lobby.LobbyType != type)
                throw new IOException("A Steam não confirmou a publicação da disponibilidade da sala.");
            // Refresh activity after membership/visibility changes, rather than
            // relying only on the activity sent before this lobby existed.
            var activity = new ClientMsgProtobuf<CMsgClientGamesPlayed>(EMsg.ClientGamesPlayed);
            activity.Body.games_played.Add(new CMsgClientGamesPlayed.GamePlayed { game_id = SteamFriendLobbyClient.AppId });
            Client.Send(activity);
            Client.GetHandler<SteamFriends>().RequestFriendInfo(new SteamID(Self), SteamKitLobbyGateway.RequestedFlags);
            ConnectionReport.Note($"host-lobby-availability={type}; lobby={LobbyId}; members={lobby.NumMembers}; flags={lobby.LobbyFlags}");
            if (!closed) _ = ReportPresenceAsync();
        } finally { availability.Release(); }
    }
    private async Task ReportPresenceAsync() {
        try {
            await Task.Delay(2500);
            if (!Alive) return;
            if (connection.FriendDirectory.TryGetCurrent(Self, out var room))
                ConnectionReport.Note($"host-self-presence: app={room.AppId}; lobby={room.LobbyId}; expected={LobbyId}; matches={room.AppId == SteamFriendLobbyClient.AppId && room.LobbyId == LobbyId}");
            else ConnectionReport.Note("host-self-presence: Steam did not return a complete self presence; visibility on a friend's account is not confirmed.");
        } catch (Exception e) { PatchHelper.Log("[SteamHost] Presença: " + e.GetType().Name); }
    }
    public void MarkAccepted(ulong peer, bool value) { if (value) accepted.Add(peer); else accepted.Remove(peer); }
    public string PlayerName(ulong peer) => peer == Self ? Client.GetHandler<SteamFriends>().GetPersonaName() : Client.GetHandler<SteamFriends>().GetFriendPersonaName(new SteamID(peer));
    public void Pump() {
        if (!Alive) throw new IOException(failure ?? "A conexão Steam do host foi interrompida.");
        while (pending.Count < 256 && signaling.TryReceive(out var incoming)) pending.Add((incoming.Peer, incoming.Bytes, DateTimeOffset.UtcNow.AddSeconds(5)));
        var members = Matchmaking.GetLobby(SteamFriendLobbyClient.AppId, new SteamID(LobbyId))?.Members.Select(x => x.SteamID.ConvertToUInt64()).ToHashSet() ?? new HashSet<ulong>();
        var needsRefresh = false;
        for (var i = pending.Count - 1; i >= 0; i--) {
            var signal = pending[i];
            if (members.Contains(signal.Peer) || accepted.Contains(signal.Peer)) {
                Native.AllowPeer(signal.Peer, true); Native.InputHostSignal(signal.Peer, signal.Bytes); pending.RemoveAt(i);
            } else if (signal.Expires < DateTimeOffset.UtcNow) pending.RemoveAt(i);
            else needsRefresh = true;
        }
        if (needsRefresh && memberRefresh.IsCompleted && DateTimeOffset.UtcNow > refreshAt) {
            refreshAt = DateTimeOffset.UtcNow.AddSeconds(1); memberRefresh = RefreshMembersAsync();
        }
        Native.Pump(); FlushSignals();
    }
    private async Task RefreshMembersAsync() {
        try { await Matchmaking.GetLobbyData(SteamFriendLobbyClient.AppId, new SteamID(LobbyId)).ToTask().WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (Exception e) { PatchHelper.Log("[SteamHost] Consulta de membros: " + e.GetType().Name); }
    }
    private void FlushSignals() { for (var i = 0; i < 256 && Native.PollSignal() is { } signal; i++) signaling.Send(signal.Peer, signal.Bytes); }
    public void Dispose() {
        if (disposed) return; disposed = true; if (ReferenceEquals(Current, this)) Current = null; cleanup = CloseAsync();
    }
    private async Task CloseAsync() {
        try {
            try {
                if (Native != null && signaling != null && Client.IsConnected) {
                    Native.BeginClose();
                    for (var i = 0; i < 8; i++) {
                        for (var n = 0; n < 256 && signaling.TryReceive(out var signal); n++) Native.InputHostSignal(signal.Peer, signal.Bytes);
                        Native.Pump(); FlushSignals(); await Task.Delay(25);
                    }
                }
            } finally { Native?.Dispose(); signaling?.Unregister(); }
            await availability.WaitAsync();
            try {
                if (Client.IsConnected) {
                    try {
                        if (LobbyId != 0) await Matchmaking.LeaveLobby(SteamFriendLobbyClient.AppId, new SteamID(LobbyId)).ToTask().WaitAsync(TimeSpan.FromSeconds(5));
                    } finally {
                        if (context && Client.IsConnected) Client.Send(new ClientMsgProtobuf<CMsgClientGamesPlayed>(EMsg.ClientGamesPlayed));
                    }
                }
            } finally { availability.Release(); }
        } catch (Exception e) { PatchHelper.Log("[SteamHost] Encerramento: " + e.GetType().Name); }
        finally { foreach (var item in subscriptions) item.Dispose(); hold.Dispose(); }
    }
}

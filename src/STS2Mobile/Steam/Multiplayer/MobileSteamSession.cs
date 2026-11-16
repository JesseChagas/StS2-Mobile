using System.Text.Json;
using SteamKit2;
using SteamKit2.Internal;
using Sts2.SteamClientPrototype;
using KitClient = SteamKit2.SteamClient;

namespace STS2Mobile.Steam.Multiplayer;

// Gameplay borrows the same authenticated CM session as the launcher and cloud.
// The CM pump only queues signaling. GNS is driven serially by OpenAsync, then Update.
internal sealed class MobileSteamSession : IDisposable
{
    private static Task lastCleanup = Task.CompletedTask;
    public static Task WaitForCleanupAsync(CancellationToken token) => lastCleanup.WaitAsync(token);
    public static MobileSteamSession Current { get; private set; }
    public KitClient Client => connection.Client;
    private readonly SteamConnection connection;
    private IDisposable connectionHold;
    public NativeGns Native { get; private set; }
    public SteamFriendLobbyClient Lobby { get; }
    private ulong self;
    public ulong Self => self != 0 ? self : Client.SteamID?.ConvertToUInt64() ?? 0;
    public ulong Host { get; private set; }
    public ulong LobbyId { get; private set; }
    public bool Alive => !disposed && connection.IsAuthenticated && failure == null;
    private readonly SteamKitLobbyGateway gateway;
    private readonly List<IDisposable> subscriptions = new();
    private SteamP2PSignaling signaling;
    private volatile bool disposed;
    private bool gameContext;
    private int signalsAccepted, signalsRefused;
    private volatile string failure;

    public MobileSteamSession(string account, string refreshToken, ulong friend)
    {
        connection = SharedSteamConnection.Get(account, refreshToken);
        connectionHold = connection.HoldOpen();
        gateway = new SteamKitLobbyGateway(Client, friend, friendDirectory: connection.FriendDirectory);
        Lobby = new SteamFriendLobbyClient(gateway);
    }

    private void Fail(string message)
    {
        failure = message;
        var error = new IOException(message);
        gateway.ConnectionFailed(message);
        PatchHelper.Log("[SteamMP] " + message);
    }

    public async Task OpenAsync(ulong friend, CancellationToken token)
    {
        var reused = connection.IsAuthenticated;
        ConnectionReport.Step(reused ? "steam-session-reuse" : "steam-session-restore",
            reused ? "Using the launcher's authenticated Steam session" : "Restoring your saved Steam session",
            reused ? "Reutilizando a sessão Steam do launcher" : "Restabelecendo a sessão Steam salva");
        await connection.GetAuthenticatedClientAsync(token);
        subscriptions.Add(connection.Subscribe<SteamUser.LoggedOffCallback>(c => {
            if (!disposed) Fail("Sessão Steam encerrada: " + c.Result);
        }));
        subscriptions.Add(connection.Subscribe<SteamUser.PlayingSessionStateCallback>(c => {
            if (disposed) return;
            ConnectionReport.Note($"steam-playing-blocked={c.PlayingBlocked}; playing-app={c.PlayingAppID}");
            if (!disposed && c.PlayingBlocked)
                Fail("A Steam bloqueou a sessão de jogo desta conta. Encerre o jogo aberto com a mesma conta em outro dispositivo e tente novamente.");
        }));
        subscriptions.Add(connection.Subscribe<KitClient.DisconnectedCallback>(c => {
            if (!disposed && !c.UserInitiated && !Client.IsConnected) Fail("Conexão Steam interrompida.");
        }));
        if (!connection.IsAuthenticated) throw new IOException("A sessão Steam terminou antes de consultar a sala.");
        self = Client.SteamID?.ConvertToUInt64() ?? 0;
        SteamSessionPolicy.ValidateTarget(Self, friend);
        Current = this;
        ConnectionReport.Step("presence", "Loading your friend's presence", "Consultando a presença do amigo");
        Client.GetHandler<SteamFriends>().SetPersonaState(EPersonaState.Online);
        await connection.WaitForFriendsAsync(token);
        ConnectionReport.Step("lobby", "Finding and entering the Steam lobby", "Localizando e entrando na sala Steam");
        var receipt = await Lobby.JoinFriendAsync(friend, token);
        if (receipt.OwnerId != friend)
            throw new IOException("A sala mudou de host. Informe o SteamID64 do host atual.");
        Host = receipt.OwnerId;
        LobbyId = receipt.LobbyId;
        PatchHelper.Log($"[SteamMP] LOBBY_STEAM_ENTROU: {LobbyId}; host={Host}");
        ConnectionReport.Step("native-identity", "Creating the network identity", "Criando a identidade de rede");
        Native = new NativeGns(Self, SteamFriendLobbyClient.AppId);
        ConnectionReport.Step("network-interfaces", "Discovering network addresses", "Descobrindo endereços de rede");
        var addresses = Native.LocalAddressCount();
        ConnectionReport.Note($"native-revision={NativeGns.TransportRevision}; local-addresses={addresses}");
        if (addresses <= 0)
            throw new IOException("Não foi possível descobrir endereços de rede para o transporte ICE. Confirme que o celular está conectado à internet.");
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        ConnectionReport.Step("certificate-config", "Loading Valve certificate authorities", "Carregando autoridades de certificado da Valve");
        var config = await http.GetStringAsync("https://api.steampowered.com/ISteamApps/GetSDRConfig/v1/?appid=2868840", token);
        using (var json = JsonDocument.Parse(config)) {
            var root = json.RootElement;
            if (!root.TryGetProperty("success", out var ok) || !ok.GetBoolean())
                throw new IOException("Configuração pública de certificados indisponível.");
            foreach (var cert in root.GetProperty("certs").EnumerateArray()) Native.AddAuthority(cert.GetString());
            if (root.TryGetProperty("revoked_keys", out var revoked))
                foreach (var key in revoked.EnumerateArray()) Native.RevokeAuthority(
                    key.ValueKind == JsonValueKind.String ? ulong.Parse(key.GetString()) : key.GetUInt64());
        }
        var context = new ClientMsgProtobuf<CMsgClientGamesPlayed>(EMsg.ClientGamesPlayed);
        context.Body.games_played.Add(new CMsgClientGamesPlayed.GamePlayed { game_id = SteamFriendLobbyClient.AppId });
        Client.Send(context);
        gameContext = true;
        ConnectionReport.Step("certificate-request", "Requesting the Steam network certificate", "Solicitando certificado de rede Steam");
        var certReply = await SteamNetworkJobs.RequestAsync(
            () => Client.GetHandler<SteamNetworking>().RequestNetworkingCertificate(SteamFriendLobbyClient.AppId, Native.PublicKey()),
            () => Alive, "client-network-certificate", token);
        if (certReply.Certificate is not { Length: > 0 } || certReply.CASignature is not { Length: 64 } || certReply.CAKeyID == 0)
            throw new IOException("Certificado Steam ausente ou inválido.");
        ConnectionReport.Step("certificate-install", "Validating the signed Steam certificate", "Validando o certificado assinado da Steam");
        Native.InstallCertificate(certReply.Certificate, certReply.CAKeyID, certReply.CASignature);
        if (!Alive) throw new IOException(failure ?? "A sessão Steam terminou antes da conexão P2P.");
        signaling = new SteamP2PSignaling(Client, Self, Host);
        ConnectionReport.Step("p2p", "Connecting directly to the host", "Conectando diretamente ao anfitrião");
        Native.Connect(Host);
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(35);
        try {
          while (DateTimeOffset.UtcNow < deadline) {
            token.ThrowIfCancellationRequested();
            Pump();
            while (Native.PollEvent() is { } state) {
                PatchHelper.Log($"[SteamMP] P2P estado={state.State}; motivo={state.Reason}; {state.Detail}");
                ConnectionReport.Note($"p2p-state={state.State}; reason={state.Reason}; {state.Detail}");
                if (state.State == 3) {
                    PatchHelper.Log("[SteamMP] TRANSPORTE_P2P_CONECTADO: SIM");
                    return;
                }
                if (state.State is 4 or 5) {
                    if (signaling.MessagesObserved == 0)
                        throw new IOException("A Steam não entregou nenhuma resposta de sinalização do host nesta tentativa. Isso não confirma que ele parou de hospedar. Atualize as salas e tente novamente. Detalhe: " + state.Detail);
                    throw new IOException("Conexão P2P encerrada: " + state.Detail);
                }
            }
            await Task.Delay(15, token);
          }
          throw new TimeoutException("Não foi possível obter uma rota P2P direta. Relay SDR ainda não está disponível.");
        } finally {
            ConnectionReport.Note($"signals-sent={signaling.Sent}; cm-observed={signaling.MessagesObserved}; filtered={signaling.MessagesRejected}; received={signaling.Received}; native-accepted={signalsAccepted}; native-refused={signalsRefused}");
        }
    }

    public void Pump()
    {
        if (!Alive) throw new IOException(failure ?? "Sessão Steam indisponível.");
        for (int i = 0; i < 256 && signaling.TryReceive(out var signal); i++) {
            if (Native.InputSignal(signal)) signalsAccepted++;
            else signalsRefused++;
        }
        Native.Pump();
        for (int i = 0; i < 256 && Native.PollSignal() is { } signal; i++) signaling.Send(signal.Peer, signal.Bytes);
    }

    public string PlayerName(ulong id)
    {
        var friends = Client.GetHandler<SteamFriends>();
        return id == Self ? friends.GetPersonaName() : friends.GetFriendPersonaName(new SteamID(id));
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (ReferenceEquals(Current, this)) Current = null;
        // Complete leave before logoff. Never block the Godot thread on an RPC.
        lastCleanup = CloseSteamAsync();
    }

    private async Task CloseSteamAsync()
    {
        try {
            // Keep signaling alive briefly while GNS closes. Destroying the
            // context first discards queued close/ack signals needed by the host.
            try {
                if (Native != null && signaling != null && Client.IsConnected) {
                    Native.BeginClose();
                    for (var pass = 0; pass < 8; pass++) {
                        for (var i = 0; i < 256 && signaling.TryReceive(out var incoming); i++) Native.InputSignal(incoming);
                        Native.Pump();
                        for (var i = 0; i < 256 && Native.PollSignal() is { } outgoing; i++) signaling.Send(outgoing.Peer, outgoing.Bytes);
                        await Task.Delay(25);
                    }
                }
            } catch (Exception e) { PatchHelper.Log("[SteamMP] Fechamento P2P: " + e.GetType().Name); }
            finally { Native?.Dispose(); signaling?.Unregister(); }
            if (Client.IsConnected) {
                if (gameContext) Client.Send(new ClientMsgProtobuf<CMsgClientGamesPlayed>(EMsg.ClientGamesPlayed));
                if (Lobby.HasLobbyMembership) await Lobby.LeaveAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
        } catch (Exception e) { PatchHelper.Log("[SteamMP] Saída da sala: " + e.Message); }
        finally {
            Client.RemoveHandler(gateway);
            foreach (var item in subscriptions) item.Dispose();
            connectionHold?.Dispose(); connectionHold = null;
            // Session ownership remains with the launcher/cloud registry.
            // Leaving a lobby never disconnects or logs off that shared client.
        }
    }
}

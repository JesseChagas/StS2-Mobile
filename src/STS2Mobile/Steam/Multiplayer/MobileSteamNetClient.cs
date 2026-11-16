using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Connection;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Platform;
using STS2Mobile.Patches;

namespace STS2Mobile.Steam.Multiplayer;

// Uses the game's own NetClientGameService, HandshakeManager and message bus.
// No packet IDs, version answers, or gameplay messages are fabricated here.
internal sealed class MobileSteamNetClient : NetClient
{
    private bool claimed, connected, closed;
    private MobileSteamSession session;
    public override bool IsConnected => connected && !closed;
    public override ulong NetId => session?.Self ?? 0;
    public override ulong HostNetId => session?.Host ?? 0;
    public override string GetRawLobbyIdentifier() => session?.LobbyId is > 0 ? session.LobbyId.ToString() : null;

    public MobileSteamNetClient(INetClientHandler handler) : base(handler) { }

    public async Task<NetErrorInfo?> ConnectAsync(ulong friend, CancellationToken token)
    {
        if (!SteamTransportOwnership.TryClaim()) {
            ConnectionReport.Fail(new InvalidOperationException("Another connection is still active. Try again after it closes."));
            return new NetErrorInfo(NetError.TryAgainLater, false);
        }
        claimed = true;
        try {
            if (string.IsNullOrEmpty(LauncherPatches.SavedAccountName) || string.IsNullOrEmpty(LauncherPatches.SavedRefreshToken))
                throw new InvalidOperationException("Entre na sua conta Steam no launcher antes de conectar.");
            await MobileSteamSession.WaitForCleanupAsync(token);
            await MobileSteamHostSession.WaitForCleanupAsync(token);
            await GameThread.Switch();
            ConnectionReport.Step("android-library", "Loading Android networking library", "Carregando biblioteca de rede Android");
            AndroidNetworkingLibrary.Prepare();
            ConnectionReport.Step("game-version", "Checking installed game metadata", "Conferindo os metadados do jogo instalado");
            var gameVersion = MegaCrit.Sts2.Core.Nodes.NGame.GetGameVersion();
            ConnectionReport.Note("installed-game-version=" + gameVersion + "; installed-branch=" + (InstalledReleaseMetadata.InstalledBranch(Godot.OS.GetDataDir()) ?? "unknown"));
            if (string.IsNullOrWhiteSpace(gameVersion) || gameVersion == "UNKNOWN")
                throw new IOException("Não foi possível ler a versão instalada. No launcher, use Mais opções → Verificar arquivos do jogo e depois Reiniciar para jogar.");
            session = new MobileSteamSession(LauncherPatches.SavedAccountName, LauncherPatches.SavedRefreshToken, friend);
            await session.OpenAsync(friend, token);
            await GameThread.Switch();
            token.ThrowIfCancellationRequested();
            connected = true;
            ConnectionReport.Step("handshake", "Verifying game compatibility", "Verificando a compatibilidade do jogo");
            // Must happen before delivering the host's queued version packet.
            _handler.OnConnectedToHost();
            PatchHelper.Log("[SteamMP] Handshake original iniciado; aguardando confirmação do jogo.");
            return null;
        } catch (Exception e) {
            await GameThread.Switch();
            PatchHelper.Log("[SteamMP] Falha ao conectar: " + e.Message);
            if (e is OperationCanceledException && token.IsCancellationRequested) {
                ConnectionReport.Cancelled();
                Release();
                throw;
            }
            ConnectionReport.Fail(e);
            Release();
            return new NetErrorInfo(e is TimeoutException or OperationCanceledException ? NetError.Timeout : NetError.UnknownNetworkError, false);
        }
    }

    public override void Update()
    {
        if (!IsConnected) return;
        try {
            session.Pump();
            while (session.Native.PollEvent() is { } state) {
                if (state.State is 4 or 5) {
                    PatchHelper.Log($"[SteamMP] Host desconectou: {state.Reason}; {state.Detail}");
                    End(new NetErrorInfo((MegaCrit.Sts2.Core.Platform.Steam.SteamDisconnectionReason)state.Reason, state.Detail, false));
                    return;
                }
            }
            // A bounded batch keeps the main thread responsive; preserve datagrams intact.
            for (int i = 0; i < 256 && IsConnected && session.Native.Receive() is { } packet; i++) {
                var mode = (packet.Flags & 8) != 0 ? NetTransferMode.Reliable : NetTransferMode.Unreliable;
                _handler.OnPacketReceived(HostNetId, packet.Bytes, mode, packet.Channel);
            }
        } catch (Exception e) {
            PatchHelper.Log("[SteamMP] Transporte interrompido: " + e.Message);
            ConnectionReport.Fail(e, false);
            End(new NetErrorInfo(NetError.UnknownNetworkError, false));
        }
    }

    public override void SendMessageToHost(byte[] bytes, int length, NetTransferMode mode, int channel = 0)
    {
        if (!IsConnected) throw new IOException("Transporte Steam não está conectado.");
        if (bytes == null || length <= 0 || length > bytes.Length || length > 524288)
            throw new ArgumentOutOfRangeException(nameof(length));
        // SteamUtil in this game uses flags 8/0 and ignores the channel on send.
        // Copy only the active writer bytes, never its unused backing capacity.
        var payload = bytes.AsSpan(0, length).ToArray();
        session.Native.Send(payload, mode switch {
            NetTransferMode.Reliable => 8,
            NetTransferMode.Unreliable => 0,
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        });
    }

    public override void DisconnectFromHost(NetError reason, bool now = false) => End(new NetErrorInfo(reason, true));

    private void End(NetErrorInfo info)
    {
        if (closed) return;
        var host = HostNetId;
        var notify = connected;
        Release();
        if (notify) _handler.OnDisconnectedFromHost(host, info);
    }

    private void Release()
    {
        connected = false;
        closed = true;
        try { session?.Dispose(); }
        finally { if (claimed) { claimed = false; SteamTransportOwnership.Release(); } }
    }
}

internal sealed class MobileSteamInitializer(ulong host) : IClientConnectionInitializer
{
    public PlatformType Platform => PlatformType.Steam;
    public ulong? GetHostId() => host;
    public Task<NetErrorInfo?> Connect(INetClientGameService gameService, CancellationToken cancelToken = default)
    {
        if (gameService.IsConnected) throw new InvalidOperationException("Já existe uma partida conectada.");
        var transport = new MobileSteamNetClient(gameService);
        gameService.Initialize(transport, PlatformType.Steam);
        return transport.ConnectAsync(host, cancelToken);
    }
    public override string ToString() => $"Steam Android host={host}";
}

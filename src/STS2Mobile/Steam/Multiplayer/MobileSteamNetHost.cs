using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Platform.Steam;
using STS2Mobile.Patches;

namespace STS2Mobile.Steam.Multiplayer;

internal sealed class MobileSteamNetHost : NetHost
{
    private readonly HashSet<ulong> peers = new();
    private MobileSteamHostSession session;
    private bool connected, stopped, claimed;
    public override IEnumerable<ulong> ConnectedPeerIds => peers.ToArray();
    public override bool IsConnected => connected && !stopped;
    public override ulong NetId => session?.Self ?? 0;
    public override string GetRawLobbyIdentifier() => session?.LobbyId is > 0 ? session.LobbyId.ToString() : null;
    public MobileSteamNetHost(INetHostHandler handler) : base(handler) { }
    public async Task<NetErrorInfo?> StartAsync(int players) {
        if (!SteamTransportOwnership.TryClaim()) return new NetErrorInfo(NetError.TryAgainLater, false);
        claimed = true;
        ConnectionReport.Begin(0);
        try {
            await MobileSteamSession.WaitForCleanupAsync(CancellationToken.None);
            await MobileSteamHostSession.WaitForCleanupAsync(CancellationToken.None);
            await GameThread.Switch(); AndroidNetworkingLibrary.Prepare();
            var version = MegaCrit.Sts2.Core.Nodes.NGame.GetGameVersion();
            if (version == "UNKNOWN") throw new IOException("Verifique os arquivos do jogo no launcher para carregar a versão antes de hospedar.");
            session = new MobileSteamHostSession(LauncherPatches.SavedAccountName, LauncherPatches.SavedRefreshToken);
            await session.OpenAsync(players, CancellationToken.None);
            await GameThread.Switch(); connected = true; return null;
        } catch (Exception e) {
            await GameThread.Switch(); ConnectionReport.Fail(e, false); Release();
            return new NetErrorInfo(NetError.UnknownNetworkError, false);
        }
    }
    public override void Update() {
        if (!IsConnected) return;
        try {
            session.Pump();
            while (session.Native.PollHostEvent() is { } state) {
                ConnectionReport.Note($"host-peer={state.Peer}; state={state.State}; reason={state.Reason}");
                if (state.State == 3 && peers.Add(state.Peer)) {
                    session.MarkAccepted(state.Peer, true); _handler.OnPeerConnected(state.Peer);
                } else if (state.State is 4 or 5) {
                    session.Native.ClosePeer(state.Peer, state.Reason == 0 ? 1000 : state.Reason, state.Detail, false);
                    session.MarkAccepted(state.Peer, false);
                    if (peers.Remove(state.Peer)) _handler.OnPeerDisconnected(state.Peer, new NetErrorInfo((SteamDisconnectionReason)state.Reason, state.Detail, false));
                }
            }
            for (var i = 0; i < 256 && IsConnected && session.Native.ReceiveFromPeer() is { } packet; i++) {
                if (peers.Contains(packet.Peer)) _handler.OnPacketReceived(packet.Peer, packet.Bytes, (packet.Flags & 8) != 0 ? NetTransferMode.Reliable : NetTransferMode.Unreliable, packet.Channel);
            }
        } catch (Exception e) {
            ConnectionReport.Fail(e, false); var error = new NetErrorInfo(NetError.UnknownNetworkError, false); Release(); _handler.OnDisconnected(error);
        }
    }
    public override void SetHostIsClosed(bool closed) {
        if (!IsConnected) return;
        _ = SetClosedAsync(closed);
    }
    private async Task SetClosedAsync(bool closed) {
        try { await session.SetClosedAsync(closed); }
        catch (Exception e) { PatchHelper.Log("[SteamHost] Disponibilidade: " + e.GetType().Name); }
    }
    public override void SendMessageToClient(ulong peer, byte[] bytes, int length, NetTransferMode mode, int channel = 0) {
        if (!IsConnected || !peers.Contains(peer)) return;
        if (bytes == null || length <= 0 || length > bytes.Length || length > 524288) throw new ArgumentOutOfRangeException(nameof(length));
        session.Native.SendToPeer(peer, bytes.AsSpan(0, length).ToArray(), mode == NetTransferMode.Reliable ? 8 : 0);
    }
    public override void SendMessageToAll(byte[] bytes, int length, NetTransferMode mode, int channel = 0) {
        foreach (var peer in peers.ToArray()) SendMessageToClient(peer, bytes, length, mode, channel);
    }
    public override void DisconnectClient(ulong peer, NetError reason, bool now = false, string debugReason = null) {
        if (session == null) return;
        session.Native.ClosePeer(peer, (int)reason.ToSteam(), debugReason, !now); session.MarkAccepted(peer, false);
        if (peers.Remove(peer)) _handler.OnPeerDisconnected(peer, new NetErrorInfo(reason, true));
    }
    public override void StopHost(NetError reason, bool now = false) {
        if (stopped) return; var notify = connected; Release();
        if (notify) _handler.OnDisconnected(new NetErrorInfo(reason, true));
    }
    private void Release() {
        stopped = true; connected = false; peers.Clear();
        try { session?.Dispose(); }
        finally { if (claimed) { claimed = false; SteamTransportOwnership.Release(); } }
    }
}

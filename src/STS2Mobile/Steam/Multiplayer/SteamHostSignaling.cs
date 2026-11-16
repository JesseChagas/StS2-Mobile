using System.Collections.Concurrent;
using SteamKit2;
using SteamKit2.Internal;
using Sts2.SteamClientPrototype;

namespace STS2Mobile.Steam.Multiplayer;

internal sealed class SteamHostSignaling : ClientMsgHandler
{
    private readonly SteamClient client;
    private readonly ulong self;
    private readonly ConcurrentQueue<(ulong Peer, byte[] Bytes)> incoming = new();
    private int queued;
    public int Sent { get; private set; }
    public int Observed { get; private set; }
    public SteamHostSignaling(SteamClient client, ulong self) { this.client = client; this.self = self; client.AddHandler(this); }
    public override void HandleMsg(IPacketMsg packet) {
        if (packet.MsgType != EMsg.ClientP2PConnectionInfo) return;
        Observed++;
        var body = new ClientMsgProtobuf<CMsgClientP2PConnectionInfo>(packet).Body;
        if (body.steam_id_src == 0 || body.steam_id_src == self || (body.steam_id_dest != 0 && body.steam_id_dest != self) ||
            (body.app_id != SteamFriendLobbyClient.AppId && !(body.app_id == 0 && body.app_id_secondary == SteamFriendLobbyClient.AppId)) ||
            body.rendezvous == null || body.rendezvous.Length is <= 0 or > 131072) return;
        if (Interlocked.Increment(ref queued) > 256) { Interlocked.Decrement(ref queued); return; }
        incoming.Enqueue((body.steam_id_src, (byte[])body.rendezvous.Clone()));
    }
    public bool TryReceive(out (ulong Peer, byte[] Bytes) signal) {
        if (!incoming.TryDequeue(out signal)) return false;
        Interlocked.Decrement(ref queued); return true;
    }
    public void Send(ulong peer, byte[] bytes) {
        if (peer == 0 || peer == self || bytes.Length is <= 0 or > 131072) throw new InvalidDataException("Destino de sinal de host inválido.");
        var message = new ClientMsgProtobuf<CMsgClientP2PConnectionInfo>(EMsg.ClientP2PConnectionInfo);
        message.ProtoHeader.routing_appid = SteamFriendLobbyClient.AppId;
        message.Body.steam_id_dest = peer; message.Body.app_id = SteamFriendLobbyClient.AppId; message.Body.rendezvous = bytes;
        client.Send(message); Sent++;
    }
    public void Unregister() => client.RemoveHandler(this);
}

using System.Collections.Concurrent;
using SteamKit2;
using SteamKit2.Internal;

namespace Sts2.SteamClientPrototype;

public sealed class SteamP2PSignaling : ClientMsgHandler
{
    private readonly SteamClient client;
    private readonly ulong self,
        peer;
    private readonly ConcurrentQueue<byte[]> incoming = new();
    private int queued;
    private int messages,
        rejected;
    public int MessagesObserved => Volatile.Read(ref messages);
    public int MessagesRejected => Volatile.Read(ref rejected);
    public int Sent { get; private set; }
    public int Received { get; private set; }

    public SteamP2PSignaling(SteamClient client, ulong self, ulong peer)
    {
        this.client = client;
        this.self = self;
        this.peer = peer;
        client.AddHandler(this);
    }

    public void Send(ulong destination, byte[] bytes)
    {
        if (destination != peer || bytes.Length is <= 0 or > 131072)
            throw new InvalidDataException("Destino ou tamanho de sinal inválido.");
        var msg = new ClientMsgProtobuf<CMsgClientP2PConnectionInfo>(EMsg.ClientP2PConnectionInfo);
        msg.ProtoHeader.routing_appid = SteamFriendLobbyClient.AppId;
        msg.Body.steam_id_dest = peer;
        msg.Body.app_id = SteamFriendLobbyClient.AppId;
        msg.Body.rendezvous = bytes;
        client.Send(msg);
        Sent++;
    }

    public override void HandleMsg(IPacketMsg packet)
    {
        if (packet.MsgType != EMsg.ClientP2PConnectionInfo)
            return;
        Interlocked.Increment(ref messages);
        var body = new ClientMsgProtobuf<CMsgClientP2PConnectionInfo>(packet).Body;
        bool appMatches =
            body.app_id == SteamFriendLobbyClient.AppId
            || (body.app_id == 0 && body.app_id_secondary == SteamFriendLobbyClient.AppId);
        if (
            !appMatches
            || body.steam_id_src != peer
            || (body.steam_id_dest != 0 && body.steam_id_dest != self)
            || body.rendezvous == null
            || body.rendezvous.Length is <= 0 or > 131072
        )
        {
            Interlocked.Increment(ref rejected);
            return;
        }
        if (Interlocked.Increment(ref queued) > 256)
        {
            Interlocked.Decrement(ref queued);
            return;
        }
        incoming.Enqueue((byte[])body.rendezvous.Clone());
    }

    public bool TryReceive(out byte[] signal)
    {
        if (incoming.TryDequeue(out signal!))
        {
            Interlocked.Decrement(ref queued);
            Received++;
            return true;
        }
        return false;
    }

    public void Unregister() => client.RemoveHandler(this);
}

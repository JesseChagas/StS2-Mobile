#nullable enable
using SteamKit2;
using SteamKit2.Internal;

namespace Sts2.SteamClientPrototype;

public record FriendLobbyAnnouncement(ulong FriendId, uint AppId, ulong LobbyId, DateTimeOffset UpdatedAt);

// Session-owned: initial persona packets must survive individual join attempts.
// Persona state is not proof that an advertised lobby is unavailable.
public sealed class SteamFriendDirectory : ClientMsgHandler
{
    private readonly object gate = new();
    private readonly Dictionary<ulong, FriendPresenceCache> current = new();
    private readonly Dictionary<ulong, FriendLobbyAnnouncement> announced = new();
    public SteamFriendDirectory(SteamClient client) => client.AddHandler(this);

    public void Reset() { lock (gate) { current.Clear(); announced.Clear(); } }

    public bool TryGetCurrent(ulong id, out FriendRoom room)
    {
        lock (gate) {
            room = new FriendRoom(id, 0, 0);
            return current.TryGetValue(id, out var cache) && cache.UpdatedAt.HasValue &&
                DateTimeOffset.UtcNow - cache.UpdatedAt.Value < TimeSpan.FromSeconds(30) && cache.TryGetRoom(out room);
        }
    }

    // A past advertisement is only a candidate: callers must query lobby data
    // and validate its owner before using it. Never label it an active host here.
    public FriendLobbyAnnouncement[] Candidates(TimeSpan maxAge)
    {
        lock (gate) return announced.Values.Where(x => DateTimeOffset.UtcNow - x.UpdatedAt <= maxAge).ToArray();
    }

    public override void HandleMsg(IPacketMsg packet)
    {
        if (packet.MsgType != EMsg.ClientPersonaState) return;
        var body = new ClientMsgProtobuf<CMsgClientPersonaState>(packet).Body;
        var flags = (EClientPersonaStateFlag)body.status_flags;
        lock (gate) {
            foreach (var person in body.friends) {
                if (!current.TryGetValue(person.friendid, out var cache))
                    current[person.friendid] = cache = new FriendPresenceCache(person.friendid);
                if (!cache.Apply(person.friendid, flags.HasFlag(EClientPersonaStateFlag.GameDataBlob),
                    flags.HasFlag(EClientPersonaStateFlag.GameExtraInfo),
                    person.ShouldSerializegame_played_app_id() ? person.game_played_app_id : null,
                    person.ShouldSerializegameid() ? person.gameid : null,
                    person.ShouldSerializegame_lobby_id() ? person.game_lobby_id : null)) continue;
                if (cache.TryGetRoom(out var room)) {
                    if (room.AppId == SteamFriendLobbyClient.AppId && room.LobbyId != 0)
                        announced[room.FriendId] = new(room.FriendId, room.AppId, room.LobbyId, DateTimeOffset.UtcNow);
                    else if (room.AppId != 0 && room.AppId != SteamFriendLobbyClient.AppId)
                        announced.Remove(person.friendid);
                }
            }
        }
    }
}

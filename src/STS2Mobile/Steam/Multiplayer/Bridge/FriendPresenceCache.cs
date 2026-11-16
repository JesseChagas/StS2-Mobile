namespace Sts2.SteamClientPrototype;

// A persona message is a delta. Missing fields only mean zero if their entire
// group was requested/present in status_flags; name-only messages erase nothing.
public sealed class FriendPresenceCache(ulong friendId)
{
    private uint? app;
    private ulong? lobby;
    public DateTimeOffset? UpdatedAt { get; private set; }
    public int Updates { get; private set; }

    public bool Apply(
        ulong id,
        bool gameGroup,
        bool lobbyGroup,
        uint? appId,
        ulong? gameId,
        ulong? lobbyId
    )
    {
        if (id != friendId)
            return false;
        uint? nextApp =
            appId
            ?? (
                gameId.HasValue ? (uint)(gameId.Value & 0xFFFFFF)
                : gameGroup ? 0u
                : null
            );
        ulong? nextLobby = lobbyId ?? (lobbyGroup ? 0UL : null);
        if (!nextApp.HasValue && !nextLobby.HasValue)
            return false;
        if (nextApp.HasValue)
        {
            if (app.HasValue && app != nextApp)
                lobby = null;
            app = nextApp;
            if (app == 0)
                lobby = 0;
        }
        if (nextLobby.HasValue)
            lobby = nextLobby;
        UpdatedAt = DateTimeOffset.UtcNow;
        Updates++;
        return true;
    }

    public bool TryGetRoom(out FriendRoom room)
    {
        room = new FriendRoom(friendId, app ?? 0, lobby ?? 0);
        // A lobby without a reported game may precede its game-data delta.
        return app.HasValue && lobby.HasValue && !(app == 0 && lobby != 0);
    }

    public string Describe() =>
        $"app={(app.HasValue ? app.Value.ToString() : "não informado")}; lobby={(lobby.HasValue ? lobby.Value.ToString() : "não informado")}; atualizações={Updates}";
}

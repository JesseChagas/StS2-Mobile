using SteamKit2;
using Sts2.SteamClientPrototype;

namespace STS2Mobile.Steam.Multiplayer;

public record SteamHostRoom(ulong HostId, ulong LobbyId, string Name);

internal static class SteamFriendLobbyBrowser
{
    public static async Task<SteamHostRoom[]> RefreshAsync(string account, string token, CancellationToken cancelled)
    {
        var connection = SharedSteamConnection.Get(account, token);
        using var hold = connection.HoldOpen();
        var client = await connection.GetAuthenticatedClientAsync(cancelled);
        await connection.WaitForFriendsAsync(cancelled);
        var friends = client.GetHandler<SteamFriends>();
        friends.SetPersonaState(EPersonaState.Online);
        var ids = Enumerable.Range(0, friends.GetFriendCount()).Select(friends.GetFriendByIndex)
            .Where(id => friends.GetFriendRelationship(id) == EFriendRelationship.Friend).ToArray();
        if (ids.Length == 0) return Array.Empty<SteamHostRoom>();
        foreach (var batch in ids.Chunk(100)) friends.RequestFriendInfo(batch, SteamKitLobbyGateway.RequestedFlags);
        await Task.Delay(1800, cancelled);
        var friendSet = ids.Select(x => x.ConvertToUInt64()).ToHashSet();
        if (!connection.FriendDirectory.Candidates(TimeSpan.FromMinutes(2)).Any(x => friendSet.Contains(x.FriendId))) {
            foreach (var batch in ids.Chunk(100)) friends.RequestFriendInfo(batch, SteamKitLobbyGateway.RequestedFlags);
            await Task.Delay(1200, cancelled);
        }
        var candidates = connection.FriendDirectory.Candidates(TimeSpan.FromMinutes(2))
            .Where(x => friendSet.Contains(x.FriendId)).GroupBy(x => x.LobbyId).Select(g => g.First()).ToArray();
        var matchmaking = client.GetHandler<SteamMatchmaking>();
        using var throttle = new SemaphoreSlim(4);
        var rooms = await Task.WhenAll(candidates.Select(async candidate => {
            await throttle.WaitAsync(cancelled);
            try {
                var reply = await matchmaking.GetLobbyData(SteamFriendLobbyClient.AppId, new SteamID(candidate.LobbyId))
                    .ToTask().WaitAsync(TimeSpan.FromSeconds(6), cancelled);
                var owner = reply.Lobby?.OwnerSteamID?.ConvertToUInt64() ?? 0;
                if (reply.AppID != SteamFriendLobbyClient.AppId || reply.Lobby?.SteamID.ConvertToUInt64() != candidate.LobbyId || !friendSet.Contains(owner)) return null;
                if (reply.Lobby.MaxMembers > 0 && reply.Lobby.NumMembers >= reply.Lobby.MaxMembers) return null;
                var name = friends.GetFriendPersonaName(new SteamID(owner));
                if (name != null) name = name.Replace('\n', ' ').Replace('\r', ' ');
                return new SteamHostRoom(owner, candidate.LobbyId, string.IsNullOrWhiteSpace(name) ? owner.ToString() : name);
            } catch (Exception) when (!cancelled.IsCancellationRequested) { return null; }
            finally { throttle.Release(); }
        }));
        cancelled.ThrowIfCancellationRequested();
        return rooms.Where(x => x != null).DistinctBy(x => x.HostId).OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }
}

#nullable enable
using SteamKit2;
using SteamKit2.Internal;
using Sts2.SteamClientPrototype;

// Register BEFORE connecting so initial persona/game information is retained.
sealed class SteamKitLobbyGateway : ClientMsgHandler, IFriendLobbyGateway
{
    public const EClientPersonaStateFlag RequestedFlags =
        EClientPersonaStateFlag.Status
        | EClientPersonaStateFlag.PlayerName
        | EClientPersonaStateFlag.SourceID
        | EClientPersonaStateFlag.Presence
        | EClientPersonaStateFlag.GameExtraInfo
        | EClientPersonaStateFlag.GameDataBlob;
    private readonly SteamMatchmaking matchmaking;
    private readonly SteamFriends friends;
    private readonly object gate = new();
    private readonly ulong targetFriend;
    private readonly ulong? explicitLobby;
    private readonly FriendPresenceCache presence;
    private readonly SteamFriendDirectory? directory;
    private TaskCompletionSource<FriendRoom>? pending;
    private bool responded;
    private string? connectionError;

    private static bool CanJoin(FriendRoom room) =>
        room.AppId == SteamFriendLobbyClient.AppId && room.LobbyId != 0;

    public SteamKitLobbyGateway(SteamClient client, ulong friendId, ulong? lobby = null, SteamFriendDirectory? friendDirectory = null)
    {
        directory = friendDirectory;
        targetFriend = friendId;
        explicitLobby = lobby;
        presence = new FriendPresenceCache(friendId);
        if (directory?.TryGetCurrent(friendId, out var initial) == true)
            presence.Apply(friendId, true, true, initial.AppId, null, initial.LobbyId);
        matchmaking = client.GetHandler<SteamMatchmaking>()!;
        friends = client.GetHandler<SteamFriends>()!;
        client.AddHandler(this);
    }

    public void ConnectionFailed(string reason)
    {
        lock (gate)
        {
            connectionError = reason;
            pending?.TrySetException(
                new IOException("[PRESENÇA] Conexão Steam interrompida: " + reason)
            );
        }
    }

    public async Task<FriendRoom> FindFriendRoomAsync(
        ulong friendId,
        CancellationToken cancellationToken
    )
    {
        if (friendId != targetFriend)
            throw new ArgumentException("Amigo diferente do configurado.");
        var id = new SteamID(friendId);
        if (friends.GetFriendRelationship(id) != EFriendRelationship.Friend)
            throw new InvalidOperationException(
                "[AMIZADE] Esse SteamID não está na lista de amigos da conta conectada."
            );
        if (explicitLobby.HasValue)
        {
            Console.WriteLine(
                "[PRESENÇA] Usando o lobby fornecido no convite; o host será validado após a resposta da Steam."
            );
            lock (gate)
                if (connectionError != null)
                    throw new IOException(
                        "[SESSÃO] Conexão Steam indisponível: " + connectionError
                    );
            return new FriendRoom(friendId, SteamFriendLobbyClient.AppId, explicitLobby.Value);
        }
        var completion = new TaskCompletionSource<FriendRoom>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        bool hasCached;
        FriendRoom cached;
        lock (gate)
        {
            if (connectionError != null)
                throw new IOException("[SESSÃO] Conexão Steam indisponível: " + connectionError);
            hasCached =
                presence.TryGetRoom(out cached)
                && CanJoin(cached)
                && presence.UpdatedAt.HasValue
                && DateTimeOffset.UtcNow - presence.UpdatedAt.Value < TimeSpan.FromSeconds(30);
            pending = completion;
            responded = false;
        }
        try
        {
            Console.WriteLine(
                "[PRESENÇA] Solicitando estado do amigo, incluindo Presence e GameExtraInfo."
            );
            friends.RequestFriendInfo(id, RequestedFlags);
            // Zero/empty presence may be temporary or hidden. Do not conclude
            // "not playing" from the first such notification; allow updates.
            var firstWait = Task.Delay(
                hasCached ? TimeSpan.FromSeconds(2) : TimeSpan.FromSeconds(4),
                cancellationToken
            );
            await Task.WhenAny(completion.Task, firstWait);
            cancellationToken.ThrowIfCancellationRequested();
            if (completion.Task.IsCompleted)
                return await completion.Task;
            if (!hasCached)
            {
                Console.WriteLine(
                    "[PRESENÇA] Ainda sem sala válida; aguardando atualização e consultando novamente."
                );
                friends.RequestFriendInfo(id, RequestedFlags);
                await Task.WhenAny(
                    completion.Task,
                    Task.Delay(TimeSpan.FromSeconds(8), cancellationToken)
                );
                cancellationToken.ThrowIfCancellationRequested();
                if (completion.Task.IsCompleted)
                    return await completion.Task;
            }
            lock (gate)
            {
                if (
                    presence.TryGetRoom(out var current)
                    && presence.UpdatedAt.HasValue
                    && DateTimeOffset.UtcNow - presence.UpdatedAt.Value < TimeSpan.FromSeconds(30)
                )
                {
                    Console.WriteLine(
                        "[PRESENÇA] Usando o estado inicial/atual já recebido: "
                            + presence.Describe()
                    );
                    if (CanJoin(current)) return current;
                }
            }
            // Empty/offline presence after a failed join is not an authoritative
            // lobby close. Revalidate a recent advertisement with matchmaking.
            var recent = directory?.Candidates(TimeSpan.FromMinutes(2)).FirstOrDefault(x => x.FriendId == friendId);
            if (recent != null) {
                try {
                    var data = await matchmaking.GetLobbyData(SteamFriendLobbyClient.AppId, new SteamID(recent.LobbyId))
                        .ToTask().WaitAsync(TimeSpan.FromSeconds(6), cancellationToken);
                    if (data.AppID == SteamFriendLobbyClient.AppId && data.Lobby?.SteamID.ConvertToUInt64() == recent.LobbyId &&
                        data.Lobby.OwnerSteamID?.ConvertToUInt64() == friendId)
                        return new FriendRoom(friendId, SteamFriendLobbyClient.AppId, recent.LobbyId);
                } catch (Exception) when (!cancellationToken.IsCancellationRequested) { }
            }
            lock (gate) {
                if (connectionError != null) throw new IOException("[PRESENÇA] A conexão Steam foi interrompida durante a consulta: " + connectionError);
            }
            throw new TimeoutException("[PRESENÇA] A Steam não confirmou uma sala acessível nesta consulta. Isso não prova que o amigo saiu do jogo. Atualize a lista e tente novamente. " + presence.Describe() + "; resposta=" + responded);
        }
        finally
        {
            lock (gate)
                pending = null;
        }
    }

    public override void HandleMsg(IPacketMsg packetMsg)
    {
        if (packetMsg.MsgType != EMsg.ClientPersonaState)
            return;
        var body = new ClientMsgProtobuf<CMsgClientPersonaState>(packetMsg).Body;
        var flags = (EClientPersonaStateFlag)body.status_flags;
        lock (gate)
        {
            foreach (var friend in body.friends)
            {
                if (friend.friendid != targetFriend)
                    continue;
                if (pending != null)
                    responded = true;
                var changed = presence.Apply(
                    friend.friendid,
                    flags.HasFlag(EClientPersonaStateFlag.GameDataBlob),
                    flags.HasFlag(EClientPersonaStateFlag.GameExtraInfo),
                    friend.ShouldSerializegame_played_app_id() ? friend.game_played_app_id : null,
                    friend.ShouldSerializegameid() ? friend.gameid : null,
                    friend.ShouldSerializegame_lobby_id() ? friend.game_lobby_id : null
                );
                if (pending == null)
                    continue;
                var state = flags.HasFlag(EClientPersonaStateFlag.Presence)
                    ? ((EPersonaState)friend.persona_state).ToString()
                    : "não informado";
                Console.WriteLine(
                    $"[PRESENÇA] estado={state}; flags={flags}; {presence.Describe()}; campos app/game/lobby explícitos={friend.ShouldSerializegame_played_app_id()}/{friend.ShouldSerializegameid()}/{friend.ShouldSerializegame_lobby_id()}"
                );
                if (changed && presence.TryGetRoom(out var room) && CanJoin(room))
                    pending.TrySetResult(room);
            }
        }
    }

    public async Task<LobbyReply> JoinLobbyAsync(ulong lobbyId)
    {
        Console.WriteLine("[LOBBY] Enviando JoinLobby...");
        var job =
            matchmaking.JoinLobby(SteamFriendLobbyClient.AppId, new SteamID(lobbyId))
            ?? throw new InvalidOperationException(
                "[LOBBY] A Steam ainda não disponibilizou o nome da conta."
            );
        var reply = await job.ToTask();
        Console.WriteLine("[LOBBY] Resposta: " + reply.ChatRoomEnterResponse);
        var lobby = reply.Lobby;
        if (
            reply.ChatRoomEnterResponse == EChatRoomEnterResponse.Success
            && lobby != null
            && (lobby.OwnerSteamID?.ConvertToUInt64() ?? 0) == 0
        )
        {
            Console.WriteLine("[LOBBY] Consultando proprietário da sala...");
            var data = await matchmaking
                .GetLobbyData(SteamFriendLobbyClient.AppId, lobby.SteamID)
                .ToTask();
            if (
                data.AppID != SteamFriendLobbyClient.AppId
                || data.Lobby.SteamID.ConvertToUInt64() != lobbyId
            )
                throw new InvalidDataException("[LOBBY] Metadados de outra sala.");
            lobby = data.Lobby;
        }
        if (
            explicitLobby.HasValue
            && reply.ChatRoomEnterResponse == EChatRoomEnterResponse.Success
            && lobby?.OwnerSteamID?.ConvertToUInt64() != targetFriend
        )
            throw new InvalidDataException(
                "[LOBBY] O proprietário da sala do convite não é o amigo informado."
            );
        return new LobbyReply(
            reply.AppID,
            lobby?.SteamID.ConvertToUInt64() ?? 0,
            lobby?.OwnerSteamID?.ConvertToUInt64() ?? 0,
            reply.ChatRoomEnterResponse == EChatRoomEnterResponse.Success,
            reply.ChatRoomEnterResponse.ToString()
        );
    }

    public async Task LeaveLobbyAsync(ulong lobbyId)
    {
        lock (gate)
            if (connectionError != null)
                throw new IOException(
                    "[LOBBY] Sessão Steam indisponível; não foi possível confirmar saída: "
                        + connectionError
                );
        var reply = await matchmaking
            .LeaveLobby(SteamFriendLobbyClient.AppId, new SteamID(lobbyId))
            .ToTask();
        if (reply.Result != EResult.OK)
            throw new InvalidOperationException("Falha ao sair do lobby: " + reply.Result);
    }
}

namespace Sts2.SteamClientPrototype;

public record FriendRoom(ulong FriendId, uint AppId, ulong LobbyId);

public record LobbyReply(uint AppId, ulong LobbyId, ulong OwnerId, bool Accepted, string Result);

public interface IFriendLobbyGateway
{
    Task<FriendRoom> FindFriendRoomAsync(ulong friendId, CancellationToken cancellationToken);
    Task<LobbyReply> JoinLobbyAsync(ulong lobbyId);
    Task LeaveLobbyAsync(ulong lobbyId);
}

// This is Steam lobby membership only, not an established game transport.
public sealed class SteamFriendLobbyClient(IFriendLobbyGateway gateway)
{
    public const uint AppId = 2868840;
    private readonly SemaphoreSlim operation = new(1, 1);
    private ulong joinedLobby;
    public bool HasLobbyMembership => joinedLobby != 0;
    public bool HasGameConnection => false;

    public async Task<LobbyReply> JoinFriendAsync(
        ulong friendId,
        CancellationToken cancellationToken = default
    )
    {
        if (friendId == 0)
            throw new ArgumentException("Informe o SteamID64 do amigo.");
        await operation.WaitAsync(cancellationToken);
        try
        {
            if (joinedLobby != 0)
                throw new InvalidOperationException(
                    "Saia do lobby atual antes de entrar em outro."
                );
            var room = await gateway.FindFriendRoomAsync(friendId, cancellationToken);
            if (room.FriendId != friendId)
                throw new InvalidDataException("Resposta pertence a outro amigo.");
            if (room.AppId != AppId)
                throw new InvalidOperationException(
                    room.AppId == 0
                        ? "A Steam não disponibilizou um jogo em execução para esse amigo (app=0). Isso não prova que ele fechou o jogo. Ele precisa estar com STS2 e uma sala aberta; se já estiver, a presença pode estar indisponível."
                        : $"A Steam informou outro jogo para esse amigo (app={room.AppId})."
                );
            if (room.LobbyId == 0)
                throw new InvalidOperationException(
                    "A Steam não informou uma sala para esse amigo. Estar online não basta: ele precisa ter criado uma sala em STS2."
                );
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                // Do not abandon a sent join RPC on local cancellation: await its
                // result, then leave. Otherwise a late reply can orphan membership.
                var result = await gateway.JoinLobbyAsync(room.LobbyId);
                if (!result.Accepted)
                    throw new InvalidOperationException(
                        "A Steam recusou a entrada: " + result.Result
                    );
                if (result.AppId != AppId || result.LobbyId != room.LobbyId || result.OwnerId == 0)
                    throw new InvalidDataException(
                        "A resposta do lobby não corresponde à solicitação."
                    );
                cancellationToken.ThrowIfCancellationRequested();
                joinedLobby = result.LobbyId;
                return result;
            }
            catch
            {
                // Best effort also covers a join that was accepted remotely but
                // whose response failed validation or was lost during disconnect.
                try
                {
                    await gateway.LeaveLobbyAsync(room.LobbyId);
                }
                catch { }
                throw;
            }
        }
        finally
        {
            operation.Release();
        }
    }

    public async Task LeaveAsync()
    {
        await operation.WaitAsync();
        try
        {
            if (joinedLobby == 0)
                return;
            await gateway.LeaveLobbyAsync(joinedLobby);
            joinedLobby = 0;
        }
        finally
        {
            operation.Release();
        }
    }
}

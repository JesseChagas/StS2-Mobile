namespace Sts2.SteamClientPrototype;

public static class LobbyLink
{
    public static ulong Parse(string text, ulong friendId)
    {
        if (ulong.TryParse(text, out var numeric) && numeric > 0)
            return numeric;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
            throw Invalid();
        string[] parts;
        if (
            uri.Scheme == "steam"
            && uri.Host.Equals("joinlobby", StringComparison.OrdinalIgnoreCase)
        )
            parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        else if (
            uri.Scheme == "https"
            && uri.Host.Equals("s.team", StringComparison.OrdinalIgnoreCase)
        )
        {
            var path = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (path.Length == 0 || path[0] != "joinlobby")
                throw Invalid();
            parts = path[1..];
        }
        else
            throw Invalid();
        if (
            parts.Length is < 2 or > 3
            || !uint.TryParse(parts[0], out var app)
            || app != SteamFriendLobbyClient.AppId
            || !ulong.TryParse(parts[1], out var lobby)
            || lobby == 0
        )
            throw Invalid();
        if (parts.Length == 3 && (!ulong.TryParse(parts[2], out var host) || host != friendId))
            throw new ArgumentException(
                "O host do convite não corresponde ao SteamID64 do amigo informado."
            );
        return lobby;
    }

    private static ArgumentException Invalid() =>
        new(
            "Informe um lobbyID numérico ou um convite steam://joinlobby/2868840/LOBBY/HOST válido."
        );
}

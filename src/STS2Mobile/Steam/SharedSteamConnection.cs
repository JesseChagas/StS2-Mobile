using SteamKit2;

namespace STS2Mobile.Steam;

// One process-owned connection for the selected account. Consumers borrow it;
// a cloud flush or multiplayer screen closing must not log out another consumer.
internal static class SharedSteamConnection
{
    private static readonly object gate = new();
    private static SteamConnection current;
    private static string account;
    private static IDisposable gameHold;

    public static SteamConnection Get(string username, string refreshToken)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(refreshToken))
            throw new SteamLoginRequiredException(EResult.AccessDenied);
        lock (gate) {
            if (current == null || !string.Equals(account, username, StringComparison.OrdinalIgnoreCase)) {
                gameHold?.Dispose(); gameHold = null;
                current?.ShutdownShared();
                current = new SteamConnection(username, refreshToken);
                current.MarkShared();
                account = username;
            }
            return current;
        }
    }

    // Only a completed interactive authentication may replace the credential.
    // Existing cloud jobs can still carry the old token in their closures.
    public static SteamConnection UpdateCredentials(string username, string refreshToken)
    {
        lock (gate) {
            var connection = Get(username, refreshToken);
            connection.UpdateRefreshToken(refreshToken);
            return connection;
        }
    }

    public static void KeepForGame(string username, string token)
    {
        var connection = Get(username, token);
        lock (gate) gameHold ??= connection.HoldOpen();
    }

    public static void Reset()
    {
        lock (gate) {
            gameHold?.Dispose(); gameHold = null;
            current?.ShutdownShared(); current = null; account = null;
        }
    }
}


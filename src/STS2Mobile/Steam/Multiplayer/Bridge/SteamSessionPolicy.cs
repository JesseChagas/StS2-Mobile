using System.Security.Cryptography;
using SteamKit2;

static class SteamSessionPolicy
{
    // SteamKit requires the logon remember flag to match the persistence used
    // when minting the refresh token. This flag does not store a password.
    public const bool PersistentLauncherSession = true;

    public static SteamUser.LogOnDetails CreateLauncherLogOn(string accountName, string refreshToken) =>
        CreateLogOn(accountName, refreshToken, PersistentLauncherSession);

    public static SteamUser.LogOnDetails CreateLogOn(string accountName, string accessToken, bool persistentSession = false) =>
        new()
        {
            Username = accountName,
            AccessToken = accessToken,
            // SteamKit defaults to the local bind address. Multiple clients on the
            // same machine/account must not reuse that default session identifier.
            LoginID = (uint)RandomNumberGenerator.GetInt32(1, int.MaxValue),
            ShouldRememberPassword = persistentSession,
        };

    public static void ValidateTarget(ulong selfId, ulong targetId)
    {
        if (selfId == 0)
            throw new InvalidOperationException(
                "A Steam não informou a identidade da conta autenticada."
            );
        if (selfId == targetId)
            throw new InvalidOperationException(
                "O SteamID informado é o da própria conta conectada. Informe o SteamID64 do amigo que está hospedando a sala."
            );
    }
}

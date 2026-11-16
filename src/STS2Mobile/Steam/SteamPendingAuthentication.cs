using System.Text.Json;
using SteamKit2.Internal;
using STS2Mobile.Steam.Multiplayer;

namespace STS2Mobile.Steam;

// Short-lived continuation of a Guard challenge, never a saved password or code.
// Uses the same Android Keystore bridge as the normal credential store.
internal sealed record PendingSteamAuthentication(string Account, ulong ClientId, byte[] RequestId,
    ulong SteamId, EAuthSessionGuardType Kind, string GuardData, DateTimeOffset Expires)
{
    internal bool Valid => !string.IsNullOrWhiteSpace(Account) && ClientId != 0 && SteamId != 0
        && RequestId is { Length: > 0 and < 1024 } && Expires > DateTimeOffset.UtcNow
        && Expires <= DateTimeOffset.UtcNow.AddMinutes(11)
        && Kind is EAuthSessionGuardType.k_EAuthSessionGuardType_DeviceConfirmation
            or EAuthSessionGuardType.k_EAuthSessionGuardType_DeviceCode or EAuthSessionGuardType.k_EAuthSessionGuardType_EmailCode;
}

internal sealed class SteamPendingAuthentication(string dataDir)
{
    private string FilePath => Path.Combine(dataDir, "steam_pending_auth.enc");
    internal PendingSteamAuthentication Load()
    {
        if (string.IsNullOrEmpty(dataDir) || !File.Exists(FilePath)) return null;
        try {
            var app = SteamCredentialStore.GetGodotApp();
            var json = app == null ? null : (string)app.Call("decryptString", File.ReadAllText(FilePath));
            var value = json == null ? null : JsonSerializer.Deserialize<PendingSteamAuthentication>(json);
            if (value?.Valid == true) return value;
            Clear();
        } catch { Clear(); }
        return null;
    }
    internal async Task SaveAsync(PendingSteamAuthentication value)
    {
        if (string.IsNullOrEmpty(dataDir)) return;
        await GameThread.Switch();
        try {
            var app = SteamCredentialStore.GetGodotApp();
            if (app == null) return;
            var encrypted = (string)app.Call("encryptString", JsonSerializer.Serialize(value));
            if (string.IsNullOrEmpty(encrypted)) return;
            Directory.CreateDirectory(dataDir);
            File.WriteAllText(FilePath + ".tmp", encrypted);
            File.Move(FilePath + ".tmp", FilePath, true);
        } catch (Exception e) { PatchHelper.Log("[Auth] Could not checkpoint Guard request: " + e.GetType().Name); }
    }
    internal void Clear() {
        if (string.IsNullOrEmpty(dataDir)) return;
        try { File.Delete(FilePath); File.Delete(FilePath + ".tmp"); } catch { }
    }
}

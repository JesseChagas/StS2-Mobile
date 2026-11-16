using System.Reflection;
using SteamKit2;
using SteamKit2.Authentication;
using SteamKit2.Internal;
using STS2Mobile.Steam;

// Exercise SteamKit's real approval polling state machine offline. Only the
// network RPC is replaced: the auth session must survive a failed transport.
var harmony = new HarmonyLib.Harmony("sts2.auth.recovery.tests");
harmony.Patch(typeof(AuthSession).GetMethod(nameof(AuthSession.PollAuthSessionStatusAsync)),
    prefix: new HarmonyLib.HarmonyMethod(typeof(Network), nameof(Network.Poll)));
var client = new SteamClient();
var authenticator = new Approval();
var session = (AuthSession)Activator.CreateInstance(typeof(AuthSession), BindingFlags.Instance | BindingFlags.NonPublic,
    null, new object[] { client.Authentication, authenticator, 123UL, new byte[] { 1, 2, 3 },
    new List<CAuthentication_AllowedConfirmation> { new() { confirmation_type = EAuthSessionGuardType.k_EAuthSessionGuardType_DeviceConfirmation } }, .001f }, null)!;
var reconnects = 0;
Network.Failures = 1;
var result = await SteamAuthRecovery.RunAsync(() => session.PollingWaitForResultAsync(),
    _ => { reconnects++; return Task.CompletedTask; }, default, _ => Task.CompletedTask);
if (Network.Calls != 2 || reconnects != 1 || result.AccountName != "offline-fixture" || authenticator.Approvals != 2)
    throw new Exception("Pending device approval was not resumed.");
Console.WriteLine("PASS: real SteamKit device-approval polling survives a failed job, reuses ClientID/RequestID and returns the approved result.");
Network.Calls = 0; Network.Error = new AuthenticationException("Rejected fixture", EResult.AccessDenied); reconnects = 0;
try {
    await SteamAuthRecovery.RunAsync(() => session.PollingWaitForResultAsync(), _ => { reconnects++; return Task.CompletedTask; }, default);
    throw new Exception("Rejected login was silently retried.");
} catch(AuthenticationException e) when(e.Result == EResult.AccessDenied) {
    if (reconnects != 0 || Network.Calls != 1) throw;
}
Console.WriteLine("PASS: AccessDenied is surfaced without a reconnect/login loop.");
Network.Error = null; Network.Calls = 0; Network.Failures = 20;
using var cancelled = new CancellationTokenSource();
try {
    await SteamAuthRecovery.RunAsync(() => session.PollingWaitForResultAsync(cancelled.Token), _ => Task.CompletedTask,
        cancelled.Token, _ => { cancelled.Cancel(); return Task.CompletedTask; });
    throw new Exception("Cancellation was ignored.");
} catch(OperationCanceledException) { if(Network.Calls != 1) throw; }
Console.WriteLine("PASS: cancellation during recovery does not start another request.");
Network.Calls = 0; Network.Failures = 20;
try {
    await SteamAuthRecovery.RunAsync(() => session.PollingWaitForResultAsync(), _ => Task.CompletedTask, default, _ => Task.CompletedTask);
    throw new Exception("Missing bounded transport failure.");
} catch(AsyncJobFailedException) { if(Network.Calls != 6) throw; }
Console.WriteLine("PASS: repeated transport failures stop after six attempts.");

// Android process loss is represented by a fresh store instance. The native
// Keystore bridge is replaced here by an AES-GCM fixture, not live Android.
var dir = Path.Combine(Path.GetTempPath(), "sts2-auth-" + Guid.NewGuid().ToString("N"));
try {
    var pending = new PendingSteamAuthentication("fixture-account", 123, new byte[] {1,2,3}, 76561198000000001,
        EAuthSessionGuardType.k_EAuthSessionGuardType_DeviceConfirmation, "fixture-guard", DateTimeOffset.UtcNow.AddMinutes(9));
    await new SteamPendingAuthentication(dir).SaveAsync(pending);
    var bytes = File.ReadAllText(Path.Combine(dir, "steam_pending_auth.enc"));
    if(bytes.Contains("fixture") || bytes.Contains("76561198000000001")) throw new Exception("Pending authentication saved as plaintext.");
    var restored = new SteamPendingAuthentication(dir).Load();
    if(restored?.Account != pending.Account || restored.ClientId != pending.ClientId || !restored.RequestId.SequenceEqual(pending.RequestId))
        throw new Exception("Guard continuation did not survive a fresh instance.");
    Console.WriteLine("PASS: pending Guard continuation survives a fresh store with encrypted bytes (simulated Keystore).");
    await new SteamPendingAuthentication(dir).SaveAsync(pending with { Expires = DateTimeOffset.UtcNow.AddMinutes(-1) });
    if(new SteamPendingAuthentication(dir).Load() != null || File.Exists(Path.Combine(dir, "steam_pending_auth.enc")))
        throw new Exception("Expired continuation was resumed or retained.");
    Console.WriteLine("PASS: expired Guard continuations are discarded.");
} finally { if(Directory.Exists(dir)) Directory.Delete(dir, true); }

static class Network {
    public static int Failures, Calls;
    public static Exception Error;
    public static bool Poll(AuthSession __instance, ref Task<AuthPollResult> __result) {
        Calls++;
        if(__instance.ClientID != 123 || !__instance.RequestID.SequenceEqual(new byte[] {1,2,3})) throw new Exception("Auth session changed after reconnect.");
        if (Error != null) __result = Task.FromException<AuthPollResult>(Error);
        else if (Failures-- > 0) __result = Task.FromException<AuthPollResult>((Exception)Activator.CreateInstance(typeof(AsyncJobFailedException), true)!);
        else {
            var response = new CAuthentication_PollAuthSessionStatus_Response { account_name = "offline-fixture", refresh_token = "synthetic-not-a-token" };
            var result = (AuthPollResult)Activator.CreateInstance(typeof(AuthPollResult), BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { response }, null)!;
            __result = Task.FromResult(result);
        }
        return false;
    }
}
sealed class Approval : IAuthenticator {
    public int Approvals;
    public Task<bool> AcceptDeviceConfirmationAsync() { Approvals++; return Task.FromResult(true); }
    public Task<string> GetDeviceCodeAsync(bool incorrect) => throw new Exception("Unexpected code request.");
    public Task<string> GetEmailCodeAsync(string email, bool incorrect) => throw new Exception("Unexpected email request.");
}

namespace STS2Mobile.Steam.Multiplayer { internal static class GameThread { internal static Task Switch() => Task.CompletedTask; } }
namespace STS2Mobile.Steam {
    internal static class PatchHelper { internal static void Log(string msg) => Console.WriteLine(msg); }
    internal static class SteamCredentialStore { internal static FixtureKeystore GetGodotApp() => new(); }
    internal sealed class FixtureKeystore {
        private static readonly byte[] Key = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        public object Call(string method, string value) {
            using var aes = new System.Security.Cryptography.AesGcm(Key, 16);
            if(method == "encryptString") {
                var data = System.Text.Encoding.UTF8.GetBytes(value);
                var nonce = System.Security.Cryptography.RandomNumberGenerator.GetBytes(12);
                var cipher = new byte[data.Length]; var tag = new byte[16];
                aes.Encrypt(nonce, data, cipher, tag);
                return Convert.ToBase64String(nonce.Concat(tag).Concat(cipher).ToArray());
            }
            var buffer = Convert.FromBase64String(value); var plain = new byte[buffer.Length - 28];
            aes.Decrypt(buffer.AsSpan(0,12), buffer.AsSpan(28), buffer.AsSpan(12,16), plain);
            return System.Text.Encoding.UTF8.GetString(plain);
        }
    }
}

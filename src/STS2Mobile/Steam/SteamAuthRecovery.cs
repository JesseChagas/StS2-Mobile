using SteamKit2;
using SteamKit2.Authentication;

namespace STS2Mobile.Steam;

// Retry transport failures only. Keep the same auth session; never reinterpret
// a rejected password/code, revoked credential, or user cancellation as a retry.
internal static class SteamAuthRecovery
{
    internal static bool IsTransient(Exception e) => e is AsyncJobFailedException or IOException or TimeoutException
        || e is AuthenticationException auth && auth.Result is EResult.NoConnection or EResult.ServiceUnavailable or EResult.Timeout or EResult.TryAnotherCM;

    internal static async Task<T> RunAsync<T>(Func<Task<T>> operation,
        Func<CancellationToken, Task> reconnect, CancellationToken token,
        Func<CancellationToken, Task> delay = null)
    {
        delay ??= token => Task.Delay(1500, token);
        Exception last = null;
        for (var attempt = 0; attempt < 6; attempt++) {
            token.ThrowIfCancellationRequested();
            try {
                if (last != null) await reconnect(token);
                return await operation();
            } catch (Exception e) when (!token.IsCancellationRequested && IsTransient(e)) {
                last = e;
                if (attempt == 5) throw;
                await delay(token);
            }
        }
        throw last;
    }
}

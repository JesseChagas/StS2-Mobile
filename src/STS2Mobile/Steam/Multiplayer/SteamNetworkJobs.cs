using SteamKit2;

namespace STS2Mobile.Steam.Multiplayer;

// WaitAsync alone does not extend SteamKit's own default ten-second job timer.
internal static class SteamNetworkJobs
{
    internal static readonly TimeSpan JobTimeout = TimeSpan.FromSeconds(30);
    public static async Task<T> RequestAsync<T>(Func<AsyncJob<T>> request, Func<bool> alive, string operation, CancellationToken token) where T : CallbackMsg
    {
        for (var attempt = 1; ; attempt++) {
            token.ThrowIfCancellationRequested();
            if (!alive()) throw new IOException("A sessão Steam terminou durante " + operation + ".");
            ConnectionReport.Note($"{operation}; attempt={attempt}; steam-job-timeout=30s");
            try {
                var job = request();
                job.Timeout = JobTimeout;
                return await job.ToTask().WaitAsync(JobTimeout + TimeSpan.FromSeconds(5), token);
            } catch (Exception e) when ((e is OperationCanceledException || e is TimeoutException) && !token.IsCancellationRequested) {
                if (!alive()) throw new IOException("A conexão Steam foi interrompida durante " + operation + ".", e);
                if (attempt >= 2) throw new IOException("A Steam não respondeu ao pedido de certificado após duas tentativas. Tente novamente; a conexão precisa de um certificado assinado válido.", e);
                ConnectionReport.Note(operation + "; timeout; retrying");
                await Task.Delay(400, token);
            }
        }
    }
}

using System.Reflection;
using SteamKit2;
using STS2Mobile.Steam.Multiplayer;

// Only suppress live job registration; real AsyncJob completion/cancellation logic is exercised offline.
var harmony = new HarmonyLib.Harmony("sts2.certificate.timeout.tests");
harmony.Patch(HarmonyLib.AccessTools.Method(typeof(SteamClient), "StartJob"), prefix: new HarmonyLib.HarmonyMethod(typeof(Offline), nameof(Offline.Skip)));
var client = new SteamClient();
int calls = 0;
AsyncJob<Reply> Make(bool fail) {
    var job = new AsyncJob<Reply>(client, client.GetNextJobID());
    _ = Task.Run(async () => {
        await Task.Delay(20);
        if (job.Timeout != TimeSpan.FromSeconds(30)) throw new Exception("Internal ten-second timer was not extended.");
        if (fail) typeof(AsyncJob<Reply>).GetMethod("SetFailed", BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(job, [false]);
        else typeof(AsyncJob<Reply>).GetMethod("AddResult", BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(job, [new Reply()]);
    });
    return job;
}
await SteamNetworkJobs.RequestAsync(() => Make(++calls == 1), () => true, "fixture", default);
if (calls != 2) throw new Exception("Timed-out job was not retried exactly once.");
Console.WriteLine("PASS: real SteamKit internal timeout extended; canceled job retried and successful callback returned (synthetic reply).");
calls = 0;
try { await SteamNetworkJobs.RequestAsync(() => { calls++; return Make(true); }, () => true, "fixture", default); throw new Exception("Unbounded retry or missing failure."); }
catch (IOException e) when (e.Message.Contains("duas tentativas")) { if(calls!=2)throw; }
Console.WriteLine("PASS: two internal timeouts produce an actionable bounded error.");
using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); calls=0;
try { await SteamNetworkJobs.RequestAsync(() => { calls++; return Make(false); }, () => true, "fixture", cancelled.Token); throw new Exception("Cancellation ignored."); }
catch (OperationCanceledException) { if(calls!=0)throw; }
Console.WriteLine("PASS: caller cancellation does not issue or retry requests.");
try { await SteamNetworkJobs.RequestAsync(() => Make(false), () => false, "fixture", default); throw new Exception("Disconnected session ignored."); }
catch(IOException e) when(e.Message.Contains("sessão Steam terminou")) { }
Console.WriteLine("PASS: disconnected session cannot request a certificate.");
static class Offline { public static bool Skip() => false; }
sealed class Reply : CallbackMsg { }
namespace STS2Mobile.Steam.Multiplayer { internal static class ConnectionReport { public static void Note(string s) => Console.WriteLine(s); } }

using System.Text;

namespace STS2Mobile.Steam.Multiplayer;

// Diagnostics are bounded, contain no authentication values, and can be copied
// from the phone. A failed adapter is not automatically a certificate failure.
internal static class ConnectionReport
{
    private static readonly object gate = new();
    private static readonly Queue<string> trace = new();
    private static string stage = "idle", status = "", error = "";
    private static bool failed, busy, popup, requiresLogin;
    private static ulong host;
    public static event Action Changed;
    public static (string Stage, string Status, bool Failed, bool Busy, bool RequiresLogin) Snapshot {
        get { lock (gate) return (stage, status, failed, busy, requiresLogin); }
    }
    public static void Begin(ulong target)
    {
        lock (gate) { trace.Clear(); host = target; failed = popup = requiresLogin = false; busy = true; error = ""; }
        Step("prepare", "Preparing multiplayer", "Preparando multiplayer");
    }
    public static void Step(string code, string en, string pt)
    {
        lock (gate) {
            stage = code; status = STS2Mobile.Launcher.Design.LauncherPreferences.Language == "pt" ? pt : en;
            trace.Enqueue(DateTimeOffset.UtcNow.ToString("HH:mm:ss") + " " + code);
            while (trace.Count > 64) trace.Dequeue();
        }
        Changed?.Invoke();
        PatchHelper.Log("[SteamMP] etapa=" + code);
    }
    public static void Fail(Exception exception, bool suppressPopup = true)
    {
        var root = exception.GetBaseException();
        var detail = root.GetType().Name + ": " + root.Message;
        lock (gate) {
            busy = false; failed = true; popup = suppressPopup;
            requiresLogin = root is SteamLoginRequiredException;
            error = Sanitize(exception.ToString());
            status = Sanitize(detail);
            trace.Enqueue("FAILED " + stage + ": " + status);
            while (trace.Count > 64) trace.Dequeue();
        }
        PatchHelper.Log("[SteamMP] " + Diagnostics);
        Changed?.Invoke();
    }
    public static void Note(string detail)
    {
        var safe = Sanitize(detail);
        lock (gate) {
            trace.Enqueue(DateTimeOffset.UtcNow.ToString("HH:mm:ss") + " " + safe);
            while (trace.Count > 64) trace.Dequeue();
        }
        PatchHelper.Log("[SteamMP] " + safe);
    }
    public static void Cancelled()
    {
        lock (gate) { busy = failed = popup = requiresLogin = false; }
        Step("cancelled", "Connection cancelled", "Conexão cancelada");
    }
    public static void Connected()
    {
        lock (gate) { busy = failed = popup = requiresLogin = false; }
        Step("connected", "Game protocol connected", "Protocolo da partida conectado");
    }
    public static bool ConsumePopup(ulong? target)
    {
        lock (gate) {
            if (!popup || !failed || target != host) return false;
            popup = false; return true;
        }
    }
    public static string Diagnostics {
        get { lock (gate) return $"STS2 Mobile r9.11\nStage: {stage}\nHost: {host}\n" + string.Join("\n", trace) + "\n" + error; }
    }
    private static string Sanitize(string text)
    {
        var token = STS2Mobile.Patches.LauncherPatches.SavedRefreshToken;
        if (!string.IsNullOrEmpty(token)) text = text.Replace(token, "[redacted]");
        return text.Length > 12000 ? text[..12000] : text;
    }
}

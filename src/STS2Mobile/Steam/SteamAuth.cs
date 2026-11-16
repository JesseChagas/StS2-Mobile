using System;
using System.Threading;
using System.Threading.Tasks;
using SteamKit2;
using SteamKit2.Authentication;
using SteamKit2.Internal;

namespace STS2Mobile.Steam;

public enum AuthNotice
{
    ApproveDevice,
    DeviceCode,
    EmailCode,
    InvalidCode,
}

public record AuthResult(string AccountName, string RefreshToken, string GuardData);

// Handles one-time interactive Steam login (password + 2FA). Creates a temporary
// SteamClient for the auth flow, returns credentials, then disposes. Does NOT
// call SteamUser.LogOn — callers use the returned refresh token with SteamConnection.
public class SteamAuth : IDisposable
{
    private readonly SteamClient _client;
    private readonly CallbackManager _callbackManager;
    private readonly SteamUser _steamUser;
    private Thread _callbackThread;
    private volatile bool _callbackRunning;

    private readonly ManualResetEventSlim _connectedGate = new(false);
    private volatile bool _disposed;
    private volatile bool _connectRequested;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _reconnectLock = new(1, 1);
    private CancellationToken _authToken;

    // Set by the caller before LoginWithCredentialsAsync. The bool indicates
    // whether the previous code was incorrect.
    public Func<bool, Task<string>> CodeProvider { get; set; }

    // Set when the WebSocket drops during 2FA wait (e.g. user backgrounds app).
    internal volatile bool NeedsReconnectForAuth;

    public event Action<string> LogMessage;
    public event Action<AuthNotice> NoticeReceived;

    private readonly SteamPendingAuthentication _pendingStore;
    private CredentialsAuthSession _activeSession;
    private PendingSteamAuthentication _pending;
    public static string PendingAccount(string dataDir) => new SteamPendingAuthentication(dataDir).Load()?.Account;
    public void ClearPending() => _pendingStore.Clear();

    public SteamAuth(string dataDir = null)
    {
        _pendingStore = new SteamPendingAuthentication(dataDir);
        var config = SteamConfiguration.Create(b => b.WithProtocolTypes(ProtocolTypes.WebSocket));
        _client = new SteamClient(config);
        _callbackManager = new CallbackManager(_client);
        _steamUser = _client.GetHandler<SteamUser>();

        _callbackManager.Subscribe<SteamClient.ConnectedCallback>(_ =>
        {
            Log("Connected to Steam");
            _connectRequested = false;
            NeedsReconnectForAuth = false;
            _connectedGate.Set();
        });

        _callbackManager.Subscribe<SteamClient.DisconnectedCallback>(cb =>
        {
            if (_client.IsConnected) return; // Ignore a queued event from an older socket.
            _connectRequested = false;
            _connectedGate.Reset();
            if (!_disposed && !cb.UserInitiated)
            {
                NeedsReconnectForAuth = true;
                Log("Steam connection interrupted; keeping the pending Steam Guard request for reconnection");
            }
        });
    }

    public void Connect()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_client.IsConnected || _connectRequested) return;
        _connectRequested = true;
        _connectedGate.Reset();
        StartCallbackThread();
        _client.Connect();
        Log("Connecting to Steam...");
    }

    public async Task<bool> WaitForConnectAsync(int timeoutMs = 10_000)
    {
        for (int i = 0; i < timeoutMs / 100; i++)
        {
            if (_disposed) return false;
            if (_connectedGate.IsSet && _client.IsConnected)
                return true;
            await Task.Delay(100);
        }
        return !_disposed && _connectedGate.IsSet && _client.IsConnected;
    }

    public async Task<AuthResult> LoginWithCredentialsAsync(
        string username,
        string password,
        string guardData,
        CancellationToken cancellationToken = default
    )
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        lifetime.CancelAfter(TimeSpan.FromMinutes(10));
        cancellationToken = _authToken = lifetime.Token;
        await EnsureConnectedForAuthAsync(cancellationToken);

        Log($"Authenticating as '{username}'...");
        _pendingStore.Clear();

        cancellationToken.ThrowIfCancellationRequested();
        var authenticator = new AuthAuthenticator(this);
        var authSession = await SteamAuthRecovery.RunAsync(() => _client.Authentication.BeginAuthSessionViaCredentialsAsync(
            new AuthSessionDetails
            {
                Username = username,
                Password = password,
                IsPersistentSession = SteamSessionPolicy.PersistentLauncherSession,
                GuardData = guardData,
                Authenticator = authenticator,
            }
        ).WaitAsync(cancellationToken), RecoverConnectionAsync, cancellationToken);

        _activeSession = authSession;
        _pending = new PendingSteamAuthentication(username, authSession.ClientID, authSession.RequestID,
            authSession.SteamID.ConvertToUInt64(), EAuthSessionGuardType.k_EAuthSessionGuardType_Unknown,
            guardData, DateTimeOffset.UtcNow.AddMinutes(10));
        password = null;
        // Reuse ClientID/RequestID: a CM socket reconnect must not discard a
        // Steam Guard approval made while Android had this app in the background.
        var pollResponse = await SteamAuthRecovery.RunAsync(
            () => authSession.PollingWaitForResultAsync(cancellationToken),
            RecoverConnectionAsync, cancellationToken);

        string newGuardData = pollResponse.NewGuardData ?? guardData;

        Log($"Authentication successful for '{pollResponse.AccountName}'");

        return new AuthResult(pollResponse.AccountName, pollResponse.RefreshToken, newGuardData);
    }

    internal Task ReconnectForAuthAsync() => EnsureConnectedForAuthAsync(_authToken);

    private async Task RecoverConnectionAsync(CancellationToken token)
    {
        Log("Reconnecting to resume the pending Steam Guard request...");
        if (_pending != null && _activeSession != null) {
            _pending = _pending with { ClientId = _activeSession.ClientID };
            await _pendingStore.SaveAsync(_pending);
        }
        // IsConnected can remain true until a suspended socket times out.
        _client.Disconnect();
        _connectRequested = false;
        _connectedGate.Reset();
        await EnsureConnectedForAuthAsync(token);
    }

    private async Task EnsureConnectedForAuthAsync(CancellationToken token)
    {
        await _reconnectLock.WaitAsync(token);
        try {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_client.IsConnected || !_connectedGate.IsSet) {
                Connect();
                if (!await WaitForConnectAsync().WaitAsync(token)) {
                    _connectRequested = false;
                    throw new TimeoutException("Could not reconnect to Steam. Check your internet connection.");
                }
            }
            NeedsReconnectForAuth = false;
        } finally { _reconnectLock.Release(); }
    }

    private async Task CheckpointAsync(EAuthSessionGuardType kind)
    {
        if (_pending == null) return;
        _pending = _pending with { Kind = kind, ClientId = _activeSession?.ClientID ?? _pending.ClientId };
        await _pendingStore.SaveAsync(_pending);
    }

    public async Task<AuthResult> ResumePendingAsync(CancellationToken token = default)
    {
        _pending = _pendingStore.Load() ?? throw new IOException("The pending Steam Guard request expired. Sign in again.");
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        lifetime.CancelAfter(_pending.Expires - DateTimeOffset.UtcNow);
        token = _authToken = lifetime.Token;
        Log("Resuming the pending Steam Guard request after an app restart...");
        var service = _client.GetHandler<SteamUnifiedMessages>().CreateService<SteamKit2.Internal.Authentication>();
        var requiresCode = _pending.Kind != EAuthSessionGuardType.k_EAuthSessionGuardType_DeviceConfirmation;
        if (!requiresCode) NoticeReceived?.Invoke(AuthNotice.ApproveDevice);
        string suppliedCode = null;
        bool invalidCode = false;
        while (true) {
            await SteamAuthRecovery.RunAsync(async () => {
                await EnsureConnectedForAuthAsync(token);
                return true;
            }, RecoverConnectionAsync, token);
            var reply = await SteamAuthRecovery.RunAsync(async () => {
                var job = service.PollAuthSessionStatus(new CAuthentication_PollAuthSessionStatus_Request {
                    client_id = _pending.ClientId, request_id = _pending.RequestId });
                job.Timeout = TimeSpan.FromSeconds(30);
                return await job.ToTask().WaitAsync(token);
            }, RecoverConnectionAsync, token);
            if (reply.Result != EResult.OK) throw new AuthenticationException("Steam Guard request could not be resumed", reply.Result);
            var body = reply.Body;
            if (body.new_client_id != 0) {
                _pending = _pending with { ClientId = body.new_client_id };
                await _pendingStore.SaveAsync(_pending);
            }
            if (!string.IsNullOrEmpty(body.refresh_token))
                return new AuthResult(body.account_name, body.refresh_token, body.new_guard_data ?? _pending.GuardData);
            if (requiresCode) {
                NoticeReceived?.Invoke(invalidCode ? AuthNotice.InvalidCode :
                    _pending.Kind == EAuthSessionGuardType.k_EAuthSessionGuardType_DeviceCode ? AuthNotice.DeviceCode : AuthNotice.EmailCode);
                suppliedCode ??= await (CodeProvider?.Invoke(invalidCode)
                    ?? throw new AuthenticationException("No code provider configured")).WaitAsync(token);
                var update = await SteamAuthRecovery.RunAsync(async () => {
                    await EnsureConnectedForAuthAsync(token);
                    var job = service.UpdateAuthSessionWithSteamGuardCode(new CAuthentication_UpdateAuthSessionWithSteamGuardCode_Request {
                        client_id = _pending.ClientId, steamid = _pending.SteamId, code = suppliedCode, code_type = _pending.Kind });
                    job.Timeout = TimeSpan.FromSeconds(30);
                    return await job.ToTask().WaitAsync(token);
                }, RecoverConnectionAsync, token);
                if (update.Result == EResult.OK) { requiresCode = false; suppliedCode = null; }
                else if (update.Result is EResult.TwoFactorCodeMismatch or EResult.InvalidLoginAuthCode) {
                    invalidCode = true; suppliedCode = null;
                } else throw new AuthenticationException("Steam Guard code rejected", update.Result);
            }
            await Task.Delay(TimeSpan.FromSeconds(2), token);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _lifetime.Cancel();
        _callbackRunning = false;
        try
        {
            _client?.Disconnect();
        }
        catch { }
        _callbackThread?.Join(2000);

    }

    private void StartCallbackThread()
    {
        if (_callbackThread != null && _callbackThread.IsAlive)
            return;

        _callbackRunning = true;
        _callbackThread = new Thread(() =>
        {
            while (_callbackRunning)
                _callbackManager.RunWaitCallbacks(TimeSpan.FromSeconds(1));
        })
        {
            IsBackground = true,
            Name = "SteamAuthCallbacks",
        };
        _callbackThread.Start();
    }

    private void Log(string msg)
    {
        PatchHelper.Log($"[Auth] {msg}");
        LogMessage?.Invoke(msg);
    }

    private class AuthAuthenticator : IAuthenticator
    {
        private readonly SteamAuth _auth;
        private string _lastDeviceCode, _lastEmailCode;

        public AuthAuthenticator(SteamAuth auth) => _auth = auth;

        public async Task<string> GetDeviceCodeAsync(bool previousCodeWasIncorrect)
        {
            await _auth.CheckpointAsync(EAuthSessionGuardType.k_EAuthSessionGuardType_DeviceCode);
            _auth.NoticeReceived?.Invoke(
                previousCodeWasIncorrect ? AuthNotice.InvalidCode : AuthNotice.DeviceCode
            );
            _auth.Log(
                previousCodeWasIncorrect
                    ? "Previous 2FA code was incorrect, requesting new code"
                    : "Steam Guard 2FA code required"
            );

            if (_auth.CodeProvider == null)
                throw new AuthenticationException("No code provider configured");

            if (previousCodeWasIncorrect) _lastDeviceCode = null;
            _lastDeviceCode ??= await _auth.CodeProvider(previousCodeWasIncorrect).WaitAsync(_auth._authToken);
            await _auth.EnsureConnectedForAuthAsync(_auth._authToken);
            return _lastDeviceCode;
        }

        public async Task<string> GetEmailCodeAsync(string email, bool previousCodeWasIncorrect)
        {
            await _auth.CheckpointAsync(EAuthSessionGuardType.k_EAuthSessionGuardType_EmailCode);
            _auth.NoticeReceived?.Invoke(
                previousCodeWasIncorrect ? AuthNotice.InvalidCode : AuthNotice.EmailCode
            );
            _auth.Log(
                previousCodeWasIncorrect
                    ? "Previous email code was incorrect, requesting new code"
                    : $"Steam Guard email code sent to {email}"
            );

            if (_auth.CodeProvider == null)
                throw new AuthenticationException("No code provider configured");

            if (previousCodeWasIncorrect) _lastEmailCode = null;
            _lastEmailCode ??= await _auth.CodeProvider(previousCodeWasIncorrect).WaitAsync(_auth._authToken);
            await _auth.EnsureConnectedForAuthAsync(_auth._authToken);
            return _lastEmailCode;
        }

        public async Task<bool> AcceptDeviceConfirmationAsync()
        {
            await _auth.CheckpointAsync(EAuthSessionGuardType.k_EAuthSessionGuardType_DeviceConfirmation);
            _auth.NoticeReceived?.Invoke(AuthNotice.ApproveDevice);
            _auth.Log("Waiting for Steam mobile app confirmation...");
            return true;
        }
    }
}

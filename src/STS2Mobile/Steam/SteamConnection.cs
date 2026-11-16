using System;
using System.Threading;
using System.Threading.Tasks;
using SteamKit2;
using SteamKit2.Internal;

namespace STS2Mobile.Steam;

public enum ConnectionState
{
    Idle,
    Connecting,
    Connected,
    Draining,
    Backoff,
}

// General-purpose on-demand Steam connection. Connects when a handler is accessed,
// auto-disconnects after idle timeout, reconnects with exponential backoff on failure.
// Reuses the same SteamClient instance across reconnects for handler/service persistence.
//
// State machine:
//   Idle → Connecting       : Handler property accessed
//   Connecting → Connected  : Auth succeeds
//   Connecting → Backoff    : Connect/auth fails
//   Connected → Connected   : Handler accessed (resets idle timer)
//   Connected → Idle        : Idle timeout, no pending work
//   Connected → Draining    : Flush requested
//   Connected → Backoff     : WebSocket drops
//   Draining → Idle         : Pending RPCs complete
//   Backoff → Connecting    : Backoff expires, work pending
//   Backoff → Idle          : Backoff expires, no work pending
public class SteamConnection : IDisposable
{
    private const int MaxBackoffMs = 32_000;
    private const int ConnectTimeoutMs = 15_000;

    private readonly string _accountName;
    internal Sts2.SteamClientPrototype.SteamFriendDirectory FriendDirectory { get; }
    private string _refreshToken;
    private readonly int _defaultIdleTimeoutMs;

    private readonly SteamClient _client;
    private readonly CallbackManager _callbackManager;
    private readonly SteamUser _steamUser;
    private readonly SteamApps _steamApps;
    private readonly SteamContent _steamContent;
    private readonly SteamUnifiedMessages _unifiedMessages;

    private Thread _callbackThread;
    private volatile bool _callbackRunning;

    private readonly object _stateLock = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly ManualResetEventSlim _connectedGate = new(false);
    private Timer _idleTimer;
    private int _backoffMs;
    private Exception _connectError;
    private SteamLoginRequiredException _authenticationFailure;
    private volatile int _idleSuspendCount;
    private bool _shared;
    private volatile bool _disposed;
    private TaskCompletionSource _friendsReady = NewFriendsCompletion();
    private static TaskCompletionSource NewFriendsCompletion() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile bool _loggedOn;
    public bool IsAuthenticated => !_disposed && _loggedOn && State == ConnectionState.Connected && _client.IsConnected && _client.SteamID != null;

    private volatile ConnectionState _state = ConnectionState.Idle;
    public ConnectionState State { get => _state; private set => _state = value; }
    public ulong AppAccessToken { get; set; }

    public SteamClient Client => _client;
    public SteamConfiguration Configuration => _client.Configuration;

    public SteamApps Apps
    {
        get
        {
            EnsureConnected();
            return _steamApps;
        }
    }

    public SteamContent Content
    {
        get
        {
            EnsureConnected();
            return _steamContent;
        }
    }

    public SteamConnection(string accountName, string refreshToken, int idleTimeoutMs = 30_000)
    {
        _accountName = accountName;
        _refreshToken = refreshToken;
        _defaultIdleTimeoutMs = idleTimeoutMs;

        var config = SteamConfiguration.Create(b => b.WithProtocolTypes(ProtocolTypes.WebSocket)
            .WithDefaultPersonaStateFlags(SteamKitLobbyGateway.RequestedFlags));
        _client = new SteamClient(config);
        FriendDirectory = new Sts2.SteamClientPrototype.SteamFriendDirectory(_client);
        _callbackManager = new CallbackManager(_client);
        _steamUser = _client.GetHandler<SteamUser>();
        _steamApps = _client.GetHandler<SteamApps>();
        _steamContent = _client.GetHandler<SteamContent>();
        _unifiedMessages = _client.GetHandler<SteamUnifiedMessages>();
        _unifiedMessages.CreateService<Cloud>();

        _callbackManager.Subscribe<SteamClient.ConnectedCallback>(_ =>
        {
            if (_disposed) return;
            _steamUser.LogOn(SteamSessionPolicy.CreateLauncherLogOn(_accountName, _refreshToken));
        });

        _callbackManager.Subscribe<SteamClient.DisconnectedCallback>(cb =>
        {
            if (_disposed || cb.UserInitiated || _client.IsConnected) return;
            _loggedOn = false;
            if (State == ConnectionState.Connecting) {
                _connectError ??= new IOException("A conexão Steam terminou durante o login.");
                _connectedGate.Set();
            }
            if (State == ConnectionState.Connected)
            {
                PatchHelper.Log("[Connection] Dropped unexpectedly");
                EnterBackoff();
            }
        });

        _callbackManager.Subscribe<SteamUser.LoggedOnCallback>(cb =>
        {
            if (_disposed) return;
            if (cb.Result == EResult.OK) {
                _loggedOn = true;
                _connectedGate.Set();
            }
            else
            {
                _loggedOn = false;
                _connectError = cb.Result is EResult.AccessDenied or EResult.InvalidPassword or EResult.Expired or EResult.Revoked
                    ? new SteamLoginRequiredException(cb.Result)
                    : new InvalidOperationException($"Login failed: {cb.Result}");
                _connectedGate.Set();
            }
        });
        _callbackManager.Subscribe<SteamUser.LoggedOffCallback>(cb => {
            if (_disposed) return;
            var wasLoggedOn = _loggedOn;
            _loggedOn = false;
            // Teardown already invalidates the login before sending LogOff.
            // A queued acknowledgement of that old login must not fail a retry.
            if (!wasLoggedOn) return;
            _connectError = cb.Result is EResult.AccessDenied or EResult.InvalidPassword or EResult.Expired or EResult.Revoked
                ? new SteamLoginRequiredException(cb.Result)
                : new IOException("A Steam encerrou a sessão autenticada: " + cb.Result);
            if (_connectError is SteamLoginRequiredException rejected) _authenticationFailure = rejected;
            if (State == ConnectionState.Connecting) _connectedGate.Set();
            else if (State == ConnectionState.Connected) EnterBackoff();
        });
        _callbackManager.Subscribe<SteamFriends.FriendsListCallback>(_ => _friendsReady.TrySetResult());
    }

    internal void MarkShared() => _shared = true;
    internal void UpdateRefreshToken(string token)
    {
        lock (_stateLock) {
            _refreshToken = token;
            _authenticationFailure = null;
            _backoffMs = 0;
            if (State == ConnectionState.Backoff) State = ConnectionState.Idle;
        }
    }
    internal IDisposable Subscribe<T>(Action<T> callback) where T : CallbackMsg => _callbackManager.Subscribe(callback);
    internal Task WaitForFriendsAsync(CancellationToken token) => _friendsReady.Task.WaitAsync(TimeSpan.FromSeconds(15), token);
    internal async Task<SteamClient> GetAuthenticatedClientAsync(CancellationToken token)
    {
        await Task.Run(EnsureConnected).WaitAsync(token);
        if (!IsAuthenticated) throw new IOException("A sessão Steam não ficou autenticada.");
        return _client;
    }
    internal IDisposable HoldOpen()
    {
        SuspendIdleTimeout();
        return new Hold(this);
    }
    private sealed class Hold(SteamConnection owner) : IDisposable
    {
        private SteamConnection connection = owner;
        public void Dispose() => Interlocked.Exchange(ref connection, null)?.ResumeIdleTimeout();
    }

    // Sends a CCloud RPC. Connects on demand, resets idle timer, retries on
    // transient connection failure.
    public async Task<TResult> SendCloud<TRequest, TResult>(string method, TRequest request)
        where TRequest : ProtoBuf.IExtensible, new()
        where TResult : ProtoBuf.IExtensible, new()
    {
        EnsureConnected();
        ResetIdleTimer();

        await _sendLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var job = _unifiedMessages.SendMessage<TRequest, TResult>($"Cloud.{method}#1", request);
            var response = await job.ToTask().ConfigureAwait(false);
            if (response.Result != EResult.OK)
                throw new InvalidOperationException($"Cloud.{method} failed: {response.Result}");
            return response.Body;
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public void SuspendIdleTimeout()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SteamConnection));
        Interlocked.Increment(ref _idleSuspendCount);
        Interlocked.Exchange(ref _idleTimer, null)?.Dispose();
    }

    public void ResumeIdleTimeout()
    {
        if (Interlocked.Decrement(ref _idleSuspendCount) < 0) Interlocked.Exchange(ref _idleSuspendCount, 0);
        if (!_disposed && State == ConnectionState.Connected) ResetIdleTimer();
    }

    // Enters Draining state: waits for pending RPCs to complete, then disconnects.
    public void Flush()
    {
        lock (_stateLock)
        {
            if (_disposed || _idleSuspendCount > 0) return;
            if (State != ConnectionState.Connected)
                return;
            State = ConnectionState.Draining;
            _idleTimer?.Dispose();
            _idleTimer = null;
        }

        PatchHelper.Log("[Connection] Draining...");

        if (_sendLock.Wait(5000))
        {
            _sendLock.Release();
            Teardown();
            TransitionTo(ConnectionState.Idle);
            PatchHelper.Log("[Connection] Drain complete, disconnected");
        }
        else
        {
            PatchHelper.Log("[Connection] Drain timed out, forcing disconnect");
            Teardown();
            TransitionTo(ConnectionState.Idle);
        }
    }

    public void Dispose()
    {
        if (_shared) { Flush(); return; }
        ShutdownShared();
    }

    internal void ShutdownShared()
    {
        lock (_stateLock) {
            if (_disposed) return;
            _disposed = true;
            _idleTimer?.Dispose(); _idleTimer = null;
            Teardown(); State = ConnectionState.Idle;
        }
        _sendLock.Dispose();
        _connectedGate.Dispose();
    }

    private void EnsureConnected()
    {
        if (IsAuthenticated)
        {
            ResetIdleTimer();
            return;
        }

        lock (_stateLock)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SteamConnection));
            if (IsAuthenticated)
            {
                ResetIdleTimer();
                return;
            }

            if (_authenticationFailure != null)
                throw new SteamLoginRequiredException(_authenticationFailure.Result);

            if (State == ConnectionState.Backoff)
            {
                PatchHelper.Log($"[Connection] Waiting {_backoffMs}ms backoff before reconnect...");
                Monitor.Exit(_stateLock);
                Thread.Sleep(_backoffMs);
                Monitor.Enter(_stateLock);
                if (_disposed) throw new ObjectDisposedException(nameof(SteamConnection));
                // Another cloud/gameplay borrower may have authenticated while
                // this caller was outside the lock waiting for backoff.
                if (IsAuthenticated) { ResetIdleTimer(); return; }
                if (_authenticationFailure != null)
                    throw new SteamLoginRequiredException(_authenticationFailure.Result);
            }

            if (_client.IsConnected) Teardown();
            _connectError = null;
            FriendDirectory.Reset();
            _friendsReady = NewFriendsCompletion();
            _connectedGate.Reset();
            TransitionTo(ConnectionState.Connecting);

            try
            {
                StartCallbackThread();
                _client.Connect();
            }
            catch (Exception ex)
            {
                PatchHelper.Log($"[Connection] Connect failed: {ex.Message}");
                EnterBackoff();
                throw;
            }

            if (!_connectedGate.Wait(ConnectTimeoutMs))
            {
                PatchHelper.Log("[Connection] Connect timed out");
                Teardown();
                EnterBackoff();
                throw new TimeoutException("Steam connection timed out");
            }

            if (_connectError != null)
            {
                var error = _connectError;
                if (error is SteamLoginRequiredException rejected) _authenticationFailure = rejected;
                Teardown();
                EnterBackoff();
                throw error;
            }

            _backoffMs = 0;
            TransitionTo(ConnectionState.Connected);
            ResetIdleTimer();
            PatchHelper.Log("[Connection] Connected to Steam");
        }
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
            Name = "SteamConnectionCallbacks",
        };
        _callbackThread.Start();
    }

    private void Teardown()
    {
        _loggedOn = false;
        _callbackRunning = false;
        try
        {
            _steamUser?.LogOff();
        }
        catch { }
        try
        {
            _client?.Disconnect();
        }
        catch { }
        _callbackThread?.Join(2000);
        _callbackThread = null;
    }

    private void EnterBackoff()
    {
        _backoffMs = _backoffMs == 0 ? 2000 : Math.Min(_backoffMs * 2, MaxBackoffMs);
        TransitionTo(ConnectionState.Backoff);
    }

    private void ResetIdleTimer()
    {
        if (_idleSuspendCount > 0 || _disposed)
            return;

        var timer = new Timer(
            _ =>
            {
                lock (_stateLock) {
                  if (!_disposed && _idleSuspendCount == 0 && State == ConnectionState.Connected) {
                    PatchHelper.Log("[Connection] Idle timeout, disconnecting");
                    Teardown();
                    TransitionTo(ConnectionState.Idle);
                  }
                }
            },
            null,
            Timeout.Infinite,
            Timeout.Infinite
        );
        Interlocked.Exchange(ref _idleTimer, timer)?.Dispose();
        try { timer.Change(_defaultIdleTimeoutMs, Timeout.Infinite); }
        catch (ObjectDisposedException) { /* A concurrent reset superseded this timer. */ }
    }

    private void TransitionTo(ConnectionState newState)
    {
        State = newState;
    }
}

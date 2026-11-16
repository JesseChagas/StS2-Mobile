using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Sts2.SteamClientPrototype;

// A small C ABI keeps all Steam C++ structures and private keys in native code.
public sealed class NativeGns : IDisposable
{
    const string Library = "sts2_gns";
    private static readonly object Gate = new();
    private static bool active;
    private bool owns;
    private readonly byte[] signalBuffer = new byte[131072];
    private readonly byte[] receiveBuffer = new byte[1048576];
    private readonly byte[] eventBuffer = new byte[512];
    public static string LibraryPathOverride { get; set; } = "";
    public static void CheckAvailability()
    {
        if (sts_gns_abi_version() != 4)
            throw new InvalidOperationException("Networking library ABI is incompatible. Reinstall the signed APK update.");
        if (OperatingSystem.IsAndroid() && TransportRevision < 7)
            throw new InvalidOperationException("Biblioteca Android antiga: instale o APK assinado atualizado.");
    }

    public static int TransportRevision {
        get { try { return sts_gns_transport_revision(); } catch (EntryPointNotFoundException) { return 4; } }
    }

    public int LocalAddressCount() => sts_gns_local_address_count();
    public bool BeginClose() => sts_gns_begin_close() != 0;
    public void Listen(int peers) { var error = new byte[1024]; Require(sts_gns_host_listen(peers, error, error.Length), error); }
    public void AllowPeer(ulong peer, bool allow) => sts_gns_host_allow(peer, allow ? 1 : 0);
    public void SetHostClosed(bool closed) => sts_gns_host_set_closed(closed ? 1 : 0);
    public bool InputHostSignal(ulong sender, byte[] bytes) => sts_gns_host_input(sender, bytes, bytes.Length) != 0;
    public (ulong Peer, int State, int Reason, string Detail)? PollHostEvent() {
        var available = sts_gns_host_poll_event(out var peer, out var state, out var reason, eventBuffer, eventBuffer.Length);
        return available == 0 ? null : (peer, state, reason, Error(eventBuffer));
    }
    public void SendToPeer(ulong peer, byte[] bytes, int flags) {
        var result = sts_gns_host_send(peer, bytes, bytes.Length, flags);
        if (result != 1) throw new IOException("Falha no envio ao cliente Steam: " + result);
    }
    public (ulong Peer, byte[] Bytes, int Flags, int Channel)? ReceiveFromPeer() {
        var size = sts_gns_host_receive(out var peer, receiveBuffer, receiveBuffer.Length, out var flags, out var channel);
        if (size < 0) throw new IOException("Mensagem do cliente excedeu o limite do transporte.");
        return size == 0 ? null : (peer, receiveBuffer[..size], flags, channel);
    }
    public void ClosePeer(ulong peer, int reason, string detail, bool linger) => sts_gns_host_close_peer(peer, reason, detail, linger ? 1 : 0);

    static NativeGns() => NativeLibrary.SetDllImportResolver(typeof(NativeGns).Assembly, Resolve);

    private static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? path)
    {
        if (name != Library)
            return IntPtr.Zero;
        if (!string.IsNullOrEmpty(LibraryPathOverride))
            return NativeLibrary.Load(LibraryPathOverride);
        // The APK installs the ARM64 library beside the engine, outside assets.
        if (OperatingSystem.IsAndroid() && NativeLibrary.TryLoad("libsts2_gns.so", out var androidHandle))
            return androidHandle;
        var rid =
            OperatingSystem.IsWindows() ? "win-x64"
            : OperatingSystem.IsAndroid() ? "android-arm64"
            : "linux-x64";
        var file = OperatingSystem.IsWindows() ? "sts2_gns.dll" : "libsts2_gns.so";
        for (
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            directory != null;
            directory = directory.Parent
        )
        {
            var candidate = Path.Combine(directory.FullName, "runtimes", rid, "native", file);
            if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out var handle))
                return handle;
        }
        return IntPtr.Zero;
    }

    static string Error(byte[] text)
    {
        int end = Array.IndexOf(text, (byte)0);
        return Encoding.UTF8.GetString(text, 0, end < 0 ? text.Length : end);
    }

    static void Require(int success, byte[] error)
    {
        if (success <= 0)
            throw new InvalidOperationException(Error(error));
    }

    public NativeGns(ulong self, uint app)
    {
        lock (Gate)
        {
            if (active)
                throw new InvalidOperationException("Já existe um transporte nativo ativo.");
            if (sts_gns_abi_version() != 4)
                throw new InvalidOperationException(
                    "Biblioteca nativa incompatível com esta versão. Extraia o ZIP completo."
                );
            var error = new byte[1024];
            Require(sts_gns_init(self, app, error, error.Length), error);
            active = owns = true;
        }
    }

    public static void SelfTest()
    {
        lock (Gate)
        {
            if (active)
                throw new InvalidOperationException("Encerre o transporte antes do autoteste.");
            if (sts_gns_abi_version() != 4)
                throw new InvalidOperationException(
                    "Biblioteca nativa incompatível com esta versão. Extraia o ZIP completo."
                );
            var error = new byte[1024];
            Require(sts_gns_selftest(error, error.Length), error);
        }
    }
    public static void HostSelfTest() {
        lock (Gate) {
            if (active) throw new InvalidOperationException("Encerre o transporte antes do autoteste do host.");
            var error = new byte[1024]; Require(sts_gns_host_selftest(error, error.Length), error);
        }
    }

    public byte[] PublicKey()
    {
        var key = new byte[32];
        var error = new byte[1024];
        Require(sts_gns_public_key(key, key.Length, error, error.Length), error);
        return key;
    }

    public void AddAuthority(string certificate)
    {
        var error = new byte[1024];
        Require(sts_gns_add_ca(certificate, error, error.Length), error);
    }

    public void RevokeAuthority(ulong id) => sts_gns_revoke_ca(id);

    public void InstallCertificate(byte[] certificate, ulong authority, byte[] signature)
    {
        var error = new byte[1024];
        Require(
            sts_gns_install_cert(
                certificate,
                certificate.Length,
                authority,
                signature,
                signature.Length,
                error,
                error.Length
            ),
            error
        );
    }

    public uint Connect(ulong peer)
    {
        var error = new byte[1024];
        uint connection = sts_gns_connect(peer, error, error.Length);
        if (connection == 0)
            throw new InvalidOperationException(Error(error));
        return connection;
    }

    public void Pump() => sts_gns_pump();

    public bool InputSignal(byte[] bytes) => sts_gns_input_signal(bytes, bytes.Length) != 0;

    public (ulong Peer, byte[] Bytes)? PollSignal()
    {
        var bytes = signalBuffer;
        int size = sts_gns_poll_signal(out var peer, bytes, bytes.Length);
        if (size < 0)
            throw new InvalidDataException("Sinal nativo excede o limite.");
        return size == 0 ? null : (peer, bytes[..size]);
    }

    public (int State, int Reason, string Detail)? PollEvent()
    {
        var detail = eventBuffer;
        int available = sts_gns_poll_event(out int state, out int reason, detail, detail.Length);
        return available == 0 ? null : (state, reason, Error(detail));
    }

    public (byte[] Bytes, int Flags, int Channel)? Receive()
    {
        var bytes = receiveBuffer;
        int size = sts_gns_receive(bytes, bytes.Length, out int flags, out int channel);
        if (size == -1)
            throw new IOException("A conexão nativa não permite receber mensagens.");
        if (size < 0)
            throw new InvalidDataException("Mensagem de jogo excede o limite do receptor.");
        return size == 0 ? null : (bytes[..size], flags, channel);
    }

    public void Send(byte[] bytes, int flags)
    {
        int result = sts_gns_send(bytes, bytes.Length, flags);
        if (result != 1)
            throw new IOException("Falha no envio GNS: " + result);
    }

    public void Dispose()
    {
        lock (Gate)
        {
            if (!owns)
                return;
            sts_gns_close();
            owns = active = false;
        }
    }

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    static extern int sts_gns_abi_version();

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    static extern int sts_gns_transport_revision();

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    static extern int sts_gns_local_address_count();

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    static extern int sts_gns_begin_close();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern int sts_gns_host_selftest([Out] byte[] error, int cap);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern int sts_gns_host_listen(int peers, [Out] byte[] error, int cap);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern void sts_gns_host_allow(ulong peer, int allow);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern void sts_gns_host_set_closed(int closed);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern int sts_gns_host_input(ulong sender, byte[] bytes, int length);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern int sts_gns_host_poll_event(out ulong peer, out int state, out int reason, [Out] byte[] detail, int cap);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern int sts_gns_host_send(ulong peer, byte[] bytes, int length, int flags);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern int sts_gns_host_receive(out ulong peer, [Out] byte[] bytes, int cap, out int flags, out int channel);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern void sts_gns_host_close_peer(ulong peer, int reason, [MarshalAs(UnmanagedType.LPUTF8Str)] string detail, int linger);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    static extern int sts_gns_init(ulong self, uint app, [Out] byte[] error, int capacity);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    static extern int sts_gns_public_key(
        [Out] byte[] key,
        int size,
        [Out] byte[] error,
        int capacity
    );

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    static extern int sts_gns_add_ca(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string certificate,
        [Out] byte[] error,
        int capacity
    );

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    static extern void sts_gns_revoke_ca(ulong id);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    static extern int sts_gns_install_cert(
        byte[] cert,
        int length,
        ulong authority,
        byte[] signature,
        int sigLength,
        [Out] byte[] error,
        int capacity
    );

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    static extern uint sts_gns_connect(ulong peer, [Out] byte[] error, int capacity);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    static extern int sts_gns_input_signal(byte[] bytes, int size);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    static extern void sts_gns_pump();

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    static extern int sts_gns_poll_signal(out ulong peer, [Out] byte[] bytes, int capacity);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    static extern int sts_gns_poll_event(
        out int state,
        out int reason,
        [Out] byte[] detail,
        int capacity
    );

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    static extern int sts_gns_receive(
        [Out] byte[] bytes,
        int capacity,
        out int flags,
        out int channel
    );

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    static extern int sts_gns_send(byte[] bytes, int size, int flags);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    static extern void sts_gns_close();

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    static extern int sts_gns_selftest([Out] byte[] error, int capacity);
}

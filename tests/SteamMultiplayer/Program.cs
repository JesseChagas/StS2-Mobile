using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Connection;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Platform;
using STS2Mobile.Steam.Multiplayer;

// Tests use the unmodified, user-supplied game assembly. Only engine environment
// queries are stubbed because there is no running Godot process in this harness.
var harmony = new Harmony("sts2.mobile.steam.integration.tests");
harmony.Patch(AccessTools.Method(typeof(OS), nameof(OS.GetCmdlineArgs)), prefix: new HarmonyMethod(typeof(EngineEnvironment), nameof(EngineEnvironment.Args)));
harmony.Patch(AccessTools.Method(typeof(OS), nameof(OS.HasFeature)), prefix: new HarmonyMethod(typeof(EngineEnvironment), nameof(EngineEnvironment.Feature)));
harmony.Patch(AccessTools.Method(typeof(MegaCrit.Sts2.Core.Logging.ConsoleLogPrinter), "Print"), prefix: new HarmonyMethod(typeof(EngineEnvironment), nameof(EngineEnvironment.Print)));
harmony.Patch(AccessTools.Method(typeof(Godot.Time), nameof(Godot.Time.GetTicksMsec)), prefix: new HarmonyMethod(typeof(EngineEnvironment), nameof(EngineEnvironment.Time)));
var version = new PeerVersionInfo { version = "v0.111.0", branch = PlatformBranch.Production, idDatabaseHash = 1234567,
 gameplayAffectingMods = [], otherMods = [] };
var writer = new PacketWriter();
HandshakeManager.WriteHandshakeMessage(76561198000000002, version, writer);
var packet = writer.Buffer.AsSpan(0, writer.BytePosition).ToArray();
if (!packet.AsSpan(0,4).SequenceEqual("STS2"u8)) throw new Exception("Original magic changed.");
var reader = new PacketReader(); reader.Reset(packet); var magic = new byte[4]; reader.ReadBytes(magic,4);
var decoded = new PeerVersionInfo();
if (!decoded.TryDeserialize(reader) || decoded.version != version.version || decoded.idDatabaseHash != version.idDatabaseHash)
 throw new Exception("Original game's version serialization failed roundtrip.");
Console.WriteLine("PASS: original game handshake serialization roundtrip, active buffer length and magic.");

foreach (var scenario in new[] { "match", "version", "hash", "mods", "magic" }) {
 bool mismatch=scenario!="match";
 var clientVersion=version;
 if(scenario=="version") clientVersion.version="v0.112.0";
 if(scenario=="hash") clientVersion.idDatabaseHash++;
 if(scenario=="mods") clientVersion.gameplayAffectingMods=["different-gameplay-mod"];
 var client=new NetClientGameService(clientVersion);
 var wire=new TestWire(client); client.Initialize(wire,PlatformType.Steam);
 bool accepted=false, rejected=false;
 client.ConnectedToHost+=()=>accepted=true;
 NetErrorInfo? failure = null;
 client.ConnectionFailed+=info=>{rejected=true;failure=info;};
 client.OnConnectedToHost();
 var incoming = (byte[])packet.Clone();
 if(scenario=="magic") incoming[0] = 0;
 client.OnPacketReceived(wire.HostNetId,incoming,NetTransferMode.Reliable,-1);
 if(mismatch) { if(!rejected || accepted) throw new Exception("Mismatched version was not rejected."); }
 else { if(!accepted || rejected || !client.IsConnected || wire.Sent.Count!=1) throw new Exception("Real game handshake did not complete.");
  if(!wire.Sent[0].AsSpan(0,4).SequenceEqual("STS2"u8)) throw new Exception("Game did not answer with handshake."); }
 if (failure is { } error) {
  var summary = HandshakeDiagnostics.Describe(error, false);
  var details = HandshakeDiagnostics.Details(error);
  if(!details.Contains("local-game-version=") || !details.Contains("host-game-version=")) throw new Exception("Peer versions missing from original handshake failure.");
  if(scenario=="version" && (!summary.Contains("celular v0.112.0; host v0.111.0") || !details.Contains("model-hash=1234567"))) throw new Exception("Actual version mismatch was not explained.");
  if(scenario=="hash" && !summary.Contains("dados do jogo diferem")) throw new Exception("Model mismatch was misreported as a version difference.");
  if(scenario=="mods" && !summary.Contains("mods de gameplay")) throw new Exception("Missing gameplay mods not explained.");
  if(scenario=="magic" && !summary.Contains("Não foi possível ler")) throw new Exception("Unreadable handshake was reported as a known host version.");
 }
 Console.WriteLine(mismatch?$"PASS: original client rejects incompatible host ({scenario}); actual reason/peer details preserved.":"PASS: original NetClientGameService consumes host handshake, answers reliably and becomes connected.");
 client.Disconnect(NetError.Quit,true);
}

static class EngineEnvironment {
 public static bool Print(string text){Console.WriteLine("GAME: "+text);return false;}
 public static bool Time(ref ulong __result){__result=(ulong)System.Environment.TickCount64;return false;}
 public static bool Args(ref string[] __result){__result=["--headless"];return false;}
 public static bool Feature(ref bool __result){__result=false;return false;}
}
sealed class TestWire(INetClientHandler handler) : NetClient(handler) {
 public List<byte[]> Sent {get;}=[];
 private bool connected=true;
 public override bool IsConnected=>connected;
 public override ulong NetId=>76561198000000001;
 public override ulong HostNetId=>76561198000000002;
 public override void Update(){}
 public override void SendMessageToHost(byte[] bytes,int length,NetTransferMode mode,int channel=0){
  if(mode!=NetTransferMode.Reliable) throw new Exception("Handshake not reliable.");Sent.Add(bytes.AsSpan(0,length).ToArray()); }
 public override void DisconnectFromHost(NetError reason,bool now=false) {if(!connected)return;connected=false;_handler.OnDisconnectedFromHost(HostNetId,new NetErrorInfo(reason,true));}
 public override string? GetRawLobbyIdentifier()=>null;
}

using System.Reflection;
using STS2Mobile.Steam;
void Check(bool condition,string message){if(!condition)throw new Exception(message);}
T Field<T>(SteamConnection c,string name)=>(T)typeof(SteamConnection).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(c)!;
var launcher=SharedSteamConnection.Get("fixture-account","old-fixture-token");
var cloud=SharedSteamConnection.Get("fixture-account","old-fixture-token");
Check(ReferenceEquals(launcher,cloud),"Launcher and cloud created different connections");
Check(!launcher.IsAuthenticated,"Saved credential was treated as a live login");
// A server logoff can arrive while the CM socket is still present. Exercise the
// actual SteamKit packet/callback path, rather than relying on socket state.
typeof(SteamConnection).GetField("_loggedOn",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(launcher,true);
typeof(SteamConnection).GetField("_state",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(launcher,ConnectionState.Connected);
var off=new SteamKit2.ClientMsgProtobuf<SteamKit2.Internal.CMsgClientLoggedOff>(SteamKit2.EMsg.ClientLoggedOff);
off.Body.eresult=(int)SteamKit2.EResult.LoggedInElsewhere;
launcher.Client.GetHandler<SteamKit2.SteamUser>()!.HandleMsg(new SteamKit2.PacketClientMsgProtobuf(SteamKit2.EMsg.ClientLoggedOff,off.Serialize()));
Field<SteamKit2.CallbackManager>(launcher,"_callbackManager").RunCallbacks();
Check(!Field<bool>(launcher,"_loggedOn")&&launcher.State==ConnectionState.Backoff,"Server logoff retained an authenticated session");
typeof(SteamConnection).GetField("_state",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(launcher,ConnectionState.Idle);
SharedSteamConnection.KeepForGame("fixture-account","old-fixture-token");
var gameplay=SharedSteamConnection.Get("fixture-account","old-fixture-token");
var hold=gameplay.HoldOpen();
Check(ReferenceEquals(launcher,gameplay)&&Field<int>(gameplay,"_idleSuspendCount")==2,"Gameplay did not borrow existing connection");
cloud.Flush();cloud.Dispose();
Check(!Field<bool>(gameplay,"_disposed")&&Field<int>(gameplay,"_idleSuspendCount")==2,"Cloud disposal closed gameplay session");
hold.Dispose();hold.Dispose();
Check(Field<int>(gameplay,"_idleSuspendCount")==1,"Gameplay lease was not idempotent or released the app hold");
typeof(SteamConnection).GetField("_authenticationFailure",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(gameplay,new SteamLoginRequiredException(SteamKit2.EResult.AccessDenied));
SharedSteamConnection.Get("fixture-account","old-fixture-token");
try { await gameplay.GetAuthenticatedClientAsync(CancellationToken.None); throw new Exception("Rejected credential was retried"); }
catch(SteamLoginRequiredException e) { Check(e.Result==SteamKit2.EResult.AccessDenied,"Lost actual rejection result"); }
var fresh=SharedSteamConnection.UpdateCredentials("fixture-account","new-fixture-token");
Check(Field<SteamLoginRequiredException>(fresh,"_authenticationFailure")==null,"Interactive renewal did not clear rejected credential");
Check(ReferenceEquals(fresh,launcher),"Renewal replaced the connection still used by cloud");
SharedSteamConnection.Get("fixture-account","old-fixture-token");
Check(Field<string>(fresh,"_refreshToken")=="new-fixture-token","Stale cloud closure reverted renewed token");
Parallel.For(0,50,_=>Check(ReferenceEquals(SharedSteamConnection.Get("fixture-account","old-fixture-token"),fresh),"Parallel consumer duplicated session"));
SharedSteamConnection.Reset();
Check(Field<bool>(fresh,"_disposed"),"Explicit signout did not close owned connection");
var next=SharedSteamConnection.Get("fixture-account","new-fixture-token");Check(!ReferenceEquals(next,fresh),"Signout retained disposed session");SharedSteamConnection.Reset();SharedSteamConnection.Reset();
Console.WriteLine("PASS: one launcher/cloud/gameplay connection, no fake cached login, idempotent leases, flush protection, safe token renewal, stale token guard, concurrent reuse, explicit logout.");
namespace STS2Mobile {internal static class PatchHelper{public static void Log(string value){}}}
static class SteamKitLobbyGateway {public const SteamKit2.EClientPersonaStateFlag RequestedFlags=SteamKit2.EClientPersonaStateFlag.Status|SteamKit2.EClientPersonaStateFlag.Presence|SteamKit2.EClientPersonaStateFlag.GameExtraInfo|SteamKit2.EClientPersonaStateFlag.GameDataBlob;}

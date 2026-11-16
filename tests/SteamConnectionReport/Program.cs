using STS2Mobile.Steam.Multiplayer;
void Check(bool ok,string message){if(!ok)throw new Exception(message);}
const string secret="test-refresh-token-never-display";
STS2Mobile.Patches.LauncherPatches.SavedRefreshToken=secret;
ConnectionReport.Begin(123);
ConnectionReport.Note("native-revision=5; local-addresses=2; "+secret);
Check(ConnectionReport.Snapshot.Stage=="prepare"&&ConnectionReport.Diagnostics.Contains("local-addresses=2"),"Native details changed the current stage or disappeared");
Check(!ConnectionReport.Diagnostics.Contains(secret),"Authentication leaked in transport details");
ConnectionReport.Step("android-library","Loading library","Carregando biblioteca");
ConnectionReport.Fail(new DllNotFoundException("A failure with "+secret));
Check(ConnectionReport.Snapshot.Failed&&!ConnectionReport.Snapshot.Busy,"Failure state not published");
Check(ConnectionReport.Diagnostics.Contains("android-library")&&ConnectionReport.Diagnostics.Contains("DllNotFoundException"),"Actual stage/type lost");
Check(!ConnectionReport.Diagnostics.Contains(secret)&&!ConnectionReport.Snapshot.Status.Contains(secret)&&!STS2Mobile.PatchHelper.Last.Contains(secret),"Authentication value leaked");
Check(!ConnectionReport.ConsumePopup(999),"Suppressed error for another host");
Check(ConnectionReport.ConsumePopup(123)&&!ConnectionReport.ConsumePopup(123),"Popup suppression not one-shot");
ConnectionReport.Begin(456);
ConnectionReport.Fail(new STS2Mobile.Steam.SteamLoginRequiredException(SteamKit2.EResult.AccessDenied));
Check(ConnectionReport.Snapshot.RequiresLogin,"Steam credential rejection did not expose recovery");
ConnectionReport.Begin(456);
Check(!ConnectionReport.Snapshot.Failed&&ConnectionReport.Snapshot.Busy,"Retry retained failure state");
for(int i=0;i<100;i++)ConnectionReport.Step("step-"+i,"step","etapa");
Check(!ConnectionReport.Diagnostics.Contains("step-0\n")&&ConnectionReport.Diagnostics.Contains("step-99"),"Diagnostic history is not bounded");
ConnectionReport.Cancelled();Check(!ConnectionReport.Snapshot.Busy&&!ConnectionReport.Snapshot.Failed,"Cancel state invalid");
ConnectionReport.Connected();Check(ConnectionReport.Snapshot.Stage=="connected"&&!ConnectionReport.Snapshot.Busy,"Connected state invalid");
Console.WriteLine("PASS: actual error/stage, token redaction in copy/log/status, one-shot host-scoped popup suppression, bounded history, retry and cancel.");
namespace STS2Mobile {internal static class PatchHelper {public static string Last="";public static void Log(string s)=>Last=s;}}
namespace STS2Mobile.Patches {internal static class LauncherPatches {public static string SavedRefreshToken="";}}
namespace STS2Mobile.Launcher.Design {internal static class LauncherPreferences {public static string Language="pt";}}

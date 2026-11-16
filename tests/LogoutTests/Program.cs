using STS2Mobile.Launcher;
using STS2Mobile.Steam;
class Program {
 static void Check(bool c,string m) { if(!c)throw new Exception(m); }
 static void Main() {
  var root=Path.Combine(Path.GetTempPath(),"sts-logout-"+Guid.NewGuid()); Directory.CreateDirectory(root);
  try {
   var game=Path.Combine(root,"game"); var cache=Path.Combine(root,".godot/mono/publish/arm64"); var saves=Path.Combine(root,".godot/app_userdata/sts2");
   Directory.CreateDirectory(game);Directory.CreateDirectory(cache);Directory.CreateDirectory(saves);
   File.WriteAllText(Path.Combine(game,"SlayTheSpire2.pck"),"game");File.WriteAllText(Path.Combine(cache,"sts2.dll"),"cache");File.WriteAllText(Path.Combine(saves,"save.json"),"save");
   File.WriteAllText(Path.Combine(root,"steam_credentials.enc"),"old-account");File.WriteAllText(Path.Combine(root,"ownership_verified.enc"),"old-ownership");File.WriteAllText(Path.Combine(root,"launcher_appearance.json"),"prefs");
   LogoutData.RemoveAccountFiles(root);
   Check(Directory.Exists(game)&&Directory.Exists(cache),"Keep files removed game");Check(!File.Exists(Path.Combine(root,"steam_credentials.enc"))&&!File.Exists(Path.Combine(root,"ownership_verified.enc")),"Old account persisted");
   if(!OperatingSystem.IsWindows()) {
   Directory.Move(cache,cache+"-outside");Directory.CreateSymbolicLink(cache,cache+"-outside");
   bool rejected=false;try{LogoutData.DeleteDownloadedGame(root);}catch(IOException){rejected=true;}
   Check(rejected&&File.Exists(Path.Combine(game,"SlayTheSpire2.pck")),"Linked cache not rejected or game rename not rolled back");
   Directory.Delete(cache);Directory.Move(cache+"-outside",cache);
   } else Console.WriteLine("SKIP: symbolic-link fixture requires Windows developer mode/admin; production link guard remains enabled.");
   LogoutData.DeleteDownloadedGame(root);
   Check(!Directory.Exists(game)&&!Directory.Exists(cache),"Game/cache left behind");Check(File.ReadAllText(Path.Combine(saves,"save.json"))=="save"&&File.Exists(Path.Combine(root,"launcher_appearance.json")),"Logout destroyed saves/preferences");LogoutData.DeleteDownloadedGame(root);
   using(var started=new ManualResetEventSlim())using(var release=new ManualResetEventSlim())using(var queue=new CloudWriteQueue()) {
    queue.Enqueue(()=>{started.Set();release.Wait();});Check(started.Wait(3000),"Writer failed to start");Check(queue.Count==1&&!queue.WaitForIdle(50),"Dequeued but running upload reported complete");release.Set();Check(queue.WaitForIdle(3000)&&queue.Count==0,"Writer failed to finish");queue.Enqueue(()=>throw new Exception("expected"));Check(queue.WaitForIdle(3000),"Throwing upload left queue busy");
   }
   Console.WriteLine("PASS: keep/delete, credential and ownership removal, save preservation, symlink rejection/rollback, idempotence, in-flight cloud write drain.");
  }finally{Directory.Delete(root,true);}
 }
}
namespace STS2Mobile { public static class PatchHelper {public static void Log(string s){} } }

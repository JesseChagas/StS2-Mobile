using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Debug;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Platform.Null;
using STS2Mobile.Patches;

public partial class Preview : Node
{
 private static readonly string FixtureDirectory=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"sts2-release-reader-"+Guid.NewGuid());
 public static bool FixtureDataDir(ref string __result){__result=FixtureDirectory;return false;}
 public override void _Ready() {
  var dir=System.IO.Path.Combine(FixtureDirectory,"game");
  var path=System.IO.Path.Combine(dir,"release_info.json");
  try {
   var harmony=new Harmony("sts2.release.path.test");
   harmony.Patch(AccessTools.Method(typeof(OS),nameof(OS.GetDataDir)),prefix:new HarmonyMethod(typeof(Preview),nameof(FixtureDataDir)));
   if(OS.GetDataDir()!=FixtureDirectory)throw new Exception("Could not isolate test metadata directory");
   System.IO.Directory.CreateDirectory(dir);System.IO.File.Delete(path);
   // Force the original desktop lookup to cache the missing file first.
   var before=NGame.GetGameVersion();
   System.IO.File.WriteAllText(path,"{\"version\":\"v0.113.1\",\"branch\":\"public-beta\",\"commit\":\"fixture-local-build\",\"date\":\"2026-11-15 00:00:00 +00:00\",\"main_assembly_hash\":123}");
   ReleaseMetadataPatches.Apply(harmony);
   ReleaseMetadataPatches.PrepareForGame();
   var after=NGame.GetGameVersion();
   if(after!="v0.113.1"||ReleaseInfoManager.Instance.SemVer==null)throw new Exception("Original ReleaseInfoManager did not load the installed version: "+after);
   harmony.Patch(AccessTools.Method(typeof(NullPlatformUtilStrategy),"GetPlatformBranch"),prefix:new HarmonyMethod(typeof(ReleaseMetadataPatches),nameof(ReleaseMetadataPatches.InstalledBranch)));
   var strategy=(NullPlatformUtilStrategy)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(NullPlatformUtilStrategy));
   if(strategy.GetPlatformBranch()!=PlatformBranch.PublicBeta)throw new Exception("Original platform strategy did not report installed beta branch");
   GD.Print("RELEASE_METADATA_PASS: cached="+before+"; original-reader="+after+"; branch="+strategy.GetPlatformBranch());
   SteamHostingPatches.Apply(harmony);
   foreach(var type in new[]{typeof(MegaCrit.Sts2.Core.Nodes.Screens.MainMenu.NMultiplayerHostSubmenu),typeof(MegaCrit.Sts2.Core.Nodes.Screens.MainMenu.NMultiplayerSubmenu)}) {
    var method=AccessTools.Method(type,"StartHostAsync");
    var machine=method.GetCustomAttributes(typeof(System.Runtime.CompilerServices.AsyncStateMachineAttribute),false).Cast<System.Runtime.CompilerServices.AsyncStateMachineAttribute>().Single().StateMachineType;
    var move=AccessTools.Method(machine,"MoveNext");
    if(!Harmony.GetPatchInfo(move).Transpilers.Any(x=>x.owner==harmony.Id)||!Harmony.GetPatchInfo(method).Prefixes.Any(x=>x.owner==harmony.Id))throw new Exception("Original new/resume host flow was not patched");
   }
   if(!SteamHostingPatches.UseSteamHosting)throw new Exception("Steam is not the primary host option");
   GD.Print("HOST_FLOW_PATCH_PASS: original new/resume state machines compiled with Steam/LAN selection and mobile host prefix.");
   GetTree().Quit();
  }catch(Exception e){GD.PrintErr(e);GetTree().Quit(1);}
  finally{if(System.IO.Directory.Exists(FixtureDirectory))System.IO.Directory.Delete(FixtureDirectory,true);}
 }
}

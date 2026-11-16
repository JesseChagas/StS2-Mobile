using STS2Mobile.Steam;
using System.Text.Json;

void Check(bool value,string message){if(!value)throw new Exception(message);}
var root=Path.Combine(Path.GetTempPath(),"sts2-release-"+Guid.NewGuid());
try {
 Directory.CreateDirectory(Path.Combine(root,"game"));
 var release=InstalledReleaseMetadata.ReleasePath(root);
 var paths=InstalledReleaseMetadata.SearchPaths(root,new[]{"/desktop/release_info.json",release});
 Check(paths.Length==2&&paths[0]==release,"Installed release path was not first or desktop fallback was lost");
 Check(InstalledReleaseMetadata.InstalledBranch(root)==null,"Missing metadata invented a branch");
 File.WriteAllText(release,"{\"version\":\"v0.113.1\",\"branch\":\"public-beta\"}");
 Check(InstalledReleaseMetadata.InstalledBranch(root)=="public-beta","Installed beta metadata was ignored");
 // A preference change is not an installation change.
 File.WriteAllText(Path.Combine(root,"game_branch"),"public");
 Check(InstalledReleaseMetadata.InstalledBranch(root)=="public-beta","Selected preference replaced installed branch");
 File.WriteAllText(release,"{\"version\":\"v0.113.1\",\"branch\":\"source-release-branch\"}");
 Check(InstalledReleaseMetadata.InstalledBranch(root)==null,"Unknown source branch was fabricated");
 InstalledReleaseMetadata.RecordVerifiedBranch(root,"public-beta");
 Check(InstalledReleaseMetadata.InstalledBranch(root)=="public-beta","Verified distribution branch was not recorded");
 File.WriteAllText(release,"{\"version\":\"v0.114.0\",\"branch\":\"public\"}");
 Check(InstalledReleaseMetadata.InstalledBranch(root)=="public","Stale verified marker overrode a different release file");
 File.WriteAllText(release,"not-json");
 Check(InstalledReleaseMetadata.InstalledBranch(root)==null,"Corrupt metadata invented a branch");
 Console.WriteLine("PASS: installed release path precedence, desktop fallback, actual branch, preference independence, verified branch binding, stale/corrupt/missing metadata.");
}finally{if(Directory.Exists(root))Directory.Delete(root,true);}

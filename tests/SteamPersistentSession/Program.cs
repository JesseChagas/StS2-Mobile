using SteamKit2.Authentication;
const string fixture="not-a-real-token";
var auth = new AuthSessionDetails { IsPersistentSession = SteamSessionPolicy.PersistentLauncherSession };
var launcher = SteamSessionPolicy.CreateLauncherLogOn("fixture-account",fixture);
if(!auth.IsPersistentSession || !launcher.ShouldRememberPassword || launcher.ShouldRememberPassword != auth.IsPersistentSession)
 throw new Exception("Persistent credential/logon mismatch (AccessDenied regression).");
if(launcher.Username!="fixture-account"||launcher.AccessToken!=fixture||launcher.LoginID.GetValueOrDefault()==0)
 throw new Exception("Account, token or explicit LoginID changed.");
if(!string.IsNullOrEmpty(launcher.Password))throw new Exception("Password introduced into token-only logon.");
var ephemeral=SteamSessionPolicy.CreateLogOn("fixture-account",fixture,persistentSession:false);
if(ephemeral.ShouldRememberPassword)throw new Exception("Temporary credential now uses persistent mode.");
bool rejected=false;try{SteamSessionPolicy.ValidateTarget(1,1);}catch(InvalidOperationException){rejected=true;}
if(!rejected)throw new Exception("Self-host identity guard lost.");
Console.WriteLine("PASS: persistent auth and reusable-token logon agree; temporary sessions remain temporary; explicit LoginID, no password, identity guard preserved.");

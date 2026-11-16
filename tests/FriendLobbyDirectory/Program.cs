using SteamKit2;
using SteamKit2.Internal;
using Sts2.SteamClientPrototype;

void Check(bool condition,string message){if(!condition)throw new Exception(message);}
var directory=new SteamFriendDirectory(new SteamClient());
void Persona(ulong id,EClientPersonaStateFlag flags,uint? app,ulong? lobby,EPersonaState state=EPersonaState.Online){
 var message=new ClientMsgProtobuf<CMsgClientPersonaState>(EMsg.ClientPersonaState);
 message.Body.status_flags=(uint)flags;
 var friend=new CMsgClientPersonaState.Friend{friendid=id,persona_state=(uint)state};
 if(app.HasValue)friend.game_played_app_id=app.Value;
 if(lobby.HasValue)friend.game_lobby_id=lobby.Value;
 message.Body.friends.Add(friend);
 directory.HandleMsg(new PacketClientMsgProtobuf(EMsg.ClientPersonaState,message.Serialize()));
}
var game=EClientPersonaStateFlag.GameDataBlob|EClientPersonaStateFlag.GameExtraInfo;
Persona(10,game,2868840,100);
Check(directory.TryGetCurrent(10,out var room)&&room.LobbyId==100,"Initial presence was not retained for future joins");
Persona(10,EClientPersonaStateFlag.Presence,null,null,EPersonaState.Offline);
Check(directory.TryGetCurrent(10,out room)&&room.LobbyId==100,"Offline-only delta erased an advertised room");
Persona(10,game,null,null,EPersonaState.Offline);
Check(directory.TryGetCurrent(10,out room)&&room.AppId==0,"Explicit empty game presence was ignored");
var candidates=directory.Candidates(TimeSpan.FromMinutes(2));
Check(candidates.Length==1&&candidates[0].LobbyId==100,"Temporary empty presence erased candidate needed for authoritative revalidation");
Persona(11,game,2868840,101);
Check(directory.Candidates(TimeSpan.FromMinutes(2)).Length==2,"Other friend's room overwrote first friend");
Persona(10,game,2868840,102);
Check(directory.Candidates(TimeSpan.FromMinutes(2)).Single(x=>x.FriendId==10).LobbyId==102,"Changed lobby reused old advertisement");
Check(directory.Candidates(TimeSpan.FromTicks(-1)).Length==0,"Expired advertisements remain usable candidates");
Persona(11,game,999,0);
Check(directory.Candidates(TimeSpan.FromMinutes(2)).All(x=>x.FriendId!=11),"Explicit change to another game retained a misleading candidate");
directory.Reset();Check(directory.Candidates(TimeSpan.FromMinutes(2)).Length==0&&!directory.TryGetCurrent(10,out _),"New account/session retained old presence");
Console.WriteLine("PASS: initial persona survives join lifetimes, offline delta preserves room, temporary empty data becomes revalidation-only candidate, changed lobbies, independent friends, expiry and session reset.");

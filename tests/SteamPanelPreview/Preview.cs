using Godot;
using STS2Mobile.Steam.Multiplayer;
public partial class Preview : Node {
 public override async void _Ready(){try{
  var args=OS.GetCmdlineUserArgs();var scenario=args.Length==0?"idle":args[0];
  var root=new Control {Size=GetViewport().GetVisibleRect().Size};AddChild(root);root.Size=GetViewport().GetVisibleRect().Size;
  var bg=new ColorRect {Color=new Color("#172231")};root.AddChild(bg);bg.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
  var heading=new Label {Text="MULTIPLAYER",Position=new Vector2(0,25),Size=new Vector2(1280,50),HorizontalAlignment=HorizontalAlignment.Center};heading.AddThemeFontSizeOverride("font_size",30);root.AddChild(heading);heading.Position=new Vector2(0,25);heading.Size=new Vector2(1280,50);heading.ZIndex=2;
  var panel=new SteamMultiplayerPanel(false);root.AddChild(panel);panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);panel.HostInput.Text="76561198000000003";panel.Layout();
  if(scenario=="launcher-repair") {
   STS2Mobile.Launcher.Design.LauncherPreferences.Configure(true,"pt");
   panel.Visible=false;heading.Text="ATUALIZAR JOGO";
   var scroll=new ScrollContainer{Position=new Vector2(232,90),Size=new Vector2(816,450),HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled};root.AddChild(scroll);
   var actions=new STS2Mobile.Launcher.Sections.ActionSection(.72f){SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};scroll.AddChild(actions);actions.ShowLaunch("Restart to play",false,true);
   int requests=0;actions.VerifyFilesPressed+=()=>requests++;
   var buttons=actions.FindChildren("*","Button",true,false).OfType<Button>().ToArray();
   buttons.Single(b=>b.Text=="Mais opções").EmitSignal(BaseButton.SignalName.Pressed);
   var verify=buttons.Single(b=>b.Text=="Verificar arquivos do jogo");
   if(!verify.IsVisibleInTree())throw new Exception("Repair action is hidden");
   actions.SetPushPullDisabled(true);if(!verify.Disabled)throw new Exception("Repair remained enabled during another operation");
   actions.SetPushPullDisabled(false);if(verify.Disabled)throw new Exception("Repair did not become available again");
   verify.EmitSignal(BaseButton.SignalName.Pressed);if(requests!=1)throw new Exception("Repair request was not delivered exactly once");
   GD.Print("LAUNCHER_REPAIR_UI_PASS");
  }
  System.Threading.Tasks.Task<bool> renewal=null;
  if(scenario=="reauth"){
   STS2Mobile.Launcher.Design.LauncherPreferences.Configure(true,"pt");
   var dialogType=typeof(SteamMultiplayerPanel).Assembly.GetType("STS2Mobile.Steam.Multiplayer.SteamReauthenticationDialog")!;
   renewal=(System.Threading.Tasks.Task<bool>)dialogType.GetMethod("ShowAsync")!.Invoke(null,new object[]{root,"fixture-account"})!;
  }
  if(scenario=="needs-login"){panel.SetStatus("A Steam precisa renovar o login desta conta (AccessDenied).",true,true);panel.SetNeedsLogin(true);}
  if(scenario=="friends") {
   panel.SetFriendRooms(new[]{new SteamHostRoom(76561198000000003,100,"Amigo com sala"),new SteamHostRoom(76561198000000002,101,"Outro anfitrião")});
   if(panel.HostInput.IsVisibleInTree())throw new Exception("Manual ID is visible in primary discovery view");
   var room=panel.FindChildren("*","Button",true,false).OfType<Button>().Single(b=>b.Text=="Outro anfitrião");room.EmitSignal(BaseButton.SignalName.Pressed);
   if(panel.HostInput.Text!="76561198000000002")throw new Exception("Friend card selected wrong host");
   panel.SetBusy(true);if(!room.Disabled)throw new Exception("Friend card not locked while joining");panel.SetBusy(false);
   panel.OpenManualEntry();if(!panel.HostInput.IsVisibleInTree())throw new Exception("Manual ID dialog did not open");
   panel.FindChildren("*","Button",true,false).OfType<Button>().Single(b=>b.Text=="Voltar").EmitSignal(BaseButton.SignalName.Pressed);
   if(panel.HostInput.IsVisibleInTree())throw new Exception("Manual ID dialog did not close");
   GD.Print("FRIEND_CARDS_PASS");
  }
  if(scenario=="manual")panel.OpenManualEntry();
  if(scenario=="host-choice") {
   var type=typeof(SteamMultiplayerPanel).Assembly.GetType("STS2Mobile.Steam.Multiplayer.SteamHostChoiceDialog")!;
   var choice=(Task<bool?>)type.GetMethod("ShowAsync")!.Invoke(null,new object[]{root})!;
   for(int i=0;i<3;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
   var dialog=root.GetNode<Control>("SteamHostChoice");
   if(!dialog.FindChildren("*","Button",true,false).OfType<Button>().Any(b=>b.Text.Contains("Steam")))throw new Exception("Steam host choice missing");
   _=choice;
  }
  if(scenario=="render-scale") {
   panel.Visible=false; heading.Text="Render scale test";
   var target=new Button{Text="Touch target",Position=new Vector2(400,250),Size=new Vector2(300,90)};root.AddChild(target);int clicks=0;target.Pressed+=()=>clicks++;
   STS2Mobile.Graphics.MobileRenderScale.SetPercent(100,false);STS2Mobile.Graphics.MobileRenderScale.Attach(GetTree().Root);
   var initialRect=target.GetGlobalRect();var initialSize=GetViewport().GetVisibleRect().Size;
   for(int i=0;i<3;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
   foreach(var percent in new[]{75,50,10,100}) {
    STS2Mobile.Graphics.MobileRenderScale.SetPercent(percent,false);
    for(int i=0;i<4;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
    await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
    var buffer=GetViewport().GetTexture().GetImage();
    var expected=(int)Math.Round(GetTree().Root.Size.X*percent/100.0);
    if(Math.Abs(buffer.GetWidth()-expected)>1)throw new Exception($"Render buffer not reduced: {percent}% expected={expected} actual={buffer.GetWidth()}");
    if(target.GetGlobalRect()!=initialRect||GetViewport().GetVisibleRect().Size!=initialSize)throw new Exception("Render scale changed logical UI/input coordinates");
    var at=initialRect.GetCenter();Input.ParseInputEvent(new InputEventMouseButton{Position=at,GlobalPosition=at,ButtonIndex=MouseButton.Left,Pressed=true});
    Input.ParseInputEvent(new InputEventMouseButton{Position=at,GlobalPosition=at,ButtonIndex=MouseButton.Left,Pressed=false});
    for(int i=0;i<2;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
    var screen=DisplayServer.ScreenGetImage(0);screen.SavePng(ProjectSettings.GlobalizePath("res://../../Previews/render-"+percent+".png"));
    GD.Print($"RENDER_SCALE_PASS percent={percent}; actual-buffer={buffer.GetWidth()}x{buffer.GetHeight()}; touch-count={clicks}");
   }
   if(clicks!=4)throw new Exception("Touch/click target changed after reducing render resolution");
  }
  if(scenario=="progress"){panel.SetBusy(true);panel.SetStatus("Validando o certificado assinado da Steam",false);}
  if(scenario=="error"){panel.SetStatus("Falha em android-library: DllNotFoundException. Não foi possível carregar a biblioteca de rede.",true,true);}
  for(int i=0;i<5;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
  if(scenario=="details")panel.OpenDetails("STS2 Mobile r9.2\nStage: android-library\nDllNotFoundException: exemplo de diagnóstico.\nNenhuma senha ou token está incluído.");
  for(int i=0;i<3;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
  if(scenario=="interactions") {
   bool lan=false;int cancels=0;string submitted="";
   panel.LanSelected+=x=>lan=x;panel.CancelRequested+=()=>cancels++;panel.JoinRequested+=x=>submitted=x;
   panel.SelectLan(true);if(!lan||panel.HostInput.IsVisibleInTree())throw new Exception("LAN mode retained Steam fields");
   panel.SelectLan(false);panel.SetBusy(true);panel.SelectLan(true);if(panel.LanMode)throw new Exception("Mode changed during connection");
   var cancel=panel.FindChildren("*","Button",true,false).OfType<Button>().Single(b=>b.Text=="Cancelar");cancel.EmitSignal(BaseButton.SignalName.Pressed);if(cancels!=1)throw new Exception("Cancel not delivered");
   panel.SetBusy(false);panel.HostInput.Text=" 76561198000000003 ";
   panel.OpenManualEntry();var join=panel.FindChildren("*","Button",true,false).OfType<Button>().Single(b=>b.Text=="Encontrar e entrar");join.EmitSignal(BaseButton.SignalName.Pressed);if(submitted!="76561198000000003")throw new Exception("Host ID lost");
   const string diagnostic="copy diagnostic fixture";panel.OpenDetails(diagnostic);
   for(int i=0;i<3;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
   var copy=panel.FindChildren("*","Button",true,false).OfType<Button>().Single(b=>b.Text=="Copiar detalhes");copy.EmitSignal(BaseButton.SignalName.Pressed);if(DisplayServer.ClipboardGet()!=diagnostic)throw new Exception("Copy failed");
   var close=panel.FindChildren("*","Button",true,false).OfType<Button>().Single(b=>b.Text=="Fechar");close.EmitSignal(BaseButton.SignalName.Pressed);
   for(int i=0;i<2;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
   if(panel.GetNodeOrNull("ConnectionDetails")!=null)throw new Exception("Details did not close");
   GD.Print("UI_INTERACTIONS_PASS");
  }
  GD.Print("PANEL SIZE "+panel.Size+" POS "+panel.Position);panel.Layout();
  if(panel.HostInput.IsVisibleInTree()){var rect=panel.HostInput.GetGlobalRect();if(rect.Position.Y<20||rect.End.Y>GetViewport().GetVisibleRect().End.Y)throw new Exception("SteamID input outside viewport");}
  await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
  var image=GetViewport().GetTexture().GetImage();var dir=ProjectSettings.GlobalizePath("res://../../Previews");System.IO.Directory.CreateDirectory(dir);image.SavePng(dir+"/multiplayer-"+scenario+".png");GD.Print("PREVIEW_OK "+scenario);
  if(renewal!=null){var dialog=root.GetNode("SteamReauthentication");dialog.GetType().GetMethod("Cancel")!.Invoke(dialog,null);if(await renewal)throw new Exception("Cancelled reauthentication succeeded");GD.Print("REAUTH_CANCEL_PASS");}
  GetTree().Quit();
 }catch(Exception e){GD.PrintErr(e);GetTree().Quit(1);}}
}

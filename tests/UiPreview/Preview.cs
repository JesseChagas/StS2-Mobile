using System;
using System.IO;
using System.Reflection;
using System.Linq;
using Godot;
using STS2Mobile.Launcher;
using STS2Mobile.Launcher.Design;
using STS2Mobile.Launcher.Sections;
using STS2Mobile.Launcher.Components;
public partial class Preview : Node
{
    private static int ListenerCount() => ((Action)typeof(LauncherPreferences).GetField("Changed",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null))?.GetInvocationList().Length ?? 0;
    public override async void _Ready()
    {
        try {
            var args=OS.GetCmdlineUserArgs();
            var scenario=args.Length>0?args[0]:"ready";
            LauncherPreferences.Configure(args.Length<2||args[1]!="light",args.Length>2?args[2]:"pt");
            var root=new Control(); AddChild(root); root.SetAnchorsPreset(Control.LayoutPreset.FullRect); root.Size=GetViewport().GetVisibleRect().Size;
            var scale=LauncherView.ScaleFor(root.Size);
            int baselineListeners = ListenerCount();
            LauncherView view=null;
            if(scenario=="warmup"||scenario=="loading") {
                var shell=new LoadingShell(scale,scenario=="warmup"?"Preparing your first run":"Loading your game",scenario=="loading");root.AddChild(shell);
                if(scenario=="warmup") {shell.SetProgress(67);shell.Status.SetCopy("Optimizing graphics...");shell.Detail.SetCopy("Optimizing: {0} / {1}",1198,1788);}
            } else {
                view=new LauncherView(root,scale);view.HideAllSections();
                if(scenario=="login"||scenario=="error"||scenario=="approval"||scenario=="code") {
                    view.SetAuthentication(true);view.SetStatus(scenario=="approval"?"Approve this sign-in in the Steam app on your phone.":"Enter your Steam credentials");
                    if(scenario=="code")view.Code.Show(false);
                    else if(scenario!="approval")view.Login.Visible=true;
                    if(scenario=="error")view.SetErrorStatus("Incorrect username or password. Please try again.");
                } else if(scenario=="download") {
                    view.SetAuthentication(false);view.Branch.Visible=true;view.Download.Visible=true;view.Download.SetProgress(62,"1.20 GB / 1.93 GB");view.SetStatus("Logged in as {0}","Jessé");
                } else {
                    view.SetAuthentication(false);view.Branch.Visible=true;view.Actions.SetCloudSyncChecked(true);view.Actions.SetCloudAlreadyPulled(scenario!="pull");view.Actions.ShowLaunch("Play",true,true);view.SetStatus("Welcome back, {0}","Jessé");
                    if(scenario=="more")((Button)typeof(ActionSection).GetField("_moreButton",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(view.Actions)).EmitSignal(BaseButton.SignalName.Pressed);
                }
                view.RefreshPresentation();
                if(scenario=="logout") view.ShowLogout(_ => {});
                if(scenario=="beta") ((Button)typeof(BranchSection).GetField("_button",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(view.Branch)).EmitSignal(BaseButton.SignalName.Pressed);
                if (scenario == "scroll")
                {
                    void Check(bool c, string m) { if (!c) throw new Exception(m); }
                    async System.Threading.Tasks.Task Frames(int n=4) { for(int i=0;i<n;i++) await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame); }
                    void Send(InputEvent e) { Input.ParseInputEvent(e); Input.FlushBufferedEvents(); }
                    T Field<T>(object o,string n) => (T)o.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(o);
                    var scroll=Field<ScrollContainer>(view,"_scroll");
                    var more=Field<Button>(view.Actions,"_moreButton");
                    more.EmitSignal(BaseButton.SignalName.Pressed);
                    await Frames();
                    var toggle=Field<Button>(view.Actions,"_cloudSyncToggle");
                    var identity=Field<RichTextLabel>(view,"_header");
                    var identityPosition=identity.GlobalPosition;
                    var origin=toggle.GetGlobalRect().GetCenter();
                    Check(origin.X<identityPosition.X,"Menu was not swapped to the left");
                    Check(scroll.GetGlobalRect().Position.X>=75*scale,"Menu has no additional lateral inset");
                    Check(scroll.GetVScrollBar().MaxValue>scroll.GetVScrollBar().Page+100,"Fixture has no scroll overflow");
                    int toggled=0;view.Actions.CloudSyncToggled+=_=>toggled++;
                    Send(new InputEventMouseMotion{Position=origin,GlobalPosition=origin});await Frames(1);
                    Send(new InputEventMouseButton{Position=origin,GlobalPosition=origin,ButtonIndex=MouseButton.Left,ButtonMask=MouseButtonMask.Left,Pressed=true});
                    var end=origin-new Vector2(0,160*scale);
                    Send(new InputEventMouseMotion{Position=end,GlobalPosition=end,Relative=end-origin,ButtonMask=MouseButtonMask.Left});
                    Send(new InputEventMouseButton{Position=end,GlobalPosition=end,ButtonIndex=MouseButton.Left,Pressed=false});
                    await Frames();
                    Check(scroll.ScrollVertical>=140*scale,"Dragging over a button did not scroll the menu");
                    Check(toggled==0&&toggle.ButtonPressed,"Dragging activated the underlying toggle");
                    Check(identity.GlobalPosition.DistanceTo(identityPosition)<1,"Identity scrolled with menu");
                    scroll.ScrollVertical=0;await Frames();origin=toggle.GetGlobalRect().GetCenter();
                    Send(new InputEventMouseMotion{Position=origin,GlobalPosition=origin});
                    Send(new InputEventMouseButton{Position=origin,GlobalPosition=origin,ButtonIndex=MouseButton.Left,ButtonMask=MouseButtonMask.Left,Pressed=true});
                    Send(new InputEventMouseMotion{Position=origin+new Vector2(0,2),GlobalPosition=origin+new Vector2(0,2),Relative=new Vector2(0,2),ButtonMask=MouseButtonMask.Left});
                    Send(new InputEventMouseButton{Position=origin+new Vector2(0,2),GlobalPosition=origin+new Vector2(0,2),ButtonIndex=MouseButton.Left,Pressed=false});
                    await Frames();Check(toggled==1&&!toggle.ButtonPressed,"Normal click stopped working after drag");
                    view.Actions.SetCloudSyncChecked(true);scroll.ScrollVertical=0;await Frames();origin=toggle.GetGlobalRect().GetCenter();
                    Send(new InputEventScreenTouch{Index=4,Position=origin,Pressed=true});
                    end=origin-new Vector2(0,180*scale);
                    Send(new InputEventScreenDrag{Index=4,Position=end,Relative=end-origin});
                    Send(new InputEventScreenTouch{Index=4,Position=end,Pressed=false});await Frames();
                    Check(scroll.ScrollVertical>=160*scale,"Screen-touch gesture failed over button");
                    Check(toggled==1&&toggle.ButtonPressed,"Touch drag activated a button");
                    Check(identity.GlobalPosition.DistanceTo(identityPosition)<1,"Touch drag moved fixed identity");
                    // A modal over the same region must prevent scrolling behind it.
                    view.ShowLogout(_=>{});await Frames();int beforeModal=scroll.ScrollVertical;
                    origin=scroll.GetGlobalRect().GetCenter();
                    Send(new InputEventScreenTouch{Index=5,Position=origin,Pressed=true});
                    Send(new InputEventScreenDrag{Index=5,Position=origin-new Vector2(0,100),Relative=new Vector2(0,-100)});
                    Send(new InputEventScreenTouch{Index=5,Position=origin-new Vector2(0,100),Pressed=false});await Frames();
                    Check(scroll.ScrollVertical==beforeModal,"Menu scrolled through logout modal");
                    root.GetChildren().OfType<StyledDialog>().Last().Free();await Frames();
                    GD.Print("PASS: real mouse and screen-touch drag over buttons; tap preserved; no accidental toggle; menu inset/swapped; identity fixed; modal blocks gestures.");
                }

                if (scenario == "interaction")
                {
                    void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
                    T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
                    view.Actions.SetCloudSyncChecked(true); view.Actions.SetCloudAlreadyPulled(false);
                    Check(view.Actions.PullIsPrimary, "Pull hidden before first sync");
                    view.Actions.SetCloudAlreadyPulled(true);
                    Check(!view.Actions.PullIsPrimary, "Pull still primary after sync");
                    view.Actions.SetCloudSyncChecked(false);
                    Check(view.Actions.PullIsPrimary, "Pull unavailable when auto sync is off");
                    view.Actions.SetCloudSyncChecked(true);
                    Field<Button>(view.Actions, "_moreButton").EmitSignal(BaseButton.SignalName.Pressed);
                    Check(Field<VBoxContainer>(view.Actions,"_extra").Visible, "More options failed to open");
                    Check(Field<Button>(view.Actions,"_pullMore").Visible && view.Branch.Visible, "Pull/branch missing in expanded options");
                    Field<Button>(view.Actions, "_moreButton").EmitSignal(BaseButton.SignalName.Pressed);
                    view.Login.UsernameField.Text = "test-account";
                    Field<LineEdit>(view.Login,"_passwordField").Text = "not-a-real-password";
                    Field<Button>(view,"_themeButton").EmitSignal(BaseButton.SignalName.Pressed);
                    Field<Button>(view,"_languageButton").EmitSignal(BaseButton.SignalName.Pressed);
                    Check(!LauncherPreferences.Dark && LauncherPreferences.Language == "en", "Theme/language buttons failed");
                    Check(Field<Button>(view.Actions,"_launchButton").Text == "Play", "Live translation failed");
                    Check(view.Login.UsernameField.Text == "test-account" && Field<LineEdit>(view.Login,"_passwordField").Text == "not-a-real-password", "Appearance change cleared credentials");
                    view.SetAuthentication(true); Field<Button>(view,"_detailsButton").EmitSignal(BaseButton.SignalName.Pressed);
                    Check(view.Log.Visible,"Sign-in diagnostics did not open");
                    view.SetAuthentication(false); Check(!view.Log.Visible,"Console visible outside sign-in");
                    bool? selected = null;
                    view.ShowLogout(delete => selected = delete);
                    var logout = root.GetChildren().OfType<StyledDialog>().Last();
                    var erase = logout.Content.GetChildren().OfType<StyledButton>().Single();
                    Check(!erase.ButtonPressed,"Logout default deletes the game");
                    ((Button)logout.Buttons.GetChild(0)).EmitSignal(BaseButton.SignalName.Pressed);
                    await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                    Check(selected == null,"Cancel logged out");
                    view.ShowLogout(delete => selected = delete);
                    logout = root.GetChildren().OfType<StyledDialog>().Last();
                    erase = logout.Content.GetChildren().OfType<StyledButton>().Single();
                    erase.ButtonPressed = true;
                    ((Button)logout.Buttons.GetChild(1)).EmitSignal(BaseButton.SignalName.Pressed);
                    await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                    Check(selected == true,"Logout ignored explicit delete choice");
                    Check(Field<Button>(view.Actions,"_logoutButton").Visible,"Logout absent from More options");
                    view.Actions.SetPushPullDisabled(true);
                    Check(Field<Button>(view.Actions,"_logoutButton").Disabled,"Logout allowed during transfer");
                    view.Actions.SetPushPullDisabled(false);
                    int before = ListenerCount(); var dialog = new StyledDialog("About & credits",scale,true); root.AddChild(dialog); dialog.Free();
                    Check(ListenerCount() == before,"Closed dialog leaked appearance listeners");
                    LauncherPreferences.ToggleTheme(); LauncherPreferences.ToggleTheme();
                    var directory=Path.Combine(OS.GetUserDataDir(),"receipt-test");Directory.CreateDirectory(directory);
                    foreach(var file in Directory.GetFiles(directory))File.Delete(file);
                    Check(!CloudPresentation.HasPulled(directory,"alice"),"Unexpected initial receipt");CloudPresentation.MarkPulled(directory,"alice");
                    Check(CloudPresentation.HasPulled(directory,"ALICE") && !CloudPresentation.HasPulled(directory,"bob"),"Cloud receipt not account-scoped");
                    Check(LauncherText.FriendlyError("Authentication failed: InvalidPassword").StartsWith("Incorrect username"),"Password error was not explained");
                    Check(LauncherText.FriendlyError("RateLimitExceeded").StartsWith("Too many"),"Rate limit was not explained");
                    GD.Print("PASS UI interactions: cloud visibility, version, More options, live theme/language, preserved credentials, login-only console, receipt isolation and listener cleanup.");
                }
            }
            for(int i=0;i<20;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            RenderingServer.ForceDraw(false);
            await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            var output=args.Length>3?args[3]:Path.Combine(OS.GetUserDataDir(),"Previews","ui-"+scenario+"-"+(LauncherPreferences.Dark?"dark":"light")+"-"+LauncherPreferences.Language+".png");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var image=GetViewport().GetTexture().GetImage();
            GD.Print("Screenshot: "+output+" "+image.GetWidth()+"x"+image.GetHeight());image.SavePng(output);
            root.Free();
            if(ListenerCount()!=baselineListeners)throw new Exception("UI leaked static appearance listeners after tree exit");
            GD.Print("PASS: UI tree released all appearance listeners.");GetTree().Quit();
        }catch(Exception ex){GD.PrintErr(ex);GetTree().Quit(1);}
    }
}

using Godot;

namespace STS2Mobile.Steam.Multiplayer;

// Touch-sized room cards are primary. Manual IDs live in an optional dialog.
public partial class SteamMultiplayerPanel : Control
{
    public event Action<string> JoinRequested;
    public event Action CancelRequested;
    public event Action ReconnectRequested;
    public event Action FriendsRefreshRequested;
    public event Action<bool> LanSelected;
    public LineEdit HostInput { get; }
    public Control InitialFocus => busy ? cancel : needsLogin ? reconnect : roomButtons.FirstOrDefault() ?? refreshFriends;
    public bool LanMode => lanMode;
    private readonly bool en;
    private readonly Color text = new("#edf3fa"), muted = new("#b3c4d5");
    private readonly ColorRect backdrop, manualShade;
    private readonly PanelContainer card;
    private readonly MarginContainer margins;
    private readonly VBoxContainer content, roomList;
    private readonly ScrollContainer roomScroll;
    private readonly HBoxContainer tabs, header, actions, secondary;
    private readonly Label heading, status, empty;
    private readonly Button steam, lan, join, cancel, details, reconnect, refreshFriends, manual;
    private readonly List<Button> roomButtons = new();
    private SteamHostRoom[] rooms = Array.Empty<SteamHostRoom>();
    private ulong selectedHost;
    private bool busy, lanMode, needsLogin, loadingFriends;
    private float scale = 1;

    public SteamMultiplayerPanel(bool english)
    {
        en = english; Name = "SteamInternetEntry"; ZIndex = 20; MouseFilter = MouseFilterEnum.Ignore;
        backdrop = new ColorRect { Color = new Color("#101d2c"), MouseFilter = MouseFilterEnum.Stop }; AddChild(backdrop);
        tabs = new HBoxContainer(); tabs.AddThemeConstantOverride("separation", 12); AddChild(tabs);
        steam = MakeButton("Steam · Internet"); lan = MakeButton(en ? "Local network (LAN)" : "Rede local (LAN)");
        tabs.AddChild(steam); tabs.AddChild(lan); steam.Pressed += () => SelectLan(false); lan.Pressed += () => SelectLan(true);
        card = new PanelContainer(); card.AddThemeStyleboxOverride("panel", Box(new("#152536"), new("#354b60"), 1)); AddChild(card);
        margins = new MarginContainer(); card.AddChild(margins); content = new VBoxContainer(); margins.AddChild(content);
        header = new HBoxContainer(); header.AddThemeConstantOverride("separation", 12); content.AddChild(header);
        heading = MakeLabel(en ? "Friends' lobbies" : "Salas dos amigos", text); heading.SizeFlagsHorizontal = SizeFlags.ExpandFill; header.AddChild(heading);
        refreshFriends = MakeButton(en ? "Refresh" : "Atualizar"); refreshFriends.SizeFlagsHorizontal = SizeFlags.ShrinkEnd; refreshFriends.ClipText = false; header.AddChild(refreshFriends); refreshFriends.Pressed += RequestFriendsRefresh;
        roomScroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = SizeFlags.ExpandFill }; content.AddChild(roomScroll);
        roomList = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; roomList.AddThemeConstantOverride("separation", 10); roomScroll.AddChild(roomList);
        empty = MakeLabel(en ? "Looking for friends' lobbies…" : "Buscando salas dos amigos…", muted); empty.AutowrapMode = TextServer.AutowrapMode.WordSmart; roomList.AddChild(empty);
        actions = new HBoxContainer(); actions.AddThemeConstantOverride("separation", 10); content.AddChild(actions);
        join = MakeButton(en ? "Join selected lobby" : "Entrar na sala selecionada"); join.Disabled = true; join.AddThemeStyleboxOverride("normal", Box(new("#1479af"), new("#39a3d9"), 1));
        cancel = MakeButton(en ? "Cancel" : "Cancelar"); cancel.Visible = false; actions.AddChild(join); actions.AddChild(cancel);
        join.Pressed += () => { if (!busy && selectedHost != 0) JoinRequested?.Invoke(selectedHost.ToString()); };
        cancel.Pressed += () => CancelRequested?.Invoke();
        status = MakeLabel(en ? "Choose a lobby above." : "Escolha uma sala na lista acima.", muted); status.AutowrapMode = TextServer.AutowrapMode.WordSmart; status.MaxLinesVisible = 2; content.AddChild(status);
        secondary = new HBoxContainer(); secondary.AddThemeConstantOverride("separation", 10); content.AddChild(secondary);
        manual = MakeButton(en ? "Find by SteamID" : "Encontrar via SteamID"); secondary.AddChild(manual); manual.Pressed += OpenManualEntry;
        details = MakeButton(en ? "Connection details" : "Detalhes da conexão"); details.Visible = false; secondary.AddChild(details); details.Pressed += () => OpenDetails(ConnectionReport.Diagnostics);
        reconnect = MakeButton(en ? "Reconnect Steam" : "Reconectar Steam"); reconnect.Visible = false; content.AddChild(reconnect); reconnect.Pressed += () => ReconnectRequested?.Invoke();
        manualShade = new ColorRect { Name = "SteamIdEntry", Color = new Color(0, 0, 0, .88f), ZIndex = 100, Visible = false }; AddChild(manualShade); manualShade.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var center = new CenterContainer(); manualShade.AddChild(center); center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var manualBox = new PanelContainer { CustomMinimumSize = new Vector2(730, 270) }; manualBox.AddThemeStyleboxOverride("panel", Box(new("#152536"), new("#486278"), 1)); center.AddChild(manualBox);
        var column = new VBoxContainer(); column.AddThemeConstantOverride("separation", 18); manualBox.AddChild(column);
        var title = MakeLabel(en ? "Find a host by SteamID64" : "Encontrar anfitrião via SteamID64", text); title.AddThemeFontSizeOverride("font_size", 26); column.AddChild(title);
        HostInput = new LineEdit { PlaceholderText = en ? "Host SteamID64" : "SteamID64 do amigo que hospeda", MaxLength = 32, VirtualKeyboardType = LineEdit.VirtualKeyboardTypeEnum.Number, CustomMinimumSize = new Vector2(0, 58) };
        HostInput.AddThemeFontSizeOverride("font_size", 27); column.AddChild(HostInput);
        var manualActions = new HBoxContainer(); manualActions.AddThemeConstantOverride("separation", 12); column.AddChild(manualActions);
        var find = MakeButton(en ? "Find and join" : "Encontrar e entrar"); var back = MakeButton(en ? "Back" : "Voltar");
        foreach (var b in new[] { find, back }) { b.CustomMinimumSize = new Vector2(0, 54); b.AddThemeFontSizeOverride("font_size", 24); manualActions.AddChild(b); }
        void SubmitManual() { if (busy) return; manualShade.Visible = false; JoinRequested?.Invoke(HostInput.Text.Trim()); }
        find.Pressed += SubmitManual; HostInput.TextSubmitted += _ => { if (manualShade.Visible) SubmitManual(); }; back.Pressed += () => manualShade.Visible = false;
        Resized += () => { Layout(); manualBox.CustomMinimumSize = new Vector2(Mathf.Min(Size.X * .82f, 850 * scale), Mathf.Min(Size.Y * .65f, 300 * scale)); };
        SelectLan(false);
    }
    private static StyleBoxFlat Box(Color bg, Color border, int width) => new() { BgColor = bg, BorderColor = border, BorderWidthLeft = width, BorderWidthRight = width, BorderWidthTop = width, BorderWidthBottom = width, CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6, CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6, ContentMarginLeft = 16, ContentMarginRight = 16 };
    private Button MakeButton(string title)
    {
        var b = new Button { Text = title, SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true };
        b.AddThemeColorOverride("font_color", text); b.AddThemeStyleboxOverride("normal", Box(new("#243a4d"), new("#425c70"), 1));
        b.AddThemeStyleboxOverride("hover", Box(new("#34516a"), new("#6ca2c3"), 1)); b.AddThemeStyleboxOverride("pressed", Box(new("#0f5a86"), new("#66b9e5"), 2));
        b.AddThemeStyleboxOverride("disabled", Box(new("#1a2b38"), new("#2e4353"), 1)); return b;
    }
    private static Label MakeLabel(string title, Color color) { var l = new Label { Text = title }; l.AddThemeColorOverride("font_color", color); return l; }
    public void Layout()
    {
        scale = Mathf.Clamp(Size.Y / 576f, .85f, 1.8f); var width = Mathf.Min(Size.X * .78f, 1100 * scale);
        backdrop.Position = new Vector2(Size.X * .095f, Size.Y * .14f); backdrop.Size = new Vector2(Size.X * .81f, Size.Y * .84f);
        tabs.Position = new Vector2((Size.X - width) / 2, Size.Y * .155f); tabs.Size = new Vector2(width, 46 * scale);
        card.Position = new Vector2((Size.X - width) / 2, Size.Y * .25f); card.Size = new Vector2(width, Size.Y * .72f);
        foreach (var name in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" }) margins.AddThemeConstantOverride(name, (int)(12 * scale));
        content.AddThemeConstantOverride("separation", (int)(8 * scale));
        heading.AddThemeFontSizeOverride("font_size", (int)(27 * scale)); status.AddThemeFontSizeOverride("font_size", (int)(19 * scale)); empty.AddThemeFontSizeOverride("font_size", (int)(23 * scale));
        roomScroll.CustomMinimumSize = new Vector2(0, 64 * scale);
        foreach (var b in new[] { steam, lan, refreshFriends, manual, details, reconnect }) { b.AddThemeFontSizeOverride("font_size", (int)(21 * scale)); b.CustomMinimumSize = new Vector2(0, 43 * scale); }
        foreach (var b in new[] { join, cancel }) { b.AddThemeFontSizeOverride("font_size", (int)(24 * scale)); b.CustomMinimumSize = new Vector2(0, 54 * scale); }
        foreach (var b in roomButtons) { b.AddThemeFontSizeOverride("font_size", (int)(25 * scale)); b.CustomMinimumSize = new Vector2(0, 64 * scale); }
    }
    public void SelectLan(bool enabled)
    {
        if (busy) return; lanMode = enabled; card.Visible = backdrop.Visible = !enabled; manualShade.Visible = false;
        steam.Modulate = enabled ? new Color(.65f, .75f, .82f) : Colors.White; lan.Modulate = enabled ? Colors.White : new Color(.65f, .75f, .82f); LanSelected?.Invoke(enabled);
    }
    public void SetBusy(bool value)
    {
        busy = value; HostInput.Editable = !value; join.Disabled = value || needsLogin || selectedHost == 0; manual.Disabled = reconnect.Disabled = value;
        refreshFriends.Disabled = value || loadingFriends; foreach (var b in roomButtons) b.Disabled = value;
        cancel.Visible = value; steam.Disabled = lan.Disabled = value; join.Text = value ? (en ? "Connecting…" : "Conectando…") : (en ? "Join selected lobby" : "Entrar na sala selecionada");
    }
    public void SetStatus(string message, bool failed, bool showDetails = false) { status.Text = message; status.AddThemeColorOverride("font_color", failed ? new Color("#ffb4a8") : muted); details.Visible = showDetails; }
    public void SetNeedsLogin(bool value) { needsLogin = value; reconnect.Visible = value; join.Visible = !value; join.Disabled = busy || value || selectedHost == 0; }
    public void RequestFriendsRefresh() { if (!busy && !loadingFriends) FriendsRefreshRequested?.Invoke(); }
    public void SetFriendsLoading(bool value) { loadingFriends = value; refreshFriends.Disabled = busy || value; refreshFriends.Text = value ? (en ? "Searching…" : "Buscando…") : (en ? "Refresh" : "Atualizar"); if (rooms.Length == 0) empty.Text = value ? (en ? "Looking for friends' lobbies…" : "Buscando salas dos amigos…") : (en ? "No lobby confirmed yet. Refresh or use a SteamID." : "Nenhuma sala confirmada ainda. Atualize ou use um SteamID."); }
    public void SetFriendRooms(SteamHostRoom[] available, string error = null)
    {
        var previous = selectedHost; rooms = available ?? Array.Empty<SteamHostRoom>();
        foreach (var b in roomButtons) { roomList.RemoveChild(b); b.QueueFree(); } roomButtons.Clear();
        empty.Visible = rooms.Length == 0; selectedHost = 0;
        foreach (var room in rooms) {
            var button = MakeButton(room.Name); button.Alignment = HorizontalAlignment.Left; button.ToggleMode = true; roomList.AddChild(button); roomButtons.Add(button);
            button.Pressed += () => SelectRoom(room.HostId);
        }
        if (rooms.Length != 0) SelectRoom(rooms.Any(x => x.HostId == previous) ? previous : rooms.FirstOrDefault(x => x.HostId.ToString() == HostInput.Text.Trim())?.HostId ?? rooms[0].HostId);
        SetFriendsLoading(false); if (error != null) empty.Text = error; Layout(); SetBusy(busy);
    }
    private void SelectRoom(ulong host) { if (busy) return; selectedHost = host; foreach (var (button, index) in roomButtons.Select((b, i) => (b, i))) button.SetPressedNoSignal(rooms[index].HostId == host); if (!manualShade.Visible) HostInput.Text = host.ToString(); join.Disabled = needsLogin || host == 0; }
    public void OpenManualEntry() { if (busy) return; manualShade.Visible = true; HostInput.GrabFocus(); }
    public void OpenDetails(string diagnostic)
    {
        if (GetNodeOrNull("ConnectionDetails") != null) return;
        var shade = new ColorRect { Name = "ConnectionDetails", Color = new Color(0, 0, 0, .88f), ZIndex = 101 }; AddChild(shade); shade.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var center = new CenterContainer(); shade.AddChild(center); center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var box = new PanelContainer { CustomMinimumSize = new Vector2(Mathf.Min(Size.X * .86f, 1100 * scale), Size.Y * .75f) }; box.AddThemeStyleboxOverride("panel", Box(new("#152536"), new("#486278"), 1)); center.AddChild(box);
        var column = new VBoxContainer(); column.AddThemeConstantOverride("separation", 12); box.AddChild(column);
        var title = MakeLabel(en ? "Connection details" : "Detalhes da conexão", text); title.AddThemeFontSizeOverride("font_size", (int)(25 * scale)); column.AddChild(title);
        var log = new RichTextLabel { Text = diagnostic, BbcodeEnabled = false, ScrollActive = true, SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, Size.Y * .46f) }; log.AddThemeFontSizeOverride("normal_font_size", (int)(20 * scale)); column.AddChild(log);
        var row = new HBoxContainer(); column.AddChild(row); var copy = MakeButton(en ? "Copy details" : "Copiar detalhes"); var close = MakeButton(en ? "Close" : "Fechar");
        foreach (var b in new[] { copy, close }) { b.CustomMinimumSize = new Vector2(0, 48 * scale); b.AddThemeFontSizeOverride("font_size", (int)(22 * scale)); row.AddChild(b); }
        copy.Pressed += () => { DisplayServer.ClipboardSet(diagnostic); copy.Text = en ? "Copied" : "Copiado"; }; close.Pressed += () => shade.QueueFree();
    }
}

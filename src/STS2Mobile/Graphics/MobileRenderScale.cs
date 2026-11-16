using Godot;

namespace STS2Mobile.Graphics;

// Keep the Window's logical/UI/input coordinates unchanged. Only its render
// target is reduced, then a separate full-size viewport presents it linearly.
public partial class MobileRenderScale : Node
{
    private static MobileRenderScale current;
    private static int percent = 100;
    private static bool loaded;
    public static event Action Changed;
    public static int Percent { get { Load(); return percent; } }
    private Window window;
    private Rid presenter, canvas, item;
    private Texture2D texture;
    private Vector2I lastOutput;
    private bool active;
    public Vector2I RenderSize { get; private set; }
    private static string PreferencePath => ProjectSettings.GlobalizePath("user://mobile_render_scale.txt");
    private static void Load() {
        if (loaded) return; loaded = true;
        try { if (System.IO.File.Exists(PreferencePath) && int.TryParse(System.IO.File.ReadAllText(PreferencePath), out var saved)) percent = Math.Clamp(saved, 10, 100); }
        catch (Exception e) { PatchHelper.Log("[Graphics] Preferência: " + e.GetType().Name); }
    }
    public static void Attach(Window root) {
        if (GodotObject.IsInstanceValid(current)) return;
        current = new MobileRenderScale { Name = "MobileRenderScale", window = root };
        current.Ready += current.LoadAndApply; current.TreeExiting += current.OnExit;
        root.CallDeferred(Node.MethodName.AddChild, current);
    }
    private void LoadAndApply() { Load(); RenderingServer.FramePreDraw += BeforeFrame; }
    public static void SetPercent(int value, bool save = true) {
        percent = Math.Clamp(value, 10, 100); loaded = true;
        if (save) try { System.IO.File.WriteAllText(PreferencePath, percent.ToString()); } catch (Exception e) { PatchHelper.Log("[Graphics] Gravação: " + e.GetType().Name); }
        if (GodotObject.IsInstanceValid(current) && percent == 100) current.Restore();
        Changed?.Invoke();
    }
    private void BeforeFrame() {
        if (percent >= 100 || !GodotObject.IsInstanceValid(window) || window.Size.X < 2 || window.Size.Y < 2) return;
        var output = window.Size; var scale = percent / 100f;
        var size = new Vector2I(Math.Max(2, (int)Math.Round(output.X * scale)), Math.Max(2, (int)Math.Round(output.Y * scale)));
        if (!active) {
            texture = window.GetTexture(); presenter = RenderingServer.ViewportCreate(); canvas = RenderingServer.CanvasCreate(); item = RenderingServer.CanvasItemCreate();
            RenderingServer.CanvasItemSetParent(item, canvas); RenderingServer.ViewportAttachCanvas(presenter, canvas);
            RenderingServer.CanvasItemSetDefaultTextureFilter(item, RenderingServer.CanvasItemTextureFilter.Linear);
            RenderingServer.ViewportSetUpdateMode(presenter, RenderingServer.ViewportUpdateMode.Always); RenderingServer.ViewportSetActive(presenter, true);
            RenderingServer.ViewportSetParentViewport(window.GetViewportRid(), presenter); active = true;
        }
        var root = window.GetViewportRid();
        RenderingServer.ViewportSetRenderDirectToScreen(root, false);
        RenderingServer.ViewportAttachToScreen(root, new Rect2(), -1);
        RenderingServer.ViewportSetUpdateMode(root, RenderingServer.ViewportUpdateMode.Always);
        RenderingServer.ViewportSetSize(root, size.X, size.Y);
        RenderingServer.ViewportSetGlobalCanvasTransform(root, window.GetFinalTransform().Scaled(new Vector2(scale, scale)));
        if (lastOutput != output) {
            RenderingServer.ViewportSetSize(presenter, output.X, output.Y);
            RenderingServer.ViewportAttachToScreen(presenter, new Rect2(Vector2.Zero, output), window.GetWindowId());
            RenderingServer.CanvasItemClear(item); RenderingServer.CanvasItemAddTextureRect(item, new Rect2(Vector2.Zero, output), texture.GetRid());
            lastOutput = output;
        }
        if (RenderSize != size) PatchHelper.Log($"[Graphics] Renderização {percent}%: {size.X}x{size.Y}; saída {output.X}x{output.Y}; filtro linear");
        RenderSize = size;
    }
    private void Restore() {
        if (!active) return;
        var root = window.GetViewportRid();
        RenderingServer.ViewportAttachToScreen(presenter, new Rect2(), -1); RenderingServer.ViewportSetActive(presenter, false);
        RenderingServer.ViewportSetParentViewport(root, new Rid()); RenderingServer.ViewportSetSize(root, window.Size.X, window.Size.Y);
        RenderingServer.ViewportSetGlobalCanvasTransform(root, window.GetFinalTransform());
        RenderingServer.ViewportAttachToScreen(root, new Rect2(Vector2.Zero, window.Size), window.GetWindowId());
        RenderingServer.FreeRid(item); RenderingServer.FreeRid(canvas); RenderingServer.FreeRid(presenter);
        active = false; lastOutput = Vector2I.Zero; texture = null; RenderSize = window.Size;
    }
    private void OnExit() { RenderingServer.FramePreDraw -= BeforeFrame; Restore(); if (ReferenceEquals(current, this)) current = null; }
}

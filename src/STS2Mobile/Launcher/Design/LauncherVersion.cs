using System;
using Godot;

namespace STS2Mobile.Launcher.Design;

public static class LauncherVersion
{
    public const string Revision = "9.10 · Steam P2P";
    private static readonly Lazy<string> Display = new(ReadVersion);
    public static string Text => Display.Value;

    private static string ReadVersion()
    {
        try
        {
            if (Engine.HasSingleton("JavaClassWrapper"))
            {
                var java = Engine.GetSingleton("JavaClassWrapper");
                var wrapper = (GodotObject)java.Call("wrap", "com.game.sts2launcher.GodotApp");
                var app = (GodotObject)wrapper.Call("getInstance");
                var version = ((string)app.Call("getVersionName"))?.Trim();
                if (!string.IsNullOrEmpty(version))
                    return $"App v{version} · r{Revision}";
            }
        }
        catch { }
        // Desktop previews have no Android package; never invent an APK version.
        return $"App r{Revision}";
    }
}

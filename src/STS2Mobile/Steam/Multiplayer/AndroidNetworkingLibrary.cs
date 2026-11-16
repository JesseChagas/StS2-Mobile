using Godot;
using Sts2.SteamClientPrototype;

namespace STS2Mobile.Steam.Multiplayer;

internal static class AndroidNetworkingLibrary
{
    public static void Prepare()
    {
        if (Engine.HasSingleton("JavaClassWrapper")) {
            var wrapper = Engine.GetSingleton("JavaClassWrapper");
            var activityClass = (GodotObject)wrapper.Call("wrap", "com.game.sts2launcher.GodotApp");
            var activity = (GodotObject)activityClass.Call("getInstance");
            var path = ((string)activity.Call("getSteamNetworkingLibraryPath"))?.Trim();
            if (string.IsNullOrEmpty(path) || !System.IO.Path.IsPathFullyQualified(path))
                throw new IOException("Android did not return the networking library path.");
            if (!System.IO.File.Exists(path))
                throw new FileNotFoundException("Networking library is missing from the installed APK.", path);
            NativeGns.LibraryPathOverride = path;
        } else if (OperatingSystem.IsAndroid()) {
            throw new InvalidOperationException("Android Java bridge is unavailable.");
        }
        NativeGns.CheckAvailability();
    }
}

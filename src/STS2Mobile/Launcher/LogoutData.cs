using System;
using System.Collections.Generic;
using System.IO;

namespace STS2Mobile.Launcher;

// Only downloaded depots and the disposable managed assembly cache are removed.
// app_userdata, external saves/mods, settings and account-scoped receipts stay intact.
public static class LogoutData
{
    public static void RemoveAccountFiles(string dataDir)
    {
        foreach (var name in new[] { "steam_credentials.enc", "ownership_verified.enc", "steam_pending_auth.enc", "steam_pending_auth.enc.tmp" })
        {
            var path = Path.Combine(dataDir, name);
            File.Delete(path);
            if (File.Exists(path))
                throw new IOException("Could not remove saved sign-in.");
        }
    }

    public static void DeleteDownloadedGame(string dataDir)
    {
        var moved = new List<(string Original, string Quarantine)>();
        try
        {
            foreach (
                var relative in new[] { "game", Path.Combine(".godot", "mono", "publish", "arm64") }
            )
            {
                var path = Path.Combine(dataDir, relative);
                if (!Directory.Exists(path))
                    continue;
                // Reject a symlink at any level rather than modifying files outside app storage.
                var part = new DirectoryInfo(path);
                var root = Path.GetFullPath(dataDir);
                while (part != null && part.FullName != root)
                {
                    if ((part.Attributes & FileAttributes.ReparsePoint) != 0)
                        throw new IOException("Unexpected linked game directory.");
                    part = part.Parent;
                }
                var quarantine = path + ".logout-" + Guid.NewGuid().ToString("N");
                Directory.Move(path, quarantine);
                moved.Add((path, quarantine));
            }
        }
        catch
        {
            for (var i = moved.Count - 1; i >= 0; i--)
                Directory.Move(moved[i].Quarantine, moved[i].Original);
            throw;
        }
        // No mapped assembly path or game PCK remains for the next process to load.
        foreach (var entry in moved)
            Directory.Delete(entry.Quarantine, true);
    }
}

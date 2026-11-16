using System;
using System.IO;
using Godot;

namespace STS2Mobile.Steam;

// Persists which Steam branch of Slay the Spire 2 the launcher downloads
// ("public" = default, "public-beta" = public beta branch).
public static class BranchSettings
{
    public const string Public = "public";
    public const string PublicBeta = "public-beta";

    private static string PrefPath => Path.Combine(OS.GetDataDir(), "game_branch");

    public static string Load()
    {
        try
        {
            if (File.Exists(PrefPath))
            {
                var branch = File.ReadAllText(PrefPath).Trim();
                if (branch.Length > 0)
                    return branch;
            }
        }
        catch { }
        return Public;
    }

    public static void Save(string branch)
    {
        try
        {
            File.WriteAllText(PrefPath, branch);
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[Branch] Save failed: {ex.Message}");
        }
    }

    public static string Toggle(string branch) => branch == PublicBeta ? Public : PublicBeta;
}

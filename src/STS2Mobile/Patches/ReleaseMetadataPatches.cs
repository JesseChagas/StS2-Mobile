using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Debug;
using MegaCrit.Sts2.Core.Platform;
using STS2Mobile.Steam;

namespace STS2Mobile.Patches;

public static class ReleaseMetadataPatches
{
    public static void Apply(Harmony harmony) => PatchHelper.Patch(harmony,
        typeof(ReleaseInfoManager), "GetPossibleReleaseInfoPaths",
        postfix: PatchHelper.Method(typeof(ReleaseMetadataPatches), nameof(InstalledPaths)));

    public static void InstalledPaths(ref string[] __result) =>
        __result = InstalledReleaseMetadata.SearchPaths(OS.GetDataDir(), __result);

    // An early UI/version lookup can cache a missing desktop-side release file.
    // Recreate the original reader after the launcher, before GameStartup.
    public static void PrepareForGame()
    {
        var path = InstalledReleaseMetadata.ReleasePath(OS.GetDataDir());
        if (!System.IO.File.Exists(path)) {
            PatchHelper.Log("[Release] Installed release_info.json is missing; verify game files in the launcher.");
            return;
        }
        AccessTools.Field(typeof(ReleaseInfoManager), "_instance").SetValue(null, null);
        var info = ReleaseInfoManager.Instance.ReleaseInfo;
        PatchHelper.Log($"[Release] Installed metadata: version={info?.Version ?? "UNKNOWN"}; branch={InstalledReleaseMetadata.InstalledBranch(OS.GetDataDir()) ?? "unknown"}");
    }

    public static bool InstalledBranch(ref PlatformBranch __result)
    {
        var branch = PlatformBranchExtensions.FromName(InstalledReleaseMetadata.InstalledBranch(OS.GetDataDir()) ?? "");
        if (!branch.HasValue) return true;
        __result = branch.Value;
        return false;
    }
}

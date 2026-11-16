using System.Security.Cryptography;
using System.Text.Json;

namespace STS2Mobile.Steam;

// Read only installed game metadata. The host's version and the currently
// selected download preference are never used as the local game's identity.
internal static class InstalledReleaseMetadata
{
    public static string ReleasePath(string dataDir) => Path.Combine(dataDir, "game", "release_info.json");
    private static string MarkerPath(string dataDir) => Path.Combine(dataDir, "download_state", "installed_release_branch.json");

    public static string[] SearchPaths(string dataDir, string[] original) =>
        new[] { ReleasePath(dataDir) }.Concat(original ?? Array.Empty<string>()).Distinct().ToArray();

    public static string InstalledBranch(string dataDir)
    {
        var path = ReleasePath(dataDir);
        if (!File.Exists(path)) return null;
        try {
            // A successful depot verification records the actual distribution
            // branch and binds it to the release file, independent of preferences.
            var marker = MarkerPath(dataDir);
            if (File.Exists(marker)) {
                using var saved = JsonDocument.Parse(File.ReadAllText(marker));
                if (saved.RootElement.GetProperty("release_sha256").GetString() == Hash(path)) {
                    var verified = saved.RootElement.GetProperty("branch").GetString();
                    if (KnownBranch(verified)) return verified;
                }
            }
        } catch (Exception e) when (e is IOException or JsonException or InvalidOperationException or KeyNotFoundException) { }
        try {
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            if (json.RootElement.TryGetProperty("branch", out var value) && value.ValueKind == JsonValueKind.String) {
                var branch = value.GetString();
                if (KnownBranch(branch)) return branch;
            }
        } catch (Exception e) when (e is IOException or JsonException or InvalidOperationException) { }
        return null;
    }

    public static void RecordVerifiedBranch(string dataDir, string branch)
    {
        if (!KnownBranch(branch)) return;
        var release = ReleasePath(dataDir);
        if (!File.Exists(release)) return;
        var marker = MarkerPath(dataDir);
        Directory.CreateDirectory(Path.GetDirectoryName(marker));
        var temporary = marker + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(new { branch, release_sha256 = Hash(release) }));
        File.Move(temporary, marker, true);
    }

    private static bool KnownBranch(string branch) => branch is "public" or "public-beta" or "dev-test" || branch?.StartsWith("private-", StringComparison.OrdinalIgnoreCase) == true;
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace STS2Mobile.Launcher.Design;

public static class CloudPresentation
{
    public static bool ShowPullInMain(bool autoSync, bool hasPulled) => !autoSync || !hasPulled;

    private static string ReceiptPath(string directory, string account) =>
        Path.Combine(
            directory,
            "cloud_pull_"
                + Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(account.Trim().ToLowerInvariant()))
                )[..20]
        );

    public static bool HasPulled(string directory, string account) =>
        !string.IsNullOrWhiteSpace(account) && File.Exists(ReceiptPath(directory, account));

    public static void MarkPulled(string directory, string account)
    {
        if (string.IsNullOrWhiteSpace(account))
            return;
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(ReceiptPath(directory, account), DateTime.UtcNow.ToString("O"));
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[UI] Could not save cloud receipt: {ex.Message}");
        }
    }
}

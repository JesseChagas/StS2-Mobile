using System;
using System.IO;
using System.Text.Json;
using Godot;

namespace STS2Mobile.Launcher.Design;

public static class LauncherPreferences
{
    public const string AppName = "STS 2 Mobile (unofficial)";
    public const string Creator = "Jessé chagas";
    public const string CreatorUrl = "https://linktr.ee/Jessechagasdev";
    public const string AppLabel = AppName + " by " + Creator;
    public static bool Dark { get; private set; } = true;
    public static string Language { get; private set; } = "en";
    public static event Action Changed;
    private static string _path;
    private static bool _loaded;

    public static void Load()
    {
        if (_loaded)
            return;
        _loaded = true;
        try
        {
            _path = Path.Combine(OS.GetDataDir(), "launcher_appearance.json");
            Language = OS.GetLocale().StartsWith("pt", StringComparison.OrdinalIgnoreCase)
                ? "pt"
                : "en";
            if (File.Exists(_path))
            {
                var data = JsonSerializer.Deserialize<Appearance>(File.ReadAllText(_path));
                if (data != null)
                {
                    Dark = data.Dark;
                    Language = data.Language == "pt" ? "pt" : "en";
                }
            }
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[UI] Preferences unavailable: {ex.Message}");
        }
    }

    public static void ToggleTheme()
    {
        Dark = !Dark;
        Save();
        Changed?.Invoke();
    }

    public static void ToggleLanguage()
    {
        Language = Language == "en" ? "pt" : "en";
        Save();
        Changed?.Invoke();
    }

    // Used by the visual preview and pure policy tests. Does not change saved preferences.
    public static void Configure(bool dark, string language)
    {
        Dark = dark;
        Language = language == "pt" ? "pt" : "en";
        Changed?.Invoke();
    }

    private static void Save()
    {
        if (_path == null)
            return;
        try
        {
            File.WriteAllText(
                _path,
                JsonSerializer.Serialize(new Appearance { Dark = Dark, Language = Language })
            );
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[UI] Could not save preferences: {ex.Message}");
        }
    }

    public sealed class Appearance
    {
        public bool Dark { get; set; } = true;
        public string Language { get; set; } = "en";
    }
}

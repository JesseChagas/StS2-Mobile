using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

namespace STS2Mobile.Patches;

// Extends ModManager to scan an external mods directory on Android so users
// can sideload mods to /storage/emulated/0/StS2Launcher/Mods/ without root.
// ModManager internals changed between game builds, so everything that is not
// a stable public API is accessed through reflection and degrades gracefully.
public static class ModLoaderPatches
{
    private static readonly BindingFlags AllStatic =
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly BindingFlags AllInstance =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly string[] LoadedNames =
    {
        "wasLoaded",
        "WasLoaded",
        "isLoaded",
        "IsLoaded",
        "loaded",
        "Loaded",
    };

    public static void Apply(Harmony harmony)
    {
        PatchHelper.Patch(
            harmony,
            typeof(ModManager),
            "Initialize",
            postfix: PatchHelper.Method(typeof(ModLoaderPatches), nameof(InitializePostfix))
        );
    }

    // Runs after the original Initialize() to pick up mods from external storage.
    // Temporarily clears _initialized so TryLoadModFromPck accepts new entries.
    public static void InitializePostfix()
    {
        try
        {
            using var dirAccess = DirAccess.Open(AppPaths.ExternalModsDir);
            if (dirAccess == null)
            {
                PatchHelper.Log(
                    $"[Mods] External mods directory not found: {AppPaths.ExternalModsDir} "
                        + $"(error: {DirAccess.GetOpenError()})"
                );
                return;
            }

            PatchHelper.Log($"[Mods] Scanning external mods: {AppPaths.ExternalModsDir}");

            var initializedField = typeof(ModManager).GetField("_initialized", AllStatic);
            var loadMethod = typeof(ModManager).GetMethod("LoadModsInDirRecursive", AllStatic);
            if (initializedField == null || loadMethod == null)
            {
                PatchHelper.Log("[Mods] ModManager internals changed, external mods disabled");
                return;
            }

            initializedField.SetValue(null, false);
            try
            {
                loadMethod.Invoke(null, new object[] { dirAccess, ModSource.ModsDirectory });
            }
            finally
            {
                initializedField.SetValue(null, true);
            }

            RebuildLoadedMods();

            PatchHelper.Log($"[Mods] External scan complete. Total loaded: {CountLoadedMods()}");
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[Mods] Failed to load external mods: {ex}");
        }
    }

    // Rebuilds ModManager's loaded-mods list so it includes the newly scanned mods.
    // Skipped when the fields or the "was loaded" flag cannot be found.
    private static void RebuildLoadedMods()
    {
        var modsField = typeof(ModManager).GetField("_mods", AllStatic);
        var loadedModsField = typeof(ModManager).GetField("_loadedMods", AllStatic);
        if (modsField == null || loadedModsField == null)
        {
            PatchHelper.Log("[Mods] Mod lists not found, skipping loaded-mods rebuild");
            return;
        }

        var readLoaded = FindLoadedReader();
        if (readLoaded == null)
        {
            PatchHelper.Log("[Mods] 'was loaded' flag not found on Mod, skipping rebuild");
            return;
        }

        if (modsField.GetValue(null) is not IEnumerable allMods)
            return;

        var loaded = new List<Mod>();
        foreach (var item in allMods)
        {
            if (item is Mod mod && readLoaded(mod))
                loaded.Add(mod);
        }

        if (loadedModsField.FieldType.IsAssignableFrom(loaded.GetType()))
            loadedModsField.SetValue(null, loaded);
        else
            PatchHelper.Log("[Mods] _loadedMods has an unexpected type, skipping rebuild");
    }

    private static Func<Mod, bool> FindLoadedReader()
    {
        foreach (var name in LoadedNames)
        {
            var field = typeof(Mod).GetField(name, AllInstance);
            if (field != null && field.FieldType == typeof(bool))
                return m => (bool)field.GetValue(m);

            var prop = typeof(Mod).GetProperty(name, AllInstance);
            if (prop != null && prop.PropertyType == typeof(bool) && prop.GetMethod != null)
                return m => (bool)prop.GetValue(m);
        }
        return null;
    }

    private static string CountLoadedMods()
    {
        try
        {
            var prop = typeof(ModManager).GetProperty("LoadedMods", AllStatic);
            var value = prop?.GetValue(null);
            if (value == null)
            {
                var method = typeof(ModManager).GetMethod(
                    "GetLoadedMods",
                    AllStatic,
                    null,
                    Type.EmptyTypes,
                    null
                );
                value = method?.Invoke(null, null);
            }

            if (value is ICollection collection)
                return collection.Count.ToString();
            if (value is IEnumerable enumerable)
                return enumerable.Cast<object>().Count().ToString();
        }
        catch { }
        return "unknown";
    }
}

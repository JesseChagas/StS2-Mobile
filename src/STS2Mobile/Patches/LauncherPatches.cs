using System;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Saves;
using STS2Mobile.Launcher;
using STS2Mobile.Launcher.Design;
using STS2Mobile.Steam;

namespace STS2Mobile.Patches;

// Core patches for the mobile launcher flow. Intercepts GameStartupWrapper to show
// the Steam login UI before the game starts, injects cloud save support via SteamKit2,
// and delegates sync logic to CloudSyncCoordinator.
public static class LauncherPatches
{
    internal static bool CloudSyncEnabled = true;
    internal static string SavedAccountName;
    internal static string SavedRefreshToken;

    public static void Apply(Harmony harmony)
    {
        CloudSyncCoordinator.CloudDataApplied += _ =>
            CloudPresentation.MarkPulled(OS.GetDataDir(), SavedAccountName);
        PatchHelper.PatchCritical(
            harmony,
            typeof(NGame),
            "GameStartupWrapper",
            prefix: PatchHelper.Method(typeof(LauncherPatches), nameof(GameStartupWrapperPrefix))
        );

        PatchHelper.Patch(
            harmony,
            typeof(SaveManager),
            "ConstructDefault",
            prefix: PatchHelper.Method(typeof(LauncherPatches), nameof(ConstructDefaultPrefix))
        );

        PatchHelper.PatchCritical(
            harmony,
            typeof(CloudSaveStore),
            "SyncCloudToLocal",
            prefix: PatchHelper.Method(typeof(LauncherPatches), nameof(SyncCloudToLocalPrefix))
        );
    }

    public static bool GameStartupWrapperPrefix(object __instance, ref Task __result)
    {
        __result = RunLauncherThenGame(__instance);
        return false;
    }

    public static bool ConstructDefaultPrefix(ref SaveManager __result)
    {
        PatchHelper.Log(
            $"[Cloud] ConstructDefaultPrefix called. HasToken={SavedRefreshToken != null}, CloudSync={CloudSyncEnabled}"
        );

        if (!CloudSyncEnabled)
        {
            PatchHelper.Log("[Cloud] Cloud sync disabled by user — using local-only SaveManager");
            return true;
        }

        if (SavedAccountName == null || SavedRefreshToken == null)
        {
            PatchHelper.Log("[Cloud] No saved credentials — using local-only SaveManager");
            return true;
        }

        try
        {
            var localStore = new GodotFileIo(UserDataPathProvider.GetAccountScopedBasePath(null));
            var cloudStore = new SteamKit2CloudSaveStore(SavedAccountName, SavedRefreshToken);
            var wrappedStore = new CloudSaveStore(localStore, cloudStore);

            __result = new SaveManager(wrappedStore);
            PatchHelper.Log("[Cloud] Created SaveManager with SteamKit2 cloud store");
            return false;
        }
        catch (Exception ex)
        {
            PatchHelper.Log(
                $"[Cloud] Cloud store injection failed, falling back to local: {ex.Message}"
            );
            return true;
        }
    }

    public static bool SyncCloudToLocalPrefix(
        CloudSaveStore __instance,
        string path,
        ref Task __result
    )
    {
        __result = CloudSyncCoordinator.AutoSyncFileAsync(
            __instance.LocalStore,
            __instance.CloudStore,
            path
        );
        return false;
    }

    private static async Task RunLauncherThenGame(object game)
    {
        var gameNode = (Node)game;

        var launcher = new LauncherUI();
        gameNode.AddChild(launcher);
        launcher.SetGameMode(true);
        launcher.Initialize();
        PatchHelper.Log("Launcher UI displayed");

        await launcher.WaitForLaunch();
        PatchHelper.Log("User launched game, proceeding to startup...");
        ReleaseMetadataPatches.PrepareForGame();

        var instanceField = typeof(SaveManager).GetField(
            "_instance",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        if (instanceField != null)
        {
            instanceField.SetValue(null, null);
            PatchHelper.Log("[Cloud] Reset SaveManager._instance for cloud store re-injection");
        }

        launcher.QueueFree();

        if (ShaderWarmupScreen.NeedsWarmup())
        {
            var warmup = new ShaderWarmupScreen();
            gameNode.AddChild(warmup);
            warmup.Initialize();
            await warmup.WaitForCompletion();
            warmup.QueueFree();
        }

        var loading = new Control { ZIndex = 100 };
        loading.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        gameNode.AddChild(loading);
        loading.AddChild(
            new LoadingShell(
                LauncherView.ScaleFor(gameNode.GetViewport().GetVisibleRect().Size),
                "Loading your game",
                true
            )
        );
        await gameNode.ToSignal(
            RenderingServer.Singleton,
            RenderingServer.SignalName.FramePostDraw
        );
        try
        {
            SaveManager.Instance.InitSettingsData();

            var gameStartup =
                game.GetType()
                    .GetMethod("GameStartup", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new MissingMethodException(game.GetType().FullName, "GameStartup");

            PatchHelper.Log("[Startup] Starting original GameStartup");
            await (Task)gameStartup.Invoke(game, null);
            PatchHelper.Log("[Startup] GameStartup completed");
            STS2Mobile.Graphics.MobileRenderScale.Attach(gameNode.GetTree().Root);
            // PlatformUtil's static constructor creates a native Steam callback.
            // Do not touch those types during early Mono/launcher bootstrap.
            try { SteamMultiplayerPatches.Apply(new Harmony("com.sts2mobile.steam")); }
            catch (Exception e) { PatchHelper.Log("[SteamMP] Integração indisponível: " + e.Message); }
        }
        catch (Exception ex)
        {
            // Exceptions from an awaited startup Task are not wrapped in
            // TargetInvocationException. Log and display both failure forms.
            var cause = ex is TargetInvocationException invocation
                ? invocation.InnerException ?? invocation
                : ex;
            PatchHelper.Log($"[Startup] Game startup failed: {cause}");

            var dialog = new AcceptDialog
            {
                Title = "Game startup failed",
                DialogText =
                    "The game could not start.\n"
                    + $"{cause.GetType().Name}: {cause.Message}\n\n"
                    + "Restart the app before trying again. See the log for details.",
            };
            gameNode.AddChild(dialog);
            dialog.PopupCentered(new Vector2I(800, 360));
            throw;
        }
        finally
        {
            if (GodotObject.IsInstanceValid(loading))
                loading.QueueFree();
        }
    }
}

using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Platform.Steam;
using STS2Mobile.Steam.Multiplayer;

namespace STS2Mobile.Patches;

public static class SteamHostingPatches
{
    [ThreadStatic] private static bool bypass;
    public static bool UseSteamHosting { get; private set; } = true;
    public static void Apply(Harmony harmony) {
        foreach (var type in new[] { typeof(NMultiplayerHostSubmenu), typeof(NMultiplayerSubmenu) }) {
            var original = AccessTools.Method(type, "StartHostAsync");
            var state = original.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType;
            var move = state?.GetMethod("MoveNext", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance) ?? throw new MissingMethodException(type.Name, "async MoveNext");
            harmony.Patch(move, transpiler: new HarmonyMethod(typeof(SteamHostingPatches), nameof(ChooseTransport)));
            harmony.Patch(original, prefix: new HarmonyMethod(typeof(SteamHostingPatches), nameof(ChooseHost)));
        }
        harmony.Patch(AccessTools.Method(typeof(NetHostGameService), nameof(NetHostGameService.StartSteamHost)), prefix: new HarmonyMethod(typeof(SteamHostingPatches), nameof(StartSteamHost)));
        PatchHelper.PatchGetter(harmony, typeof(SteamPlatformUtilStrategy), "SupportsInviteDialog", PatchHelper.Method(typeof(SteamHostingPatches), nameof(NoOverlay)));
        PatchHelper.Log("[SteamHost] Original new/resume host flows connected to mobile Steam transport.");
    }
    public static IEnumerable<CodeInstruction> ChooseTransport(IEnumerable<CodeInstruction> instructions) {
        var native = AccessTools.PropertyGetter(typeof(SteamInitializer), nameof(SteamInitializer.Initialized));
        var mobile = AccessTools.PropertyGetter(typeof(SteamHostingPatches), nameof(UseSteamHosting)); var changed = 0;
        foreach (var instruction in instructions) {
            if (instruction.Calls(native)) { instruction.opcode = OpCodes.Call; instruction.operand = mobile; changed++; }
            yield return instruction;
        }
        if (changed != 1) throw new InvalidOperationException("Unexpected original host flow: Steam platform check count=" + changed);
    }
    public static bool ChooseHost(object __instance, MethodBase __originalMethod, object[] __args, ref Task __result) {
        if (bypass) return true;
        var parent = __instance as Node ?? __args.OfType<Control>().FirstOrDefault()?.GetParent();
        if (parent == null) throw new InvalidOperationException("A tela de criação de sala não está disponível.");
        __result = SelectAndStart(parent, __instance, (MethodInfo)__originalMethod, __args); return false;
    }
    private static async Task SelectAndStart(Node parent, object instance, MethodInfo method, object[] args) {
        var choice = await SteamHostChoiceDialog.ShowAsync(parent); await GameThread.Switch();
        if (!choice.HasValue || !GodotObject.IsInstanceValid(parent)) return;
        UseSteamHosting = choice.Value;
        Task start;
        bypass = true;
        try { start = (Task)method.Invoke(instance, args); }
        finally { bypass = false; }
        await start;
    }
    public static bool StartSteamHost(NetHostGameService __instance, int maxClients, ref Task<NetErrorInfo?> __result) {
        var host = new MobileSteamNetHost(__instance);
        AccessTools.Field(typeof(NetHostGameService), "_netHost").SetValue(__instance, host);
        AccessTools.Property(typeof(NetHostGameService), nameof(NetHostGameService.Platform)).SetValue(__instance, PlatformType.Steam);
        __result = host.StartAsync(maxClients); return false;
    }
    public static bool NoOverlay(ref bool __result) { __result = false; return false; }
}

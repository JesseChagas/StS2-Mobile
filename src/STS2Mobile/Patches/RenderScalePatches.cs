using Godot;
using HarmonyLib;
using STS2Mobile.Graphics;
namespace STS2Mobile.Patches;

public static class RenderScalePatches
{
    public static void Apply(Harmony harmony) {
        var assembly = typeof(MegaCrit.Sts2.Core.Nodes.NGame).Assembly;
        var settings = assembly.GetType("MegaCrit.Sts2.Core.Nodes.Screens.Settings.NSettingsScreen");
        if (settings != null) PatchHelper.Patch(harmony, settings, "_Ready", postfix: PatchHelper.Method(typeof(RenderScalePatches), nameof(AddSetting)));
    }
    public static void AddSetting(Node __instance) {
        var panel = __instance.GetNodeOrNull("%GraphicsSettings");
        var content = panel == null ? null : AccessTools.Property(panel.GetType(), "Content")?.GetValue(panel) as Node;
        if (content == null || content.GetNodeOrNull("MobileRenderScaleSetting") != null) return;
        var row = new RenderScaleSettingsRow(); content.AddChild(row);
        var reset = content.GetNodeOrNull("ResetGraphics"); if (reset != null) content.MoveChild(row, reset.GetIndex());
        panel.CallDeferred("RefreshSize"); panel.CallDeferred("UpdateNavigation");
    }
}

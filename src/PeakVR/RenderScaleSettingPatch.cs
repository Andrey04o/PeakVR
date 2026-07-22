using HarmonyLib;

namespace PeakVR;

[HarmonyPatch(typeof(RenderScaleSetting), nameof(RenderScaleSetting.ApplyValue))]
internal static class RenderScaleSettingPatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        if (Plugin.Config != null && Plugin.Config.SharpenImage.Value != "Enable")
            return;

        VRRender.ApplySharpening();
    }
}

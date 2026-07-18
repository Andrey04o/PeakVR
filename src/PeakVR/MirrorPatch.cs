using HarmonyLib;
using UnityEngine;

namespace PeakVR;

[HarmonyPatch(typeof(Mirror), "Start")]
internal static class MirrorPatch
{
    [HarmonyPostfix]
    private static void Postfix(Mirror __instance)
    {
        if (!Plugin.VrEnabled || __instance.mirrorCamera == null)
            return;

        var ui = LayerMask.NameToLayer("UI");
        if (ui >= 0)
            __instance.mirrorCamera.cullingMask &= ~(1 << ui);

        Plugin.Log.LogInfo("[PeakVR] Mirror camera set to exclude VR UI + tunneling layer");
    }
}

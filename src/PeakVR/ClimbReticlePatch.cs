using HarmonyLib;
using UnityEngine;

namespace PeakVR;

[HarmonyPatch(typeof(GUIManager), "SetReticle")]
internal static class ClimbReticlePatch
{
    [HarmonyPrefix]
    private static void Prefix(GUIManager __instance, ref GameObject activeReticle)
    {
        if (activeReticle == null || !VRHandClimb.Enabled)
            return;

        if (activeReticle == __instance.reticleRope || activeReticle == __instance.reticleSpike
            || activeReticle == __instance.reticleVine)
            activeReticle = __instance.reticleDefault;
    }
}

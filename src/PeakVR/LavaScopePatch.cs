using HarmonyLib;
using UnityEngine;

namespace PeakVR;

[HarmonyPatch(typeof(LavaPost), "LateUpdate")]
internal static class LavaScopePatch
{
    [HarmonyPostfix]
    private static void Postfix(LavaPost __instance)
    {
        if (!Plugin.VrEnabled || !VRBinoculars.ScopeActive)
            return;

        var rend = __instance.GetComponent<MeshRenderer>();
        if (rend != null && !rend.enabled)
            rend.enabled = true;
    }
}

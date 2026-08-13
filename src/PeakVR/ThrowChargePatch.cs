using HarmonyLib;
using UnityEngine;

namespace PeakVR;

[HarmonyPatch(typeof(Item), "RPC_SetThrownData")]
internal static class ThrowChargePatch
{
    [HarmonyPrefix]
    private static void Prefix(ref float thrownAmount)
    {
        if (thrownAmount < 0f)
            thrownAmount = Mathf.Clamp01(-thrownAmount - 1f);
    }
}

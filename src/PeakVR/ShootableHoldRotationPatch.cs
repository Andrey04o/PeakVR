using HarmonyLib;
using UnityEngine;

namespace PeakVR;

[HarmonyPatch(typeof(CharacterItems))]
internal static class ShootableHoldRotationPatch
{
    public static bool Enabled = true;

    [HarmonyPatch("GetItemHoldForward")]
    [HarmonyPostfix]
    private static void ForwardPostfix(CharacterItems __instance, Item item, ref Vector3 __result)
    {
        if (TryController(__instance, item, out var t))
            __result = t.forward;
    }

    [HarmonyPatch("GetItemHoldUp")]
    [HarmonyPostfix]
    private static void UpPostfix(CharacterItems __instance, Item item, ref Vector3 __result)
    {
        if (TryController(__instance, item, out var t))
            __result = t.up;
    }

    private static bool TryController(CharacterItems items, Item item, out Transform t)
    {
        t = VRHands.Right;
        return Enabled
            && t != null
            && item != null
            && item.UIData.isShootable
            && items.character == Character.localCharacter;
    }
}

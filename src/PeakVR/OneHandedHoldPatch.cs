using HarmonyLib;
using UnityEngine;

namespace PeakVR;

[HarmonyPatch(typeof(CharacterItems))]
internal static class OneHandedHoldPatch
{
    public static bool Enabled = true;

    [HarmonyPatch("AttachItem")]
    [HarmonyPostfix]
    private static void AttachItemPostfix(CharacterItems __instance)
    {
        if (!ShouldApply(__instance))
            return;

        DestroyHandJoint(__instance, BodypartType.Hand_L);
    }

    [HarmonyPatch("UnAttachItem")]
    [HarmonyPrefix]
    private static bool UnAttachItemPrefix(CharacterItems __instance)
    {
        if (!ShouldApply(__instance))
            return true;

        DestroyHandJoint(__instance, BodypartType.Hand_L);
        DestroyHandJoint(__instance, BodypartType.Hand_R);
        return false;
    }

    private static void DestroyHandJoint(CharacterItems items, BodypartType part)
    {
        var rig = items.character.GetBodypartRig(part);
        if (rig == null)
            return;

        if (rig.gameObject.TryGetComponent<FixedJoint>(out var joint))
            Object.Destroy(joint);
    }

    private static bool ShouldApply(CharacterItems items)
    {
        return Enabled
            && items.character == Character.localCharacter
            && VRHands.Right != null;
    }
}

using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace PeakVR;

[HarmonyPatch(typeof(CharacterItems))]
internal static class OneHandedHoldPatch
{
    public static bool Enabled = true;

    private static readonly MethodInfo GetPosRight = AccessTools.Method(typeof(CharacterItems), "GetItemPosRightWorld");
    private static readonly MethodInfo GetRotRight = AccessTools.Method(typeof(CharacterItems), "GetItemRotRightWorld");

    // Attach ONLY the right hand — for the local VR player AND remote VR players.
    [HarmonyPatch("AttachItem")]
    [HarmonyPrefix]
    private static bool AttachItemPrefix(CharacterItems __instance, Item item)
    {
        if (!ShouldApply(__instance) || item == null || GetPosRight == null || GetRotRight == null)
            return true;

        var handR = __instance.character.GetBodypartRig(BodypartType.Hand_R);
        if (handR == null)
            return true;

        handR.transform.position = (Vector3)GetPosRight.Invoke(__instance, new object[] { item });
        handR.transform.rotation = (Quaternion)GetRotRight.Invoke(__instance, new object[] { item });
        if (item.rig != null)
            handR.gameObject.AddComponent<FixedJoint>().connectedBody = item.rig;
        return false;
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
        if (!Enabled || items.character == null)
            return false;

        var c = items.character;
        if (c == Character.localCharacter && VRHands.Right != null)
            return true;

        return VRNetworking.IsActiveRemote(c);
    }
}

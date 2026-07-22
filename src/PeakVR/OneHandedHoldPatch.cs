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

    // Hold the item at the right hand instead of the body centre. Without this the item spawns in front
    // of the torso and the hold physics drag it across the body to the hand (shoving the player through
    // collisions) before AttachItem joints it. We reproduce the AttachItem relationship (item's "Hand_R"
    // grip child coincident with the Hand_R bone) so the item appears at the hand — with the correct
    // rotation — from the first frame, at any hand position. HoldItem torques toward GetItemHoldForward/Up
    // and forces toward GetItemHoldPos, so all three are redirected to the grip target.
    [HarmonyPatch("GetItemHoldPos")]
    [HarmonyPostfix]
    private static void GetItemHoldPosPostfix(CharacterItems __instance, Item item, ref Vector3 __result)
    {
        if (TryHoldTarget(__instance, item, out Vector3 pos, out _))
            __result = pos;
    }

    [HarmonyPatch("GetItemHoldForward")]
    [HarmonyPostfix]
    private static void GetItemHoldForwardPostfix(CharacterItems __instance, Item item, ref Vector3 __result)
    {
        if (TryHoldTarget(__instance, item, out _, out Quaternion rot))
            __result = rot * Vector3.forward;
    }

    [HarmonyPatch("GetItemHoldUp")]
    [HarmonyPostfix]
    private static void GetItemHoldUpPostfix(CharacterItems __instance, Item item, ref Vector3 __result)
    {
        if (TryHoldTarget(__instance, item, out _, out Quaternion rot))
            __result = rot * Vector3.up;
    }

    [HarmonyPatch("GetItemHoldRotation")]
    [HarmonyPostfix]
    private static void GetItemHoldRotationPostfix(CharacterItems __instance, Item item, ref Quaternion __result)
    {
        if (TryHoldTarget(__instance, item, out _, out Quaternion rot))
            __result = rot;
    }

    private static bool TryHoldTarget(CharacterItems items, Item item, out Vector3 pos, out Quaternion rot)
    {
        pos = Vector3.zero;
        rot = Quaternion.identity;

        if (item == null || !ShouldApply(items))
            return false;

        Rigidbody handR = items.character.GetBodypartRig(BodypartType.Hand_R);
        if (handR == null)
            return false;

        Transform grip = item.transform.Find("Hand_R");
        if (grip == null)
            return false;

        Quaternion itemRotInv = Quaternion.Inverse(item.transform.rotation);
        Quaternion gripLocalRot = itemRotInv * grip.rotation;
        Vector3 gripLocalDir = itemRotInv * (grip.position - item.transform.position);

        rot = handR.transform.rotation * Quaternion.Inverse(gripLocalRot);
        pos = handR.transform.position - rot * gripLocalDir;
        return true;
    }

    // Attach ONLY the right hand — for the local VR player AND remote VR players.
    [HarmonyPatch("AttachItem")]
    [HarmonyPrefix]
    private static bool AttachItemPrefix(CharacterItems __instance, Item item)
    {
        if (!ShouldApply(__instance) || item == null || GetPosRight == null || GetRotRight == null)
            return true;

        Rigidbody handR = __instance.character.GetBodypartRig(BodypartType.Hand_R);
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

        if (IsLocalVR(items))
            return true;

        return VRNetworking.IsActiveRemote(items.character);
    }

    private static bool IsLocalVR(CharacterItems items)
    {
        return Enabled && items.character != null
            && items.character == Character.localCharacter && VRHands.Right != null;
    }
}

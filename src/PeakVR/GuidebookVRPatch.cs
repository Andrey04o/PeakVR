using HarmonyLib;
using UnityEngine;

namespace PeakVR;

[HarmonyPatch(typeof(Guidebook), "LateUpdate")]
internal static class GuidebookVRPatch
{
    private const float ForwardOffset = 0.22f;
    private const float UpOffset = 0.04f;

    [HarmonyPrefix]
    private static bool Prefix(Guidebook __instance)
    {
        var hand = VRHands.Right;
        if (hand == null || !__instance.isOpen || __instance.bookTransform == null
            || __instance.holderCharacter == null || !__instance.holderCharacter.IsLocal
            || MainCamera.instance == null)
            return true;

        var head = MainCamera.instance.cam.transform;
        var target = hand.position + hand.forward * ForwardOffset + hand.up * UpOffset;

        __instance.bookTransform.position = Vector3.Lerp(__instance.bookTransform.position, target, Time.deltaTime * 10f);
        __instance.bookTransform.forward = (target - head.position).normalized;
        return false;
    }
}

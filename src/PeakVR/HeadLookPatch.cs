using HarmonyLib;
using UnityEngine;

namespace PeakVR;

[HarmonyPatch(typeof(CharacterMovement), "CameraLook")]
internal static class HeadLookPatch
{
    [HarmonyPostfix]
    private static void Postfix(CharacterMovement __instance)
    {
        if (!Plugin.VrEnabled)
            return;

        var character = Character.localCharacter;
        if (character == null || MainCamera.instance == null)
            return;

        if (__instance.GetComponent<Character>() != character)
            return;

        Transform head = MainCamera.instance.cam.transform;
        Vector3 forward = Quaternion.Inverse(VRHeadRig.ViewTilt) * head.forward;
        if ((character.data.fullyPassedOut || character.data.dead) && VRGhostSync.TryGetLook(character, head.position, out Vector3 ghostLook))
            forward = ghostLook;
        character.data.lookValues = HelperFunctions.DirectionToLook(forward);
        character.RecalculateLookDirections();
    }
}

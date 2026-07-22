using HarmonyLib;
using UnityEngine;

namespace PeakVR;

[HarmonyPatch(typeof(MainCameraMovement), "CharacterCam")]
internal static class HeadRotationCopyPatch
{
    private static Quaternion smoothed = Quaternion.identity;
    private const float SmoothSpeed = 12f;

    [HarmonyPostfix]
    private static void Postfix(MainCameraMovement __instance)
    {
        if (Plugin.VrEnabled || Plugin.Config == null || !Plugin.Config.CopyHeadRotation.Value)
        {
            smoothed = Quaternion.identity;
            return;
        }

        Character character = Character.localCharacter;
        if (character == null)
            return;

        Rigidbody headRig = character.GetBodypartRig(BodypartType.Head);
        Camera cam = MainCamera.instance != null ? MainCamera.instance.cam : null;
        if (headRig == null || cam == null)
            return;

        Vector3 lookDir = character.data.lookDirection;
        if (lookDir.sqrMagnitude < 1e-4f)
            return;

        Quaternion target = headRig.transform.rotation * Quaternion.Inverse(Quaternion.LookRotation(lookDir));
        smoothed = Quaternion.Slerp(smoothed, target, Mathf.Clamp01(Time.deltaTime * SmoothSpeed));
        cam.transform.rotation = smoothed * cam.transform.rotation;
    }
}

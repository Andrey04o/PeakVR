using HarmonyLib;
using UnityEngine;

namespace PeakVR;

[HarmonyPatch(typeof(CharacterAnimations))]
internal static class VRArmIKPatch
{
    public static Quaternion HandRotationOffset = Quaternion.Euler(-90f, 180f, 0f);
    public static float ArmScale = 1f;

    private static readonly Vector3 ShoulderOffsetLeft = new(-0.18f, -0.2f, 0f);
    private static readonly Vector3 ShoulderOffsetRight = new(0.18f, -0.2f, 0f);

    [HarmonyPatch("HandleIK")]
    [HarmonyPostfix]
    private static void HandleIKPostfix(CharacterAnimations __instance)
    {
        if (!ShouldDrive(__instance, out var c))
            return;

        c.refs.ikRig.weight = 1f;
        c.refs.ikLeft.weight = 1f;
        c.refs.ikRight.weight = 1f;
    }

    [HarmonyPatch(nameof(CharacterAnimations.ConfigureIK))]
    [HarmonyPostfix]
    private static void ConfigureIKPostfix(CharacterAnimations __instance)
    {
        if (!ShouldDrive(__instance, out var c))
            return;

        var refs = c.refs;
        if (refs.ikLeft.data.root == null || refs.ikRight.data.root == null)
            return;

        var cam = MainCamera.instance.cam.transform;
        var shoulderL = cam.rotation * ShoulderOffsetLeft;
        var shoulderR = cam.rotation * ShoulderOffsetRight;

        refs.IKHandTargetLeft.position =
            refs.ikLeft.data.root.position + ArmScale * (VRHands.Left.position - cam.position - shoulderL);
        refs.IKHandTargetRight.position =
            refs.ikRight.data.root.position + ArmScale * (VRHands.Right.position - cam.position - shoulderR);

        refs.IKHandTargetLeft.rotation = VRHands.Left.rotation * HandRotationOffset;
        refs.IKHandTargetRight.rotation = VRHands.Right.rotation * HandRotationOffset;

        refs.ikRig.weight = 1f;
        refs.ikLeft.weight = 1f;
        refs.ikRight.weight = 1f;
    }

    private static bool ShouldDrive(CharacterAnimations anim, out Character c)
    {
        c = Character.localCharacter;
        if (c == null || VRHands.Left == null || VRHands.Right == null)
            return false;
        if (c.refs.animations != anim)
            return false;
        if (c.refs.IKHandTargetLeft == null || c.refs.ikRig == null ||
            c.refs.ikLeft == null || c.refs.ikRight == null)
            return false;
        return true;
    }
}

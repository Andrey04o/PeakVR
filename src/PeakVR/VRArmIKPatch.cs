using HarmonyLib;
using UnityEngine;

namespace PeakVR;

[HarmonyPatch(typeof(CharacterAnimations))]
internal static class VRArmIKPatch
{
    public static Quaternion HandRotationOffset = Quaternion.Euler(-90f, 180f, 0f);

    private static bool logged;

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
        refs.IKHandTargetLeft.position = VRHands.Left.position;
        refs.IKHandTargetRight.position = VRHands.Right.position;
        refs.IKHandTargetLeft.rotation = VRHands.Left.rotation * HandRotationOffset;
        refs.IKHandTargetRight.rotation = VRHands.Right.rotation * HandRotationOffset;

        refs.ikRig.weight = 1f;
        refs.ikLeft.weight = 1f;
        refs.ikRight.weight = 1f;

        if (!logged)
        {
            logged = true;
            Plugin.Log.LogInfo($"[PeakVR] Arm IK driving hands (currentItem={c.data.currentItem})");
        }
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

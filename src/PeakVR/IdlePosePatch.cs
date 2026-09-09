using HarmonyLib;
using UnityEngine;

namespace PeakVR;

[HarmonyPatch(typeof(CharacterAnimations), "Update")]
internal static class IdlePosePatch
{
    private static readonly int Idle = Animator.StringToHash("Idle");

    private const int DefaultPose = 3;

    private static int pose = DefaultPose;

    [HarmonyPostfix]
    private static void Postfix(CharacterAnimations __instance)
    {
        var c = __instance.character;
        if (c == null || c.refs == null || c.refs.animations != __instance || c.refs.animator == null)
            return;

        if (c.data != null && c.data.fullyPassedOut)
            return;

        if (!IsVr(c))
            return;

        c.refs.animator.SetFloat(Idle, pose);
    }

    public static void Cycle()
    {
        var max = MaxIdles();
        pose = (pose + 1) % max;
        Plugin.Log.LogInfo($"[PeakVR] VR idle pose {pose} of 0-{max - 1} (default {DefaultPose})");
    }

    private static int MaxIdles()
    {
        var c = Character.localCharacter;
        if (c != null && c.refs != null && c.refs.customization != null && c.refs.customization.maxIdles > 0)
            return c.refs.customization.maxIdles;

        return 8;
    }

    private static bool IsVr(Character c)
    {
        if (c == Character.localCharacter)
            return Plugin.VrEnabled;

        return VRNetworking.IsActiveRemote(c);
    }
}

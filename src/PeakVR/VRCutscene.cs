using HarmonyLib;
using UnityEngine;

namespace PeakVR;

internal static class VRCutscene
{
    public static bool Active;
    public static Camera[] Cameras;

    public static Transform CurrentTransform()
    {
        if (Cameras == null)
            return null;

        Transform fallback = null;
        foreach (var c in Cameras)
        {
            if (c == null)
                continue;
            fallback = c.transform;
            if (c.isActiveAndEnabled)
                return c.transform;
        }
        return fallback;
    }
}

[HarmonyPatch(typeof(PeakHandler), "EndCutscene")]
internal static class EndCutsceneVRPatch
{
    [HarmonyPostfix]
    private static void Postfix(PeakHandler __instance)
    {
        if (!Plugin.VrEnabled || __instance.endCutscene == null)
            return;

        if (MainCamera.instance != null)
            MainCamera.instance.gameObject.SetActive(true);

        var cams = __instance.endCutscene.GetComponentsInChildren<Camera>(true);
        if (cams.Length == 0)
            return;

        foreach (var c in cams)
            c.stereoTargetEye = StereoTargetEyeMask.None;

        if (MainCamera.instance != null)
            MainCamera.instance.cam.cullingMask = cams[0].cullingMask;

        VRCutscene.Cameras = cams;
        VRCutscene.Active = true;

        Plugin.Log.LogInfo($"[PeakVR] EndCutscene: following {cams.Length} cutscene camera(s)");
    }
}

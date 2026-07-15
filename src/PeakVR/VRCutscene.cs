using HarmonyLib;
using UnityEngine;

namespace PeakVR;

internal static class VRCutscene
{
    public static bool Active;
    public static Camera[] Cameras;
    public static RenderTexture Sink;

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

        // Our stereo MainCamera renders the HMD image (following the active cutscene cam's
        // transform), so the game's cutscene cameras are redundant full-scene renders in VR.
        // Neuter their rendering to a 1x1 sink to cut GPU load (they stay enabled so active-cam
        // detection in CurrentTransform() is unchanged). Extra render passes at the ending are a
        // suspected trigger of VDXR's D3D12 device-removed failure.
        if (VRCutscene.Sink == null)
            VRCutscene.Sink = new RenderTexture(1, 1, 0) { name = "PeakVR CutsceneSink" };

        foreach (var c in cams)
        {
            c.stereoTargetEye = StereoTargetEyeMask.None;
            c.targetTexture = VRCutscene.Sink;
        }

        if (MainCamera.instance != null)
            MainCamera.instance.cam.cullingMask = cams[0].cullingMask;

        VRCutscene.Cameras = cams;
        VRCutscene.Active = true;

        Plugin.Log.LogInfo($"[PeakVR] EndCutscene: following {cams.Length} cutscene camera(s), rendering to sink");
    }
}

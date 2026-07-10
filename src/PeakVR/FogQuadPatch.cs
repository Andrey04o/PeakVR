using HarmonyLib;
using UnityEngine;

namespace PeakVR;

[HarmonyPatch(typeof(CameraQuad), "LateUpdate")]
internal static class FogQuadPatch
{
    private const float Margin = 1.6f;

    [HarmonyPrefix]
    private static bool Prefix(CameraQuad __instance)
    {
        var cam = MainCamera.instance != null ? MainCamera.instance.cam : null;
        if (cam == null)
            return false;

        var proj = cam.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left);
        if (proj.m00 <= 0.0001f || proj.m11 <= 0.0001f)
            return false;

        var t = __instance.transform;
        var d = cam.nearClipPlane + 0.01f;
        var w = 2f * d / proj.m00 * Margin;
        var h = 2f * d / proj.m11 * Margin;

        if (t.parent != cam.transform)
            t.SetParent(cam.transform, false);

        t.localPosition = new Vector3(0f, 0f, d);
        t.localRotation = Quaternion.identity;
        t.localScale = new Vector3(w, h, 1f);

        return false;
    }
}

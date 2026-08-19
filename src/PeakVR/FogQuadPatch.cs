using HarmonyLib;
using UnityEngine;

namespace PeakVR;

[HarmonyPatch(typeof(CameraQuad), "LateUpdate")]
internal static class FogQuadPatch
{
    private const float Margin = 1.6f;

    private static readonly VRRestore Reparented = new();

    [HarmonyPrefix]
    private static bool Prefix(CameraQuad __instance)
    {
        if (!Plugin.VrEnabled)
            return true;

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
        {
            Reparented.Record(t);
            t.SetParent(cam.transform, false);
        }

        t.localPosition = new Vector3(0f, 0f, d);
        t.localRotation = Quaternion.identity;
        t.localScale = new Vector3(w, h, 1f);

        return false;
    }

    // The quads are parented onto the VR camera; the vanilla LateUpdate sizes them from the viewport
    // instead, so they have to go back to their own parents or they inherit the camera's transform.
    public static void Restore() => Reparented.RestoreAll();
}

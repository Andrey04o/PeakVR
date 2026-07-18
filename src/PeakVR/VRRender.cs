using System;
using System.Reflection;
using HarmonyLib;

namespace PeakVR;

internal static class VRRender
{
    private static bool logged;

    // PEAK's build strips URP's XR post-processing passes (UberPostXR / FinalPostXR = pass index 1).
    // When the OpenXR runtime provides a visibility/occlusion mesh, URP renders UberPost/FinalPost
    // through that mesh with pass 1 (PostProcessPassRenderGraph, xr.hasValidVisibleMesh branch) — which
    // is missing in the build, giving "invalid pass index 1" and a flickering/black headset (Unity
    // 6000.3 / URP 17.3). Disabling the visibility mesh makes URP fall back to a full-screen blit with
    // pass 0, so post-processing works. Reflection keeps this version-agnostic (no-op on URP versions
    // without the toggle).
    public static void DisableXRVisibilityMesh()
    {
        try
        {
            var settings = AccessTools.TypeByName("UnityEngine.Rendering.XRSRPSettings");
            var prop = settings?.GetProperty("useVisibilityMesh", BindingFlags.Public | BindingFlags.Static);
            if (prop != null && prop.CanWrite)
            {
                if ((bool)prop.GetValue(null))
                {
                    prop.SetValue(null, false);
                    Log("[PeakVR] Disabled XR visibility mesh (URP post-processing uses pass 0)");
                }
                return;
            }

            var xrSystem = AccessTools.TypeByName("UnityEngine.Rendering.XRSystem");
            var setter = xrSystem?.GetMethod("SetUseVisibilityMesh", BindingFlags.NonPublic | BindingFlags.Static);
            if (setter != null)
            {
                setter.Invoke(null, new object[] { false });
                Log("[PeakVR] Disabled XR visibility mesh via XRSystem");
            }
        }
        catch (Exception e)
        {
            Log($"[PeakVR] Could not disable XR visibility mesh: {e.Message}");
        }
    }

    private static void Log(string message)
    {
        if (logged)
            return;
        logged = true;
        Plugin.Log.LogInfo(message);
    }
}

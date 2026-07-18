using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace PeakVR;

internal static class VRRender
{
    private static bool logged;
    private static bool aoDisabled;

    // HBAO (Horizon-Based Ambient Occlusion, a screen-space AO renderer feature) renders wrong per-eye
    // under URP 17.3's XR path (PEAK beta, Unity 6000.3), giving inconsistent surface lighting between
    // the eyes. It's fine on the stable 6000.0 line (URP 17.0) — and the user wants AO there — so only
    // disable it on the newer Unity/URP.
    public static void DisableBrokenAO()
    {
        if (Application.unityVersion.StartsWith("6000.0."))
            return;

        try
        {
            UrpDiagnostics.SetFeatureActive("HBAO", false);
            if (!aoDisabled)
            {
                aoDisabled = true;
                Plugin.Log.LogInfo($"[PeakVR] Disabled HBAO ambient occlusion (broken under Unity {Application.unityVersion} / URP 17.3 XR path)");
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"[PeakVR] Could not disable HBAO: {e.Message}");
        }
    }

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

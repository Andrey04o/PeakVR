using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace PeakVR;

internal static class VRRender
{
    private static bool logged;
    private static bool aoDisabled;

    // Set the URP upscaling filter from config. The game default (STP) is a TEMPORAL upscaler that looks
    // great on a flat screen but blurs the whole image under VR MultiPass (its temporal reprojection is
    // broken there, and renderScale 1.0 doesn't help since the temporal pass still runs). Linear/FSR are
    // spatial and stay sharp. Anti-aliasing is intentionally left to other mods — this only sets upscaling,
    // which no other PEAK mod exposes. Reflection keeps it URP-version-agnostic.
    public static void ApplyUpscaling()
    {
        try
        {
            UnityEngine.Rendering.RenderPipelineAsset asset = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            PropertyInfo upProp = asset?.GetType().GetProperty("upscalingFilter");
            if (upProp == null || !upProp.CanWrite)
                return;

            string choice = Plugin.Config != null ? Plugin.Config.UpscalingFilter.Value : "Linear";
            object target = ParseEnum(upProp.PropertyType, choice);
            if (target == null || target.Equals(upProp.GetValue(asset)))
                return;

            upProp.SetValue(asset, target);
            Plugin.Log.LogInfo($"[PeakVR] Upscaling filter -> {target}");
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"[PeakVR] Could not set upscaling filter: {e.Message}");
        }
    }

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

    private static object ParseEnum(Type type, params string[] names)
    {
        foreach (string n in names)
        {
            try { return Enum.Parse(type, n); }
            catch { }
        }
        return null;
    }

    private static void Log(string message)
    {
        if (logged)
            return;
        logged = true;
        Plugin.Log.LogInfo(message);
    }
}

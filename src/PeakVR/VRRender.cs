using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace PeakVR;

internal static class VRRender
{
    private static bool logged;
    private static bool aoDisabled;

    private static int originalMsaa = -1;

    public static void ApplySharpening()
    {
        if (!Plugin.VrEnabled)
            return;

        try
        {
            bool enable = Plugin.Config == null || Plugin.Config.SharpenImage.Value == "Enable";

            UnityEngine.Rendering.RenderPipelineAsset asset = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            PropertyInfo upProp = asset?.GetType().GetProperty("upscalingFilter");
            PropertyInfo msProp = asset?.GetType().GetProperty("msaaSampleCount");
            PropertyInfo rsProp = asset?.GetType().GetProperty("renderScale");

            Camera cam = MainCamera.instance != null ? MainCamera.instance.cam : Camera.main;
            Component addData = cam != null ? cam.GetComponent("UniversalAdditionalCameraData") : null;
            PropertyInfo aaProp = addData?.GetType().GetProperty("antialiasing");

            if (msProp != null && originalMsaa < 0)
                originalMsaa = (int)msProp.GetValue(asset);

            bool changed = false;

            if (upProp != null && upProp.CanWrite)
            {
                object target;
                if (enable)
                {
                    target = ParseEnum(upProp.PropertyType, "Linear", "Auto");
                }
                else
                {
                    float rs = rsProp != null ? (float)rsProp.GetValue(asset) : 0.8f;
                    target = ParseEnum(upProp.PropertyType, rs >= 0.999f ? "Linear" : "STP", "STP");
                }

                if (target != null && !target.Equals(upProp.GetValue(asset)))
                {
                    upProp.SetValue(asset, target);
                    changed = true;
                }
            }

            if (msProp != null && msProp.CanWrite && originalMsaa >= 0)
            {
                int target = enable ? 1 : originalMsaa;
                if ((int)msProp.GetValue(asset) != target)
                {
                    msProp.SetValue(asset, target);
                    changed = true;
                }
            }

            // Camera post-AA: None(0) when sharpening, TemporalAA(3) when disabled (the game default).
            if (aaProp != null && aaProp.CanWrite)
            {
                int target = enable ? 0 : 3;
                if (Convert.ToInt32(aaProp.GetValue(addData)) != target)
                {
                    aaProp.SetValue(addData, Enum.ToObject(aaProp.PropertyType, target));
                    changed = true;
                }
            }

            if (changed)
                Plugin.Log.LogInfo($"[PeakVR] Image sharpening {(enable ? "ENABLED" : "disabled")}");
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"[PeakVR] Could not apply sharpening: {e.Message}");
        }
    }

    // HBAO (Horizon-Based Ambient Occlusion, a screen-space AO renderer feature) renders wrong per-eye
    // under PEAK's URP 17.3 XR path (Unity 6000.3), giving inconsistent surface lighting between the
    // eyes, so disable it. (It was fine on the old 6000.0 / URP 17.0 line, but PEAK has since moved
    // everyone onto 6000.3.)
    public static void DisableBrokenAO()
    {
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

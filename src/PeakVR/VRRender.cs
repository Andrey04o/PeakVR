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
    private static int originalAA = -1;
    private static object originalUpscaling;
    private static float originalFarPlane = -1f;

    public static void ApplySharpening()
    {
        if (!Plugin.VrEnabled)
            return;

        ApplySharpening(Plugin.Config == null || Plugin.Config.SharpenImage.Value == "Enable");
    }

    public static void RestoreForFlat()
    {
        try
        {
            var asset = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;

            if (originalUpscaling != null)
                asset?.GetType().GetProperty("upscalingFilter")?.SetValue(asset, originalUpscaling);

            if (originalMsaa >= 0)
                asset?.GetType().GetProperty("msaaSampleCount")?.SetValue(asset, originalMsaa);

            var cam = MainCamera.instance != null ? MainCamera.instance.cam : Camera.main;
            var addData = cam != null ? cam.GetComponent("UniversalAdditionalCameraData") : null;
            var aaProp = addData?.GetType().GetProperty("antialiasing");

            if (originalAA >= 0 && aaProp != null && aaProp.CanWrite)
                aaProp.SetValue(addData, Enum.ToObject(aaProp.PropertyType, originalAA));

            Plugin.Log.LogInfo($"[PeakVR] Image pipeline restored (msaa={originalMsaa} upscaling={originalUpscaling} aa={originalAA})");
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"[PeakVR] Could not restore the image pipeline: {e.Message}");
        }

        originalUpscaling = null;
        originalMsaa = -1;
        originalAA = -1;

        if (originalFarPlane > 0f && MainCamera.instance != null && MainCamera.instance.cam != null)
            MainCamera.instance.cam.farClipPlane = originalFarPlane;
        originalFarPlane = -1f;

        if (aoDisabled)
        {
            aoDisabled = false;
            try
            {
                UrpDiagnostics.SetFeatureActive("HBAO", true);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[PeakVR] Could not restore HBAO: {e.Message}");
            }
        }
    }

    private static void ApplySharpening(bool enable)
    {
        try
        {
            UnityEngine.Rendering.RenderPipelineAsset asset = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            PropertyInfo upProp = asset?.GetType().GetProperty("upscalingFilter");
            PropertyInfo msProp = asset?.GetType().GetProperty("msaaSampleCount");
            PropertyInfo rsProp = asset?.GetType().GetProperty("renderScale");

            Camera cam = MainCamera.instance != null ? MainCamera.instance.cam : Camera.main;
            Component addData = cam != null ? cam.GetComponent("UniversalAdditionalCameraData") : null;
            PropertyInfo aaProp = addData?.GetType().GetProperty("antialiasing");

            if (msProp != null && originalMsaa < 0)
                originalMsaa = (int)msProp.GetValue(asset);
            if (upProp != null && originalUpscaling == null)
                originalUpscaling = upProp.GetValue(asset);
            if (aaProp != null && originalAA < 0)
                originalAA = Convert.ToInt32(aaProp.GetValue(addData));

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

    public static void DisableBrokenAO()
    {
        ApplyHBAO();
    }

    public static void ApplyFarPlane()
    {
        if (!Plugin.VrEnabled || Plugin.Config == null)
            return;

        var main = MainCamera.instance;
        if (main == null || main.cam == null)
            return;

        var distance = Plugin.Config.FarPlane.Value;
        if (distance <= main.cam.nearClipPlane)
            return;

        if (originalFarPlane < 0f)
            originalFarPlane = main.cam.farClipPlane;

        main.cam.farClipPlane = distance;
    }

    public static void ApplyHBAO()
    {
        if (!Plugin.VrEnabled)
            return;

        bool disable = Plugin.Config == null || Plugin.Config.ForceDisableHBAO.Value;
        try
        {
            UrpDiagnostics.SetFeatureActive("HBAO", !disable);
            if (disable && !aoDisabled)
            {
                aoDisabled = true;
                Plugin.Log.LogInfo($"[PeakVR] Disabled HBAO ambient occlusion (broken under Unity {Application.unityVersion} / URP 17.3 XR path)");
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"[PeakVR] Could not apply HBAO setting: {e.Message}");
        }
    }

    public static void DisableXRVisibilityMesh()
    {
        try
        {
            var settings = AccessTools.TypeByName("UnityEngine.Rendering.XRSRPSettings");
            if (settings != null)
            {
                bool changed = false;

                var visMesh = settings.GetProperty("useVisibilityMesh", BindingFlags.Public | BindingFlags.Static);
                if (visMesh != null && visMesh.CanWrite && (bool)visMesh.GetValue(null))
                {
                    visMesh.SetValue(null, false);
                    changed = true;
                }

                var occScale = settings.GetProperty("occlusionMeshScale", BindingFlags.Public | BindingFlags.Static);
                if (occScale != null && occScale.CanWrite && (float)occScale.GetValue(null) != 0f)
                {
                    occScale.SetValue(null, 0f);
                    changed = true;
                }

                if (changed)
                    Log("[PeakVR] Disabled XR visibility + occlusion meshes (useVisibilityMesh=false, occlusionMeshScale=0)");
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

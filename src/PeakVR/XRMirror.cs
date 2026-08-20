using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.XR;

namespace PeakVR;

internal static class XRMirror
{
    private const string XRSystemTypeName = "UnityEngine.Experimental.Rendering.XRSystem";
    private const string NativeShaderName = "Hidden/Universal Render Pipeline/XR/XRMirrorView";

    private static FieldInfo materialField;
    private static Material material;
    private static Shader shader;
    private static bool reclaimed;
    private static int lastBlitMode = int.MinValue;

    public static void Setup()
    {
        if (!Resolve())
            return;

        Assert();
    }

    public static void AssertBlitMode()
    {
        var displays = new List<XRDisplaySubsystem>();
        SubsystemManager.GetInstances(displays);

        foreach (var display in displays)
        {
            if (display == null || !display.running)
                continue;

            var before = display.GetPreferredMirrorBlitMode();
            if (before != XRMirrorViewBlitMode.LeftEye)
                display.SetPreferredMirrorBlitMode(XRMirrorViewBlitMode.LeftEye);

            if (before == lastBlitMode)
                continue;

            lastBlitMode = before;

            Plugin.Log.LogInfo($"[PeakVR][Mirror] blitMode {before} -> {display.GetPreferredMirrorBlitMode()} " +
                $"material={(materialField?.GetValue(null) is Material m && m != null ? m.shader.name : "<null>")} " +
                $"eyeTex={XRSettings.eyeTextureWidth}x{XRSettings.eyeTextureHeight} " +
                $"gameView={XRSettings.gameViewRenderMode} active={XRSettings.isDeviceActive}");
        }
    }

    public static void Assert()
    {
        if (materialField == null)
            return;

        if (material == null && !Resolve())
            return;

        if (ReferenceEquals(materialField.GetValue(null), material))
            return;

        materialField.SetValue(null, material);

        if (reclaimed)
            return;

        reclaimed = true;
        Plugin.Log.LogInfo("[PeakVR] Desktop mirror material re-installed");
    }

    private static bool Resolve()
    {
        if (materialField != null && material != null)
            return true;

        if (shader == null)
            shader = PeakAssets.MirrorView != null ? PeakAssets.MirrorView : Shader.Find(NativeShaderName);

        if (shader == null || !shader.isSupported)
        {
            Plugin.Log.LogWarning("[PeakVR] No XR mirror shader available; desktop view stays black");
            return false;
        }

        var type = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType(XRSystemTypeName, false))
            .FirstOrDefault(t => t != null);

        if (type == null)
        {
            Plugin.Log.LogError("[PeakVR] Could not find XRSystem type for desktop mirror");
            return false;
        }

        materialField = type.GetField("s_MirrorViewMaterial", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        if (materialField == null)
        {
            Plugin.Log.LogError("[PeakVR] XRSystem.s_MirrorViewMaterial field not found");
            return false;
        }

        material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        reclaimed = false;
        Plugin.Log.LogInfo($"[PeakVR] Desktop mirror material ready ({shader.name})");
        return true;
    }
}

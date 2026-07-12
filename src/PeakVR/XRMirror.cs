using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace PeakVR;

internal static class XRMirror
{
    private const string XRSystemTypeName = "UnityEngine.Experimental.Rendering.XRSystem";
    private const string NativeShaderName = "Hidden/Universal Render Pipeline/XR/XRMirrorView";

    private static FieldInfo materialField;
    private static Material material;

    public static void Setup()
    {
        if (!Resolve())
            return;

        Assert();
    }

    public static void Assert()
    {
        if (materialField == null)
            return;

        if (materialField.GetValue(null) is Material existing && existing != null)
        {
            material = existing;
            return;
        }

        if (material == null)
            return;

        materialField.SetValue(null, material);
    }

    private static bool Resolve()
    {
        if (materialField != null && material != null)
            return true;

        var shader = Shader.Find(NativeShaderName);
        if (shader == null || !shader.isSupported)
            shader = PeakAssets.MirrorView;

        if (shader == null)
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
        Plugin.Log.LogInfo("[PeakVR] Desktop mirror material ready");
        return true;
    }
}

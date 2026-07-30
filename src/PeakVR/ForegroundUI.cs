using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PeakVR;

internal static class ForegroundUI
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly List<ScriptableRendererData> Stripped = new();
    private static readonly List<LayerMask> SavedOpaque = new();
    private static readonly List<LayerMask> SavedTransparent = new();

    private static ForegroundUIPass pass;
    private static bool hooked;

    public static bool Active { get; private set; }

    public static void Apply()
    {
        if (!Plugin.VrEnabled)
            return;

        var wanted = Plugin.Config == null || Plugin.Config.ForegroundUI.Value;
        if (wanted == Active)
            return;

        if (wanted)
            Enable();
        else
            Disable();
    }

    private static void Enable()
    {
        var layer = VRLayers.UI;
        if (layer < 0)
        {
            Plugin.Log.LogWarning("[PeakVR][ForegroundUI] no UI layer; not enabling");
            return;
        }

        LayerMask mask = 1 << layer;

        foreach (var data in GetRendererData())
        {
            var opaque = data.GetType().GetProperty("opaqueLayerMask");
            var transparent = data.GetType().GetProperty("transparentLayerMask");
            if (opaque == null || transparent == null)
                continue;

            var opaqueValue = (LayerMask)opaque.GetValue(data);
            var transparentValue = (LayerMask)transparent.GetValue(data);

            Stripped.Add(data);
            SavedOpaque.Add(opaqueValue);
            SavedTransparent.Add(transparentValue);

            opaque.SetValue(data, (LayerMask)(opaqueValue.value & ~mask.value));
            transparent.SetValue(data, (LayerMask)(transparentValue.value & ~mask.value));
            data.SetDirty();
        }

        if (Stripped.Count == 0)
        {
            Plugin.Log.LogWarning("[PeakVR][ForegroundUI] no renderer data found; not enabling");
            return;
        }

        pass ??= new ForegroundUIPass();
        pass.Layers = mask;
        pass.ClearDepth = Plugin.Config == null || Plugin.Config.ForegroundUIOverGeometry.Value;

        if (!hooked)
        {
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            hooked = true;
        }

        Active = true;
        Plugin.Log.LogInfo($"[PeakVR][ForegroundUI] enabled (layer {layer}, {Stripped.Count} renderer data)");
    }

    private static void Disable()
    {
        for (var i = 0; i < Stripped.Count; i++)
        {
            var data = Stripped[i];
            if (data == null)
                continue;

            data.GetType().GetProperty("opaqueLayerMask")?.SetValue(data, SavedOpaque[i]);
            data.GetType().GetProperty("transparentLayerMask")?.SetValue(data, SavedTransparent[i]);
            data.SetDirty();
        }

        Stripped.Clear();
        SavedOpaque.Clear();
        SavedTransparent.Clear();

        if (hooked)
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            hooked = false;
        }

        Active = false;
        Plugin.Log.LogInfo("[PeakVR][ForegroundUI] disabled");
    }

    public static void ApplyDepthMode()
    {
        if (pass != null)
            pass.ClearDepth = Plugin.Config == null || Plugin.Config.ForegroundUIOverGeometry.Value;
    }

    private static void OnBeginCamera(ScriptableRenderContext context, Camera camera)
    {
        if (!Active || camera.cameraType != CameraType.Game)
            return;

        var data = camera.GetUniversalAdditionalCameraData();
        if (data == null || data.scriptableRenderer == null)
            return;

        data.scriptableRenderer.EnqueuePass(pass);
    }

    public static void LogUiLayerRenderers()
    {
        var layer = VRLayers.UI;
        if (layer < 0)
            return;

        var found = 0;
        foreach (var renderer in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (renderer.gameObject.layer != layer)
                continue;

            if (IsOurs(renderer.transform))
                continue;

            if (found++ < 30)
                Plugin.Log.LogInfo($"[PeakVR][ForegroundUI] game renderer on UI layer: {Path(renderer.transform)} ({renderer.GetType().Name})");
        }

        Plugin.Log.LogInfo($"[PeakVR][ForegroundUI] {found} non-PeakVR renderers on the UI layer");
    }

    private static bool IsOurs(Transform t)
    {
        while (t != null)
        {
            if (t.name.StartsWith("PeakVR"))
                return true;
            t = t.parent;
        }
        return false;
    }

    private static string Path(Transform t)
    {
        var path = t.name;
        var parent = t.parent;
        var depth = 0;
        while (parent != null && depth++ < 6)
        {
            path = parent.name + "/" + path;
            parent = parent.parent;
        }
        return path;
    }

    private static IEnumerable<ScriptableRendererData> GetRendererData()
    {
        var asset = GraphicsSettings.currentRenderPipeline;
        if (asset == null)
            yield break;

        if (asset.GetType().GetField("m_RendererDataList", Any)?.GetValue(asset) is not ScriptableRendererData[] list)
            yield break;

        foreach (var data in list)
            if (data != null)
                yield return data;
    }
}

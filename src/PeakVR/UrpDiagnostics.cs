using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace PeakVR;

// One-shot dump of the active URP configuration (e.g. ambient occlusion / lighting features).
// Reflection-based so it survives URP version changes (PEAK runs URP 17.3 on Unity 6000.3; the
// build references 17.0.4). Logged once at level load and re-triggerable with a debug key.
internal static class UrpDiagnostics
{
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    private static bool dumped;

    public static void DumpOnce()
    {
        if (dumped)
            return;
        dumped = true;
        Dump();
    }

    public static void Dump()
    {
        try
        {
            var log = Plugin.Log;
            log.LogInfo("[PeakVR][URP] ===== render settings dump =====");
            log.LogInfo($"[PeakVR][URP] unity={Application.unityVersion}");

            var asset = GraphicsSettings.currentRenderPipeline;
            log.LogInfo($"[PeakVR][URP] pipeline={(asset != null ? $"{asset.name} ({asset.GetType().Name})" : "<null>")}");

            log.LogInfo($"[PeakVR][URP] quality: lodBias={QualitySettings.lodBias} maxLOD={QualitySettings.maximumLODLevel} " +
                $"pixelLights={QualitySettings.pixelLightCount} aa={QualitySettings.antiAliasing} shadows={QualitySettings.shadows} " +
                $"shadowDist={QualitySettings.shadowDistance} softParticles={QualitySettings.softParticles}");
            log.LogInfo($"[PeakVR][URP] lighting: ambientMode={RenderSettings.ambientMode} ambientIntensity={RenderSettings.ambientIntensity:F2} " +
                $"ambient={RenderSettings.ambientLight} fog={RenderSettings.fog} reflIntensity={RenderSettings.reflectionIntensity:F2}");

            if (asset != null)
            {
                DumpProps(asset, "asset", new[]
                {
                    "supportsHDR", "msaaSampleCount", "renderScale", "upscalingFilter", "shadowDistance",
                    "supportsMainLightShadows", "supportsAdditionalLightShadows", "supportsSoftShadows",
                    "mainLightRenderingMode", "additionalLightsRenderingMode", "maxAdditionalLightsCount",
                    "colorGradingMode", "colorGradingLutSize", "useSRPBatcher"
                });
                DumpRenderers(asset);
            }

            DumpVolumes();

            log.LogInfo("[PeakVR][URP] ===== end dump =====");
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"[PeakVR][URP] dump failed: {e}");
        }
    }

    private static void DumpRenderers(object asset)
    {
        var field = asset.GetType().GetField("m_RendererDataList", Any);
        if (field?.GetValue(asset) is not Array list)
        {
            Plugin.Log.LogInfo("[PeakVR][URP] (no m_RendererDataList field)");
            return;
        }

        for (var i = 0; i < list.Length; i++)
        {
            var data = list.GetValue(i);
            if (data == null)
                continue;

            var name = (data as UnityEngine.Object)?.name ?? data.GetType().Name;
            Plugin.Log.LogInfo($"[PeakVR][URP] renderer[{i}] '{name}' type={data.GetType().Name} " +
                $"renderingMode={Member(data, "renderingMode")} depthPriming={Member(data, "depthPrimingMode")}");

            if (data.GetType().GetProperty("rendererFeatures", Any)?.GetValue(data) is not IEnumerable feats)
                continue;

            foreach (var f in feats)
            {
                if (f == null)
                {
                    Plugin.Log.LogInfo("[PeakVR][URP]    feature=<null>");
                    continue;
                }

                var fname = (f as UnityEngine.Object)?.name ?? f.GetType().Name;
                Plugin.Log.LogInfo($"[PeakVR][URP]    feature '{fname}' type={f.GetType().Name} active={Member(f, "isActive")}");

                var tn = f.GetType().Name.ToLowerInvariant();
                if (tn.Contains("occlusion") || tn.Contains("ssao") || tn.Contains("hbao") || tn.Contains("gtao"))
                    DumpFields(Member(f, "m_Settings") ?? f, "settings");
            }
        }
    }

    private static void DumpVolumes()
    {
        var volumes = UnityEngine.Object.FindObjectsByType<Volume>(FindObjectsSortMode.None);
        Plugin.Log.LogInfo($"[PeakVR][URP] volumes={volumes.Length} (active overrides per volume):");

        foreach (var v in volumes)
        {
            if (v == null || !v.isActiveAndEnabled || v.profile == null)
                continue;

            var overrides = new List<string>();
            foreach (var c in v.profile.components)
                if (c != null && c.active)
                    overrides.Add(c.GetType().Name);

            if (overrides.Count == 0)
                continue;

            Plugin.Log.LogInfo($"[PeakVR][URP]   '{v.name}' global={v.isGlobal} pri={v.priority} weight={v.weight:F2} -> {string.Join(", ", overrides)}");
        }
    }

    private static void DumpProps(object obj, string tag, string[] names)
    {
        var sb = new StringBuilder($"[PeakVR][URP] {tag}:");
        foreach (var n in names)
        {
            var v = Member(obj, n);
            if (v != null)
                sb.Append($" {n}={v}");
        }
        Plugin.Log.LogInfo(sb.ToString());
    }

    private static void DumpFields(object obj, string tag)
    {
        if (obj == null)
            return;

        var sb = new StringBuilder($"[PeakVR][URP]       {tag}:");
        foreach (var f in obj.GetType().GetFields(Any))
        {
            object v = null;
            try { v = f.GetValue(obj); } catch { }
            sb.Append($" {f.Name}={v}");
        }
        Plugin.Log.LogInfo(sb.ToString());
    }

    private static int testMode;
    private static float baseRenderScale = -1f;

    // Step through isolating the URP 17.3 lighting suspects (bound to a debug key) to narrow which
    // effect renders differently under the XR path.
    //   0 = everything on (baseline)
    //   1 = HBAO (ambient occlusion) off
    //   2 = + EdgeDetection off
    //   3 = + renderScale 1.0 (neutralises the STP temporal upscaler)
    public static void CycleTestMode()
    {
        testMode = (testMode + 1) % 4;

        SetFeatureActive("HBAO", testMode == 0);
        SetFeatureActive("EdgeDetection", testMode <= 1);
        SetRenderScaleOriginal(testMode <= 2);

        Plugin.Log.LogInfo($"[PeakVR][URP] test mode {testMode}: " +
            $"HBAO={(testMode == 0 ? "on" : "OFF")} EdgeDetection={(testMode <= 1 ? "on" : "OFF")} " +
            $"renderScale={(testMode <= 2 ? "orig" : "1.0")}");
    }

    public static void SetFeatureActive(string nameContains, bool active)
    {
        var asset = GraphicsSettings.currentRenderPipeline;
        if (asset == null || asset.GetType().GetField("m_RendererDataList", Any)?.GetValue(asset) is not Array list)
            return;

        foreach (var data in list)
        {
            if (data == null ||
                data.GetType().GetProperty("rendererFeatures", Any)?.GetValue(data) is not IEnumerable feats)
                continue;

            foreach (var f in feats)
            {
                if (f == null)
                    continue;

                var fname = (f as UnityEngine.Object)?.name ?? f.GetType().Name;
                if (fname.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) < 0 &&
                    f.GetType().Name.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                var setActive = f.GetType().GetMethod("SetActive", new[] { typeof(bool) });
                if (setActive != null)
                    setActive.Invoke(f, new object[] { active });
                else
                    f.GetType().GetProperty("isActive", Any)?.SetValue(f, active);
            }
        }
    }

    private static void SetRenderScaleOriginal(bool original)
    {
        var asset = GraphicsSettings.currentRenderPipeline;
        var prop = asset?.GetType().GetProperty("renderScale", Any);
        if (prop == null || !prop.CanWrite)
            return;

        if (baseRenderScale < 0f)
            baseRenderScale = (float)prop.GetValue(asset);

        prop.SetValue(asset, original ? baseRenderScale : 1.0f);
    }

    private static object Member(object obj, string name)
    {
        var t = obj.GetType();
        var p = t.GetProperty(name, Any);
        if (p != null && p.CanRead)
            try { return p.GetValue(obj); } catch { }

        var f = t.GetField(name, Any);
        if (f != null)
            try { return f.GetValue(obj); } catch { }

        return null;
    }
}

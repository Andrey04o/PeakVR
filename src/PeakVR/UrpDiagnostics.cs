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

            DumpGpuResidentDrawer();
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

    // URP 17's GPU Resident Drawer does its own per-view LOD/occlusion/small-mesh culling, separate
    // from Camera.useOcclusionCulling. Per-view means per-eye under MultiPass.
    private static readonly string[] GrdKeywords = { "gpuresident", "occlusionculling", "smallmesh", "instanceocclusion" };

    private static void DumpGpuResidentDrawer()
    {
        var asset = GraphicsSettings.currentRenderPipeline;
        if (asset == null)
            return;

        var type = asset.GetType();
        var found = 0;

        foreach (var p in type.GetProperties(Any | BindingFlags.Static))
        {
            if (!Matches(p.Name) || !p.CanRead)
                continue;
            Plugin.Log.LogInfo($"[PeakVR][GRD] prop {p.Name} = {SafeGet(() => p.GetValue(asset))}");
            found++;
        }

        foreach (var f in type.GetFields(Any | BindingFlags.Static))
        {
            if (!Matches(f.Name))
                continue;
            Plugin.Log.LogInfo($"[PeakVR][GRD] field {f.Name} = {SafeGet(() => f.GetValue(asset))}");
            found++;
        }

        var drawer = Type.GetType("UnityEngine.Rendering.GPUResidentDrawer, Unity.RenderPipelines.Core.Runtime");
        if (drawer != null)
        {
            foreach (var m in drawer.GetMethods(BindingFlags.Public | BindingFlags.Static))
                if (m.GetParameters().Length == 0 && m.ReturnType == typeof(bool) && Matches(m.Name))
                    Plugin.Log.LogInfo($"[PeakVR][GRD] {m.Name}() = {SafeGet(() => m.Invoke(null, null))}");
        }
        else
        {
            Plugin.Log.LogInfo("[PeakVR][GRD] GPUResidentDrawer type not found");
        }

        if (found == 0)
            Plugin.Log.LogInfo("[PeakVR][GRD] no GPU-Resident-Drawer members on the pipeline asset");
    }

    private static bool Matches(string name)
    {
        var lower = name.ToLowerInvariant();
        foreach (var k in GrdKeywords)
            if (lower.Contains(k))
                return true;
        return false;
    }

    private static string SafeGet(Func<object> get)
    {
        try { return get()?.ToString() ?? "<null>"; }
        catch (Exception e) { return $"<{e.GetType().Name}>"; }
    }

    // Debug toggle: force every GPU-Resident-Drawer knob we can find to its "off" value (mode 0,
    // occlusion culling false, small-mesh percentage 0) and back again.
    public static bool GrdDisabled;

    // Original values, captured the first time we disable so restore puts back what PEAK shipped
    // rather than a guessed default.
    private static readonly Dictionary<string, object> grdOriginals = new();

    public static void ToggleGpuResidentDrawer()
    {
        var asset = GraphicsSettings.currentRenderPipeline;
        if (asset == null)
        {
            Plugin.Log.LogWarning("[PeakVR][GRD] no active pipeline asset");
            return;
        }

        GrdDisabled = !GrdDisabled;
        var changed = 0;

        // Prefer the properties: URP's setters call GPUResidentDrawer.ReinitializeIfNeeded(), which is
        // what actually restarts the drawer. Writing the m_ backing fields alone does nothing.
        foreach (var p in asset.GetType().GetProperties(Any))
        {
            if (!Matches(p.Name) || !p.CanRead || !p.CanWrite)
                continue;

            try
            {
                if (!grdOriginals.ContainsKey(p.Name))
                    grdOriginals[p.Name] = p.GetValue(asset);

                var value = GrdDisabled ? OffValue(p.PropertyType) : grdOriginals[p.Name];
                if (value == null)
                    continue;

                p.SetValue(asset, value);
                Plugin.Log.LogInfo($"[PeakVR][GRD] prop {p.Name} -> {p.GetValue(asset)}");
                changed++;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[PeakVR][GRD] prop {p.Name} set failed: {e.Message}");
            }
        }

        ReinitializeDrawer();
        Plugin.Log.LogInfo($"[PeakVR][GRD] {(GrdDisabled ? "DISABLED" : "restored")} ({changed} properties)");
    }

    // URP's GPU Resident Drawer culls meshes below smallMeshScreenPercentage of the screen, and does it
    // per view — so under MultiPass an object can survive the cull in one eye and be dropped in the
    // other. Zeroing the threshold stops that cull while leaving InstancedDrawing (and its batching)
    // intact, which measured as free: 37 fps either way.
    //
    // Applied once at startup BEFORE XRMirror.Setup(), because the drawer's reinitialize tears down
    // URP's XR system and freezes an already-installed desktop mirror.
    public static void ApplySmallMeshCulling()
    {
        if (!Plugin.VrEnabled)
            return;

        var asset = GraphicsSettings.currentRenderPipeline;
        if (asset == null)
        {
            Plugin.Log.LogWarning("[PeakVR][GRD] no active pipeline asset; per-eye culling fix not applied");
            return;
        }

        var prop = asset.GetType().GetProperty("smallMeshScreenPercentage", Any);
        if (prop == null || !prop.CanWrite)
        {
            Plugin.Log.LogWarning("[PeakVR][GRD] smallMeshScreenPercentage not writable; per-eye culling fix unavailable");
            return;
        }

        if (!grdOriginals.ContainsKey(prop.Name))
            grdOriginals[prop.Name] = prop.GetValue(asset);

        var fix = Plugin.Config == null || Plugin.Config.FixPerEyeCulling.Value;
        var target = fix ? 0f : grdOriginals[prop.Name];

        if (Equals(prop.GetValue(asset), target))
            return;

        prop.SetValue(asset, target);
        ReinitializeDrawer();

        Plugin.Log.LogInfo($"[PeakVR][GRD] smallMeshScreenPercentage -> {prop.GetValue(asset)} " +
            $"(drawer mode {Member(asset, "gpuResidentDrawerMode")})");
    }

    public static void ApplyGpuOcclusionCulling()
    {
        if (!Plugin.VrEnabled || Plugin.Config == null)
            return;

        var asset = GraphicsSettings.currentRenderPipeline;
        if (asset == null)
            return;

        var prop = asset.GetType().GetProperty("gpuResidentDrawerEnableOcclusionCullingInCameras", Any);
        if (prop == null || !prop.CanWrite)
        {
            Plugin.Log.LogWarning("[PeakVR][GRD] GPU occlusion culling not available on this URP version");
            return;
        }

        var target = Plugin.Config.GpuOcclusionCulling.Value;
        if (Equals(prop.GetValue(asset), target))
            return;

        prop.SetValue(asset, target);
        ReinitializeDrawer();

        Plugin.Log.LogInfo($"[PeakVR][GRD] GPU occlusion culling -> {prop.GetValue(asset)} " +
            $"(drawer mode {Member(asset, "gpuResidentDrawerMode")})");
    }

    // Debug key: flip the config entry so the toggle and the setting can't drift apart.
    public static void ToggleSmallMeshCulling()
    {
        if (Plugin.Config == null)
            return;

        Plugin.Config.FixPerEyeCulling.Value = !Plugin.Config.FixPerEyeCulling.Value;
    }

    private static object OffValue(Type type)
    {
        if (type == typeof(bool))
            return false;
        if (type == typeof(float))
            return 0f;
        if (type == typeof(int))
            return 0;
        if (type.IsEnum)
            return Enum.ToObject(type, 0);
        return null;
    }

    // GPUResidentDrawer is internal, so find it by scanning loaded assemblies rather than by
    // assembly-qualified name (which is why the earlier Type.GetType lookup failed).
    private static void ReinitializeDrawer()
    {
        try
        {
            Type type = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = asm.GetType("UnityEngine.Rendering.GPUResidentDrawer", false);
                if (type != null)
                    break;
            }

            if (type == null)
            {
                Plugin.Log.LogInfo("[PeakVR][GRD] GPUResidentDrawer type not found in any loaded assembly");
                return;
            }

            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            foreach (var name in new[] { "ReinitializeIfNeeded", "Reinitialize", "CleanUp" })
            {
                var m = type.GetMethod(name, flags, null, Type.EmptyTypes, null);
                if (m == null)
                    continue;

                m.Invoke(null, null);
                Plugin.Log.LogInfo($"[PeakVR][GRD] {name}() invoked");
                return;
            }

            Plugin.Log.LogInfo("[PeakVR][GRD] no reinitialize method found");
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"[PeakVR][GRD] reinitialize failed: {e.Message}");
        }
    }

    public static bool DepthPriming { get; private set; }

    public static void ApplyDepthPriming()
    {
        if (!Plugin.VrEnabled || Plugin.Config == null)
            return;

        var wanted = Plugin.Config.DepthPrepass.Value;
        if (wanted == DepthPriming)
            return;

        SetDepthPriming(wanted);
    }

    public static void ToggleDepthPriming()
    {
        SetDepthPriming(!DepthPriming);
    }

    private static void SetDepthPriming(bool enabled)
    {
        var asset = GraphicsSettings.currentRenderPipeline;
        if (asset == null)
            return;

        var field = asset.GetType().GetField("m_RendererDataList", Any);
        if (field?.GetValue(asset) is not Array list)
        {
            Plugin.Log.LogWarning("[PeakVR][Priming] renderer data list not found");
            return;
        }

        DepthPriming = enabled;
        var changed = 0;

        for (var i = 0; i < list.Length; i++)
        {
            var data = list.GetValue(i);
            if (data == null)
                continue;

            var mode = data.GetType().GetField("m_DepthPrimingMode", Any);
            if (mode == null)
                continue;

            try
            {
                var value = Enum.ToObject(mode.FieldType, DepthPriming ? 2 : 0);
                mode.SetValue(data, value);

                var dirty = data.GetType().GetMethod("SetDirty", Any);
                dirty?.Invoke(data, null);

                Plugin.Log.LogInfo($"[PeakVR][Priming] renderer[{i}] depthPrimingMode -> {mode.GetValue(data)}");
                changed++;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[PeakVR][Priming] renderer[{i}] failed: {e.Message}");
            }
        }

        Plugin.Log.LogInfo($"[PeakVR][Priming] {(DepthPriming ? "ENABLED" : "disabled")} on {changed} renderer(s)");
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

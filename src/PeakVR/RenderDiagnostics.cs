using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PeakVR;

internal static class RenderDiagnostics
{
    private static readonly string[] DisableInVR =
    {
        "HBAO",
        "EdgeDetectionRenderer"
    };

    private struct BigLod
    {
        public LODGroup group;
        public float[] thresholds;
        public Vector3 localRef;
        public float worldSize;
        public int lastLevel;
    }

    private static readonly List<BigLod> bigLods = new();
    private static readonly HashSet<LODGroup> tracked = new();

    private static bool featuresDisabled;
    private static float nextScan;

    private const float LodBias = 2.5f;
    private const float ScanInterval = 3f;

    public static void Apply()
    {
        if (featuresDisabled)
            return;
        featuresDisabled = true;

        var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (urp == null)
        {
            Plugin.Log.LogWarning("[PeakVR] No URP asset active");
            return;
        }

        var field = typeof(UniversalRenderPipelineAsset)
            .GetField("m_RendererDataList", BindingFlags.NonPublic | BindingFlags.Instance);
        if (field?.GetValue(urp) is not ScriptableRendererData[] dataList)
            return;

        foreach (var data in dataList)
        {
            if (data == null)
                continue;

            foreach (var f in data.rendererFeatures)
            {
                if (f == null)
                    continue;

                foreach (var name in DisableInVR)
                {
                    if (f.name == name && f.isActive)
                    {
                        f.SetActive(false);
                        Plugin.Log.LogInfo($"[PeakVR] Disabled render feature '{f.name}' for VR");
                    }
                }
            }
        }
    }

    public static void ApplyLodBias()
    {
        var prev = QualitySettings.lodBias;
        QualitySettings.lodBias = Mathf.Max(prev, LodBias);
        QualitySettings.maximumLODLevel = 0;
        Plugin.Log.LogInfo($"[PeakVR] lodBias {prev} -> {QualitySettings.lodBias}");
    }

    public static void ScheduleScan()
    {
        tracked.Clear();
        bigLods.Clear();
        nextScan = 0f;
    }

    public static void Tick(Camera cam)
    {
        if (Time.time >= nextScan)
        {
            nextScan = Time.time + ScanInterval;
            Rescan();
        }

        ForceLods(cam);
    }

    private static void Rescan()
    {
        for (int i = bigLods.Count - 1; i >= 0; i--)
            if (bigLods[i].group == null)
                bigLods.RemoveAt(i);

        tracked.RemoveWhere(g => g == null);

        var groups = Object.FindObjectsByType<LODGroup>(FindObjectsSortMode.None);
        var added = 0;

        foreach (var g in groups)
        {
            if (g == null || !tracked.Add(g))
                continue;

            g.fadeMode = LODFadeMode.None;
            g.animateCrossFading = false;

            var lods = g.GetLODs();
            if (lods.Length == 0)
                continue;

            var thresholds = new float[lods.Length];
            for (int i = 0; i < lods.Length; i++)
                thresholds[i] = lods[i].screenRelativeTransitionHeight;

            var scale = g.transform.lossyScale;
            var maxScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));

            bigLods.Add(new BigLod
            {
                group = g,
                thresholds = thresholds,
                localRef = g.localReferencePoint,
                worldSize = g.size * maxScale,
                lastLevel = -2
            });
            added++;
        }

        if (added > 0)
            Plugin.Log.LogInfo($"[PeakVR] LOD scan: +{added} new (driving {bigLods.Count})");
    }

    private static void ForceLods(Camera cam)
    {
        if (cam == null || bigLods.Count == 0)
            return;

        var halfAngle = Mathf.Tan(Mathf.Deg2Rad * cam.fieldOfView * 0.5f);
        if (halfAngle <= 0f)
            return;

        var bias = QualitySettings.lodBias;
        var camPos = cam.transform.position;

        for (int i = 0; i < bigLods.Count; i++)
        {
            var b = bigLods[i];
            if (b.group == null || !b.group.enabled || !b.group.gameObject.activeInHierarchy)
                continue;

            var dist = Vector3.Distance(camPos, b.group.transform.TransformPoint(b.localRef));
            if (dist <= 0.001f)
                continue;

            var relativeHeight = b.worldSize * bias / (2f * dist * halfAngle);

            var level = -1;
            for (int j = 0; j < b.thresholds.Length; j++)
            {
                if (relativeHeight >= b.thresholds[j])
                {
                    level = j;
                    break;
                }
            }

            if (level != b.lastLevel)
            {
                b.group.ForceLOD(level);
                b.lastLevel = level;
                bigLods[i] = b;
            }
        }
    }
}

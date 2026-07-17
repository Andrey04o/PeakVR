using System.Collections.Generic;
using UnityEngine;

namespace PeakVR;

internal static class RenderDiagnostics
{
    private struct BigLod
    {
        public LODGroup group;
        public Renderer[] renderers;
        public float[] thresholds;
        public Vector3 localRef;
        public float worldSize;
        public int lastLevel;
    }

    private static readonly List<BigLod> bigLods = new();
    private static readonly HashSet<LODGroup> tracked = new();

    private static float nextScan;

    private const float LodBias = 2.5f;
    private const float ScanInterval = 3f;

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
                renderers = g.GetComponentsInChildren<Renderer>(true),
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

    public static void LogLookedAt(Camera cam)
    {
        if (cam == null)
            return;

        if (!Physics.Raycast(cam.transform.position, cam.transform.forward, out var hit, 60f, ~0, QueryTriggerInteraction.Ignore))
        {
            Plugin.Log.LogInfo("[PeakVR][LODdbg] no raycast hit");
            return;
        }

        Plugin.Log.LogInfo($"[PeakVR][LODdbg] hit path='{Path(hit.collider.transform)}' dist={hit.distance:F1}");

        // The LODGroup governing what we hit (walk up), plus any LODGroups nested under it.
        var owner = hit.collider.GetComponentInParent<LODGroup>();
        if (owner == null)
        {
            Plugin.Log.LogInfo("[PeakVR][LODdbg]   NO LODGroup in parents of the hit object");
            return;
        }

        LogGroup(owner, cam, "HIT");
        foreach (var child in owner.transform.GetComponentsInChildren<LODGroup>(true))
            if (child != owner)
                LogGroup(child, cam, "child");
    }

    private static void LogGroup(LODGroup g, Camera cam, string tag)
    {
        if (g == null)
            return;

        var lods = g.GetLODs();
        var counts = "";
        for (var i = 0; i < lods.Length; i++)
            counts += $"[L{i}:{lods[i].renderers.Length}r@{lods[i].screenRelativeTransitionHeight:F3}]";

        var scale = g.transform.lossyScale;
        var maxScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        var worldSize = g.size * maxScale;
        var halfAngle = Mathf.Tan(Mathf.Deg2Rad * cam.fieldOfView * 0.5f);
        var dist = Vector3.Distance(cam.transform.position, g.transform.TransformPoint(g.localReferencePoint));
        var relHeight = dist > 0.001f && halfAngle > 0f ? worldSize * QualitySettings.lodBias / (2f * dist * halfAngle) : -1f;

        var level = -1;
        for (var j = 0; j < lods.Length; j++)
            if (relHeight >= lods[j].screenRelativeTransitionHeight) { level = j; break; }

        Plugin.Log.LogInfo(
            $"[PeakVR][LODdbg]   [{tag}] '{g.name}' lods={lods.Length} tracked={tracked.Contains(g)} enabled={g.enabled} " +
            $"fade={g.fadeMode} size={g.size:F2} dist={dist:F1} relH={relHeight:F3} ourLevel={level} {counts}");
    }

    private static string Path(Transform t)
    {
        var path = t.name;
        var p = t.parent;
        var depth = 0;
        while (p != null && depth++ < 6)
        {
            path = p.name + "/" + path;
            p = p.parent;
        }
        return path;
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

            if (level == b.lastLevel)
                continue;

            if (level >= 0)
            {
                // Coming back from a culled state: re-enable the renderers + group before forcing.
                if (b.lastLevel < 0)
                {
                    b.group.enabled = true;
                    foreach (var r in b.renderers)
                        if (r != null)
                            r.enabled = true;
                }

                b.group.ForceLOD(level);
            }
            else
            {
                // Past the smallest LOD: cull it OURSELVES (both eyes identically) instead of calling
                // ForceLOD(-1), which returns the group to Unity's per-eye automatic LOD and makes the
                // object flicker between eyes at distance.
                b.group.enabled = false;
                foreach (var r in b.renderers)
                    if (r != null)
                        r.enabled = false;
            }

            b.lastLevel = level;
            bigLods[i] = b;
        }
    }
}

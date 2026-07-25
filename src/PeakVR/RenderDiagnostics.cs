using System.Collections.Generic;
using UnityEngine;

namespace PeakVR;

internal static class RenderDiagnostics
{
    private struct BigLod
    {
        public LODGroup group;
        public float[] thresholds;
        public Vector3 localRef;
        public float worldSize;
        public int lastLevel;
    }

    // Runtime toggle for the whole per-eye LOD-forcing system (debug button). When turned off we hand
    // every group back to Unity's automatic LOD.
    public static bool Enabled = true;

    private static readonly List<BigLod> bigLods = new();
    private static readonly HashSet<LODGroup> tracked = new();

    // "LOD0 only" experiment: every LODGroup is switched off entirely and only its LOD0 renderers are
    // left visible, so no LOD distance work happens at all (Unity's own, nor ours).
    public static bool Lod0Only;

    private static readonly HashSet<LODGroup> suppressed = new();
    private static readonly List<Renderer> hidden = new();
    private static float nextLod0Sweep;

    private static float nextScan;
    private static int cursor;

    private const float DefaultLodBias = 2.5f;
    private const float ScanInterval = 3f;
    private const int SpreadFrames = 4;

    public static void ApplyLodBias()
    {
        float target = Plugin.Config != null ? Plugin.Config.LodBias.Value : DefaultLodBias;
        float prev = QualitySettings.lodBias;
        QualitySettings.lodBias = target;
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
        // Catch renderers that streamed in after the toggle.
        if (ForceMeshLod0 && Time.time >= nextMeshLodSweep)
        {
            nextMeshLodSweep = Time.time + ScanInterval;
            ApplyForceMeshLod(0);
        }

        if (Lod0Only)
        {
            // Re-sweep periodically so streamed-in / newly spawned groups also get flattened.
            if (Time.time >= nextLod0Sweep)
            {
                nextLod0Sweep = Time.time + ScanInterval;
                ApplyLod0Only();
            }
            return;
        }

        if (!Enabled)
            return;

        if (Time.time >= nextScan)
        {
            nextScan = Time.time + ScanInterval;
            Rescan();
        }

        ForceLods(cam);
    }

    // Toggle the LOD-forcing on/off at runtime (debug button). Off = release every group back to
    // Unity's automatic (per-eye) LOD; on = rescan and resume forcing.
    public static void Toggle()
    {
        Enabled = !Enabled;

        if (!Enabled)
            foreach (var b in bigLods)
                if (b.group != null)
                    b.group.ForceLOD(-1);
        else
            ScheduleScan();

        Plugin.Log.LogInfo($"[PeakVR] LOD forcing {(Enabled ? "ENABLED" : "DISABLED (automatic per-eye LOD)")}");
    }

    // Unity 6.2+ "Mesh LOD" picks a per-renderer detail level from on-screen size WITHOUT a LODGroup,
    // using the project-wide threshold below. Higher = favour less detailed levels. Selection runs per
    // view, so in MultiPass each eye can land on a different level (or drop the mesh entirely).
    private static readonly float[] MeshLodThresholds = { 0f, 0.5f, 1f, 2f };
    private static int meshLodIndex = 2;

    public static void CycleMeshLodThreshold()
    {
        meshLodIndex = (meshLodIndex + 1) % MeshLodThresholds.Length;
        float value = MeshLodThresholds[meshLodIndex];
        QualitySettings.meshLodThreshold = value;
        Plugin.Log.LogInfo($"[PeakVR] meshLodThreshold = {value}");
    }

    // Pin every renderer to mesh-LOD level 0, which removes the per-view selection entirely. -1 hands
    // it back to automatic.
    public static bool ForceMeshLod0;
    private static float nextMeshLodSweep;

    public static void ToggleForceMeshLod0()
    {
        ForceMeshLod0 = !ForceMeshLod0;
        nextMeshLodSweep = 0f;
        ApplyForceMeshLod(ForceMeshLod0 ? (short)0 : (short)-1);
        Plugin.Log.LogInfo($"[PeakVR] forceMeshLod = {(ForceMeshLod0 ? "0 (pinned to full detail)" : "-1 (automatic)")}");
    }

    private static void ApplyForceMeshLod(short level)
    {
        int count = 0;
        foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (r == null || r.forceMeshLod == level)
                continue;

            r.forceMeshLod = level;
            count++;
        }

        if (count > 0)
            Plugin.Log.LogInfo($"[PeakVR] forceMeshLod {level} applied to {count} renderers");
    }

    // Debug experiment: kill LOD entirely. Every LODGroup component is disabled (so neither Unity nor
    // our ForceLods does any distance math) and all renderers that aren't part of LOD0 are hidden, so
    // the scene draws at full detail everywhere. Purely for measuring how much the LOD system costs.
    public static void ToggleLod0Only()
    {
        Lod0Only = !Lod0Only;

        if (Lod0Only)
        {
            nextLod0Sweep = 0f;
            ApplyLod0Only();
        }
        else
        {
            RestoreLods();
            ScheduleScan();
        }

        Plugin.Log.LogInfo($"[PeakVR] LOD0-only {(Lod0Only ? "ON" : "OFF")} (groups={suppressed.Count} hiddenRenderers={hidden.Count})");
    }

    private static void ApplyLod0Only()
    {
        LODGroup[] groups = Object.FindObjectsByType<LODGroup>(FindObjectsSortMode.None);
        int added = 0;

        foreach (LODGroup g in groups)
        {
            if (g == null || !suppressed.Add(g))
                continue;

            LOD[] lods = g.GetLODs();
            if (lods.Length == 0)
                continue;

            HashSet<Renderer> keep = new();
            foreach (Renderer r in lods[0].renderers)
                if (r != null)
                    keep.Add(r);

            for (int i = 1; i < lods.Length; i++)
            {
                foreach (Renderer r in lods[i].renderers)
                {
                    if (r == null || keep.Contains(r) || !r.enabled)
                        continue;

                    r.enabled = false;
                    hidden.Add(r);
                }
            }

            foreach (Renderer r in keep)
                r.enabled = true;

            g.enabled = false;
            added++;
        }

        if (added > 0)
            Plugin.Log.LogInfo($"[PeakVR] LOD0-only sweep: +{added} groups flattened (total {suppressed.Count})");
    }

    private static void RestoreLods()
    {
        foreach (Renderer r in hidden)
            if (r != null)
                r.enabled = true;
        hidden.Clear();

        foreach (LODGroup g in suppressed)
        {
            if (g == null)
                continue;

            g.enabled = true;
            g.ForceLOD(-1);
        }
        suppressed.Clear();
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

    // Probe for objects that vanish per-eye but have no LODGroup of their own. Scans by name so it can
    // be run while the object is invisible in one eye (LogLookedAt needs a raycast hit).
    public static void LogByName(Camera cam)
    {
        if (cam == null)
            return;

        LogEngineLodFeatures();

        string[] filters = { "Coconut", "ElevatorPing", "Light" };
        Vector3 camPos = cam.transform.position;

        foreach (string filter in filters)
        {
            int found = 0;

            foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (r == null || r.gameObject.name.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                if (found++ >= 6)
                    break;

                LODGroup owner = r.GetComponentInParent<LODGroup>();
                Bounds b = r.bounds;
                float dist = Vector3.Distance(camPos, b.center);

                string ownerInfo = "none";
                if (owner != null)
                {
                    LOD[] lods = owner.GetLODs();
                    string thresholds = "";
                    for (int i = 0; i < lods.Length; i++)
                        thresholds += $"[L{i}@{lods[i].screenRelativeTransitionHeight:F3}]";
                    ownerInfo = $"'{owner.name}' enabled={owner.enabled} size={owner.size:F2} lods={lods.Length}{thresholds}";
                }

                Plugin.Log.LogInfo(
                    $"[PeakVR][LODname] {filter}: path='{Path(r.transform)}' type={r.GetType().Name} " +
                    $"enabled={r.enabled} visible={r.isVisible} layer={r.gameObject.layer} " +
                    $"boundsSize={b.size.magnitude:F2} dist={dist:F1} lodGroup={ownerInfo}");

                LogComponents(r.transform);
            }

            if (found == 0)
                Plugin.Log.LogInfo($"[PeakVR][LODname] {filter}: no renderers matched");
        }

        int lightCount = 0;
        foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if (l == null || lightCount++ >= 8)
                break;

            Plugin.Log.LogInfo(
                $"[PeakVR][LODname] Light: path='{Path(l.transform)}' type={l.type} enabled={l.enabled} " +
                $"range={l.range:F1} intensity={l.intensity:F2} dist={Vector3.Distance(camPos, l.transform.position):F1} " +
                $"lodGroup={(l.GetComponentInParent<LODGroup>() != null ? "yes" : "none")}");
        }
    }

    // Every component on the object and its ancestors, so runtime-added culling scripts show up.
    private static void LogComponents(Transform t)
    {
        Transform node = t;
        int depth = 0;

        while (node != null && depth++ < 4)
        {
            string names = "";
            foreach (Component c in node.GetComponents<Component>())
                names += (c == null ? "<missing>" : c.GetType().Name) + " ";

            Plugin.Log.LogInfo($"[PeakVR][LODname]    ^{depth} '{node.name}' active={node.gameObject.activeInHierarchy} :: {names}");
            node = node.parent;
        }
    }

    private static bool loggedFeatures;

    // Unity 6.2+ added per-mesh "Mesh LOD", which selects detail (and can drop a renderer) WITHOUT a
    // LODGroup and is driven by the same LOD bias. Report whether this build exposes it.
    private static void LogEngineLodFeatures()
    {
        if (loggedFeatures)
            return;
        loggedFeatures = true;

        Plugin.Log.LogInfo($"[PeakVR][LODfeat] lodBias={QualitySettings.lodBias} maxLOD={QualitySettings.maximumLODLevel} " +
            $"unity={Application.unityVersion}");

        System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static;
        foreach (System.Reflection.PropertyInfo p in typeof(QualitySettings).GetProperties(flags))
            if (p.Name.IndexOf("lod", System.StringComparison.OrdinalIgnoreCase) >= 0)
                Plugin.Log.LogInfo($"[PeakVR][LODfeat] QualitySettings.{p.Name} = {p.GetValue(null)}");

        System.Reflection.BindingFlags inst = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
        foreach (System.Reflection.PropertyInfo p in typeof(Renderer).GetProperties(inst))
            if (p.Name.IndexOf("lod", System.StringComparison.OrdinalIgnoreCase) >= 0)
                Plugin.Log.LogInfo($"[PeakVR][LODfeat] Renderer.{p.Name} ({p.PropertyType.Name})");
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
        var count = bigLods.Count;
        if (cam == null || count == 0)
            return;

        var halfAngle = Mathf.Tan(Mathf.Deg2Rad * cam.fieldOfView * 0.5f);
        if (halfAngle <= 0f)
            return;

        var bias = QualitySettings.lodBias;
        var camPos = cam.transform.position;

        // Spread the work across frames: only ~1/SpreadFrames of the LODGroups are re-evaluated each
        // frame (round-robin). LOD transitions are distance-gradual, so the few-frame latency is
        // invisible while the per-frame cost drops ~SpreadFrames×.
        var perFrame = Mathf.CeilToInt(count / (float)SpreadFrames);

        for (var n = 0; n < perFrame; n++)
        {
            if (cursor >= count)
                cursor = 0;
            var i = cursor++;

            var b = bigLods[i];
            if (b.group == null || !b.group.enabled || !b.group.gameObject.activeInHierarchy)
                continue;
            if (b.thresholds.Length == 0)
                continue;

            var dist = Vector3.Distance(camPos, b.group.transform.TransformPoint(b.localRef));
            if (dist <= 0.001f)
                continue;

            var relativeHeight = b.worldSize * bias / (2f * dist * halfAngle);

            // Default to the smallest (last) LOD when the object is past every threshold: keep it
            // rendered at lowest detail in BOTH eyes rather than culling it. Culling vanished small
            // objects per-eye and, once culled, they never re-appeared when approached again.
            var level = b.thresholds.Length - 1;
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

            b.group.ForceLOD(level);
            b.lastLevel = level;
            bigLods[i] = b;
        }
    }
}

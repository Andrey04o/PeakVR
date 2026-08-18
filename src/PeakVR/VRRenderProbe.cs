using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace PeakVR;

internal static class VRRenderProbe
{
    private const float RayLength = 60f;
    private const float RayRadius = 2.5f;
    private const int MaxRenderers = 30;

    private static readonly string[] NameFilters = { "spike", "chain", "pole", "swing" };

    public static void Probe(Camera cam)
    {
        if (cam == null)
            return;

        var origin = cam.transform.position;
        var dir = cam.transform.forward;

        Plugin.Log.LogInfo("[PeakVR][Probe] ===== render probe =====");
        Plugin.Log.LogInfo($"[PeakVR][Probe] camera mask=0x{cam.cullingMask:X} near={cam.nearClipPlane:F2} "
            + $"far={cam.farClipPlane:F0} lodBias={QualitySettings.lodBias:F2} "
            + $"configLodBias={(Plugin.Config != null ? Plugin.Config.LodBias.Value : -1f):F2} "
            + $"maxLod={QualitySettings.maximumLODLevel} stereoMargin={VRStereoCulling.Margin:F2} "
            + $"occlusion={cam.useOcclusionCulling}");

        LogRendererIndex(cam);
        LogCullDistances(cam);
        LogLayerMasks();

        if (Physics.Raycast(origin, dir, out var info, RayLength, ~0, QueryTriggerInteraction.Collide))
            Plugin.Log.LogInfo($"[PeakVR][Probe] ray hit '{Path(info.collider.transform)}' at {info.distance:F1}m");
        else
            Plugin.Log.LogInfo("[PeakVR][Probe] ray hit nothing");

        var along = new List<(float dist, Renderer r)>();
        var named = new List<(float dist, Renderer r)>();

        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (r == null)
                continue;

            var toCentre = r.bounds.center - origin;
            var depth = Vector3.Dot(toCentre, dir);
            var range = toCentre.magnitude;

            if (depth > 0f && depth <= RayLength)
            {
                var onRay = origin + dir * depth;
                if (r.bounds.SqrDistance(onRay) <= RayRadius * RayRadius)
                    along.Add((depth, r));
            }

            if (range <= RayLength && MatchesName(r))
                named.Add((range, r));
        }

        Report("along the aim ray", along, cam);
        Report("matching spike/chain/pole/swing", named, cam);
    }

    private static readonly Dictionary<Renderer, (int layer, Shader[] shaders)> Originals = new();
    private static int fixStage;

    public static void CycleFix()
    {
        fixStage = (fixStage + 1) % 6;

        var targets = new List<Renderer>();
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            if (r != null && MatchesName(r))
                targets.Add(r);

        foreach (var r in targets)
        {
            if (!Originals.ContainsKey(r))
            {
                var shaders = new Shader[r.materials.Length];
                for (var i = 0; i < shaders.Length; i++)
                    shaders[i] = r.materials[i] != null ? r.materials[i].shader : null;

                Originals[r] = (r.gameObject.layer, shaders);
            }

            var (layer, original) = Originals[r];
            var mats = r.materials;

            r.gameObject.layer = layer;
            for (var i = 0; i < mats.Length; i++)
                if (mats[i] != null && i < original.Length && original[i] != null)
                    mats[i].shader = original[i];

            var movable = r.GetComponent<Collider>() == null;

            switch (fixStage)
            {
                case 1:
                    if (movable)
                        r.gameObject.layer = 0;
                    break;
                case 2:
                    SetLit(mats);
                    break;
                case 3:
                    if (movable)
                        r.gameObject.layer = 0;
                    SetLit(mats);
                    break;
                case 4:
                    r.enabled = false;
                    r.enabled = true;
                    break;
                case 5:
                    r.gameObject.SetActive(false);
                    r.gameObject.SetActive(true);
                    break;
            }
        }

        var label = fixStage switch
        {
            1 => "layer -> Default(0)",
            2 => "shader -> URP/Lit",
            3 => "layer -> Default(0) AND shader -> URP/Lit",
            4 => "renderer disabled+re-enabled, layer untouched",
            5 => "GameObject deactivated+reactivated, layer untouched",
            _ => "restored"
        };

        Plugin.Log.LogInfo($"[PeakVR][Probe] fix stage {fixStage}: {label} on {targets.Count} renderers");
    }

    private static void SetLit(Material[] mats)
    {
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        if (lit == null)
            return;

        foreach (var m in mats)
            if (m != null)
                m.shader = lit;
    }

    private static bool MatchesName(Renderer r)
    {
        var name = r.gameObject.name;

        foreach (var filter in NameFilters)
            if (name.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

        return false;
    }

    private static void Report(string label, List<(float dist, Renderer r)> found, Camera cam)
    {
        found.Sort((a, b) => a.dist.CompareTo(b.dist));
        Plugin.Log.LogInfo($"[PeakVR][Probe] --- {found.Count} renderers {label} ---");

        var shown = 0;
        foreach (var (dist, r) in found)
        {
            if (shown++ >= MaxRenderers)
            {
                Plugin.Log.LogInfo($"[PeakVR][Probe] ... {found.Count - MaxRenderers} more");
                break;
            }

            Plugin.Log.LogInfo(Describe(r, dist, cam));
        }
    }

    private static string Describe(Renderer r, float dist, Camera cam)
    {
        var sb = new StringBuilder();
        sb.Append("[PeakVR][Probe]   ").Append($"{dist:F1}m '{Path(r.transform)}' ");
        sb.Append($"type={r.GetType().Name} layer={LayerMask.LayerToName(r.gameObject.layer)}({r.gameObject.layer}) ");
        sb.Append($"active={r.gameObject.activeInHierarchy} enabled={r.enabled} ");
        sb.Append($"forcedOff={r.forceRenderingOff} visible={r.isVisible} ");

        var inMask = (cam.cullingMask & (1 << r.gameObject.layer)) != 0;
        sb.Append($"inMask={inMask} ");

        var col = r.GetComponent<Collider>();
        sb.Append($"collider={(col == null ? "none" : col.GetType().Name + (col.isTrigger ? ":trigger" : ":solid"))} ");
        sb.Append($"shadow={r.shadowCastingMode} queue={(r.sharedMaterial != null ? r.sharedMaterial.renderQueue : -1)} ");
        sb.Append($"renderLayer=0x{r.renderingLayerMask:X} staticBatch={r.isPartOfStaticBatch} ");
        sb.Append($"motionVectors={r.motionVectorGenerationMode} static={r.gameObject.isStatic} ");
        sb.Append($"inRealFrustum={InFrustum(GeometryUtility.CalculateFrustumPlanes(cam.projectionMatrix * cam.worldToCameraMatrix), r)} ");
        sb.Append($"inCullMatrix={InFrustum(GeometryUtility.CalculateFrustumPlanes(cam.cullingMatrix), r)} ");

        var size = r.bounds.size;
        sb.Append($"bounds=({size.x:F2},{size.y:F2},{size.z:F2}) ");

        var shaders = new List<string>();
        foreach (var m in r.sharedMaterials)
            shaders.Add(m == null ? "null" : m.shader == null ? "noshader" : m.shader.name);
        sb.Append($"shaders=[{string.Join(",", shaders)}] ");

        var lod = r.GetComponentInParent<LODGroup>();
        if (lod != null)
            sb.Append($"lodGroup='{lod.name}' lods={lod.lodCount} lodEnabled={lod.enabled} size={lod.size:F2} "
                + $"relHeight={RelativeHeight(lod, cam):F4} ");

        if (r is MeshRenderer && r.TryGetComponent<MeshFilter>(out var filter) && filter.sharedMesh != null)
            sb.Append($"mesh='{filter.sharedMesh.name}' verts={filter.sharedMesh.vertexCount}");

        return sb.ToString();
    }

    private static void LogRendererIndex(Camera cam)
    {
        var data = cam.GetComponent("UniversalAdditionalCameraData");
        if (data == null)
        {
            Plugin.Log.LogInfo("[PeakVR][Probe] camera has no UniversalAdditionalCameraData");
            return;
        }

        var any = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic;

        var index = data.GetType().GetField("m_RendererIndex", any)?.GetValue(data);
        var renderer = data.GetType().GetProperty("scriptableRenderer", any)?.GetValue(data);

        Plugin.Log.LogInfo($"[PeakVR][Probe] camera '{cam.name}' rendererIndex={index} "
            + $"renderer={(renderer != null ? renderer.GetType().Name : "null")} "
            + $"targetTexture={(cam.targetTexture != null ? cam.targetTexture.name : "none")}");
    }

    private static void LogCullDistances(Camera cam)
    {
        var distances = cam.layerCullDistances;
        if (distances == null)
            return;

        var sb = new StringBuilder();
        for (var layer = 0; layer < distances.Length && layer < 32; layer++)
            if (distances[layer] > 0f)
                sb.Append($"{layer}({LayerMask.LayerToName(layer)})={distances[layer]:F0}m ");

        Plugin.Log.LogInfo($"[PeakVR][Probe] layerCullSpherical={cam.layerCullSpherical} "
            + $"perLayerCull=[{(sb.Length == 0 ? "none" : sb.ToString())}]");
    }

    private static void LogLayerMasks()
    {
        var pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
        if (pipeline == null)
            return;

        var listField = pipeline.GetType().GetField("m_RendererDataList",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Public);

        if (listField?.GetValue(pipeline) is not object[] datas)
            return;

        foreach (var data in datas)
        {
            if (data == null)
                continue;

            var opaque = data.GetType().GetProperty("opaqueLayerMask");
            var transparent = data.GetType().GetProperty("transparentLayerMask");
            if (opaque == null || transparent == null)
                continue;

            var o = ((LayerMask)opaque.GetValue(data)).value;
            var t = ((LayerMask)transparent.GetValue(data)).value;

            Plugin.Log.LogInfo($"[PeakVR][Probe] renderer '{((Object)data).name}' opaqueMask=0x{o:X8} "
                + $"transparentMask=0x{t:X8} {LayerReport(o, t)}");
        }
    }

    private static string LayerReport(int opaque, int transparent)
    {
        var sb = new StringBuilder("missingFromOpaque=[");
        var first = true;

        for (var layer = 0; layer < 32; layer++)
        {
            var name = LayerMask.LayerToName(layer);
            if (string.IsNullOrEmpty(name) || (opaque & (1 << layer)) != 0)
                continue;

            if (!first)
                sb.Append(',');
            sb.Append($"{name}({layer})");
            first = false;
        }

        return sb.Append(']').ToString();
    }

    private static bool InFrustum(Plane[] planes, Renderer r)
    {
        return GeometryUtility.TestPlanesAABB(planes, r.bounds);
    }

    private static float RelativeHeight(LODGroup lod, Camera cam)
    {
        var worldSize = lod.size * lod.transform.lossyScale.x;
        var distance = Vector3.Distance(lod.transform.TransformPoint(lod.localReferencePoint),
            cam.transform.position);

        if (distance <= 0.001f)
            return 1f;

        var halfAngle = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        return worldSize / (2f * distance * halfAngle);
    }

    private static string Path(Transform t)
    {
        var sb = new StringBuilder(t.name);
        var p = t.parent;

        while (p != null)
        {
            sb.Insert(0, p.name + "/");
            p = p.parent;
        }

        return sb.ToString();
    }
}

using System.Text;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace PeakVR;

[HarmonyPatch(typeof(LensFlareComponentSRP), "OnEnable")]
internal static class SunFlarePatch
{
    [HarmonyPostfix]
    private static void Postfix(LensFlareComponentSRP __instance)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[PeakVR] LensFlareComponentSRP enabled on '{GetPath(__instance.transform)}'");
        sb.AppendLine($"[PeakVR]   useOcclusion={__instance.useOcclusion} radius={__instance.occlusionRadius} samples={__instance.sampleCount} occlusionOffset={__instance.occlusionOffset}");
        sb.AppendLine($"[PeakVR]   allowOffScreen={__instance.allowOffScreen} intensity={__instance.intensity} scale={__instance.scale} data={(__instance.lensFlareData != null ? __instance.lensFlareData.name : "null")}");

        for (var t = __instance.transform; t != null; t = t.parent)
        {
            foreach (var c in t.GetComponents<Component>())
                sb.AppendLine($"[PeakVR]   [{t.name}] {(c != null ? c.GetType().FullName : "missing")}");
        }

        foreach (var vol in Object.FindObjectsByType<Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (vol.profile == null)
                continue;

            foreach (var comp in vol.profile.components)
                sb.AppendLine($"[PeakVR]   Volume '{vol.gameObject.name}' global={vol.isGlobal} enabled={vol.isActiveAndEnabled}: {comp.GetType().FullName} active={comp.active}");
        }

        Plugin.Log.LogInfo(sb.ToString());
    }

    private static string GetPath(Transform t)
    {
        var path = t.name;
        for (var p = t.parent; p != null; p = p.parent)
            path = p.name + "/" + path;
        return path;
    }
}

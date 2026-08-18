using System.Collections.Generic;
using UnityEngine;

namespace PeakVR;

internal static class VRHazardLayer
{
    private const string LayerName = "Hazard";
    private const float ScanInterval = 5f;

    private static readonly Dictionary<GameObject, int> Original = new();
    private static float nextScan;
    private static int moved;

    public static void ScheduleScan()
    {
        Original.Clear();
        nextScan = 0f;
        moved = 0;
    }

    public static void Tick()
    {
        if (!Plugin.VrEnabled || Plugin.Config == null)
            return;

        if (!Plugin.Config.FixHazardRendering.Value)
        {
            Restore();
            return;
        }

        if (Time.time < nextScan)
            return;

        nextScan = Time.time + ScanInterval;
        Apply();
    }

    private static void Restore()
    {
        if (Original.Count == 0)
            return;

        foreach (var pair in Original)
            if (pair.Key != null)
                pair.Key.layer = pair.Value;

        Plugin.Log.LogInfo($"[PeakVR] Hazard layer fix: restored {Original.Count} renderers");
        Original.Clear();
        moved = 0;
    }

    private static void Apply()
    {
        var hazard = LayerMask.NameToLayer(LayerName);
        if (hazard < 0)
            return;

        var found = 0;

        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (r == null || r.gameObject.layer != hazard)
                continue;

            if (r.GetComponent<Collider>() != null)
                continue;

            Original[r.gameObject] = r.gameObject.layer;
            r.gameObject.layer = 0;
            found++;
        }

        if (found <= 0)
            return;

        moved += found;
        Plugin.Log.LogInfo($"[PeakVR] Hazard layer fix: moved {found} collider-less renderers to Default "
            + $"(total {moved})");
    }
}

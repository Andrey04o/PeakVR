using System.Collections.Generic;
using UnityEngine;

namespace PeakVR;

internal static class VRFoliage
{
    private const float ScanInterval = 3f;
    private const int SpreadFrames = 4;

    private static readonly List<Renderer> foliage = new();
    private static readonly HashSet<Renderer> tracked = new();

    private static float nextScan;
    private static int cursor;
    private static bool culling;

    public static void ScheduleScan()
    {
        foliage.Clear();
        tracked.Clear();
        nextScan = 0f;
        cursor = 0;
    }

    public static void Tick(Camera cam)
    {
        if (cam == null || Plugin.Config == null)
            return;

        var distance = Plugin.Config.FoliageDistance.Value;
        if (distance <= 0f)
        {
            if (culling)
                Restore();
            return;
        }

        culling = true;

        if (Time.time >= nextScan)
        {
            nextScan = Time.time + ScanInterval;
            Rescan();
        }

        var count = foliage.Count;
        if (count == 0)
            return;

        var camPos = cam.transform.position;
        var limit = distance * distance;
        var perFrame = Mathf.CeilToInt(count / (float)SpreadFrames);

        for (var n = 0; n < perFrame; n++)
        {
            if (cursor >= count)
                cursor = 0;

            var r = foliage[cursor++];
            if (r == null)
                continue;

            var beyond = (r.bounds.center - camPos).sqrMagnitude > limit;
            if (r.forceRenderingOff != beyond)
                r.forceRenderingOff = beyond;
        }
    }

    private static void Rescan()
    {
        for (var i = foliage.Count - 1; i >= 0; i--)
            if (foliage[i] == null)
                foliage.RemoveAt(i);

        tracked.RemoveWhere(r => r == null);

        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (r == null || tracked.Contains(r) || !IsFoliage(r))
                continue;

            tracked.Add(r);
            foliage.Add(r);
        }
    }

    private static bool IsFoliage(Renderer r)
    {
        var mat = r.sharedMaterial;
        if (mat == null || mat.shader == null)
            return false;

        var shader = mat.shader.name;
        return shader.IndexOf("Foliage", System.StringComparison.OrdinalIgnoreCase) >= 0
            || shader.IndexOf("Grass", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void Restore()
    {
        culling = false;

        foreach (var r in foliage)
            if (r != null)
                r.forceRenderingOff = false;
    }
}

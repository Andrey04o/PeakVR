using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace PeakVR;

internal static class UIOverlay
{
    private static readonly int ZTestUI = Shader.PropertyToID("unity_GUIZTestMode");
    private static readonly int ZTestTMP = Shader.PropertyToID("_ZTestMode");
    private const int Always = (int)CompareFunction.Always;

    // Base render queues. Elements get base + hierarchy index so draw order follows the UI
    // hierarchy (parents/earlier siblings behind, children/later siblings on top) even with
    // ZTest Always — otherwise a flat queue lets backgrounds cover foreground images.
    private const int DefaultQueue = 3000;
    private const int ForegroundQueue = 3005; // above world transparents (glass/fog)
    public const int HandQueue = 4000;        // above rain/airplane-window glass, for the wrist HUD

    private static readonly Dictionary<Graphic, Material> Cache = new();

    public static void MakeAlwaysVisible(Canvas canvas, bool foreground)
        => Apply(canvas, foreground ? ForegroundQueue : DefaultQueue);

    public static void MakeAlwaysVisible(Canvas canvas, int baseQueue)
        => Apply(canvas, baseQueue);

    private static void Apply(Canvas canvas, int baseQueue)
    {
        if (canvas == null)
            return;

        var graphics = canvas.GetComponentsInChildren<Graphic>(true);
        for (var i = 0; i < graphics.Length; i++)
        {
            var g = graphics[i];
            if (g == null)
                continue;

            var mat = GetMaterial(g);
            if (mat == null)
                continue;

            mat.SetInt(ZTestUI, Always);
            mat.SetInt(ZTestTMP, Always);
            mat.renderQueue = baseQueue + i;
        }
    }

    private static Material GetMaterial(Graphic g)
    {
        if (Cache.TryGetValue(g, out var cached) && cached != null)
            return cached;

        Material mat;

        if (g is TMP_Text tmp)
        {
            mat = tmp.fontMaterial; // per-instance material
            if (mat == null)
                return null;
        }
        else
        {
            var src = g.material != null ? g.material : g.defaultMaterial;
            if (src == null)
                return null;

            mat = new Material(src);
            g.material = mat;
        }

        Cache[g] = mat;
        return mat;
    }
}

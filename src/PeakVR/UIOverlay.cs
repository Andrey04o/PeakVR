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

    private const int DefaultQueue = 3000;
    private const int ForegroundQueue = 3005; // above world transparents (glass/fog)
    public const int HandQueue = 4000;        // above rain/airplane-window glass, for the wrist HUD
    public const int ReticleQueue = 4200;     // above every menu/popup, so the cursor is never hidden

    private static readonly Dictionary<Graphic, Material> Cache = new();

    // Diagnostics: press F4 in-game to log the render order / clip state of every canvas we touch.
    public static bool Logging;
    private static readonly HashSet<Canvas> Logged = new();

    public static void SetLogging(bool on)
    {
        Logging = on;
        if (on)
            Logged.Clear();
        Plugin.Log.LogInfo($"[PeakVR][UIOrder] logging {(on ? "ON" : "OFF")}");
    }

    public static void MakeAlwaysVisible(Canvas canvas, bool foreground)
        => Apply(canvas, foreground ? ForegroundQueue : DefaultQueue);

    public static void MakeAlwaysVisible(Canvas canvas, int baseQueue)
        => Apply(canvas, baseQueue);

    // Force a single graphic (e.g. the laser reticle) to draw on top of everything, ignoring depth.
    public static void MakeTopmost(Graphic graphic, int queue)
    {
        if (graphic == null)
            return;

        var mat = GetMaterial(graphic);
        if (mat == null)
            return;

        mat.SetInt(ZTestUI, Always);
        mat.SetInt(ZTestTMP, Always);
        mat.renderQueue = queue;
    }

    private static void Apply(Canvas canvas, int baseQueue)
    {
        if (canvas == null)
            return;

        // Only bump the queue for foreground layers (menus, loading, wrist HUD). Default-queue
        // callers (HUD, passport window) get ZTest only. A flat same-queue bump is safe for stencil
        // masks (mask still draws before its children in hierarchy order). NOTE: dynamic masked
        // graphics (the stamina fill) still fall back below world glass because they regenerate their
        // material each frame — deferred to the future URP UI-camera-stacking port.
        var applyQueue = baseQueue != DefaultQueue;
        var log = Logging && Logged.Add(canvas);
        if (log)
            Plugin.Log.LogInfo($"[PeakVR][UIOrder] ===== {canvas.name} base={baseQueue} sorting={canvas.sortingOrder} mode={canvas.renderMode} =====");

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

            if (applyQueue)
                mat.renderQueue = baseQueue;

            if (log)
                Plugin.Log.LogInfo($"[PeakVR][UIOrder] [{i,3}] q={mat.renderQueue} stencil={(InStencilMask(g) ? 1 : 0)} rect={(InRectMask(g) ? 1 : 0)} {g.GetType().Name} :: {Path(g.transform)}");
        }
    }

    private static Material GetMaterial(Graphic g)
    {
        if (Cache.TryGetValue(g, out var cached) && cached != null)
            return cached;

        Material mat;

        if (g is TMP_Text tmp)
        {
            mat = tmp.fontMaterial;
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

    private static bool InStencilMask(Graphic g)
    {
        var mask = g.GetComponentInParent<Mask>();
        return mask != null && mask.enabled;
    }

    private static bool InRectMask(Graphic g)
    {
        var mask = g.GetComponentInParent<RectMask2D>();
        return mask != null && mask.enabled;
    }

    private static string Path(Transform t)
    {
        var path = t.name;
        var p = t.parent;
        var depth = 0;
        while (p != null && depth++ < 8)
        {
            path = p.name + "/" + path;
            p = p.parent;
        }
        return path;
    }
}

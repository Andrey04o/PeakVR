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
    public const int PopupQueue = 3010;       // the topmost menu, above menus left world-space behind it
    public const int HandQueue = 4000;        // above rain/airplane-window glass, for the wrist HUD
    public const int ReticleQueue = 4200;     // above every menu/popup, so the cursor is never hidden

    private static readonly Dictionary<Graphic, Material> Cache = new();

    // Reused so the periodic refresh (wrist HUD, hand prompt) allocates nothing per pass — the lists
    // replace per-call component arrays, Preserve replaces the params array HideFromMirror would build.
    private static readonly List<Graphic> Graphics = new();
    private static readonly List<TMP_SubMeshUI> SubMeshes = new();
    private static readonly int[] Preserve = { VRControllerHud.HudLayer, 7 };

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

    // Elements built after a canvas was first treated (dynamic lobby lists, player lists) keep their
    // own layer, and Apply's fast path skips the sweep once the canvas root is already on the UI
    // layer — so they miss the injected foreground pass and end up behind the panel they sit on.
    // Callers that own a canvas which rebuilds itself re-sweep on an interval.
    public static void SweepForegroundLayer(Canvas canvas)
    {
        if (canvas == null || !ForegroundUI.Active)
            return;

        VRLayers.HideFromMirror(canvas.gameObject, Preserve);
    }

    // TMP renders a <font="..."> tag through a TMP_SubMeshUI carrying the FALLBACK font's material,
    // not the text's own fontMaterial, and GetMaterial skips sub-meshes - so the VR button glyphs
    // never got the depth override the surrounding text has. Treat the source material once instead.
    public static void SetZTestAlways(Material mat)
    {
        if (mat == null)
            return;

        mat.SetInt(ZTestUI, Always);
        mat.SetInt(ZTestTMP, Always);
    }

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
        if (ForegroundUI.Active && canvas.gameObject.layer != VRLayers.UI)
            VRLayers.HideFromMirror(canvas.gameObject, Preserve);

        var applyQueue = baseQueue != DefaultQueue;
        var log = Logging && Logged.Add(canvas);
        if (log)
            Plugin.Log.LogInfo($"[PeakVR][UIOrder] ===== {canvas.name} base={baseQueue} sorting={canvas.sortingOrder} mode={canvas.renderMode} =====");

        canvas.GetComponentsInChildren(true, Graphics);
        for (var i = 0; i < Graphics.Count; i++)
        {
            var g = Graphics[i];
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

        ApplyToFallbackFonts(canvas, baseQueue, applyQueue);
    }

    // Any character the main font lacks (Cyrillic lobby names, our VR button glyphs) is drawn by a
    // TMP_SubMeshUI carrying the FALLBACK font's material, which GetMaterial skips - it is not a
    // Graphic we own, and reading its .material can throw. sharedMaterial is a plain field read.
    private static void ApplyToFallbackFonts(Canvas canvas, int baseQueue, bool applyQueue)
    {
        canvas.GetComponentsInChildren(true, SubMeshes);
        for (var i = 0; i < SubMeshes.Count; i++)
        {
            var sub = SubMeshes[i];
            if (sub == null)
                continue;

            try
            {
                var mat = sub.sharedMaterial;
                if (mat == null)
                    continue;

                mat.SetInt(ZTestUI, Always);
                mat.SetInt(ZTestTMP, Always);

                if (applyQueue)
                    mat.renderQueue = baseQueue;
            }
            catch (System.Exception)
            {
            }
        }
    }

    private static Material GetMaterial(Graphic g)
    {
        if (Cache.TryGetValue(g, out var cached) && cached != null)
            return cached;

        if (g is TMP_SubMeshUI)
            return null;

        Material mat;

        try
        {
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
        }
        catch (System.Exception)
        {
            return null;
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

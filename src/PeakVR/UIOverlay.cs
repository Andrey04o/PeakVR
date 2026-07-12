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
    private const int Foreground = 3005;

    private static readonly HashSet<Graphic> Handled = new HashSet<Graphic>();
    private static readonly List<Material> Materials = new List<Material>();

    public static void MakeAlwaysVisible(Canvas canvas)
    {
        if (canvas == null)
            return;

        foreach (var g in canvas.GetComponentsInChildren<Graphic>(true))
        {
            if (g == null || Handled.Contains(g))
                continue;

            Apply(g);
            Handled.Add(g);
        }

        for (var i = 0; i < Materials.Count; i++)
        {
            if (Materials[i] == null)
                continue;

            Materials[i].SetInt(ZTestUI, Always);
            Materials[i].SetInt(ZTestTMP, Always);
        }
    }

    private static void Apply(Graphic g)
    {
        Material mat;

        if (g is TMP_Text tmp)
        {
            mat = tmp.fontMaterial;
            if (mat == null)
                return;
        }
        else
        {
            var src = g.material != null ? g.material : g.defaultMaterial;
            if (src == null)
                return;

            mat = new Material(src);
            g.material = mat;
        }

        mat.SetInt(ZTestUI, Always);
        mat.SetInt(ZTestTMP, Always);

        if (!InStencilMask(g))
            mat.renderQueue = Foreground;

        Materials.Add(mat);
    }

    private static bool InStencilMask(Graphic g)
    {
        var mask = g.GetComponentInParent<Mask>();
        return mask != null && mask.enabled;
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace PeakVR;

internal static class VRLayers
{
    private static int ui = -2;

    private static readonly List<Transform> Nodes = new();

    public static int UI
    {
        get
        {
            if (ui == -2)
                ui = LayerMask.NameToLayer("UI");
            return ui;
        }
    }

    public static void HideFromMirror(GameObject root, params int[] preserve)
    {
        if (root == null || UI < 0)
            return;

        root.GetComponentsInChildren(true, Nodes);

        for (var n = 0; n < Nodes.Count; n++)
        {
            var t = Nodes[n];
            if (t == null)
                continue;

            var layer = t.gameObject.layer;
            var keep = false;
            for (var i = 0; i < preserve.Length; i++)
                if (layer == preserve[i]) { keep = true; break; }

            if (keep || layer == UI)
                continue;

            Moved.TryAdd(t.gameObject, layer);
            t.gameObject.layer = UI;
        }
    }

    public static void RestoreAll()
    {
        var restored = 0;

        foreach (var pair in Moved)
        {
            if (pair.Key == null)
                continue;

            pair.Key.layer = pair.Value;
            restored++;
        }

        Moved.Clear();

        if (restored > 0)
            Plugin.Log.LogInfo($"[PeakVR] {restored} object(s) moved back off the UI layer");
    }

    private static readonly Dictionary<GameObject, int> Moved = new();
}

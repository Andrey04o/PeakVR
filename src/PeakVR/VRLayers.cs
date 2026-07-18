using UnityEngine;

namespace PeakVR;

internal static class VRLayers
{
    private static int ui = -2;

    // The UI layer (5). The airport Mirror camera strips this layer (MirrorPatch), so anything moved
    // onto it won't be reflected. The main VR camera renders every layer, so it stays visible in-view.
    public static int UI
    {
        get
        {
            if (ui == -2)
                ui = LayerMask.NameToLayer("UI");
            return ui;
        }
    }

    // Move a whole subtree onto the UI layer (mirror-excluded). Nodes on a preserved layer are left
    // alone so raycast collider layers (e.g. the wrist HUD cells / emote button) keep working.
    public static void HideFromMirror(GameObject root, params int[] preserve)
    {
        if (root == null || UI < 0)
            return;

        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            var layer = t.gameObject.layer;
            var keep = false;
            for (var i = 0; i < preserve.Length; i++)
                if (layer == preserve[i]) { keep = true; break; }

            if (!keep)
                t.gameObject.layer = UI;
        }
    }
}

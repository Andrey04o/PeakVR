using System.Collections.Generic;
using UnityEngine;

namespace PeakVR;

// State + control for the "Hide Controllers" setting (T3). When hidden, the controller model
// renderers are turned off (you see only the scout's hands) and aim originates from the hand bone
// (see VRAim). Only renderers are toggled, so the HUD anchor transforms on the model survive.
internal static class VRControllerVisibility
{
    public static bool Hidden =>
        Plugin.Config != null && Plugin.Config.HideControllers.Value;

    private static readonly List<Renderer> renderers = new();
    private static bool? applied;

    public static void Register(GameObject model)
    {
        if (model == null)
            return;

        foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            if (r != null)
                renderers.Add(r);

        applied = null;
        Tick();
    }

    public static void Tick()
    {
        var hidden = Hidden;
        if (applied == hidden)
            return;
        applied = hidden;

        for (var i = renderers.Count - 1; i >= 0; i--)
        {
            if (renderers[i] == null)
                renderers.RemoveAt(i);
            else
                renderers[i].enabled = !hidden;
        }
    }
}

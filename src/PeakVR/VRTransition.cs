using HarmonyLib;
using UnityEngine;

namespace PeakVR;

// The spectate-switch fade (Transition_CanvasGroup) plays on a screen-space canvas, which the headset
// never shows. While spectating, head-lock that canvas in world space so the fade is visible when you
// change the followed player. Guarded to a dedicated transitions canvas so we never touch the game HUD.
[HarmonyPatch(typeof(Transitions), "Awake")]
internal static class VRTransitionPatch
{
    [HarmonyPostfix]
    private static void Postfix(Transitions __instance)
    {
        if (Plugin.VrEnabled)
            __instance.gameObject.AddComponent<VRTransition>();
    }
}

[DefaultExecutionOrder(1300)]
internal class VRTransition : MonoBehaviour
{
    private const float Distance = 1.2f;
    private const float Scale = 0.0035f;

    private Canvas canvas;
    private bool resolved;
    private RenderMode originalMode;
    private bool converted;

    private void Resolve()
    {
        resolved = true;

        var group = GetComponentInChildren<CanvasGroup>(true);
        var c = group != null ? group.GetComponentInParent<Canvas>() : GetComponentInParent<Canvas>();
        if (c == null)
            return;

        // Only surface a canvas dedicated to transitions — never the shared game HUD.
        if (c.GetComponentInParent<GUIManager>() != null || c.name.Contains("HUD") || c.name.Contains("GUI"))
        {
            Plugin.Log.LogWarning($"[PeakVR] Transition canvas '{c.name}' looks shared with the HUD — not surfacing in VR");
            return;
        }

        canvas = c;
        originalMode = c.renderMode;
    }

    private void LateUpdate()
    {
        if (!resolved)
            Resolve();
        if (canvas == null)
            return;

        var local = Character.localCharacter;
        var spectating = local != null && local.data != null && local.data.fullyPassedOut;

        if (!spectating)
        {
            if (converted)
            {
                canvas.renderMode = originalMode;
                converted = false;
            }
            return;
        }

        var cam = MainCamera.instance != null ? MainCamera.instance.cam : Camera.main;
        if (cam == null)
            return;

        if (!converted)
        {
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = cam;
            UIOverlay.MakeAlwaysVisible(canvas, true);
            converted = true;
        }

        var t = cam.transform;
        canvas.transform.SetPositionAndRotation(t.position + t.forward * Distance, t.rotation);
        canvas.transform.localScale = Vector3.one * Scale;
    }
}

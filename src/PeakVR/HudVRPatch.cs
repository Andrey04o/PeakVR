using HarmonyLib;
using UnityEngine;

namespace PeakVR;

[HarmonyPatch(typeof(GUIManager), "Awake")]
internal static class HudVRPatch
{
    [HarmonyPostfix]
    private static void Postfix(GUIManager __instance)
    {
        __instance.gameObject.AddComponent<VRHud>();
    }
}

// Attached in both modes and driven by VrEnabled, so a mid-session switch either way is picked up on
// the next frame without the component having to be added or removed.
internal class VRHud : MonoBehaviour
{
    private const float Distance = 1.5f;
    private const float Scale = 0.001f;

    private bool converted;
    private int frame;

    private Canvas hud;
    private RenderMode originalMode;
    private Transform originalParent;
    private Vector3 originalScale;
    private Vector3 originalPosition;
    private Quaternion originalRotation;

    private void Update()
    {
        if (!Plugin.VrEnabled)
        {
            if (converted)
                Restore();
            return;
        }

        if (!converted)
        {
            Convert();
            return;
        }

        if (++frame % 30 == 0 && GUIManager.instance != null)
            UIOverlay.MakeAlwaysVisible(GUIManager.instance.hudCanvas, false);
    }

    private void Convert()
    {
        var canvas = GUIManager.instance != null ? GUIManager.instance.hudCanvas : null;
        var cam = MainCamera.instance != null ? MainCamera.instance.cam : null;
        if (canvas == null || cam == null)
            return;

        converted = true;
        hud = canvas;

        var rt = (RectTransform)canvas.transform;
        originalMode = canvas.renderMode;
        originalParent = rt.parent;
        originalScale = rt.localScale;
        originalPosition = rt.localPosition;
        originalRotation = rt.localRotation;

        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = cam;

        rt.SetParent(cam.transform, false);
        rt.localScale = Vector3.one * Scale;
        rt.localPosition = new Vector3(0f, 0f, Distance);
        rt.localRotation = Quaternion.identity;

        UIOverlay.MakeAlwaysVisible(canvas, false);

        Plugin.Log.LogInfo("[PeakVR] HUD converted to world space");
    }

    private void Restore()
    {
        converted = false;

        if (hud == null)
            return;

        var rt = (RectTransform)hud.transform;
        rt.SetParent(originalParent, false);
        rt.localScale = originalScale;
        rt.localPosition = originalPosition;
        rt.localRotation = originalRotation;

        hud.renderMode = originalMode;
        hud.worldCamera = null;
        hud = null;

        Plugin.Log.LogInfo("[PeakVR] HUD returned to the screen");
    }
}

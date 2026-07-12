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

internal class VRHud : MonoBehaviour
{
    private const float Distance = 1.5f;
    private const float Scale = 0.001f;

    private bool done;
    private int frame;

    private void Update()
    {
        if (!done)
        {
            var canvas = GUIManager.instance != null ? GUIManager.instance.hudCanvas : null;
            var cam = MainCamera.instance != null ? MainCamera.instance.cam : null;
            if (canvas == null || cam == null)
                return;

            done = true;

            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = cam;

            var rt = (RectTransform)canvas.transform;
            rt.SetParent(cam.transform, false);
            rt.localScale = Vector3.one * Scale;
            rt.localPosition = new Vector3(0f, 0f, Distance);
            rt.localRotation = Quaternion.identity;

            UIOverlay.MakeAlwaysVisible(canvas, false);

            Plugin.Log.LogInfo("[PeakVR] HUD converted to world space");
            return;
        }

        if (++frame % 30 == 0 && GUIManager.instance != null)
            UIOverlay.MakeAlwaysVisible(GUIManager.instance.hudCanvas, false);
    }
}

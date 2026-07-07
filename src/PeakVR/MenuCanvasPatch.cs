using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace PeakVR;

[HarmonyPatch(typeof(MainMenu), nameof(MainMenu.Start))]
internal static class MenuCanvasPatch
{
    private const float Scale = 0.003f;
    private const float Distance = 4f;
    private const float Height = 0.6f;

    [HarmonyPostfix]
    private static void Postfix(MainMenu __instance)
    {
        var canvas = __instance.GetComponentInChildren<Canvas>(true);
        if (canvas == null)
        {
            Plugin.Log.LogWarning("[PeakVR] Menu canvas not found");
            return;
        }

        var cam = MainCamera.instance != null ? MainCamera.instance.cam : Camera.main;

        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = cam;

        if (canvas.GetComponent<GraphicRaycaster>() == null)
            canvas.gameObject.AddComponent<GraphicRaycaster>();

        var head = cam.transform;
        var fwd = head.forward;
        fwd.y = 0f;
        fwd = fwd.sqrMagnitude < 0.001f ? head.forward : fwd.normalized;

        var rt = (RectTransform)canvas.transform;
        rt.localScale = Vector3.one * Scale;
        rt.position = head.position + fwd * Distance + Vector3.up * Height;
        rt.rotation = Quaternion.LookRotation(fwd, Vector3.up);

        Plugin.Log.LogInfo($"[PeakVR] Menu canvas -> world space at {rt.position} (scale {Scale})");
    }
}

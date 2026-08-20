using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace PeakVR;

[HarmonyPatch(typeof(MainMenu), nameof(MainMenu.Start))]
internal static class MenuCanvasPatch
{
    private const float Scale = 0.003f;
    private const float Distance = 4f;
    private const float Height = 0.6f;

    internal static Canvas MenuCanvas;
    internal static TrackedDeviceGraphicRaycaster MenuRaycaster;

    private static VRCanvasState originalState;
    private static Vector3 originalScale;
    private static Vector3 originalPosition;
    private static Quaternion originalRotation;

    [HarmonyPostfix]
    private static void Postfix(MainMenu __instance)
    {
        if (!Plugin.VrEnabled)
            return;

        Build(__instance);
    }

    public static void Build(MainMenu __instance)
    {
        var canvas = __instance.GetComponentInChildren<Canvas>(true);
        if (canvas == null)
        {
            Plugin.Log.LogWarning("[PeakVR] Menu canvas not found");
            return;
        }

        var cam = MainCamera.instance != null ? MainCamera.instance.cam : Camera.main;
        if (cam == null)
            return;

        if (canvas != MenuCanvas)
        {
            originalState = VRCanvasState.Capture(canvas);
            originalScale = canvas.transform.localScale;
            originalPosition = canvas.transform.position;
            originalRotation = canvas.transform.rotation;
        }

        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = cam;

        var raycaster = VRPointer.Attach(canvas);

        if (EventSystem.current == null)
            new GameObject("PeakVR EventSystem").AddComponent<EventSystem>();

        MenuCanvas = canvas;
        MenuRaycaster = raycaster;
        VRPointer.Canvas = canvas;
        VRPointer.Raycaster = raycaster;

        if (__instance.GetComponent<VRMenuPopup>() == null)
            __instance.gameObject.AddComponent<VRMenuPopup>();

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

    public static void Restore()
    {
        if (MenuCanvas == null)
        {
            MenuCanvas = null;
            MenuRaycaster = null;
            return;
        }

        originalState.Apply(MenuCanvas);

        var rt = MenuCanvas.transform;
        rt.localScale = originalScale;
        rt.SetPositionAndRotation(originalPosition, originalRotation);

        VRPointer.Detach(MenuCanvas);

        Plugin.Log.LogInfo("[PeakVR] Menu canvas returned to the screen");

        MenuCanvas = null;
        MenuRaycaster = null;
    }
}

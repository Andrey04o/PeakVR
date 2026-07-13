using HarmonyLib;
using UnityEngine;

namespace PeakVR;

[HarmonyPatch(typeof(LoadingScreen), "Awake")]
internal static class LoadingScreenVRPatch
{
    [HarmonyPostfix]
    private static void Postfix(LoadingScreen __instance)
    {
        __instance.gameObject.AddComponent<VRLoadingScreen>();
    }
}

[DefaultExecutionOrder(3000)]
internal class VRLoadingScreen : MonoBehaviour
{
    private const float Scale = 0.003f;
    private const float Distance = 3f;

    private LoadingScreen loadingScreen;
    private bool converted;

    private void Awake()
    {
        loadingScreen = GetComponent<LoadingScreen>();
    }

    private void LateUpdate()
    {
        if (!Plugin.VrEnabled || loadingScreen == null || loadingScreen.canvas == null)
            return;
        if (!loadingScreen.canvas.enabled)
            return;

        var cam = Camera.main;
        if (cam == null && MainCamera.instance != null)
            cam = MainCamera.instance.cam;
        if (cam == null)
            return;

        if (!converted)
        {
            loadingScreen.canvas.renderMode = RenderMode.WorldSpace;
            loadingScreen.canvas.worldCamera = cam;
            UIOverlay.MakeAlwaysVisible(loadingScreen.canvas, true);
            converted = true;
        }

        var head = cam.transform;
        var rt = (RectTransform)loadingScreen.canvas.transform;
        rt.localScale = Vector3.one * Scale;
        rt.position = head.position + head.forward * Distance;
        rt.rotation = head.rotation;
    }
}

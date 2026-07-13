using HarmonyLib;
using UnityEngine;

namespace PeakVR;

[HarmonyPatch(typeof(UIPlayerNames), nameof(UIPlayerNames.UpdateName))]
internal static class VRNameplatePatch
{
    private const float Scale = 0.005f;

    private static Canvas worldCanvas;

    [HarmonyPostfix]
    private static void Postfix(UIPlayerNames __instance, int index, Vector3 position, bool visible)
    {
        if (!Plugin.VrEnabled || MainCamera.instance == null)
            return;
        if (index >= __instance.playerNameText.Length)
            return;

        var plate = __instance.playerNameText[index];
        if (plate == null || !plate.gameObject.activeSelf)
            return;

        EnsureCanvas();

        if (plate.transform.parent != worldCanvas.transform)
        {
            plate.transform.SetParent(worldCanvas.transform, false);
            UIOverlay.MakeAlwaysVisible(worldCanvas, true);
        }

        var cam = MainCamera.instance.cam.transform;
        plate.transform.position = position;
        plate.transform.rotation = Quaternion.LookRotation(position - cam.position, Vector3.up);
        plate.transform.localScale = Vector3.one * Scale;
    }

    private static void EnsureCanvas()
    {
        if (worldCanvas != null)
            return;

        var go = new GameObject("PeakVR Nameplates");
        Object.DontDestroyOnLoad(go);

        worldCanvas = go.AddComponent<Canvas>();
        worldCanvas.renderMode = RenderMode.WorldSpace;
        worldCanvas.worldCamera = MainCamera.instance.cam;
    }
}

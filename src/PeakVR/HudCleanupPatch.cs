using HarmonyLib;
using TMPro;

namespace PeakVR;

// Task 1: the persistent item control-tips (bottom of the HUD) show gamepad/keyboard button
// sprites that are meaningless in VR. Keep them permanently hidden.
[HarmonyPatch(typeof(GUIManager), nameof(GUIManager.UpdateItemPrompts))]
internal static class HudCleanupPatch
{
    [HarmonyPostfix]
    private static void Postfix(GUIManager __instance)
    {
        if (!Plugin.VrEnabled)
            return;

        Hide(__instance.itemPromptMain);
        Hide(__instance.itemPromptScroll);
        Hide(__instance.itemPromptSecondary);
        Hide(__instance.itemPromptDrop);
        Hide(__instance.itemPromptThrow);
    }

    private static void Hide(TextMeshProUGUI text)
    {
        if (text != null && text.gameObject.activeSelf)
            text.gameObject.SetActive(false);
    }
}

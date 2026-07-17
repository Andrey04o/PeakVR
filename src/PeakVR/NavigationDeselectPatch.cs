using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine.EventSystems;
using Zorro.ControllerSupport;

namespace PeakVR;

// Task 2: the menu auto-selects/highlights the first button ("Host Game") because our VR input
// reports as an Unknown control scheme (not KeyboardMouse), so NavigationContainerHandler keeps
// re-selecting the default (spamming "Reselecting ..."). In VR we point with the laser, so behave
// like KeyboardMouse: clear any selection and never re-select.
[HarmonyPatch(typeof(NavigationContainerHandler), "LateUpdate")]
internal static class NavigationDeselectPatch
{
    private static readonly MethodInfo ClearOldFrames =
        AccessTools.Method(typeof(NavigationContainerHandler), "ClearOldFrames");

    [HarmonyPrefix]
    private static bool Prefix(NavigationContainerHandler __instance)
    {
        if (!Plugin.VrEnabled)
            return true;

        ClearOldFrames?.Invoke(__instance, null);

        var es = EventSystem.current;
        if (es != null && es.currentSelectedGameObject != null
            && es.currentSelectedGameObject.GetComponent<TMP_InputField>() == null)
            es.SetSelectedGameObject(null);

        return false;
    }
}

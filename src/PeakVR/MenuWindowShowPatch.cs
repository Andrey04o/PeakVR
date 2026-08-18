using System.Collections.Generic;
using HarmonyLib;

namespace PeakVR;

// MenuWindow.Open() registers the window in AllActiveWindows, which is how VRMenuManager finds the
// panel to pull into world space. Some mods (PEAKInvitation) call the bare Show() instead, which only
// activates the GameObject — the window never counts as open, so its screen-space overlay canvas is
// left untouched and is invisible in the headset while still showing on the flat mirror.
[HarmonyPatch(typeof(MenuWindow), nameof(MenuWindow.Show))]
internal static class MenuWindowShowPatch
{
    private static readonly List<MenuWindow> Shown = new();

    [HarmonyPostfix]
    private static void Postfix(MenuWindow __instance)
    {
        if (!Plugin.VrEnabled || __instance.isOpen || Shown.Contains(__instance))
            return;

        Shown.Add(__instance);
        Plugin.Log.LogInfo($"[PeakVR] Menu window '{__instance.name}' shown without Open() - tracking it for VR");
    }

    public static MenuWindow Current()
    {
        for (var i = Shown.Count - 1; i >= 0; i--)
        {
            var w = Shown[i];
            if (w == null)
            {
                Shown.RemoveAt(i);
                continue;
            }

            if (w.isOpen || w.panel == null || !w.panel.activeInHierarchy)
                continue;

            if (w.GetComponentInParent<GUIManager>() != null)
                continue;

            return w;
        }

        return null;
    }
}

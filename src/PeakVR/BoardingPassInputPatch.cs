using HarmonyLib;

namespace PeakVR;

// Task 3: the airport check-in (boarding pass) menu normally freezes the player. In VR we want to
// walk around while it's open (it's world-anchored at the kiosk by VRMenuManager), so report that
// it doesn't block player input. BoardingPass doesn't override this getter, so patch MenuWindow's.
[HarmonyPatch(typeof(MenuWindow), "get_blocksPlayerInput")]
internal static class BoardingPassInputPatch
{
    [HarmonyPostfix]
    private static void Postfix(MenuWindow __instance, ref bool __result)
    {
        if (Plugin.VrEnabled && __instance is BoardingPass)
            __result = false;
    }
}

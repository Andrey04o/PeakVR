using HarmonyLib;
using UnityEngine;

namespace PeakVR;

// A (right primary) pings a location; redirect the ping raycast to come from the right controller
// instead of the screen-center / mouse (the game raycasts Camera.main.ScreenPointToRay(mousePos)).
[HarmonyPatch(typeof(PointPinger), "TryGetPingHit")]
internal static class PingRedirectPatch
{
    [HarmonyPrefix]
    private static bool Prefix(ref bool __result, out RaycastHit hit)
    {
        hit = default;

        if (!Plugin.VrEnabled || !VRAim.TryRight(out var origin, out var dir))
            return true;

        var mask = HelperFunctions.GetMask(HelperFunctions.LayerType.TerrainMap);
        __result = Physics.Raycast(origin, dir, out hit, 2000f, mask);
        return false;
    }
}

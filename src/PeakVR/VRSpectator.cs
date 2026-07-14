using HarmonyLib;
using UnityEngine;

namespace PeakVR;

[HarmonyPatch(typeof(MainCameraMovement), "Spectate")]
internal static class VRSpectator
{
    public static Vector3 Pivot;
    public static bool HasTarget;

    [HarmonyPostfix]
    private static void Postfix(MainCameraMovement __instance)
    {
        var spec = Traverse.Create(__instance).Field("specCharacter").GetValue<Character>();
        if (spec != null)
        {
            Pivot = spec.GetSpectatePosition();
            HasTarget = true;
        }
        else
        {
            HasTarget = false;
        }
    }
}

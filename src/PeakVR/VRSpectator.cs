using HarmonyLib;
using UnityEngine;

namespace PeakVR;

[HarmonyPatch(typeof(MainCameraMovement), "Spectate")]
internal static class VRSpectator
{
    public static Vector3 Pivot;
    public static bool HasTarget;

    [HarmonyPostfix]
    private static void Postfix()
    {
        // specCharacter is a static auto-property (backing field <specCharacter>k__BackingField), so the
        // old Traverse.Field("specCharacter") always returned null → HasTarget stayed false → the VR rig
        // fell back to the local dead body (skeleton). Read the property directly so we follow the actual
        // spectated player.
        var spec = MainCameraMovement.specCharacter;
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

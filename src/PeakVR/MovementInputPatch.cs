using HarmonyLib;
using UnityEngine;

namespace PeakVR;

[HarmonyPatch(typeof(CharacterInput), "Sample")]
internal static class MovementInputPatch
{
    [HarmonyPostfix]
    private static void Postfix(CharacterInput __instance, bool playerMovementActive)
    {
        if (!playerMovementActive || VRControls.MoveStick == null)
            return;

        var local = Character.localCharacter;
        if (local == null || local.input != __instance)
            return;

        var move = VRControls.MoveStick.ReadValue<Vector2>() + VRHeadRig.RoomInput;
        if (move.sqrMagnitude > 0.02f)
            __instance.movementInput = Vector2.ClampMagnitude(move, 1f);
    }
}

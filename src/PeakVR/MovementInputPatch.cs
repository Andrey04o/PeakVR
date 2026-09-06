using HarmonyLib;
using UnityEngine;

namespace PeakVR;

[HarmonyPatch(typeof(CharacterInput), "Sample")]
internal static class MovementInputPatch
{
    [HarmonyPostfix]
    private static void Postfix(CharacterInput __instance, bool playerMovementActive)
    {
        if (!Plugin.VrEnabled || !playerMovementActive || VRControls.MoveStick == null)
            return;

        var local = Character.localCharacter;
        if (local == null || local.input != __instance)
            return;

        if (VRKeyboard.IsOpen)
        {
            __instance.movementInput = Vector2.zero;
            return;
        }

        var stick = VRControls.Move();
        var move = stick + VRHeadRig.RoomInput;

        if (stick != Vector2.zero || move.sqrMagnitude > 0.02f)
            __instance.movementInput = Vector2.ClampMagnitude(move, 1f);
    }
}

using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PeakVR;

[HarmonyPatch(typeof(CharacterInput), "Sample")]
internal static class InteractionInputPatch
{
    [HarmonyPostfix]
    private static void Postfix(CharacterInput __instance, bool playerMovementActive)
    {
        var local = Character.localCharacter;
        if (local == null || local.input != __instance || VRControls.RightGrip == null)
            return;

        Inject(VRControls.RightGrip, ref __instance.interactWasPressed, ref __instance.interactIsPressed,
            ref __instance.interactWasReleased);

        Inject(VRControls.LeftGrip, ref __instance.dropWasPressed, ref __instance.dropIsPressed,
            ref __instance.dropWasReleased);
        if (VRControls.RightPrimary.WasPressedThisFrame())
            __instance.selectSlotForwardWasPressed = true;
        if (VRControls.LeftPrimary.WasPressedThisFrame())
            __instance.selectSlotBackwardWasPressed = true;

        if (VRControls.Pause.WasPressedThisFrame())
        {
            __instance.pauseWasPressed = true;
            __instance.jumpWasPressed = false;
            __instance.jumpIsPressed = false;
        }

        if (!playerMovementActive)
            return;

        Inject(VRControls.RightTrigger, ref __instance.usePrimaryWasPressed, ref __instance.usePrimaryIsPressed,
            ref __instance.usePrimaryWasReleased);
        Inject(VRControls.LeftTrigger, ref __instance.useSecondaryWasPressed, ref __instance.useSecondaryIsPressed,
            ref __instance.useSecondaryWasReleased);

        if (VRControls.Sprint.IsPressed())
            __instance.sprintIsPressed = true;

        var scroll = VRControls.TurnStick.ReadValue<Vector2>().y;
        if (scroll > 0.6f)
            __instance.scrollForwardIsPressed = true;
        else if (scroll < -0.6f)
            __instance.scrollBackwardIsPressed = true;
    }

    private static void Inject(InputAction action, ref bool wasPressed, ref bool isPressed, ref bool wasReleased)
    {
        if (action.WasPressedThisFrame())
            wasPressed = true;
        if (action.IsPressed())
            isPressed = true;
        if (action.WasReleasedThisFrame())
            wasReleased = true;
    }
}

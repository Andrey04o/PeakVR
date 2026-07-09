using HarmonyLib;
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

        if (!playerMovementActive)
            return;

        Inject(VRControls.RightTrigger, ref __instance.usePrimaryWasPressed, ref __instance.usePrimaryIsPressed,
            ref __instance.usePrimaryWasReleased);
        Inject(VRControls.LeftTrigger, ref __instance.useSecondaryWasPressed, ref __instance.useSecondaryIsPressed,
            ref __instance.useSecondaryWasReleased);

        if (VRControls.LeftGrip.WasPressedThisFrame())
            __instance.dropWasPressed = true;
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

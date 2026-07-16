using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PeakVR;

[HarmonyPatch(typeof(CharacterInput), "Sample")]
internal static class InteractionInputPatch
{
    private const float ScrollOn = 0.6f;
    private const float ScrollOff = 0.4f;
    private const float ScrollRepeat = 0.18f;

    private static int scrollDir;
    private static float scrollTickTime;

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
        // A (right primary) = ping. Slot select moved to the left-controller HUD pointer (#17).
        if (VRControls.RightPrimary.WasPressedThisFrame())
            __instance.pingWasPressed = true;

        if (VRControls.Pause.WasPressedThisFrame())
        {
            __instance.pauseWasPressed = true;
            __instance.jumpWasPressed = false;
            __instance.jumpIsPressed = false;
        }

        // Emote wheel held open by the left-wrist emote button (T6).
        if (VREmoteWheel.EmoteActive)
            __instance.emoteIsPressed = true;

        if (!playerMovementActive)
            return;

        if (!VREmoteWheel.RightTriggerConsumed)
            Inject(VRControls.RightTrigger, ref __instance.usePrimaryWasPressed, ref __instance.usePrimaryIsPressed,
                ref __instance.usePrimaryWasReleased);
        if (!VRControllerHud.LeftTriggerConsumed)
            Inject(VRControls.LeftTrigger, ref __instance.useSecondaryWasPressed, ref __instance.useSecondaryIsPressed,
                ref __instance.useSecondaryWasReleased);

        if (VRControls.Sprint.IsPressed())
            __instance.sprintIsPressed = true;

        // X (left primary) = hold-to-crouch, coexisting with physical bend-crouch.
        if (VRHeadRig.Crouching || VRControls.LeftPrimary.IsPressed())
            __instance.crouchIsPressed = true;

        if (VRControls.Stash.WasPressedThisFrame())
            __instance.unselectSlotWasPressed = true;

        InjectScroll(__instance);
    }

    private static void InjectScroll(CharacterInput input)
    {
        var scrollY = VRControls.TurnStick.ReadValue<Vector2>().y;
        var dir = scrollY > ScrollOn ? 1 : scrollY < -ScrollOn ? -1 : 0;

        if (scrollDir != 0 && Mathf.Abs(scrollY) < ScrollOff)
            scrollDir = 0;

        if (dir == 0)
            return;

        if (dir > 0)
            input.scrollForwardIsPressed = true;
        else
            input.scrollBackwardIsPressed = true;

        var now = Time.time;
        if (dir != scrollDir || now - scrollTickTime >= ScrollRepeat)
        {
            scrollDir = dir;
            scrollTickTime = now;

            if (dir > 0)
                input.scrollForwardWasPressed = true;
            else
                input.scrollBackwardWasPressed = true;
        }
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

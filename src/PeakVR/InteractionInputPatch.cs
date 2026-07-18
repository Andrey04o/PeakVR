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
    private static bool crouchToggle;

    [HarmonyPostfix]
    private static void Postfix(CharacterInput __instance, bool playerMovementActive)
    {
        var local = Character.localCharacter;
        if (local == null || local.input != __instance || VRControls.RightGrip == null)
            return;

        if (local.data != null && local.data.fullyPassedOut)
            crouchToggle = false;

        Inject(VRControls.RightGrip, ref __instance.interactWasPressed, ref __instance.interactIsPressed,
            ref __instance.interactWasReleased);

        Inject(VRControls.LeftGrip, ref __instance.dropWasPressed, ref __instance.dropIsPressed,
            ref __instance.dropWasReleased);

        // Jump is driven ONLY by A. Clear the game's native jump first (the VD-emulated gamepad
        // maps a button to it — the old "B also jumps") so B is free to hold/unhold, and so the
        // gamepad-scheme switching can't release the jump mid-move.
        __instance.jumpWasPressed = false;
        __instance.jumpIsPressed = false;
        if (VRControls.RightPrimary.WasPressedThisFrame())
            __instance.jumpWasPressed = true;
        if (VRControls.RightPrimary.IsPressed())
            __instance.jumpIsPressed = true;

        // Right thumbstick click = ping.
        if (VRControls.Stash.WasPressedThisFrame())
            __instance.pingWasPressed = true;

        // B (right secondary) = hold / unhold the selected item.
        if (VRControls.RightSecondary.WasPressedThisFrame())
            __instance.unselectSlotWasPressed = true;

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

        // Sprint (left-stick click). Inject the edge too so climbing lunge (RPCA_ClimbJump) arms —
        // it needs sprintWasPressed to set sprintHasBeenPressedSinceClimb, not just sprintIsPressed.
        if (VRControls.Sprint.WasPressedThisFrame())
            __instance.sprintWasPressed = true;
        if (VRControls.Sprint.IsPressed())
            __instance.sprintIsPressed = true;

        // X (left primary): if crouched (toggle or physical), STAND UP and take the current head
        // height as the new standing baseline; otherwise crouch. Physical bend-crouch still works.
        if (VRControls.LeftPrimary.WasPressedThisFrame())
        {
            if (crouchToggle || VRHeadRig.Crouching)
            {
                crouchToggle = false;
                VRHeadRig.RequestRecalibrate();
            }
            else
            {
                crouchToggle = true;
            }
        }
        if (VRHeadRig.Crouching || crouchToggle)
            __instance.crouchIsPressed = true;

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

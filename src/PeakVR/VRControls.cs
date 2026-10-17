using UnityEngine;
using UnityEngine.InputSystem;

namespace PeakVR;

internal static class VRControls
{
    private const float FallbackDeadzone = 0.15f;

    public static InputAction MoveStick { get; private set; }
    public static InputAction TurnStick { get; private set; }

    public static InputAction LeftGrip { get; private set; }
    public static InputAction RightGrip { get; private set; }
    public static InputAction LeftTrigger { get; private set; }
    public static InputAction RightTrigger { get; private set; }
    public static InputAction LeftPrimary { get; private set; }
    public static InputAction RightPrimary { get; private set; }
    public static InputAction RightSecondary { get; private set; }
    public static InputAction Pause { get; private set; }
    public static InputAction Sprint { get; private set; }
    public static InputAction Stash { get; private set; }

    public static void Init()
    {
        MoveStick = new InputAction("VR Move", InputActionType.Value, expectedControlType: "Vector2");
        Bind(MoveStick, "LeftHand", "thumbstick");
        TurnStick = new InputAction("VR Turn", InputActionType.Value, expectedControlType: "Vector2");
        Bind(TurnStick, "RightHand", "thumbstick");

        LeftGrip = Button("VR LeftGrip", "LeftHand", "gripPressed");
        RightGrip = Button("VR RightGrip", "RightHand", "gripPressed");
        LeftTrigger = Button("VR LeftTrigger", "LeftHand", "triggerPressed");
        RightTrigger = Button("VR RightTrigger", "RightHand", "triggerPressed");
        LeftPrimary = Button("VR LeftPrimary", "LeftHand", "primaryButton");
        RightPrimary = Button("VR RightPrimary", "RightHand", "primaryButton");
        RightSecondary = Button("VR RightSecondary", "RightHand", "secondaryButton");

        Pause = Button("VR Pause", "LeftHand", "secondaryButton");

        Sprint = Button("VR Sprint", "LeftHand", "thumbstickClicked");
        Sprint.AddBinding("<XRController>{LeftHand}/thumbstickpressed");

        Stash = Button("VR Stash", "RightHand", "thumbstickClicked");
        Stash.AddBinding("<XRController>{RightHand}/thumbstickpressed");

        MoveStick.Enable();
        TurnStick.Enable();
        LeftGrip.Enable();
        RightGrip.Enable();
        LeftTrigger.Enable();
        RightTrigger.Enable();
        LeftPrimary.Enable();
        RightPrimary.Enable();
        RightSecondary.Enable();
        Pause.Enable();
        Sprint.Enable();
        Stash.Enable();
    }

    public static void Bind(InputAction action, string hand, string control)
    {
        action.AddBinding($"<XRController>{{{hand}}}/{control}");
        action.AddBinding($"<{OpenVRInput.Layout}>{{{hand}}}/{control}");
    }

    public static string PosePositionPath(string hand) =>
        OpenVRBackend.Active
            ? $"<XRController>{{{hand}}}/devicePosition"
            : $"<XRController>{{{hand}}}/pointer/position";

    public static string PoseRotationPath(string hand) =>
        OpenVRBackend.Active
            ? $"<XRController>{{{hand}}}/deviceRotation"
            : $"<XRController>{{{hand}}}/pointer/rotation";

    public static InputAction Button(string name, string hand, string control)
    {
        InputAction action = new InputAction(name, InputActionType.Button);
        Bind(action, hand, control);
        return action;
    }

    public static Vector2 Move() =>
        Deadzoned(MoveStick, Plugin.Config != null ? Plugin.Config.MoveDeadzone.Value : FallbackDeadzone);

    public static Vector2 Turn() =>
        Deadzoned(TurnStick, Plugin.Config != null ? Plugin.Config.TurnDeadzone.Value : FallbackDeadzone);

    private static Vector2 Deadzoned(InputAction action, float deadzone)
    {
        if (action == null)
            return Vector2.zero;

        var value = action.ReadValue<Vector2>();

        if (deadzone <= 0f)
            return value;

        var magnitude = value.magnitude;
        if (magnitude <= deadzone)
            return Vector2.zero;

        return value * ((magnitude - deadzone) / (1f - deadzone) / magnitude);
    }
}

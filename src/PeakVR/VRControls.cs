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
        MoveStick = new InputAction("VR Move", InputActionType.Value,
            ControlPath("LeftHand", "thumbstick"), expectedControlType: "Vector2");
        TurnStick = new InputAction("VR Turn", InputActionType.Value,
            ControlPath("RightHand", "thumbstick"), expectedControlType: "Vector2");

        LeftGrip = Button("VR LeftGrip", ControlPath("LeftHand", "gripPressed"));
        RightGrip = Button("VR RightGrip", ControlPath("RightHand", "gripPressed"));
        LeftTrigger = Button("VR LeftTrigger", ControlPath("LeftHand", "triggerPressed"));
        RightTrigger = Button("VR RightTrigger", ControlPath("RightHand", "triggerPressed"));
        LeftPrimary = Button("VR LeftPrimary", ControlPath("LeftHand", "primaryButton"));
        RightPrimary = Button("VR RightPrimary", ControlPath("RightHand", "primaryButton"));
        RightSecondary = Button("VR RightSecondary", ControlPath("RightHand", "secondaryButton"));

        Pause = Button("VR Pause", ControlPath("LeftHand", "secondaryButton"));

        Sprint = new InputAction("VR Sprint", InputActionType.Button);
        Sprint.AddBinding(ControlPath("LeftHand", "thumbstickClicked"));
        Sprint.AddBinding(ControlPath("LeftHand", "thumbstickpressed"));

        Stash = new InputAction("VR Stash", InputActionType.Button);
        Stash.AddBinding(ControlPath("RightHand", "thumbstickClicked"));
        Stash.AddBinding(ControlPath("RightHand", "thumbstickpressed"));

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

    public static string ControlPath(string hand, string control) =>
        OpenVRBackend.Selected
            ? $"<{OpenVRInput.Layout}>{{{hand}}}/{control}"
            : $"<XRController>{{{hand}}}/{control}";

    public static string PosePositionPath(string hand) =>
        OpenVRBackend.Selected
            ? $"<XRController>{{{hand}}}/devicePosition"
            : $"<XRController>{{{hand}}}/pointer/position";

    public static string PoseRotationPath(string hand) =>
        OpenVRBackend.Selected
            ? $"<XRController>{{{hand}}}/deviceRotation"
            : $"<XRController>{{{hand}}}/pointer/rotation";

    private static InputAction Button(string name, string binding)
    {
        return new InputAction(name, InputActionType.Button, binding);
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

using UnityEngine.InputSystem;

namespace PeakVR;

internal static class VRControls
{
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
            "<XRController>{LeftHand}/thumbstick", expectedControlType: "Vector2");
        TurnStick = new InputAction("VR Turn", InputActionType.Value,
            "<XRController>{RightHand}/thumbstick", expectedControlType: "Vector2");

        LeftGrip = Button("VR LeftGrip", "<XRController>{LeftHand}/gripPressed");
        RightGrip = Button("VR RightGrip", "<XRController>{RightHand}/gripPressed");
        LeftTrigger = Button("VR LeftTrigger", "<XRController>{LeftHand}/triggerPressed");
        RightTrigger = Button("VR RightTrigger", "<XRController>{RightHand}/triggerPressed");
        LeftPrimary = Button("VR LeftPrimary", "<XRController>{LeftHand}/primaryButton");
        RightPrimary = Button("VR RightPrimary", "<XRController>{RightHand}/primaryButton");
        RightSecondary = Button("VR RightSecondary", "<XRController>{RightHand}/secondaryButton");

        Pause = Button("VR Pause", "<XRController>{LeftHand}/secondaryButton");

        Sprint = new InputAction("VR Sprint", InputActionType.Button);
        Sprint.AddBinding("<XRController>{LeftHand}/thumbstickClicked");
        Sprint.AddBinding("<XRController>{LeftHand}/thumbstickpressed");

        Stash = new InputAction("VR Stash", InputActionType.Button);
        Stash.AddBinding("<XRController>{RightHand}/thumbstickClicked");
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

    private static InputAction Button(string name, string binding)
    {
        return new InputAction(name, InputActionType.Button, binding);
    }
}

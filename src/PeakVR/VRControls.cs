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

        MoveStick.Enable();
        TurnStick.Enable();
        LeftGrip.Enable();
        RightGrip.Enable();
        LeftTrigger.Enable();
        RightTrigger.Enable();
        LeftPrimary.Enable();
        RightPrimary.Enable();
    }

    private static InputAction Button(string name, string binding)
    {
        return new InputAction(name, InputActionType.Button, binding);
    }
}

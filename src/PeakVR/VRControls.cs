using UnityEngine.InputSystem;

namespace PeakVR;

internal static class VRControls
{
    public static InputAction MoveStick { get; private set; }
    public static InputAction TurnStick { get; private set; }

    public static void Init()
    {
        MoveStick = new InputAction("VR Move", InputActionType.Value,
            "<XRController>{LeftHand}/thumbstick", expectedControlType: "Vector2");
        TurnStick = new InputAction("VR Turn", InputActionType.Value,
            "<XRController>{RightHand}/thumbstick", expectedControlType: "Vector2");

        MoveStick.Enable();
        TurnStick.Enable();
    }
}

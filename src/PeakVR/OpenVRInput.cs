using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;
using Valve.VR;

namespace PeakVR;

[StructLayout(LayoutKind.Explicit, Size = 20)]
internal struct OpenVRControllerState : IInputStateTypeInfo
{
    public FourCC format => new FourCC('P', 'V', 'R', 'O');

    [InputControl(name = "thumbstick", layout = "Vector2")]
    [FieldOffset(0)] public Vector2 thumbstick;

    [InputControl(name = "trigger", layout = "Axis")]
    [FieldOffset(8)] public float trigger;

    [InputControl(name = "grip", layout = "Axis")]
    [FieldOffset(12)] public float grip;

    [InputControl(name = "primaryButton", layout = "Button", bit = 0)]
    [InputControl(name = "secondaryButton", layout = "Button", bit = 1)]
    [InputControl(name = "triggerPressed", layout = "Button", bit = 2)]
    [InputControl(name = "gripPressed", layout = "Button", bit = 3)]
    [InputControl(name = "thumbstickClicked", layout = "Button", bit = 4)]
    [InputControl(name = "thumbstickTouched", layout = "Button", bit = 5)]
    [FieldOffset(16)] public uint buttons;
}

[InputControlLayout(stateType = typeof(OpenVRControllerState), displayName = "PeakVR OpenVR Controller")]
internal class OpenVRController : InputDevice
{
    protected override unsafe long ExecuteCommand(InputDeviceCommand* commandPtr)
    {
        if (commandPtr->type == QueryCanRunInBackground.Type)
        {
            ((QueryCanRunInBackground*)commandPtr)->canRunInBackground = true;
            return InputDeviceCommand.GenericSuccess;
        }

        return base.ExecuteCommand(commandPtr);
    }
}

internal class OpenVRInputRunner : MonoBehaviour
{
    private void Update() => OpenVRInput.Activate();
}

internal static class OpenVRInput
{
    public const string Layout = "PeakVROpenVRController";

    private const float PressThreshold = 0.75f;
    private const float ReleaseThreshold = 0.55f;

    private const int ButtonApplicationMenu = 1;
    private const int ButtonGrip = 2;
    private const int ButtonA = 7;
    private const int ButtonAxis0 = 32;
    private const int ButtonAxis1 = 33;
    private const int ButtonAxis3 = 35;

    private static readonly uint StateSize = (uint)Marshal.SizeOf(typeof(VRControllerState_t));

    private class Hand
    {
        public ETrackedControllerRole role;
        public OpenVRController device;
        public uint index = OpenVR.k_unTrackedDeviceIndexInvalid;
        public bool knuckles;
        public bool trigger;
        public bool grip;
    }

    private static readonly Hand Left = new() { role = ETrackedControllerRole.LeftHand };
    private static readonly Hand Right = new() { role = ETrackedControllerRole.RightHand };

    private static OpenVRInputRunner runner;
    private static bool registered;
    private static bool wanted;
    private static bool active;
    private static bool failed;

    public static void Start()
    {
        wanted = true;

        if (runner != null)
            return;

        var obj = new GameObject("PeakVR OpenVR Input");
        Object.DontDestroyOnLoad(obj);
        runner = obj.AddComponent<OpenVRInputRunner>();
    }

    public static void Stop()
    {
        wanted = false;
        Deactivate();
    }

    public static void Activate()
    {
        if (!wanted || active || failed)
            return;

        try
        {
            if (!registered)
            {
                InputSystem.RegisterLayout<OpenVRController>(Layout);
                registered = true;
            }

            Left.device = AddDevice("PeakVR OpenVR Left", UnityEngine.InputSystem.CommonUsages.LeftHand);
            Right.device = AddDevice("PeakVR OpenVR Right", UnityEngine.InputSystem.CommonUsages.RightHand);
            Left.index = Right.index = OpenVR.k_unTrackedDeviceIndexInvalid;

            InputSystem.onBeforeUpdate += Poll;
            active = true;

            Plugin.Log.LogInfo("[PeakVR][OpenVR] controller input started (legacy controller state)");
        }
        catch (System.Exception e)
        {
            failed = true;
            Plugin.Log.LogError($"[PeakVR][OpenVR] controller input failed to start: {e}");
            RemoveDevice(Left);
            RemoveDevice(Right);
        }
    }

    private static void Deactivate()
    {
        if (!active)
            return;

        InputSystem.onBeforeUpdate -= Poll;
        active = false;

        RemoveDevice(Left);
        RemoveDevice(Right);
    }

    private static OpenVRController AddDevice(string name, InternedString usage)
    {
        var device = InputSystem.AddDevice<OpenVRController>(name);
        InputSystem.SetDeviceUsage(device, usage);
        return device;
    }

    private static void RemoveDevice(Hand hand)
    {
        if (hand.device != null && hand.device.added)
            InputSystem.RemoveDevice(hand.device);

        hand.device = null;
        hand.trigger = hand.grip = false;
    }

    private static void Poll()
    {
        if (!active || InputState.currentUpdateType == InputUpdateType.BeforeRender)
            return;

        var system = OpenVR.System;
        Queue(Left, system);
        Queue(Right, system);
    }

    private static void Queue(Hand hand, CVRSystem system)
    {
        if (hand.device == null || !hand.device.added)
            return;

        var state = new OpenVRControllerState();

        if (system != null)
            Read(hand, system, ref state);

        InputSystem.QueueStateEvent(hand.device, state);
    }

    private static void Read(Hand hand, CVRSystem system, ref OpenVRControllerState state)
    {
        var index = system.GetTrackedDeviceIndexForControllerRole(hand.role);

        if (index >= OpenVR.k_unMaxTrackedDeviceCount)
            return;

        if (index != hand.index)
            Identify(hand, system, index);

        var raw = new VRControllerState_t();

        if (!system.GetControllerState(index, ref raw, StateSize))
            return;

        var pressed = raw.ulButtonPressed;
        var touched = raw.ulButtonTouched;

        var stick = hand.knuckles ? raw.rAxis3 : raw.rAxis0;
        var stickBit = hand.knuckles ? ButtonAxis3 : ButtonAxis0;
        var primaryBit = hand.knuckles ? ButtonGrip : ButtonA;

        state.thumbstick = new Vector2(stick.x, stick.y);
        state.trigger = raw.rAxis1.x;
        state.grip = raw.rAxis2.x;

        hand.trigger = Hysteresis(hand.trigger, state.trigger) || Bit(pressed, ButtonAxis1);
        hand.grip = Hysteresis(hand.grip, state.grip) || (!hand.knuckles && Bit(pressed, ButtonGrip));

        uint buttons = 0;
        if (Bit(pressed, primaryBit)) buttons |= 1u << 0;
        if (Bit(pressed, ButtonApplicationMenu)) buttons |= 1u << 1;
        if (hand.trigger) buttons |= 1u << 2;
        if (hand.grip) buttons |= 1u << 3;
        if (Bit(pressed, stickBit)) buttons |= 1u << 4;
        if (Bit(touched, stickBit)) buttons |= 1u << 5;
        state.buttons = buttons;
    }

    private static void Identify(Hand hand, CVRSystem system, uint index)
    {
        hand.index = index;

        var type = StringProperty(system, index, ETrackedDeviceProperty.Prop_ControllerType_String);
        var model = StringProperty(system, index, ETrackedDeviceProperty.Prop_ModelNumber_String);
        hand.knuckles = type == "knuckles";

        Plugin.Log.LogInfo($"[PeakVR][OpenVR] {hand.role} controller: device {index}, type '{type}', model '{model}'");
    }

    private static string StringProperty(CVRSystem system, uint index, ETrackedDeviceProperty property)
    {
        var error = ETrackedPropertyError.TrackedProp_Success;
        var buffer = new StringBuilder(256);
        system.GetStringTrackedDeviceProperty(index, property, buffer, (uint)buffer.Capacity, ref error);
        return error == ETrackedPropertyError.TrackedProp_Success ? buffer.ToString() : "";
    }

    private static bool Hysteresis(bool wasPressed, float value) =>
        wasPressed ? value > ReleaseThreshold : value >= PressThreshold;

    private static bool Bit(ulong mask, int bit) => (mask & (1UL << bit)) != 0;
}

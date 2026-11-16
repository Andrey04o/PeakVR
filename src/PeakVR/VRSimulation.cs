using System;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.InputSystem.XR;

namespace PeakVR;

[StructLayout(LayoutKind.Explicit, Size = 120)]
internal struct VRSimHmdState : IInputStateTypeInfo
{
    public FourCC format => new FourCC('P', 'V', 'S', 'H');

    [InputControl(name = "trackingState", layout = "Integer")]
    [FieldOffset(0)] public int trackingState;

    [InputControl(name = "isTracked", layout = "Button", bit = 0)]
    [FieldOffset(4)] public uint buttons;

    [InputControl(name = "devicePosition", layout = "Vector3")]
    [FieldOffset(8)] public Vector3 devicePosition;

    [InputControl(name = "deviceRotation", layout = "Quaternion")]
    [FieldOffset(20)] public Quaternion deviceRotation;

    [InputControl(name = "leftEyePosition", layout = "Vector3")]
    [FieldOffset(36)] public Vector3 leftEyePosition;

    [InputControl(name = "leftEyeRotation", layout = "Quaternion")]
    [FieldOffset(48)] public Quaternion leftEyeRotation;

    [InputControl(name = "rightEyePosition", layout = "Vector3")]
    [FieldOffset(64)] public Vector3 rightEyePosition;

    [InputControl(name = "rightEyeRotation", layout = "Quaternion")]
    [FieldOffset(76)] public Quaternion rightEyeRotation;

    [InputControl(name = "centerEyePosition", layout = "Vector3")]
    [FieldOffset(92)] public Vector3 centerEyePosition;

    [InputControl(name = "centerEyeRotation", layout = "Quaternion")]
    [FieldOffset(104)] public Quaternion centerEyeRotation;
}

[StructLayout(LayoutKind.Explicit, Size = 52)]
internal struct VRSimControllerState : IInputStateTypeInfo
{
    public FourCC format => new FourCC('P', 'V', 'S', 'C');

    [InputControl(name = "trackingState", layout = "Integer")]
    [FieldOffset(0)] public int trackingState;

    [InputControl(name = "isTracked", layout = "Button", bit = 0)]
    [InputControl(name = "primaryButton", layout = "Button", bit = 1)]
    [InputControl(name = "secondaryButton", layout = "Button", bit = 2)]
    [InputControl(name = "triggerPressed", layout = "Button", bit = 3)]
    [InputControl(name = "gripPressed", layout = "Button", bit = 4)]
    [InputControl(name = "thumbstickClicked", layout = "Button", bit = 5)]
    [InputControl(name = "primaryTouched", layout = "Button", bit = 6)]
    [InputControl(name = "secondaryTouched", layout = "Button", bit = 7)]
    [InputControl(name = "thumbstickTouched", layout = "Button", bit = 8)]
    [FieldOffset(4)] public uint buttons;

    [InputControl(name = "devicePosition", layout = "Vector3")]
    [FieldOffset(8)] public Vector3 devicePosition;

    [InputControl(name = "deviceRotation", layout = "Quaternion")]
    [FieldOffset(20)] public Quaternion deviceRotation;

    [InputControl(name = "thumbstick", layout = "Vector2")]
    [FieldOffset(36)] public Vector2 thumbstick;

    [InputControl(name = "trigger", layout = "Axis")]
    [FieldOffset(44)] public float trigger;

    [InputControl(name = "grip", layout = "Axis")]
    [FieldOffset(48)] public float grip;
}

[InputControlLayout(stateType = typeof(VRSimHmdState), displayName = "PeakVR Simulated HMD")]
internal class VRSimHmd : XRHMD
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

[InputControlLayout(stateType = typeof(VRSimControllerState), commonUsages = new[] { "LeftHand", "RightHand" },
    displayName = "PeakVR Simulated Controller")]
internal class VRSimController : XRController
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

internal class VRSimRunner : MonoBehaviour
{
    private void Update()
    {
        VRSimulation.AddDevices();
        enabled = false;
    }
}

internal class VRSimHand
{
    public Vector3 Position;
    public Quaternion Rotation = Quaternion.identity;
    public Vector2 Stick;
    public float Trigger;
    public float Grip;
    public bool IsPrimary;
    public bool IsSecondary;
    public bool IsStickClick;
    public bool IsThumbTouch;

    public VRSimHand(Vector3 position)
    {
        Position = position;
    }

    public void Release()
    {
        Stick = Vector2.zero;
        Trigger = 0f;
        Grip = 0f;
        IsPrimary = false;
        IsSecondary = false;
        IsStickClick = false;
        IsThumbTouch = false;
    }
}

internal static class VRSimulation
{
    public const string Argument = "-vr-simulate";
    public const string Variable = "PEAKVR_SIMULATE";

    private const string HmdLayout = "PeakVRSimHMD";
    private const string ControllerLayout = "PeakVRSimController";
    private const float PressThreshold = 0.5f;
    private const float EyeOffset = 0.032f;
    private const int TrackingAll = 3;

    public static readonly Vector3 DefaultHead = new Vector3(0f, 1.65f, 0f);
    public static readonly Vector3 DefaultLeft = new Vector3(-0.2f, 1.1f, 0.3f);
    public static readonly Vector3 DefaultRight = new Vector3(0.2f, 1.1f, 0.3f);

    public static Vector3 HeadPosition = DefaultHead;
    public static Quaternion HeadRotation = Quaternion.identity;
    public static readonly VRSimHand Left = new VRSimHand(DefaultLeft);
    public static readonly VRSimHand Right = new VRSimHand(DefaultRight);

    private static VRSimHmd? hmd;
    private static VRSimController? leftDevice;
    private static VRSimController? rightDevice;

    public static bool Active { get; private set; }

    public static bool IsRequested(string[] args)
    {
        if (args.Contains(Argument, StringComparer.OrdinalIgnoreCase))
            return true;

        return Environment.GetEnvironmentVariable(Variable) == "1";
    }

    public static void Start()
    {
        if (Active)
            return;

        Active = true;

        GameObject runner = new GameObject("PeakVR Sim Input");
        UnityEngine.Object.DontDestroyOnLoad(runner);
        runner.AddComponent<VRSimRunner>();
    }

    public static void AddDevices()
    {
        if (hmd != null)
            return;

        InputSystem.RegisterLayout<VRSimHmd>(HmdLayout);
        InputSystem.RegisterLayout<VRSimController>(ControllerLayout);

        hmd = InputSystem.AddDevice<VRSimHmd>("PeakVR Sim HMD");
        leftDevice = InputSystem.AddDevice<VRSimController>("PeakVR Sim Left");
        rightDevice = InputSystem.AddDevice<VRSimController>("PeakVR Sim Right");
        InputSystem.SetDeviceUsage(leftDevice, UnityEngine.InputSystem.CommonUsages.LeftHand);
        InputSystem.SetDeviceUsage(rightDevice, UnityEngine.InputSystem.CommonUsages.RightHand);

        InputSystem.onBeforeUpdate += Queue;

        Plugin.Log.LogWarning("[PeakVR][Sim] simulated VR is active (no headset, poses come from VRSimulation)");
    }

    public static void ResetPose()
    {
        HeadPosition = DefaultHead;
        HeadRotation = Quaternion.identity;
        Left.Position = DefaultLeft;
        Left.Rotation = Quaternion.identity;
        Right.Position = DefaultRight;
        Right.Rotation = Quaternion.identity;
        Left.Release();
        Right.Release();
    }

    private static void Queue()
    {
        if (!Active || InputState.currentUpdateType == InputUpdateType.BeforeRender)
            return;

        if (hmd != null && hmd.added)
            InputSystem.QueueStateEvent(hmd, BuildHmd());

        if (leftDevice != null && leftDevice.added)
            InputSystem.QueueStateEvent(leftDevice, BuildController(Left));

        if (rightDevice != null && rightDevice.added)
            InputSystem.QueueStateEvent(rightDevice, BuildController(Right));
    }

    private static VRSimHmdState BuildHmd()
    {
        Quaternion rotation = HeadRotation;
        Vector3 side = rotation * Vector3.right * EyeOffset;

        VRSimHmdState state = new VRSimHmdState();
        state.trackingState = TrackingAll;
        state.buttons = 1u;
        state.devicePosition = HeadPosition;
        state.deviceRotation = rotation;
        state.centerEyePosition = HeadPosition;
        state.centerEyeRotation = rotation;
        state.leftEyePosition = HeadPosition - side;
        state.leftEyeRotation = rotation;
        state.rightEyePosition = HeadPosition + side;
        state.rightEyeRotation = rotation;
        return state;
    }

    private static VRSimControllerState BuildController(VRSimHand hand)
    {
        uint buttons = 1u;
        if (hand.IsPrimary) buttons |= 1u << 1;
        if (hand.IsSecondary) buttons |= 1u << 2;
        if (hand.Trigger >= PressThreshold) buttons |= 1u << 3;
        if (hand.Grip >= PressThreshold) buttons |= 1u << 4;
        if (hand.IsStickClick) buttons |= 1u << 5;
        if (hand.IsThumbTouch) buttons |= (1u << 6) | (1u << 8);

        VRSimControllerState state = new VRSimControllerState();
        state.trackingState = TrackingAll;
        state.buttons = buttons;
        state.devicePosition = hand.Position;
        state.deviceRotation = hand.Rotation;
        state.thumbstick = hand.Stick;
        state.trigger = hand.Trigger;
        state.grip = hand.Grip;
        return state;
    }
}

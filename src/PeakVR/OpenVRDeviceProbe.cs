using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.XR;

namespace PeakVR;

internal class OpenVRDeviceProbe : MonoBehaviour
{
    private const float SettleTime = 3f;

    private float elapsed;
    private bool pending = true;
    private static GameObject host;
    private static OpenVRDeviceProbe active;

    public static void Arm()
    {
        if (host != null)
            return;

        host = new GameObject("PeakVR OpenVR Probe");
        Object.DontDestroyOnLoad(host);
        active = host.AddComponent<OpenVRDeviceProbe>();

        InputDevices.deviceConnected += OnConnected;
    }

    private static void OnConnected(InputDevice device)
    {
        Plugin.Log.LogInfo($"[PeakVR][Probe] XR device connected: '{device.name}' " +
            $"manufacturer='{device.manufacturer}' characteristics={device.characteristics}");

        if (active == null)
            return;

        active.elapsed = 0f;
        active.pending = true;
    }

    private const float SampleWindow = 60f;
    private const float SampleEvery = 1f;
    private float sampleElapsed;
    private float sampleTick;

    private void Update()
    {
        Sample();

        if (!pending)
            return;

        elapsed += Time.unscaledDeltaTime;
        if (elapsed < SettleTime)
            return;

        pending = false;
        Report();
    }

    private void Sample()
    {
        sampleElapsed += Time.unscaledDeltaTime;
        if (sampleElapsed > SampleWindow)
            return;

        sampleTick += Time.unscaledDeltaTime;
        if (sampleTick < SampleEvery)
            return;
        sampleTick = 0f;

        foreach (var d in UnityEngine.InputSystem.InputSystem.devices)
        {
            if (d is not UnityEngine.InputSystem.XR.XRController && d is not OpenVRController)
                continue;

            Plugin.Log.LogWarning($"[PeakVR][Probe][Sample t={sampleElapsed:F0}] {d.name} " +
                $"trigger={Read(d, "trigger")} grip={Read(d, "grip")} stick={Read(d, "thumbstick")} " +
                $"primary={Read(d, "primaryButton")} secondary={Read(d, "secondaryButton")} " +
                $"stickClick={Read(d, "thumbstickClicked")}");
        }

        var xr = new List<InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Controller, xr);
        foreach (var d in xr)
        {
            d.TryGetFeatureValue(CommonUsages.trigger, out var trigger);
            d.TryGetFeatureValue(CommonUsages.primary2DAxis, out var axis);
            d.TryGetFeatureValue(CommonUsages.primaryButton, out var primary);
            Plugin.Log.LogWarning($"[PeakVR][Probe][Sample t={sampleElapsed:F0}] XR '{d.name}' " +
                $"trigger={trigger:F2} axis={axis} primary={primary}");
        }
    }

    private static string Read(UnityEngine.InputSystem.InputDevice device, string control)
    {
        return device.TryGetChildControl(control) switch
        {
            null => "missing",
            UnityEngine.InputSystem.Controls.Vector2Control v => v.ReadValue().ToString("F2"),
            UnityEngine.InputSystem.Controls.AxisControl a => a.ReadValue().ToString("F2"),
            var other => other.GetType().Name
        };
    }

    private static void Report()
    {
        var xrDevices = new List<InputDevice>();
        InputDevices.GetDevices(xrDevices);
        Plugin.Log.LogWarning($"[PeakVR][Probe] ===== {xrDevices.Count} XR device(s) =====");

        var usages = new List<InputFeatureUsage>();
        foreach (var d in xrDevices)
        {
            Plugin.Log.LogWarning($"[PeakVR][Probe] '{d.name}' characteristics={d.characteristics}");

            usages.Clear();
            if (!d.TryGetFeatureUsages(usages))
                continue;

            Plugin.Log.LogWarning($"[PeakVR][Probe]   features: " +
                string.Join(", ", usages.Select(u => $"{u.name}:{u.type.Name}")));
        }

        var input = UnityEngine.InputSystem.InputSystem.devices;
        Plugin.Log.LogWarning($"[PeakVR][Probe] ===== {input.Count} Input System device(s) =====");

        foreach (var d in input)
        {
            if (d is not UnityEngine.InputSystem.XR.XRController && d is not UnityEngine.InputSystem.XR.XRHMD)
                continue;

            Plugin.Log.LogWarning($"[PeakVR][Probe] '{d.displayName}' layout='{d.layout}' path='{d.path}' " +
                $"usages=[{string.Join(",", d.usages)}]");
            Plugin.Log.LogWarning($"[PeakVR][Probe]   controls: " +
                string.Join(", ", d.allControls.Select(c => c.name)));
        }

        var bindable = input.Count(d => d is UnityEngine.InputSystem.XR.XRController);
        Plugin.Log.LogWarning($"[PeakVR][Probe] XRController devices bindable: {bindable}");

        ReportActions();
    }

    private static void ReportActions()
    {
        Plugin.Log.LogWarning("[PeakVR][Probe] ===== VRControls action resolution =====");

        Dump("MoveStick", VRControls.MoveStick);
        Dump("TurnStick", VRControls.TurnStick);
        Dump("LeftGrip", VRControls.LeftGrip);
        Dump("RightGrip", VRControls.RightGrip);
        Dump("LeftTrigger", VRControls.LeftTrigger);
        Dump("RightTrigger", VRControls.RightTrigger);
        Dump("LeftPrimary", VRControls.LeftPrimary);
        Dump("RightPrimary", VRControls.RightPrimary);
        Dump("RightSecondary", VRControls.RightSecondary);
        Dump("Pause", VRControls.Pause);
        Dump("Sprint", VRControls.Sprint);
        Dump("Stash", VRControls.Stash);
    }

    private static void Dump(string label, UnityEngine.InputSystem.InputAction action)
    {
        if (action == null)
        {
            Plugin.Log.LogWarning($"[PeakVR][Probe]   {label}: <null>");
            return;
        }

        var bindings = string.Join(" | ", action.bindings.Select(b => b.path));
        var resolved = action.controls.Count == 0
            ? "NONE"
            : string.Join(", ", action.controls.Select(c => c.path));

        Plugin.Log.LogWarning($"[PeakVR][Probe]   {label}: enabled={action.enabled} " +
            $"bindings=[{bindings}] resolved=[{resolved}]");
    }
}

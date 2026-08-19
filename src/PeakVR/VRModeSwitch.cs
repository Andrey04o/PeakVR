using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace PeakVR;

internal static class VRModeSwitch
{
    public static void Toggle()
    {
        if (VRSession.Switching)
            return;

        Report("before");

        if (Plugin.VrEnabled)
            VRSession.Stop();
        else
            VRSession.Start();

        Report("after");
    }

    private static void Report(string when)
    {
        var displays = new List<XRDisplaySubsystem>();
        SubsystemManager.GetInstances(displays);

        var running = 0;
        foreach (var display in displays)
            if (display != null && display.running)
                running++;

        var runtime = LCVR.OpenXR.GetActiveRuntimeName(out var name) ? name : "n/a";

        Plugin.Log.LogWarning($"[PeakVR][Mode] {when}: vrEnabled={Plugin.VrEnabled} " +
            $"displays={displays.Count} running={running} runtime={runtime} " +
            $"xrDevice={XRSettings.loadedDeviceName} xrActive={XRSettings.isDeviceActive}");
    }
}

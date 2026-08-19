using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace PeakVR;

// SPIKE: does OpenXR survive being stopped and started again mid-session? Nothing else is touched -
// no Harmony patches are applied or removed, no rig is rebuilt - so a working toggle here only proves
// the XR lifecycle itself is viable. Flat -> VR is the doubtful direction: the graphics device is
// created before we get control, which is why the preloader runs so early (see the VDXR/Vulkan notes).
internal static class VRModeSwitch
{
    public static void Toggle()
    {
        Report("before");

        if (Plugin.VrEnabled)
            Stop();
        else
            Start();

        Report("after");
    }

    private static void Stop()
    {
        Plugin.Log.LogWarning("[PeakVR][ModeSwitch] stopping XR...");

        if (!LCVR.OpenXR.Loader.ShutdownXR())
        {
            Plugin.Log.LogError("[PeakVR][ModeSwitch] shutdown reported no XR manager - nothing to stop");
            return;
        }

        // Only the flag, not the patches. Most per-frame VR code early-returns on it, which keeps the
        // log readable enough to see what the XR teardown itself did.
        Plugin.SetVrEnabled(false);
        Plugin.Log.LogWarning("[PeakVR][ModeSwitch] XR stopped");
    }

    private static void Start()
    {
        Plugin.Log.LogWarning("[PeakVR][ModeSwitch] starting XR...");

        if (!LCVR.OpenXR.Loader.RestartXR())
        {
            Plugin.Log.LogError("[PeakVR][ModeSwitch] XR failed to start - staying flat");
            return;
        }

        Plugin.SetVrEnabled(true);
        Plugin.Log.LogWarning("[PeakVR][ModeSwitch] XR started");
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

        Plugin.Log.LogWarning($"[PeakVR][ModeSwitch] {when}: vrEnabled={Plugin.VrEnabled} " +
            $"displays={displays.Count} running={running} runtime={runtime} " +
            $"xrDevice={XRSettings.loadedDeviceName} xrActive={XRSettings.isDeviceActive}");
    }
}

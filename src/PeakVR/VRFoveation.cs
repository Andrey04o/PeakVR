using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace PeakVR;

// Fixed foveated rendering via Unity's SRP foveation path (XRDisplaySubsystem.foveatedRenderingLevel).
internal static class VRFoveation
{
    private static bool logged;
    private static readonly List<XRDisplaySubsystem> displays = new();

    public static void Tick()
    {
        if (!Plugin.VrEnabled)
            return;

        float level = Plugin.Config != null ? Plugin.Config.FoveationLevel.Value : 0f;

        SubsystemManager.GetSubsystems(displays);
        foreach (XRDisplaySubsystem d in displays)
        {
            if (d == null || !d.running)
                continue;
            try
            {
                d.foveatedRenderingFlags = XRDisplaySubsystem.FoveatedRenderingFlags.GazeAllowed;
                d.foveatedRenderingLevel = level;
            }
            catch
            {
                // ignore per-frame set failures
            }
        }
    }

    public static void Apply()
    {
        if (!Plugin.VrEnabled)
            return;

        Tick();

        if (logged)
            return;

        try
        {
            UnityEngine.Rendering.FoveatedRenderingCaps caps = SystemInfo.foveatedRenderingCaps;
            float level = Plugin.Config != null ? Plugin.Config.FoveationLevel.Value : 0f;

            int running = 0;
            foreach (XRDisplaySubsystem d in displays)
                if (d != null && d.running)
                    running++;

            logged = true;
            Plugin.Log.LogInfo($"[PeakVR][Foveation] caps={caps} displays={displays.Count} running={running} " +
                               $"level={level}" +
                               (caps == UnityEngine.Rendering.FoveatedRenderingCaps.None
                                   ? " (runtime reports no foveation support — no effect)" : ""));
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"[PeakVR][Foveation] error: {e.Message}");
        }
    }
}

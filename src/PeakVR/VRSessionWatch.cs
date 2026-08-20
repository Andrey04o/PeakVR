using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.XR.OpenXR;

namespace PeakVR;

internal static class VRSessionWatch
{
    private static bool installed;
    private static bool quitting;
    private static bool armed;
    private static string reason;

    private static PropertyInfo restarterRunning;
    private static bool restarterProbed;

    public static void Install()
    {
        if (installed)
            return;

        installed = true;

        try
        {
            OpenXRRuntime.wantsToQuit += VetoQuit;
            Application.quitting += () => quitting = true;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"[PeakVR][XR] could not hook the OpenXR quit request: {e.Message}");
        }
    }

    public static void ReportLoss(string what)
    {
        if (quitting || armed || VRSession.Switching || !Plugin.VrEnabled)
            return;

        armed = true;
        reason = what;

        Plugin.Log.LogWarning($"[PeakVR][XR] {what} — returning to flat mode");
    }

    public static void Tick()
    {
        if (!armed)
            return;

        if (quitting || VRSession.Switching || !Plugin.VrEnabled)
        {
            armed = false;
            return;
        }

        if (RestarterBusy())
            return;

        armed = false;

        if (LCVR.OpenXR.Loader.IsRunning())
        {
            Plugin.Log.LogWarning($"[PeakVR][XR] session recovered after {reason} — staying in VR");
            return;
        }

        VRSession.Stop();
    }

    private static bool VetoQuit()
    {
        if (quitting || !Plugin.VrEnabled)
            return true;

        Plugin.Log.LogWarning("[PeakVR][XR] OpenXR asked the game to quit — vetoed");
        ReportLoss("the runtime asked the game to quit");
        return false;
    }

    private static bool RestarterBusy()
    {
        try
        {
            var host = GameObject.Find("~oxrestarter");
            if (host == null)
                return false;

            if (!restarterProbed)
            {
                restarterProbed = true;
                foreach (var component in host.GetComponents<MonoBehaviour>())
                    if (component != null && component.GetType().Name == "OpenXRRestarter")
                        restarterRunning = component.GetType().GetProperty("isRunning",
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            }

            if (restarterRunning == null)
                return false;

            foreach (var component in host.GetComponents<MonoBehaviour>())
                if (component != null && component.GetType().Name == "OpenXRRestarter")
                    return (bool)restarterRunning.GetValue(component);
        }
        catch
        {
        }

        return false;
    }
}

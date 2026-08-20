using UnityEngine.XR.OpenXR.Features;

namespace PeakVR;

internal class XRSessionMonitor : OpenXRFeature
{
    private static void Log(string msg)
    {
        if (Plugin.Log != null)
            Plugin.Log.LogWarning($"[PeakVR][XR] {msg}");
    }

    private static string StateName(int s) => s switch
    {
        0 => "Unknown",
        1 => "Idle",
        2 => "Ready",
        3 => "Synchronized",
        4 => "Visible",
        5 => "Focused",
        6 => "Stopping",
        7 => "LossPending",
        8 => "Exiting",
        _ => s.ToString()
    };

    public override void OnSessionStateChange(int oldState, int newState)
    {
        Log($"state {StateName(oldState)} -> {StateName(newState)}");

        if (newState != 5)
            return;

        VRAudio.Subscribe();
        VRAudio.WatchSetting();
        VRAudio.ReacquireOutputDevice();
    }

    public override void OnSessionExiting(ulong xrSession) => Log("OnSessionExiting");
    public override void OnSessionLossPending(ulong xrSession) => Log("OnSessionLossPending");
    public override void OnInstanceLossPending(ulong xrInstance) => Log("OnInstanceLossPending");
    public override void OnSessionCreate(ulong xrSession) => Log("OnSessionCreate");
    public override void OnSessionBegin(ulong xrSession) => Log("OnSessionBegin");
    public override void OnSessionEnd(ulong xrSession) => Log("OnSessionEnd");
    public override void OnSessionDestroy(ulong xrSession) => Log("OnSessionDestroy");
    public override void OnSubsystemStop() => Log("OnSubsystemStop");
    public override void OnSubsystemStart()
    {
        Log("OnSubsystemStart");
        XRMirror.Assert();
        XRMirror.AssertBlitMode();
    }
    public override void OnSubsystemDestroy() => Log("OnSubsystemDestroy");
    public override void OnInstanceDestroy(ulong xrInstance) => Log("OnInstanceDestroy");
}

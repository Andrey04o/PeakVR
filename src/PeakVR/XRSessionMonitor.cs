using UnityEngine.XR.OpenXR.Features;

namespace PeakVR;

// Diagnostics for the OpenXR session lifecycle. On VDXR the ending sequence sends the session
// straight to EXITING; Unity then tears the session down and DEADLOCKS in the runtime's native
// teardown, so NO managed recovery can run past it (Unity's own OpenXRRestarter also hangs there,
// and a second ShutdownAndRestart just errors "Only one shutdown or restart can be executed at a
// time"). The only possible code fix is preventing the EXITING event from reaching Unity via a
// native xrPollEvent hook (HookGetInstanceProcAddr) — not attempted here. This monitor only logs.
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

    public override void OnSessionStateChange(int oldState, int newState) => Log($"state {StateName(oldState)} -> {StateName(newState)}");
    public override void OnSessionExiting(ulong xrSession) => Log("OnSessionExiting");
    public override void OnSessionLossPending(ulong xrSession) => Log("OnSessionLossPending");
    public override void OnInstanceLossPending(ulong xrInstance) => Log("OnInstanceLossPending");
    public override void OnSessionCreate(ulong xrSession) => Log("OnSessionCreate");
    public override void OnSessionBegin(ulong xrSession) => Log("OnSessionBegin");
    public override void OnSessionEnd(ulong xrSession) => Log("OnSessionEnd");
    public override void OnSessionDestroy(ulong xrSession) => Log("OnSessionDestroy");
    public override void OnSubsystemStop() => Log("OnSubsystemStop");
    public override void OnSubsystemStart() => Log("OnSubsystemStart");
    public override void OnSubsystemDestroy() => Log("OnSubsystemDestroy");
    public override void OnInstanceDestroy(ulong xrInstance) => Log("OnInstanceDestroy");
}

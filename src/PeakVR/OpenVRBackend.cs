using System.Runtime.InteropServices;
using Unity.XR.OpenVR;
using Valve.VR;

namespace PeakVR;

internal static class OpenVRBackend
{
    public const string DisplaySubsystem = "OpenVR Display";
    public const string InputSubsystem = "OpenVR Input";

    private struct UserDefinedSettings
    {
        public ushort stereoRenderingMode;
        public ushort initializationType;
        public ushort mirrorViewMode;
        [MarshalAs(UnmanagedType.LPStr)] public string editorAppKey;
        [MarshalAs(UnmanagedType.LPStr)] public string actionManifestPath;
        [MarshalAs(UnmanagedType.LPStr)] public string applicationName;
    }

    [DllImport("XRSDKOpenVR", CharSet = CharSet.Auto)]
    private static extern void SetUserDefinedSettings(UserDefinedSettings settings);

    [DllImport("XRSDKOpenVR", CharSet = CharSet.Auto)]
    private static extern EVRInitError GetInitializationResult();

    public const string Choice = "SteamVR (OpenVR)";

    public static bool Preferred =>
        Plugin.Config != null && Plugin.Config.OpenXRRuntime.Value == Choice;

    public static bool Active { get; set; }

    public static bool Installed() => Query(OpenVR.IsRuntimeInstalled);

    public static bool HeadsetPresent() => Query(OpenVR.IsHmdPresent);

    private static bool Query(System.Func<bool> check)
    {
        try
        {
            return check();
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"[PeakVR][OpenVR] openvr_api unavailable: {e.Message}");
            return false;
        }
    }

    public static UnityEngine.Vector3 RotationOffset =>
        Active ? new UnityEngine.Vector3(45f, 0f, 0f) : UnityEngine.Vector3.zero;

    public static void Configure()
    {
        UserDefinedSettings settings = new UserDefinedSettings
        {
            stereoRenderingMode = (ushort)OpenVRSettings.StereoRenderingModes.MultiPass,
            initializationType = (ushort)OpenVRSettings.InitializationTypes.Scene,
            mirrorViewMode = (ushort)OpenVRSettings.MirrorViewModes.Left,
            editorAppKey = null,
            actionManifestPath = null,
            applicationName = "PEAK"
        };

        SetUserDefinedSettings(settings);
        Plugin.Log.LogInfo("[PeakVR][OpenVR] settings pushed (MultiPass, Scene, left-eye mirror)");
    }

    public static string InitError()
    {
        try
        {
            return GetInitializationResult().ToString();
        }
        catch (System.Exception e)
        {
            return $"unavailable ({e.GetType().Name})";
        }
    }
}

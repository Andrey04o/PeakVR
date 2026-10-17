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

    public static bool Selected =>
        Plugin.Config != null && Plugin.Config.VRBackend.Value == "OpenVR";

    public static UnityEngine.Vector3 RotationOffset =>
        Selected ? new UnityEngine.Vector3(45f, 0f, 0f) : UnityEngine.Vector3.zero;

    public static void Configure()
    {
        var settings = new UserDefinedSettings
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

    public static void RegisterLayouts()
    {
        Plugin.Log.LogInfo("[PeakVR][OpenVR] leaving input layouts to the Input System XR layout builder");
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

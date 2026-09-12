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

    public static void Configure()
    {
        var settings = new UserDefinedSettings
        {
            stereoRenderingMode = (ushort)OpenVRSettings.StereoRenderingModes.MultiPass,
            initializationType = (ushort)OpenVRSettings.InitializationTypes.Scene,
            mirrorViewMode = (ushort)OpenVRSettings.MirrorViewModes.None,
            editorAppKey = null,
            actionManifestPath = null,
            applicationName = "PEAK"
        };

        SetUserDefinedSettings(settings);
        Plugin.Log.LogInfo("[PeakVR][OpenVR] settings pushed (MultiPass, Scene, no mirror)");
    }

    public static void RegisterLayouts()
    {
        try
        {
            InputLayoutLoader.RegisterInputLayouts();
            Plugin.Log.LogInfo("[PeakVR][OpenVR] input layouts registered");
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"[PeakVR][OpenVR] could not register input layouts: {e.Message}");
        }
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

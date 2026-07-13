using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using PEAKLib.Core;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Composites;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Interactions;
namespace PeakVR;

// Here are some basic resources on code style and naming conventions to help
// you in your first CSharp plugin!
// https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions
// https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/identifier-names
// https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/names-of-namespaces

// This BepInAutoPlugin attribute comes from the Hamunii.BepInEx.AutoPlugin
// NuGet package, and it will generate the BepInPlugin attribute for you!
// For more info, see https://github.com/Hamunii/BepInEx.AutoPlugin
[BepInAutoPlugin]
[BepInDependency("com.github.PEAKModding.PEAKLib.ModConfig", BepInDependency.DependencyFlags.SoftDependency)]
public partial class Plugin : BaseUnityPlugin
{
    internal static ManualLogSource Log { get; private set; } = null!;
    public new static LCVR.Config Config { get; private set; }
    public static bool VrEnabled { get; private set; } = true;
    private void Awake()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        InputSystem.PerformDefaultPluginInitialization();

        ButtonFallbackComposite.Initialize();
        IntegerFallbackComposite.Initialize();
        QuaternionFallbackComposite.Initialize();
        Vector3FallbackComposite.Initialize();
        SectorInteraction.Initialize();
        
        Log = Logger;
        LCVR.Logger.SetSource(Logger);

        Config = new LCVR.Config(Info.Location, base.Config);
        //Config.DeserializeFromES3();
        //Config.File.SettingChanged += (_, _) => Config.SerializeToES3();

        // BepInEx gives us a logger which we can use to log information.
        // See https://lethal.wiki/dev/fundamentals/logging
        
        var args = Environment.GetCommandLineArgs();
        var disableVr = args.Contains("--disable-vr", StringComparer.OrdinalIgnoreCase);

        VRNetworking.CreateReceiver();

        if (disableVr)
        {
            VrEnabled = false;
            ApplyRemoteOnlyPatches();
            Log.LogWarning("[PeakVR] VR disabled by the '--disable-vr' command line flag — running in flat (non-VR) mode; only remote-VR-render patches applied.");
            Log.LogInfo($"Plugin {Name} is loaded (VR disabled)!");
            return;
        }

        if (!PreloadRuntimeDependencies()) {
            Logger.LogError("Disabling mod because required runtime dependencies could not be loaded!");
            return;
        }

        if (!InitializeVR())
        {
            VrEnabled = false;
            ApplyRemoteOnlyPatches();
            Log.LogWarning("[PeakVR] VR failed to initialize (no headset or OpenXR runtime?) — running in flat (non-VR) mode; only remote-VR-render patches applied.");
            Log.LogInfo($"Plugin {Name} is loaded (VR unavailable)!");
            return;
        }

        PeakAssets.Load();
        XRMirror.Setup();
        VRControls.Init();

        new Harmony(Id).PatchAll(typeof(Plugin).Assembly);
        // BepInEx also gives us a config file for easy configuration.
        // See https://lethal.wiki/dev/intermediate/custom-configs

        // We can apply our hooks here.
        // See https://lethal.wiki/dev/fundamentals/patching-code

        // Log our awake here so we can see it in LogOutput.log file
        Log.LogInfo($"Plugin {Name} is loaded!");
        //Peak.UI.KickButton kickButton;
    }

    private int mirrorFrame;

    private void Update()
    {
        if (!VrEnabled)
            return;

        if (++mirrorFrame % 300 == 0)
            XRMirror.Assert();
    }

    private static void ApplyRemoteOnlyPatches()
    {
        var harmony = new Harmony(Id);
        harmony.CreateClassProcessor(typeof(RemoteIKPatch)).Patch();
        harmony.CreateClassProcessor(typeof(OneHandedHoldPatch)).Patch();
    }

    private bool PreloadRuntimeDependencies()
    {
        try
        {
            var deps = Path.Combine(Path.GetDirectoryName(Info.Location)!, "RuntimeDeps");

            foreach (var file in Directory.GetFiles(deps, "*.dll"))
            {
                var filename = Path.GetFileName(file);

                // Ignore known unmanaged libraries
                if (filename is "UnityOpenXR.dll" or "openxr_loader.dll")
                    continue;

                Logger.LogDebug($"Preloading '{filename}'...");

                try
                {
                    Assembly.LoadFile(file);
                }
                catch (Exception ex)
                {
                    Logger.LogWarning($"Failed to preload '{filename}': {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(
                $"Unexpected error occured while preloading runtime dependencies (incorrect folder structure?): {ex.Message}");
            return false;
        }

        return true;
    }

    private static bool InitializeVR()
    {
        LCVR.Logger.LogInfo("Loading VR...");

        if (!LCVR.OpenXR.Loader.InitializeXR())
        {
            LCVR.Logger.LogError("Failed to start in VR Mode! Only Non-VR features are available!");
            LCVR.Logger.LogWarning("You may ignore the previous error if you are intending to play without VR");

            //Flags |= Flags.StartupFailed;

            return false;
        }

        if (LCVR.OpenXR.GetActiveRuntimeName(out var name) &&
            LCVR.OpenXR.GetActiveRuntimeVersion(out var major, out var minor, out var patch))
            LCVR.Logger.LogInfo($"OpenXR runtime being used: {name} ({major}.{minor}.{patch})");
        else
            LCVR.Logger.LogError("Could not get OpenXR runtime info?");

        return true;
    }
}

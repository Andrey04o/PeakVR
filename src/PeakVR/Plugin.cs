using System;
using System.Collections.Generic;
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
[BepInDependency("com.github.PEAKModding.PEAKLib.UI", BepInDependency.DependencyFlags.SoftDependency)]
public partial class Plugin : BaseUnityPlugin
{
    internal static ManualLogSource Log { get; private set; } = null!;
    public new static LCVR.Config Config { get; private set; }
    public static bool VrEnabled { get; private set; } = true;
    public static bool DebugButtons { get; private set; }
    private void Awake()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        UnityEngine.Application.runInBackground = true;

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
        DebugButtons = args.Contains("-vr-debugbuttons", StringComparer.OrdinalIgnoreCase);

        VRNetworking.CreateReceiver();

        PeakAssets.Load();

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

        VRArmIKPatch.LoadArmScaleFromConfig();
        try
        {
            VRCalibration.Register();
        }
        catch (Exception e)
        {
            Log.LogWarning($"[PeakVR] Calibration menu unavailable (PEAKLib.UI missing?): {e.Message}");
        }
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
        if (DebugButtons && Keyboard.current != null)
            HandleDebugKeys();

        if (!VrEnabled)
            return;

        TryBindAnyKey();

        mirrorFrame++;
        if (mirrorFrame % 300 == 0)
            XRMirror.Assert();
    }

    private bool anyKeyBound;

    private void TryBindAnyKey()
    {
        if (anyKeyBound)
            return;

        var actions = InputSystem.actions;
        if (actions == null)
            return;

        var anyKey = actions.FindAction("AnyKey");
        if (anyKey == null)
            return;

        anyKeyBound = true;

        try
        {
            var wasEnabled = actions.enabled;
            if (wasEnabled)
                actions.Disable();

            anyKey.AddBinding("<XRController>{RightHand}/triggerPressed");
            anyKey.AddBinding("<XRController>{LeftHand}/triggerPressed");
            anyKey.AddBinding("<XRController>{RightHand}/primaryButton");
            anyKey.AddBinding("<XRController>{RightHand}/secondaryButton");

            if (wasEnabled)
                actions.Enable();

            Log.LogInfo("[PeakVR] AnyKey bound to VR buttons (credits skip)");
        }
        catch (Exception e)
        {
            Log.LogWarning($"[PeakVR] AnyKey bind failed: {e.Message}");
        }
    }

    private static void HandleDebugKeys()
    {
        var kb = Keyboard.current;

        if (kb.f1Key.wasPressedThisFrame)
            DumpCanvases();

        if (kb.f4Key.wasPressedThisFrame)
            UIOverlay.SetLogging(!UIOverlay.Logging);

        if (kb.f9Key.wasPressedThisFrame)
        {
            var m = VRStereoCulling.Margin - 0.1f;
            VRStereoCulling.Margin = m < 1f ? 1f : m;
            Log.LogInfo($"[PeakVR] StereoCulling Margin = {VRStereoCulling.Margin:F2}");
        }

        if (kb.f10Key.wasPressedThisFrame)
        {
            VRStereoCulling.Margin += 0.1f;
            Log.LogInfo($"[PeakVR] StereoCulling Margin = {VRStereoCulling.Margin:F2}");
        }

        if (kb.f11Key.wasPressedThisFrame)
        {
            VRStereoCulling.DisableOcclusion = !VRStereoCulling.DisableOcclusion;
            Log.LogInfo($"[PeakVR] Occlusion culling {(VRStereoCulling.DisableOcclusion ? "DISABLED" : "ENABLED")}");
        }

        if (kb.f12Key.wasPressedThisFrame && MainCamera.instance != null)
            RenderDiagnostics.LogLookedAt(MainCamera.instance.cam);
    }

    private static void DumpCanvases()
    {
        Log.LogInfo("[PeakVR][Canvas] ===== active canvases =====");
        foreach (var c in FindObjectsByType<UnityEngine.Canvas>(UnityEngine.FindObjectsSortMode.None))
            Log.LogInfo($"[PeakVR][Canvas] mode={c.renderMode} sort={c.sortingOrder} enabled={c.isActiveAndEnabled} path={CanvasPath(c.transform)}");

        var gui = GUIManager.instance;
        if (gui == null)
            return;

        Log.LogInfo("[PeakVR][HUD] ===== hud elements =====");
        Log.LogInfo($"[PeakVR][HUD] staminaGroup={CanvasPath(gui.staminaCanvasGroup != null ? gui.staminaCanvasGroup.transform : null)}");
        Log.LogInfo($"[PeakVR][HUD] bar={CanvasPath(gui.bar != null ? gui.bar.transform : null)}");
        if (gui.items != null)
            for (var i = 0; i < gui.items.Length; i++)
                Log.LogInfo($"[PeakVR][HUD] item[{i}]={CanvasPath(gui.items[i] != null ? gui.items[i].transform : null)}");
        Log.LogInfo($"[PeakVR][HUD] backpack={CanvasPath(gui.backpack != null ? gui.backpack.transform : null)}");
        Log.LogInfo($"[PeakVR][HUD] temporaryItem={CanvasPath(gui.temporaryItem != null ? gui.temporaryItem.transform : null)}");
    }

    private static string CanvasPath(UnityEngine.Transform t)
    {
        if (t == null)
            return "<null>";

        var path = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }
        return path;
    }

    private static void ApplyRemoteOnlyPatches()
    {
        var harmony = new Harmony(Id);
        harmony.CreateClassProcessor(typeof(RemoteIKPatch)).Patch();
        harmony.CreateClassProcessor(typeof(OneHandedHoldPatch)).Patch();
        harmony.CreateClassProcessor(typeof(AboutButtonPatch)).Patch();
    }

    private bool PreloadRuntimeDependencies()
    {
        try
        {
            var deps = Path.Combine(Path.GetDirectoryName(Info.Location)!, "RuntimeDeps");
            var loaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var versionFolder = GetVersionSpecificDeps(deps);
            if (versionFolder != null)
                PreloadFromFolder(versionFolder, loaded);

            PreloadFromFolder(deps, loaded);
        }
        catch (Exception ex)
        {
            Logger.LogError(
                $"Unexpected error occured while preloading runtime dependencies (incorrect folder structure?): {ex.Message}");
            return false;
        }

        return true;
    }

    private string GetVersionSpecificDeps(string deps)
    {
        var version = UnityEngine.Application.unityVersion;
        var mapped = version.StartsWith("6000.") ? "6." + version.Substring("6000.".Length) : version;

        var folder = Path.Combine(deps, mapped);
        if (Directory.Exists(folder))
        {
            Logger.LogInfo($"Unity {version}: using version-specific RuntimeDeps folder '{mapped}'");
            return folder;
        }

        return null;
    }

    private void PreloadFromFolder(string folder, HashSet<string> loaded)
    {
        foreach (var file in Directory.GetFiles(folder, "*.dll"))
        {
            var filename = Path.GetFileName(file);

            // Ignore known unmanaged libraries
            if (filename is "UnityOpenXR.dll" or "openxr_loader.dll")
                continue;

            // A version-specific DLL loaded first wins over the same-named base DLL
            if (!loaded.Add(filename))
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

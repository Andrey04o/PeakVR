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

[BepInAutoPlugin]
[BepInDependency("com.github.PEAKModding.PEAKLib.ModConfig", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("com.github.PEAKModding.PEAKLib.UI", BepInDependency.DependencyFlags.SoftDependency)]
public partial class Plugin : BaseUnityPlugin
{
    internal static ManualLogSource Log { get; private set; } = null!;
    public new static LCVR.Config Config { get; private set; }
    public static bool VrEnabled { get; private set; } = true;

    internal static void SetVrEnabled(bool value) => VrEnabled = value;
    public static bool DebugButtons { get; private set; }
    private void Awake()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        UnityEngine.Application.runInBackground = true;

        typeof(InputSystem).GetMethod("PerformDefaultPluginInitialization",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.Invoke(null, null);

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

        var args = Environment.GetCommandLineArgs();
        var disableVr = args.Contains("--disable-vr", StringComparer.OrdinalIgnoreCase)
            || !Config.StartInVR.Value;
        DebugButtons = args.Contains("-vr-debugbuttons", StringComparer.OrdinalIgnoreCase);

        VRNetworking.CreateReceiver();
        VRModCanvas.Create();
        VRMenuScroll.Create();
        VRLogBuffer.Start();

        PeakAssets.Load();

        gameObject.AddComponent<VRRemoteBinoculars>();

        if (!PreloadRuntimeDependencies())
        {
            Logger.LogError("Disabling mod because required runtime dependencies could not be loaded!");
            return;
        }

        new Harmony(Id).PatchAll(typeof(Plugin).Assembly);

        UrpDiagnostics.ApplySmallMeshCulling();
        UrpDiagnostics.ApplyGpuOcclusionCulling();

        if (disableVr)
        {
            VrEnabled = false;
            Log.LogWarning("[PeakVR] Starting in flat (non-VR) mode ('--disable-vr' or the 'Start In VR' setting). Switch to VR from the VR Settings page or the mode hotkey.");
        }
        else if (!VRSession.StartAtBoot())
        {
            VrEnabled = false;
            Log.LogWarning("[PeakVR] VR failed to initialize (no headset or OpenXR runtime?) — running in flat (non-VR) mode. Wake the headset and switch to VR from the VR Settings page or the mode hotkey.");
        }

        try
        {
            VRCalibration.Register();
        }
        catch (Exception e)
        {
            Log.LogWarning($"[PeakVR] Calibration menu unavailable (PEAKLib.UI missing?): {e.Message}");
        }

        RegisterSettingsPage();

        Log.LogInfo($"Plugin {Name} is loaded ({(VrEnabled ? "VR" : "flat")} mode)!");
    }

    private int mirrorFrame;

    private void Update()
    {
        if (DebugButtons && Keyboard.current != null)
            HandleDebugKeys();

        HandleModeHotkey();

        VRFrameTiming.Tick();

        if (!VrEnabled)
            return;

        TryBindAnyKey();

        mirrorFrame++;
        if (mirrorFrame % 60 == 0)
        {
            XRMirror.Assert();
            XRMirror.AssertBlitMode();
        }
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

    private static void HandleModeHotkey()
    {
        if (Config == null || !Config.ModeHotkeyEnabled.Value || Keyboard.current == null)
            return;

        if (!Enum.TryParse<UnityEngine.InputSystem.Key>(Config.ModeHotkey.Value.ToString(), out var key))
            return;

        var control = Keyboard.current[key];
        if (control != null && control.wasPressedThisFrame)
            VRModeSwitch.Toggle();
    }

    private static void HandleDebugKeys()
    {
        var kb = Keyboard.current;

        if (kb.tKey.wasPressedThisFrame)
            VRFrameTiming.Toggle();

        if (kb.yKey.wasPressedThisFrame)
            VRFrameTiming.CycleRenderScale();

        if (kb.f1Key.wasPressedThisFrame)
            DumpCanvases();

        if (kb.f2Key.wasPressedThisFrame)
            UrpDiagnostics.Dump();

        if (kb.f3Key.wasPressedThisFrame)
            UrpDiagnostics.CycleTestMode();

        if (kb.lKey.wasPressedThisFrame)
            RenderDiagnostics.Toggle();

        if (kb.kKey.wasPressedThisFrame)
            RenderDiagnostics.ToggleLod0Only();

        if (kb.jKey.wasPressedThisFrame && MainCamera.instance != null)
            RenderDiagnostics.LogByName(MainCamera.instance.cam);

        if (kb.vKey.wasPressedThisFrame && MainCamera.instance != null)
            RenderDiagnostics.LogShaders(MainCamera.instance.cam);

        if (kb.pKey.wasPressedThisFrame)
            UrpDiagnostics.ToggleDepthPriming();

        if (kb.mKey.wasPressedThisFrame)
            RenderDiagnostics.CycleMeshLodThreshold();

        if (kb.nKey.wasPressedThisFrame)
            RenderDiagnostics.ToggleForceMeshLod0();

        if (kb.gKey.wasPressedThisFrame)
            UrpDiagnostics.ToggleGpuResidentDrawer();

        if (kb.hKey.wasPressedThisFrame)
            UrpDiagnostics.ToggleSmallMeshCulling();

        if (kb.f4Key.wasPressedThisFrame)
            UIOverlay.SetLogging(!UIOverlay.Logging);

        if (kb.digit5Key.wasPressedThisFrame && MainCamera.instance != null)
            VRRenderProbe.Probe(MainCamera.instance.cam);

        if (kb.digit6Key.wasPressedThisFrame)
            VRRenderProbe.CycleFix();

        if (kb.digit7Key.wasPressedThisFrame)
            VRModeSwitch.Toggle();

        if (kb.f7Key.wasPressedThisFrame)
            UrpDiagnostics.ToggleEdgeDetection();

        if (kb.f8Key.wasPressedThisFrame && MainCamera.instance != null)
            VRLayerProbe.Toggle(MainCamera.instance.cam);

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

    private static void RegisterSettingsPage()
    {
        try
        {
            VRSettingsPage.Register();
        }
        catch (Exception e)
        {
            Log.LogWarning($"[PeakVR] VR settings menu unavailable (PEAKLib.UI missing?): {e.Message}");
        }
    }

    private bool PreloadRuntimeDependencies()
    {
        try
        {
            var deps = Path.Combine(Path.GetDirectoryName(Info.Location)!, "RuntimeDeps");
            PreloadFromFolder(deps);
        }
        catch (Exception ex)
        {
            Logger.LogError(
                $"Unexpected error occured while preloading runtime dependencies (incorrect folder structure?): {ex.Message}");
            return false;
        }

        return true;
    }

    private void PreloadFromFolder(string folder)
    {
        foreach (var file in Directory.GetFiles(folder, "*.dll"))
        {
            var filename = Path.GetFileName(file);

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

}

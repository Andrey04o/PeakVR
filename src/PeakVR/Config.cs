using BepInEx.Configuration;

namespace LCVR;

public class Config
{
    public string AssemblyPath { get; }
    public ConfigFile File { get; }

    public ConfigEntry<string> OpenXRRuntime { get; }
    public ConfigEntry<bool> EnableVerboseLogging { get; }
    public ConfigEntry<bool> ReacquireAudioDevice { get; }
    public ConfigEntry<bool> ModForegroundUI { get; }
    public ConfigEntry<bool> ModUIOnLeftHand { get; }

    public ConfigEntry<bool> SmoothTurn { get; }
    public ConfigEntry<float> SnapTurnAngle { get; }
    public ConfigEntry<float> SmoothTurnSpeed { get; }

    public ConfigEntry<bool> MovementTunneling { get; }
    public ConfigEntry<float> TunnelingStrength { get; }

    public ConfigEntry<bool> CameraRoll { get; }
    public ConfigEntry<bool> CopyHeadRotation { get; }

    public ConfigEntry<bool> HideControllers { get; }
    public ConfigEntry<PeakVR.LineVisibility> InteractionLine { get; }
    public ConfigEntry<PeakVR.LineVisibility> HudLine { get; }
    public ConfigEntry<bool> AimAtObjectCenter { get; }

    public ConfigEntry<float> ArmSpanScale { get; }
    public ConfigEntry<float> VirtualArmSpan { get; }

    public ConfigEntry<float> LodBias { get; }
    public ConfigEntry<string> SharpenImage { get; }
    public ConfigEntry<bool> ForceDisableHBAO { get; }
    public ConfigEntry<float> FoveationLevel { get; }
    public ConfigEntry<bool> FixPerEyeCulling { get; }
    public ConfigEntry<bool> GpuOcclusionCulling { get; }
    public ConfigEntry<float> FoliageDistance { get; }
    public ConfigEntry<bool> DepthPrepass { get; }
    public ConfigEntry<bool> ForegroundUI { get; }
    public ConfigEntry<bool> ForegroundUIOverGeometry { get; }
    public ConfigEntry<float> FarPlane { get; }
    public ConfigEntry<float> ControllerOffsetX { get; }
    public ConfigEntry<float> ControllerOffsetY { get; }
    public ConfigEntry<float> ControllerOffsetZ { get; }

    public ConfigEntry<string> OpenXRRuntimeFile { get; }

    public Config(string assemblyPath, ConfigFile file)
    {
        AssemblyPath = assemblyPath;
        File = file;

        OpenXRRuntime = file.Bind("VR", "OpenXR Runtime", "System Default",
            new ConfigDescription(
                "The OpenXR runtime (headset software) that VR launches with. Restart the game to apply.",
                new AcceptableValueList<string>(OpenXR.GetRuntimeChoices().ToArray())));

        EnableVerboseLogging = file.Bind("VR", "Verbose Logging", false,
            "Enables verbose debug logging during OpenXR initialization.");

        ModForegroundUI = file.Bind("VR", "Other Mods UI In VR", true,
            "Pull flat screen-space UI created by other mods into VR as a head-locked foreground panel. "
            + "Turn off to leave it alone (it will then be invisible in the headset).");

        ModUIOnLeftHand = file.Bind("VR", "sPEAKer UI On Left Hand", true,
            "Move the sPEAKer music HUD (timer, current song, mixtape name and icon) onto the left wrist "
            + "instead of leaving it head-locked. Requires the sPEAKer mod.");

        ReacquireAudioDevice = file.Bind("VR", "Reacquire Audio Device", false,
            "Troubleshooting only. Re-initialises Unity's audio output when the headset session becomes focused so "
            + "the game re-reads the current Windows default playback device. Only try this if you get NO sound in "
            + "the headset - the re-initialise can itself silence audio that was working. Does NOT choose a device: "
            + "set the headset as the Windows default output for that.");

        SmoothTurn = file.Bind("Comfort", "Smooth Turn", false,
            "Turn smoothly with the right stick instead of snapping by a fixed angle.");
        SnapTurnAngle = file.Bind("Comfort", "Snap Turn Angle", 45f,
            new ConfigDescription("Degrees rotated per snap turn.",
                new AcceptableValueRange<float>(15f, 90f)));
        SmoothTurnSpeed = file.Bind("Comfort", "Smooth Turn Speed", 120f,
            new ConfigDescription("Smooth turn speed in degrees per second.",
                new AcceptableValueRange<float>(45f, 240f)));

        MovementTunneling = file.Bind("Comfort", "Movement Tunneling", true,
            "Close a tunnel around your view while moving or falling to reduce motion sickness.");
        TunnelingStrength = file.Bind("Comfort", "Tunneling Strength", 0.7f,
            new ConfigDescription("How far the tunnel closes in (smaller circle). 0 disables it.",
                new AcceptableValueRange<float>(0f, 1f)));

        CameraRoll = file.Bind("Comfort", "Camera Roll", false,
            "Roll and tumble the headset view along with the character camera during backflips, falls " +
            "and ragdolls, matching the flat game. More immersive but can cause motion sickness.");

        CopyHeadRotation = file.Bind("Special", "Copy Head Rotation", false,
            "Copy the character's head-bone tilt (the subtle head lean/bob during movement) onto the view. " +
            "Can be applied to flat players too. Caution: turn it on only if you don't get motion sickness from VR.");

        HideControllers = file.Bind("VR", "Hide Controllers", true,
            "Hide the VR controller models and show only your character's hands. Interaction/aim lasers " +
            "then originate from your hand instead of the controller.");

        InteractionLine = file.Bind("Lasers", "Interaction Line", PeakVR.LineVisibility.OnInteract,
            "When to show the right-hand interaction line: Always, only when something is interactable " +
            "(OnInteract), or never (Hidden).");
        HudLine = file.Bind("Lasers", "HUD Pointer Line", PeakVR.LineVisibility.OnInteract,
            "When to show the left-controller HUD selection line: Always, only when pointing at a HUD " +
            "item (OnInteract), or Hidden.");
        AimAtObjectCenter = file.Bind("Lasers", "Aim At Object Center", true,
            "When an interactable is available, snap the interaction line's end to the object's center " +
            "instead of pointing straight ahead.");

        ArmSpanScale = file.Bind("VR", "Arm Span Scale", 1.089f,
            new ConfigDescription("Multiplier from your real arm reach to the character's arm reach. " +
                "Calibrate it in-game with the T-pose window instead of editing by hand.",
                new AcceptableValueRange<float>(0.6f, 1.8f)));
        VirtualArmSpan = file.Bind("Internal", "VirtualArmSpan", 1.851f,
            new ConfigDescription("FOR INTERNAL USE ONLY, DO NOT EDIT — cached character wingspan for calibration.",
                null, "Hidden"));

        ControllerOffsetX = file.Bind("VR", "Controller Rotation Offset X", 0f,
            new ConfigDescription(
                "Tilt of the controllers up or down, in degrees. Both the hands and the aim follow it, " +
                "so raise it if you hold the controllers angled and the aim points too low.",
                new AcceptableValueRange<float>(-90f, 90f)));
        ControllerOffsetX.SettingChanged += (_, _) => PeakVR.VRHands.ApplyRotationOffset();

        ControllerOffsetY = file.Bind("VR", "Controller Rotation Offset Y", 0f,
            new ConfigDescription("Turn of the controllers left or right, in degrees.",
                new AcceptableValueRange<float>(-90f, 90f)));
        ControllerOffsetY.SettingChanged += (_, _) => PeakVR.VRHands.ApplyRotationOffset();

        ControllerOffsetZ = file.Bind("VR", "Controller Rotation Offset Z", 0f,
            new ConfigDescription("Roll of the controllers, in degrees.",
                new AcceptableValueRange<float>(-90f, 90f)));
        ControllerOffsetZ.SettingChanged += (_, _) => PeakVR.VRHands.ApplyRotationOffset();

        LodBias = file.Bind("VR Graphics", "LOD Bias", 2.5f,
            new ConfigDescription("Level-of-detail bias in VR. Higher keeps distant objects detailed; " +
                "lower boosts performance. Applies immediately.",
                new AcceptableValueRange<float>(0.5f, 5f)));
        LodBias.SettingChanged += (_, _) => UnityEngine.QualitySettings.lodBias = LodBias.Value;

        SharpenImage = file.Bind("VR Graphics", "Make Image Sharper", "Enable",
            new ConfigDescription(
                "Sharpen the VR image: disables temporal anti-aliasing (TAA) and MSAA and uses a spatial " +
                "(Linear) upscaler. TAA in particular blurs the image in a VR headset. Disable to keep the " +
                "game's own graphics settings.",
                new AcceptableValueList<string>("Enable", "Disable")));
        SharpenImage.SettingChanged += (_, _) => PeakVR.VRRender.ApplySharpening();

        ForceDisableHBAO = file.Bind("VR Graphics", "Force Disable HBAO", true,
            new ConfigDescription(
                "Disable HBAO ambient occlusion, which renders wrong per-eye in VR on PEAK's Unity 6.3 " +
                "(URP 17.3) and costs performance. On by default; turn off to keep the game's ambient occlusion."));
        ForceDisableHBAO.SettingChanged += (_, _) => PeakVR.VRRender.ApplyHBAO();

        FarPlane = file.Bind("VR Graphics", "Far Plane Culling Distance", 3000f,
            new ConfigDescription(
                "How far away the game still draws things, in metres. Lower values cull distant scenery " +
                "and gain performance; too low and far islands or mountain tops pop out of view.",
                new AcceptableValueRange<float>(500f, 20000f)));
        FarPlane.SettingChanged += (_, _) => PeakVR.VRRender.ApplyFarPlane();

        DepthPrepass = file.Bind("VR Graphics", "Depth Prepass", true,
            new ConfigDescription(
                "Draw scene depth first so hidden pixels are skipped instead of shaded. Grass and leaves " +
                "are alpha-clipped, which normally defeats that optimisation, and they are the most " +
                "expensive thing on screen in VR - this can nearly double the framerate with no visual " +
                "change. Takes effect on restart."));

        ForegroundUI = file.Bind("VR Graphics", "Foreground UI", true,
            new ConfigDescription(
                "Draw the VR interface in its own render pass after everything else, so world glass, " +
                "rain and fog can never cover the wrist HUD, menus or the stamina bar. Replaces the old " +
                "render-order workaround, which could not win against transparent scenery."));
        ForegroundUI.SettingChanged += (_, _) => PeakVR.ForegroundUI.Apply();

        ForegroundUIOverGeometry = file.Bind("VR Graphics", "Foreground UI Over Geometry", true,
            new ConfigDescription(
                "Let the foreground interface draw over solid objects too, instead of being hidden " +
                "behind walls and terrain. Only applies when Foreground UI is on."));
        ForegroundUIOverGeometry.SettingChanged += (_, _) => PeakVR.ForegroundUI.ApplyDepthMode();

        FoliageDistance = file.Bind("VR Graphics", "Foliage Draw Distance", 0f,
            new ConfigDescription(
                "Hide grass, leaves and bushes further away than this many metres. Foliage is the most " +
                "expensive thing on screen in VR - hundreds of separate alpha-clipped draws, each one " +
                "rendered twice, once per eye. 0 disables the limit and draws everything.",
                new AcceptableValueRange<float>(0f, 300f)));

        FixPerEyeCulling = file.Bind("VR Graphics", "Fix Per-Eye Object Culling", true,
            new ConfigDescription(
                "URP's GPU Resident Drawer culls small meshes per view, so in VR an object can vanish in " +
                "one eye only (coconuts, lights, elevator pings). Turning this on stops that cull and costs " +
                "no measurable performance. Changing it mid-game can freeze the desktop mirror until restart."));
        FixPerEyeCulling.SettingChanged += (_, _) => PeakVR.UrpDiagnostics.ApplySmallMeshCulling();

        GpuOcclusionCulling = file.Bind("VR Graphics", "GPU Occlusion Culling", false,
            new ConfigDescription(
                "Let the GPU skip objects hidden behind other objects, using the previous frame's depth. " +
                "Measured no framerate gain in PEAK, which is limited by the processor rather than the " +
                "graphics card, so leave it off unless you want to experiment. Takes effect on restart."));

        FoveationLevel = file.Bind("VR Graphics", "Foveated Rendering", 0f,
            new ConfigDescription(
                "Render the periphery at lower detail to save GPU. Seems like it don't work." + 
                "0 = off, 1 = strongest.",
                new AcceptableValueRange<float>(0f, 1f)));
        FoveationLevel.SettingChanged += (_, _) => PeakVR.VRFoveation.Apply();

        OpenXRRuntimeFile = file.Bind("Internal", "OpenXRRuntimeFile", "",
            new ConfigDescription("FOR INTERNAL USE ONLY, DO NOT EDIT", null, "Hidden"));

        OpenXRRuntimeFile.Value = OpenXR.ResolveRuntimePath(OpenXRRuntime.Value);
        OpenXRRuntime.SettingChanged += (_, _) =>
            OpenXRRuntimeFile.Value = OpenXR.ResolveRuntimePath(OpenXRRuntime.Value);

        ConfigVersion = file.Bind("Internal", "ConfigVersion", 0,
            new ConfigDescription("FOR INTERNAL USE ONLY, DO NOT EDIT", null, "Hidden"));

        Migrate(file);
    }

    private const int CurrentConfigVersion = 1;

    public ConfigEntry<int> ConfigVersion { get; }

    private void Migrate(ConfigFile file)
    {
        var from = ConfigVersion.Value;
        if (from >= CurrentConfigVersion)
            return;

        if (from < 1 && SharpenImage.Value != "Enable")
        {
            SharpenImage.Value = "Enable";
            PeakVR.Plugin.Log.LogInfo("[PeakVR] Config update: turned on \"Make Image Sharper\" (it is now on by default)");
        }

        ConfigVersion.Value = CurrentConfigVersion;
        file.Save();

        PeakVR.Plugin.Log.LogInfo($"[PeakVR] Config migrated {from} -> {CurrentConfigVersion}");
    }
}

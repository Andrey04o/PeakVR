using BepInEx.Configuration;

namespace LCVR;

public class Config
{
    public string AssemblyPath { get; }
    public ConfigFile File { get; }

    public ConfigEntry<string> OpenXRRuntime { get; }
    public ConfigEntry<bool> EnableVerboseLogging { get; }

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

        LodBias = file.Bind("VR Graphics", "LOD Bias", 2.5f,
            new ConfigDescription("Level-of-detail bias in VR. Higher keeps distant objects detailed; " +
                "lower boosts performance. Applies immediately.",
                new AcceptableValueRange<float>(0.5f, 5f)));
        LodBias.SettingChanged += (_, _) => UnityEngine.QualitySettings.lodBias = LodBias.Value;

        SharpenImage = file.Bind("VR Graphics", "Make Image Sharper", "Disable",
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

        OpenXRRuntimeFile = file.Bind("Internal", "OpenXRRuntimeFile", "",
            new ConfigDescription("FOR INTERNAL USE ONLY, DO NOT EDIT", null, "Hidden"));

        OpenXRRuntimeFile.Value = OpenXR.ResolveRuntimePath(OpenXRRuntime.Value);
        OpenXRRuntime.SettingChanged += (_, _) =>
            OpenXRRuntimeFile.Value = OpenXR.ResolveRuntimePath(OpenXRRuntime.Value);
    }
}

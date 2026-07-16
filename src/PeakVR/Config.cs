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

    public ConfigEntry<bool> HideControllers { get; }
    public ConfigEntry<PeakVR.LineVisibility> InteractionLine { get; }
    public ConfigEntry<PeakVR.LineVisibility> HudLine { get; }
    public ConfigEntry<bool> AimAtObjectCenter { get; }

    public ConfigEntry<bool> RecoverOpenXR { get; }

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

        HideControllers = file.Bind("VR", "Hide Controllers", true,
            "Hide the VR controller models and show only your character's hands. Interaction/aim lasers " +
            "then originate from your hand instead of the controller.");

        InteractionLine = file.Bind("VR", "Interaction Line", PeakVR.LineVisibility.OnInteract,
            "When to show the right-hand interaction line: Always, only when something is interactable " +
            "(OnInteract), or never (Hidden).");
        HudLine = file.Bind("VR", "HUD Pointer Line", PeakVR.LineVisibility.OnInteract,
            "When to show the left-controller HUD selection line: Always, only when pointing at a HUD " +
            "item (OnInteract), or Hidden.");
        AimAtObjectCenter = file.Bind("VR", "Aim At Object Center", true,
            "When an interactable is available, snap the interaction line's end to the object's center " +
            "instead of pointing straight ahead.");

        RecoverOpenXR = file.Bind("VR", "Recover OpenXR", false,
            "Experimental: attempt to restart the OpenXR runtime when the headset session drops " +
            "(e.g. after the ending, or when returning to the Airport/menu on some runtimes like VDXR). " +
            "Leave off unless you get an OpenXR shutdown/freeze.");

        OpenXRRuntimeFile = file.Bind("Internal", "OpenXRRuntimeFile", "",
            new ConfigDescription("FOR INTERNAL USE ONLY, DO NOT EDIT", null, "Hidden"));

        OpenXRRuntimeFile.Value = OpenXR.ResolveRuntimePath(OpenXRRuntime.Value);
        OpenXRRuntime.SettingChanged += (_, _) =>
            OpenXRRuntimeFile.Value = OpenXR.ResolveRuntimePath(OpenXRRuntime.Value);
    }
}

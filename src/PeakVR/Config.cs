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

    public ConfigEntry<bool> MovementVignette { get; }
    public ConfigEntry<float> VignetteStrength { get; }

    public ConfigEntry<string> OpenXRRuntimeFile { get; }

    public Config(string assemblyPath, ConfigFile file)
    {
        AssemblyPath = assemblyPath;
        File = file;

        OpenXRRuntime = file.Bind("VR", "OpenXRRuntime", "System Default",
            new ConfigDescription(
                "The OpenXR runtime (headset software) that VR launches with. Restart the game to apply.",
                new AcceptableValueList<string>(OpenXR.GetRuntimeChoices().ToArray())));

        EnableVerboseLogging = file.Bind("VR", "VerboseLogging", false,
            "Enables verbose debug logging during OpenXR initialization.");

        SmoothTurn = file.Bind("Comfort", "SmoothTurn", false,
            "Turn smoothly with the right stick instead of snapping by a fixed angle.");
        SnapTurnAngle = file.Bind("Comfort", "SnapTurnAngle", 45f,
            new ConfigDescription("Degrees rotated per snap turn.",
                new AcceptableValueRange<float>(15f, 90f)));
        SmoothTurnSpeed = file.Bind("Comfort", "SmoothTurnSpeed", 120f,
            new ConfigDescription("Smooth turn speed in degrees per second.",
                new AcceptableValueRange<float>(45f, 240f)));

        MovementVignette = file.Bind("Comfort", "MovementVignette", true,
            "Darken the edges of your view while moving to reduce motion sickness.");
        VignetteStrength = file.Bind("Comfort", "VignetteStrength", 1f,
            new ConfigDescription("How dark the movement vignette gets.",
                new AcceptableValueRange<float>(0f, 1f)));

        OpenXRRuntimeFile = file.Bind("Internal", "OpenXRRuntimeFile", "",
            new ConfigDescription("FOR INTERNAL USE ONLY, DO NOT EDIT", null, "Hidden"));

        OpenXRRuntimeFile.Value = OpenXR.ResolveRuntimePath(OpenXRRuntime.Value);
        OpenXRRuntime.SettingChanged += (_, _) =>
            OpenXRRuntimeFile.Value = OpenXR.ResolveRuntimePath(OpenXRRuntime.Value);
    }
}

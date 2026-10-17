using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using Mono.Cecil;

namespace LCVR.Preload;

public static class Preload
{
    public static IEnumerable<string> TargetDLLs { get; } = [];

    private const string VR_MANIFEST = """
                                       {
                                         "name": "OpenXR XR Plugin",
                                         "version": "1.8.2",
                                         "libraryName": "UnityOpenXR",
                                         "displays": [
                                           {
                                             "id": "OpenXR Display"
                                           }
                                         ],
                                         "inputs": [
                                           {
                                             "id": "OpenXR Input"
                                           }
                                         ]
                                       }
                                       """;
    
    private const string OPENVR_MANIFEST = """
                                          {
                                            "name": "XRSDKOpenVR",
                                            "version": "1.0.0-preview.1",
                                            "libraryName": "XRSDKOpenVR",
                                            "displays": [
                                              {
                                                "id": "OpenVR Display"
                                              }
                                            ],
                                            "inputs": [
                                              {
                                                "id": "OpenVR Input",
                                                "disablesLegacyInput": true,
                                                "disablesLegacyVr": true
                                              }
                                            ]
                                          }
                                          """;

    private static readonly ManualLogSource Logger = BepInEx.Logging.Logger.CreateLogSource("LCVR.Preload");
    
    public static void Initialize()
    {
        Logger.LogInfo("Setting up VR runtime assets");
        
        SetupRuntimeAssets();
        
        Logger.LogInfo("We're done here. Goodbye!");
    }
    
    /// <summary>
    /// Place required runtime libraries and configuration in the game files to allow VR to be started
    /// </summary>
    private static void SetupRuntimeAssets()
    {
        var root = Path.Combine(Paths.GameRootPath, "PEAK_Data");
        var subsystems = Path.Combine(root, "UnitySubsystems");
        if (!Directory.Exists(subsystems))
            Directory.CreateDirectory(subsystems);

        var openXr = Path.Combine(subsystems, "UnityOpenXR");
        if (!Directory.Exists(openXr))
            Directory.CreateDirectory(openXr);

        var manifest = Path.Combine(openXr, "UnitySubsystemsManifest.json");
        if (!File.Exists(manifest))
            File.WriteAllText(manifest, VR_MANIFEST);

        var plugins = Path.Combine(root, "Plugins");
        var oxrPluginTarget = Path.Combine(plugins, "UnityOpenXR.dll");
        var oxrLoaderTarget = Path.Combine(plugins, "openxr_loader.dll");

        var current = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var oxrPlugin = Path.Combine(current, "RuntimeDeps/UnityOpenXR.dll");
        var oxrLoader = Path.Combine(current, "RuntimeDeps/openxr_loader.dll");

        if (!CopyResourceFile(oxrPlugin, oxrPluginTarget))
            Logger.LogWarning("Could not find plugin UnityOpenXR.dll, VR might not work!");

        if (!CopyResourceFile(oxrLoader, oxrLoaderTarget))
            Logger.LogWarning("Could not find plugin openxr_loader.dll, VR might not work!");

        SetupOpenVrAssets(subsystems, plugins, current);
    }

    private static void SetupOpenVrAssets(string subsystems, string plugins, string current)
    {
        var openVr = Path.Combine(subsystems, "XRSDKOpenVR");
        if (!Directory.Exists(openVr))
            Directory.CreateDirectory(openVr);

        var manifest = Path.Combine(openVr, "UnitySubsystemsManifest.json");
        if (!File.Exists(manifest) || File.ReadAllText(manifest) != OPENVR_MANIFEST)
        {
            File.WriteAllText(manifest, OPENVR_MANIFEST);
            Logger.LogInfo("Wrote the OpenVR subsystem manifest");
        }

        if (!CopyResourceFile(Path.Combine(current, "RuntimeDeps/XRSDKOpenVR.dll"),
                Path.Combine(plugins, "XRSDKOpenVR.dll")))
            Logger.LogWarning("Could not find plugin XRSDKOpenVR.dll, the OpenVR backend will not work!");

        if (!CopyResourceFile(Path.Combine(current, "RuntimeDeps/openvr_api.dll"),
                Path.Combine(plugins, "openvr_api.dll")))
            Logger.LogWarning("Could not find plugin openvr_api.dll, the OpenVR backend will not work!");
    }
    
    /// <summary>
    /// Helper function for SetupRuntimeAssets() to copy resource files and return false if the source does not exist
    /// </summary>
    private static bool CopyResourceFile(string sourceFile, string destinationFile)
    {
        if (!File.Exists(sourceFile))
            return false;

        if (File.Exists(destinationFile))
        {
            var sourceHash = Utils.ComputeHash(File.ReadAllBytes(sourceFile));
            var destHash = Utils.ComputeHash(File.ReadAllBytes(destinationFile));

            if (sourceHash.SequenceEqual(destHash))
                return true;
        }

        File.Copy(sourceFile, destinationFile, true);

        return true;
    }

    public static void Patch(AssemblyDefinition assembly)
    {
        // No-op
    }
}
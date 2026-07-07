using System.IO;
using LCVR.Input;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LCVR.Assets;

public static class AssetManager
{
    private static AssetBundle assetsBundle;
    private static AssetBundle scenesBundle;

    public static GameObject Interactable;
    public static GameObject Keyboard;
    public static GameObject VolumeManager;
    public static GameObject SpectatorLight;
    public static GameObject SpectatorGhost;
    public static GameObject SteeringWheelPoints;
    public static GameObject PopupText;
    public static GameObject SpectatingMenu;
    public static GameObject Reticle;
    
    public static GameObject InitMenuEnvironment;
    public static GameObject MainMenuEnvironment;
    public static GameObject PauseMenuEnvironment;

    public static GameObject SettingsPanel;
    
    public static Material SplashMaterial;
    public static Material DefaultRayMat;

    public static Shader TMPAlwaysOnTop;
    public static Shader VignettePostProcess;
    
    public static InputActionAsset VRActions;
    public static InputActionAsset DefaultXRActions;
    public static InputActionAsset NullActions;

    //public static RemappableControls RemappableControls;

    public static Sprite GithubImage;
    public static Sprite KofiImage;
    public static Sprite DiscordImage;
    public static Sprite WarningImage;
    public static Sprite SprintImage;

    public static AudioClip DoorLocked;
    internal static bool LoadAssets()
    {
        assetsBundle =
            AssetBundle.LoadFromFile(Path.Combine(Path.GetDirectoryName(PeakVR.Plugin.Config.AssemblyPath)!,
                "lethalcompanyvr"));
        scenesBundle = AssetBundle.LoadFromFile(Path.Combine(Path.GetDirectoryName(PeakVR.Plugin.Config.AssemblyPath)!,
            "lethalcompanyvr-levels"));

        if (assetsBundle == null || scenesBundle == null)
        {
            Logger.LogError("Failed to load asset bundle!");
            return false;
        }

        DefaultXRActions = assetsBundle.LoadAsset<InputActionAsset>("DefaultXRActions");

        return true;
    }
}

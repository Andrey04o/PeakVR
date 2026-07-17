using System.IO;
using UnityEngine;

namespace PeakVR;

internal static class PeakAssets
{
    private static AssetBundle bundle;

    public static Sprite Reticle { get; private set; }
    public static GameObject Controller { get; private set; }
    public static Texture2D Vignette { get; private set; }
    public static Shader MirrorView { get; private set; }

    public static Sprite EmoteButton { get; private set; }
    public static Sprite Logo { get; private set; }
    public static Sprite AboutButton { get; private set; }
    public static Sprite TPose { get; private set; }

    public static void Load()
    {
        if (bundle != null)
            return;

        var path = Path.Combine(Path.GetDirectoryName(Plugin.Config.AssemblyPath)!, "peakvr");
        bundle = AssetBundle.LoadFromFile(path);

        if (bundle == null)
        {
            Plugin.Log.LogError($"[PeakVR] Failed to load asset bundle at {path}");
            return;
        }

        Reticle = bundle.LoadAsset<Sprite>("reticlevr");
        Controller = bundle.LoadAsset<GameObject>("UniversalControllerWithAnchors")
            ?? bundle.LoadAsset<GameObject>("UniversalController");
        Vignette = bundle.LoadAsset<Texture2D>("vignette");
        MirrorView = bundle.LoadAsset<Shader>("XRMirrorView");

        EmoteButton = bundle.LoadAsset<Sprite>("ButtonEmote");
        Logo = bundle.LoadAsset<Sprite>("Logo");
        AboutButton = bundle.LoadAsset<Sprite>("SmallVRButton");
        TPose = bundle.LoadAsset<Sprite>("TPoseWhite");

        Plugin.Log.LogInfo($"[PeakVR] Bundle loaded (reticle={Reticle != null}, controller={Controller != null}, vignette={Vignette != null}, mirror={MirrorView != null})");
        Plugin.Log.LogInfo($"[PeakVR] Sprites (emote={EmoteButton != null}, logo={Logo != null}, about={AboutButton != null}, tpose={TPose != null})");
    }
}

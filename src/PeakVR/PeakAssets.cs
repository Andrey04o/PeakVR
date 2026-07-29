using System.IO;
using TMPro;
using UnityEngine;

namespace PeakVR;

internal static class PeakAssets
{
    public const string QuestFontName = "kenney_input_meta_quest SDF";

    private static AssetBundle bundle;

    public static Sprite Reticle { get; private set; }
    public static GameObject Controller { get; private set; }
    public static Texture2D Vignette { get; private set; }
    public static Shader MirrorView { get; private set; }

    public static Sprite EmoteButton { get; private set; }
    public static Sprite Logo { get; private set; }
    public static Sprite AboutButton { get; private set; }
    public static Sprite TPose { get; private set; }

    // Kenney Meta Quest button font (glyphs in the Private Use Area) used for the VR input prompts.
    public static TMP_FontAsset QuestFont { get; private set; }

    // Authored placement for the binoculars: a prefab with a "Scope" child (quad offset/rotation/size relative to the item) and a "Grip" child (item offset/rotation relative to the controller).
    public static GameObject BinocularsRig { get; private set; }

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

        BinocularsRig = bundle.LoadAsset<GameObject>("BinocularsRig");

        LoadQuestFont();

        Plugin.Log.LogInfo($"[PeakVR] Bundle loaded (reticle={Reticle != null}, controller={Controller != null}, vignette={Vignette != null}, mirror={MirrorView != null})");
        Plugin.Log.LogInfo($"[PeakVR] Sprites (emote={EmoteButton != null}, logo={Logo != null}, about={AboutButton != null}, tpose={TPose != null})");
        Plugin.Log.LogInfo($"[PeakVR] Binoculars rig prefab: {(BinocularsRig != null ? "loaded" : "not in bundle, using built-in offsets")}");
    }

    // TMP resolves a <font="name"> tag by looking in Resources, which a bundled asset is not in, so
    // register it with the MaterialReferenceManager instead — that's what the tag lookup checks first.
    private static void LoadQuestFont()
    {
        QuestFont = bundle.LoadAsset<TMP_FontAsset>(QuestFontName);
        if (QuestFont == null)
        {
            Plugin.Log.LogWarning($"[PeakVR] '{QuestFontName}' not in the bundle — input prompts fall back to text labels");
            return;
        }

        try
        {
            MaterialReferenceManager.AddFontAsset(QuestFont);
            Plugin.Log.LogInfo($"[PeakVR] Quest button font registered ('{QuestFont.name}')");
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"[PeakVR] Could not register the Quest button font: {e.Message}");
            QuestFont = null;
        }
    }
}

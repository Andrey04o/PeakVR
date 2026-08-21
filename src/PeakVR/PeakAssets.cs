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
    public static Shader SimpleSmoke { get; private set; }
    public static Shader SimpleSmoke2 { get; private set; }

    public static Sprite EmoteButton { get; private set; }
    public static Sprite ChatButton { get; private set; }
    public static Sprite Logo { get; private set; }
    public static Sprite AboutButton { get; private set; }
    public static Sprite TPose { get; private set; }

    // Kenney Meta Quest button font (glyphs in the Private Use Area) used for the VR input prompts.
    public static TMP_FontAsset QuestFont { get; private set; }

    // Authored placement for the binoculars: a prefab with a "Scope" child (quad offset/rotation/size relative to the item) and a "Grip" child (item offset/rotation relative to the controller).
    public static GameObject BinocularsRig { get; private set; }

    public static GameObject Keyboard { get; private set; }

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
        SimpleSmoke = bundle.LoadAsset<Shader>("SimpleSmoke");
        SimpleSmoke2 = bundle.LoadAsset<Shader>("SimpleSmoke2");

        EmoteButton = bundle.LoadAsset<Sprite>("ButtonEmote");
        ChatButton = bundle.LoadAsset<Sprite>("ButtonChat");
        Logo = bundle.LoadAsset<Sprite>("Logo");
        AboutButton = bundle.LoadAsset<Sprite>("SmallVRButton");
        TPose = bundle.LoadAsset<Sprite>("TPoseWhite");

        BinocularsRig = bundle.LoadAsset<GameObject>("BinocularsRig");
        Keyboard = bundle.LoadAsset<GameObject>("VRKeyboard");

        LoadQuestFont();

        Plugin.Log.LogInfo($"[PeakVR] Bundle loaded (reticle={Reticle != null}, controller={Controller != null}, vignette={Vignette != null}, mirror={MirrorView != null})");
        Plugin.Log.LogInfo($"[PeakVR] Sprites (emote={EmoteButton != null}, chat={ChatButton != null}, logo={Logo != null}, about={AboutButton != null}, tpose={TPose != null})");
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
            UIOverlay.SetZTestAlways(FontMaterial(QuestFont));
            Plugin.Log.LogInfo($"[PeakVR] Quest button font registered ('{QuestFont.name}')");
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"[PeakVR] Could not register the Quest button font: {e.Message}");
            QuestFont = null;
        }
    }

    // TMP_Asset.material is a public FIELD in the TextMeshPro 3.0.6 package we compile against and a
    // PROPERTY in the one PEAK ships, so touching it directly throws MissingFieldException at runtime.
    private static Material FontMaterial(TMP_FontAsset font)
    {
        if (font == null)
            return null;

        var type = font.GetType();

        var property = HarmonyLib.AccessTools.Property(type, "material");
        if (property != null)
            return property.GetValue(font) as Material;

        var field = HarmonyLib.AccessTools.Field(type, "material")
            ?? HarmonyLib.AccessTools.Field(type, "m_Material");
        if (field != null)
            return field.GetValue(font) as Material;

        Plugin.Log.LogWarning("[PeakVR] No 'material' member on TMP_FontAsset; button glyphs keep default depth testing");
        return null;
    }
}

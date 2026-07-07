using System.IO;
using UnityEngine;

namespace PeakVR;

internal static class PeakAssets
{
    private static AssetBundle bundle;

    public static Sprite Reticle { get; private set; }
    public static GameObject Controller { get; private set; }

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
        Controller = bundle.LoadAsset<GameObject>("UniversalController");

        Plugin.Log.LogInfo($"[PeakVR] Bundle loaded (reticle={Reticle != null}, controller={Controller != null})");
    }
}

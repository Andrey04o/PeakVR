using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace PeakVR;

[HarmonyPatch(typeof(Mirror), "Start")]
internal static class MirrorPatch
{
    private static readonly Dictionary<Mirror, int> Original = new();

    [HarmonyPostfix]
    private static void Postfix(Mirror __instance)
    {
        if (__instance == null || __instance.mirrorCamera == null)
            return;

        Original[__instance] = __instance.mirrorCamera.cullingMask;
        Apply();
    }

    public static void Apply()
    {
        var ui = LayerMask.NameToLayer("UI");
        if (ui < 0)
            return;

        var mask = 1 << ui;
        var stale = new List<Mirror>();

        foreach (var pair in Original)
        {
            var mirror = pair.Key;

            if (mirror == null || mirror.mirrorCamera == null)
            {
                stale.Add(mirror);
                continue;
            }

            mirror.mirrorCamera.cullingMask = Plugin.VrEnabled
                ? pair.Value & ~mask
                : pair.Value;
        }

        foreach (var mirror in stale)
            Original.Remove(mirror);
    }
}

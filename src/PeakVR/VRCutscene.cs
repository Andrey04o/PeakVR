using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace PeakVR;

internal static class VRCutscene
{
    public static bool Active;
    public static RenderTexture Sink;

    private static readonly List<GameObject> Roots = new();
    private static readonly List<Camera> Cameras = new();
    private static Transform lastKnown;
    private static int rootMask;

    public static void Begin(PeakHandler handler)
    {
        Roots.Clear();
        Cameras.Clear();
        lastKnown = null;
        rootMask = -1;

        foreach (var field in typeof(PeakHandler).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            if (field.FieldType != typeof(GameObject)
                || field.Name.IndexOf("cutscene", StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            if (field.GetValue(handler) is GameObject root && root != null)
                Roots.Add(root);
        }

        Active = Roots.Count > 0;

        var names = new string[Roots.Count];
        for (var i = 0; i < Roots.Count; i++)
            names[i] = Roots[i].name;

        Plugin.Log.LogInfo($"[PeakVR] Cutscene: watching {Roots.Count} root(s) [{string.Join(", ", names)}]");
    }

    public static Transform CurrentTransform()
    {
        if (!Active || Roots.Count == 0)
            return null;

        var mask = 0;
        for (var i = 0; i < Roots.Count && i < 31; i++)
            if (Roots[i] != null && Roots[i].activeInHierarchy)
                mask |= 1 << i;

        if (mask != rootMask)
        {
            rootMask = mask;
            Rescan();
        }

        foreach (var c in Cameras)
        {
            if (c == null || !c.isActiveAndEnabled)
                continue;

            lastKnown = c.transform;
            return lastKnown;
        }

        return lastKnown;
    }

    private static void Rescan()
    {
        Cameras.Clear();

        foreach (var root in Roots)
        {
            if (root == null)
                continue;

            foreach (var c in root.GetComponentsInChildren<Camera>(true))
            {
                Cameras.Add(c);
                Neuter(c);
            }
        }

        if (Cameras.Count == 0)
            return;

        Camera active = null;
        foreach (var c in Cameras)
        {
            if (c == null || !c.isActiveAndEnabled)
                continue;

            active = c;
            break;
        }

        if (lastKnown == null)
            lastKnown = (active != null ? active : Cameras[0]).transform;

        if (MainCamera.instance != null)
            MainCamera.instance.cam.cullingMask = (active != null ? active : Cameras[0]).cullingMask;

        Plugin.Log.LogInfo($"[PeakVR] Cutscene: {Cameras.Count} camera(s), active='{(active != null ? active.name : "none yet")}'");
    }

    private static void Neuter(Camera c)
    {
        if (c == null || c.targetTexture == Sink)
            return;

        if (Sink == null)
        {
            Sink = new RenderTexture(1, 1, 24, RenderTextureFormat.Default) { name = "PeakVR CutsceneSink" };
            Sink.Create();
        }

        c.stereoTargetEye = StereoTargetEyeMask.None;
        c.targetTexture = Sink;
    }
}

[HarmonyPatch(typeof(PeakHandler), "PrepareEndCutscene")]
internal static class PrepareEndCutsceneVRPatch
{
    [HarmonyPostfix]
    private static void Postfix(PeakHandler __instance)
    {
        if (!Plugin.VrEnabled)
            return;

        if (MainCamera.instance != null)
            MainCamera.instance.gameObject.SetActive(true);

        VRCutscene.Begin(__instance);
    }
}

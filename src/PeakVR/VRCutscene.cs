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
    private static readonly Dictionary<int, int> lastEnabled = new();
    private static Transform lastKnown;
    private static int rootMask;

    public static void Begin(PeakHandler handler)
    {
        Roots.Clear();
        Cameras.Clear();
        logged.Clear();
        lastEnabled.Clear();
        lastKnown = null;
        rootMask = -1;
        repairs = 0;

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

        EnforceSink();

        Camera pick = null;
        var newest = -1;

        foreach (var c in Cameras)
        {
            if (c == null || !c.gameObject.activeInHierarchy)
                continue;

            var id = c.GetInstanceID();

            if (c.enabled)
            {
                if (!lastEnabled.ContainsKey(id))
                    Plugin.Log.LogInfo($"[PeakVR] Cutscene: suppressing camera '{c.name}' parent='{(c.transform.parent != null ? c.transform.parent.name : "none")}'");

                lastEnabled[id] = Time.frameCount;
                c.enabled = false;
            }

            var seen = lastEnabled.TryGetValue(id, out var frame) ? frame : -1;
            if (seen > newest)
            {
                newest = seen;
                pick = c;
            }
        }

        if (pick != null)
            lastKnown = pick.transform;

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
            if (c == null || !c.gameObject.activeInHierarchy)
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

    private static Camera[] buffer = new Camera[16];
    private static readonly HashSet<int> logged = new();
    private static int repairs;

    private static void EnforceSink()
    {
        var ours = MainCamera.instance != null ? MainCamera.instance.cam : null;
        if (ours == null)
            return;

        KeepOursPresenting(ours);

        var count = Camera.allCamerasCount;
        if (buffer.Length < count)
            buffer = new Camera[count];

        Camera.GetAllCameras(buffer);

        for (var i = 0; i < count; i++)
        {
            var c = buffer[i];
            if (c == null || c == ours || c.targetTexture != null)
                continue;

            if (logged.Add(c.GetInstanceID()))
                Plugin.Log.LogInfo($"[PeakVR] Cutscene: sinking '{c.name}' parent='{(c.transform.parent != null ? c.transform.parent.name : "none")}' depth={c.depth} eye={c.stereoTargetEye}");

            Neuter(c);
        }
    }

    private static void KeepOursPresenting(Camera ours)
    {
        var repaired = string.Empty;

        if (!ours.gameObject.activeSelf)
        {
            ours.gameObject.SetActive(true);
            repaired += " gameObject";
        }

        if (!ours.enabled)
        {
            ours.enabled = true;
            repaired += " enabled";
        }

        if (ours.targetTexture != null)
        {
            ours.targetTexture = null;
            repaired += " targetTexture";
        }

        if (ours.stereoTargetEye != StereoTargetEyeMask.Both)
        {
            ours.stereoTargetEye = StereoTargetEyeMask.Both;
            repaired += " stereoTargetEye";
        }

        if (repaired.Length > 0 && repairs++ < 10)
            Plugin.Log.LogWarning($"[PeakVR] Cutscene: VR camera was taken away, restored:{repaired}");
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

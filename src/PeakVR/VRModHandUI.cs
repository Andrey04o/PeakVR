using System.Collections.Generic;
using UnityEngine;

namespace PeakVR;

internal static class VRModHandUI
{
    private const float Scale = 0.0006f;
    private static readonly Vector3 HudOffset = new(0f, 150f, 0f);
    private static readonly Vector3 HandPos = new(0f, 0.03f, -0.06f);
    private static readonly Vector3 HandEuler = new(0f, 90f, 90f);

    private static readonly HashSet<string> HandCanvases = new()
    {
        "sPEAKerCanvas",
        "sPEAKerToastCanvas",
    };

    private static Canvas canvas;
    private static readonly List<RectTransform> Pending = new();

    public static bool Claim(Canvas source)
    {
        if (source == null || !HandCanvases.Contains(source.name))
            return false;
        if (Plugin.Config == null || !Plugin.Config.ModUIOnLeftHand.Value)
            return false;

        Pending.Clear();

        foreach (Transform child in source.transform)
            if (child is RectTransform rt)
                Pending.Add(rt);

        if (Pending.Count == 0)
            return false;

        if (!EnsureCanvas())
            return false;

        foreach (var child in Pending)
            child.SetParent(canvas.transform, false);

        UIOverlay.MakeAlwaysVisible(canvas, UIOverlay.HandQueue);
        VRLayers.HideFromMirror(canvas.gameObject, VRControllerHud.HudLayer, 7);

        Plugin.Log.LogInfo($"[PeakVR] Moved {Pending.Count} element(s) from '{source.name}' to the left-hand UI");
        Pending.Clear();
        return true;
    }

    public static void Follow()
    {
        if (canvas == null)
            return;

        var hud = VRControllerHud.LeftHud;
        var hand = VRHands.Left;

        canvas.gameObject.SetActive(hud != null || hand != null);

        if (canvas.worldCamera == null && MainCamera.instance != null)
            canvas.worldCamera = MainCamera.instance.cam;

        var t = canvas.transform;

        if (hud != null)
            t.SetPositionAndRotation(hud.TransformPoint(HudOffset), hud.rotation);
        else if (hand != null)
            t.SetPositionAndRotation(hand.TransformPoint(HandPos), hand.rotation * Quaternion.Euler(HandEuler));

        t.localScale = Vector3.one * Scale;

        if (MainCamera.instance == null)
            return;

        var camPos = MainCamera.instance.cam.transform.position;
        var facing = Vector3.Dot(t.forward, camPos - t.position) < 0f;
        if (canvas.enabled != facing)
            canvas.enabled = facing;
    }

    private static bool EnsureCanvas()
    {
        if (canvas != null)
            return true;

        var cam = MainCamera.instance != null ? MainCamera.instance.cam : null;
        if (cam == null)
            return false;

        var go = new GameObject("PeakVR ModHandUI");
        Object.DontDestroyOnLoad(go);

        canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = cam;
        canvas.sortingOrder = 3000;

        var rt = (RectTransform)canvas.transform;
        rt.sizeDelta = new Vector2(1200f, 220f);
        rt.localScale = Vector3.one * Scale;

        Plugin.Log.LogInfo("[PeakVR] Left-hand mod UI canvas created");
        return true;
    }
}

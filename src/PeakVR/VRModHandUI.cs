using System.Collections.Generic;
using UnityEngine;

namespace PeakVR;

internal static class VRModHandUI
{
    private const float Scale = 0.0006f;
    private static readonly Vector3 HudOffset = new(0f, 25f, 0f);
    private static readonly Vector3 HandPos = new(0f, 0.03f, -0.06f);
    private static readonly Vector3 HandEuler = new(0f, 90f, 90f);

    private const string ChatCanvas = "TextChatCanvas";
    private const float ChatBase = 70f;
    private const float ChatGap = 50f;

    private static readonly HashSet<string> HandCanvases = new()
    {
        "sPEAKerCanvas",
        "sPEAKerToastCanvas",
    };

    private static Canvas canvas;
    private static readonly List<RectTransform> Pending = new();
    private static readonly List<RectTransform> ChatPanels = new();
    private static readonly VRRestore HandRestore = new();
    private static readonly VRRestore ChatRestore = new();
    private static float chatY = float.NaN;

    public static bool Claim(Canvas source)
    {
        if (source == null)
            return false;

        if (source.name == ChatCanvas)
            return ClaimChat(source);

        if (!HandCanvases.Contains(source.name))
            return false;
        if (Plugin.Config == null || !Plugin.Config.ModUIOnLeftHand.Value)
            return false;

        if (!Collect(source) || !EnsureCanvas())
            return false;

        foreach (var child in Pending)
            HandRestore.Record(child);

        foreach (var child in Pending)
            child.SetParent(canvas.transform, false);

        UIOverlay.MakeAlwaysVisible(canvas, UIOverlay.HandQueue);
        VRLayers.HideFromMirror(canvas.gameObject, VRControllerHud.HudLayer, 7);

        Plugin.Log.LogInfo($"[PeakVR] Moved {Pending.Count} element(s) from '{source.name}' to the left-hand UI");
        Pending.Clear();
        return true;
    }

    private static bool ClaimChat(Canvas source)
    {
        var wrist = VRControllerHud.LeftHudCanvas;
        if (wrist == null || !Collect(source))
            return false;

        ChatPanels.Clear();
        chatY = float.NaN;

        foreach (var child in Pending)
            ChatRestore.Record(child);

        foreach (var child in Pending)
        {
            child.SetParent(wrist.transform, false);
            child.anchorMin = child.anchorMax = child.pivot = new Vector2(0.5f, 0.5f);
            child.localScale = Vector3.one;
            child.localRotation = Quaternion.identity;
            ChatPanels.Add(child);
        }

        ApplyChatHeight();
        PeakTextChatPatch.Claimed = true;

        UIOverlay.MakeAlwaysVisible(wrist, UIOverlay.HandQueue);
        VRLayers.HideFromMirror(wrist.gameObject, VRControllerHud.HudLayer, 7);

        Plugin.Log.LogInfo($"[PeakVR] Moved {Pending.Count} element(s) from '{source.name}' to the left wrist HUD");
        Pending.Clear();
        return true;
    }

    private static void ReleaseIfDisabled()
    {
        if (HandRestore.Count == 0 || Plugin.Config == null || Plugin.Config.ModUIOnLeftHand.Value)
            return;

        var restored = HandRestore.RestoreAll();
        DestroyCanvas();
        Plugin.Log.LogInfo($"[PeakVR] Returned {restored} element(s) to their own canvas (left-hand mod UI turned off)");
    }

    public static void ReleaseAll()
    {
        var restored = HandRestore.RestoreAll() + ChatRestore.RestoreAll();
        DestroyCanvas();

        ChatPanels.Clear();
        chatY = float.NaN;
        PeakTextChatPatch.Claimed = false;

        if (restored > 0)
            Plugin.Log.LogInfo($"[PeakVR] Returned {restored} element(s) to their own canvas");
    }

    private static void DestroyCanvas()
    {
        if (canvas != null)
            Object.Destroy(canvas.gameObject);
        canvas = null;
    }

    private static void ApplyChatHeight()
    {
        if (ChatPanels.Count == 0)
            return;

        var y = HudOffset.y + ChatBase + (canvas != null ? ChatGap : 0f);
        if (Mathf.Approximately(y, chatY))
            return;

        chatY = y;

        for (var i = ChatPanels.Count - 1; i >= 0; i--)
        {
            var panel = ChatPanels[i];
            if (panel == null)
            {
                ChatPanels.RemoveAt(i);
                continue;
            }

            panel.anchoredPosition = new Vector2(0f, y);

            var local = panel.localPosition;
            panel.localPosition = new Vector3(local.x, local.y, 0f);
        }
    }

    private static bool Collect(Canvas source)
    {
        Pending.Clear();

        foreach (Transform child in source.transform)
            if (child is RectTransform rt)
                Pending.Add(rt);

        return Pending.Count > 0;
    }

    public static void Follow()
    {
        ReleaseIfDisabled();
        ApplyChatHeight();

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

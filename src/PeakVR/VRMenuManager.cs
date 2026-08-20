using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace PeakVR;

[DefaultExecutionOrder(1100)]
internal class VRMenuManager : MonoBehaviour
{
    private const float Scale = 0.0022f;
    private const float Distance = 2.5f;
    private const float KioskForward = 0.5f;
    private const float KioskUp = 0.25f;

    private static readonly FieldInfo PauseMenuField =
        typeof(GUIManager).GetField("pauseMenu", BindingFlags.NonPublic | BindingFlags.Instance);

    private const int LayerSweepInterval = 30;

    private static readonly Dictionary<Canvas, VRCanvasState> Converted = new();

    private Canvas current;
    private bool currentForeground;
    private int frame;

    private void Update()
    {
        if (!Plugin.VrEnabled)
            return;

        var gui = GUIManager.instance;
        var hud = gui != null ? gui.hudCanvas : null;

        var menuCanvas = FindActiveCanvas(out var menuForeground);
        var wheelCanvas = GetWheelCanvas(gui);

        var pointerTarget = menuCanvas != null ? menuCanvas : wheelCanvas;
        var convertTarget = menuCanvas != null
            ? menuCanvas
            : (wheelCanvas != null && wheelCanvas != hud ? wheelCanvas : null);

        if (convertTarget != current)
        {
            current = convertTarget;
            currentForeground = convertTarget != null && convertTarget == menuCanvas && menuForeground;

            if (current != null)
                ConvertToWorld(current);
        }

        if (current != null)
        {
            if (currentForeground)
                UIOverlay.MakeAlwaysVisible(current, UIOverlay.PopupQueue);
            else
                UIOverlay.MakeAlwaysVisible(current, false);

            if (++frame % LayerSweepInterval == 0)
                UIOverlay.SweepForegroundLayer(current);

            var lc = Character.localCharacter;
            if (lc != null && lc.data.fullyPassedOut)
                PlaceInFront(current);
        }

        if (pointerTarget != null)
        {
            EnsureEventSystem();
            VRPointer.Canvas = pointerTarget;
            VRPointer.Raycaster = EnsureRaycaster(pointerTarget);
            VRHands.SetPointersActive(true);
        }
        else
        {
            VRPointer.Canvas = null;
            VRPointer.Raycaster = null;
            VRHands.SetPointersActive(false);
        }
    }

    private void OnDestroy() => RestoreAll();

    public static void RestoreAll()
    {
        var restored = 0;

        foreach (var pair in Converted)
        {
            var canvas = pair.Key;
            if (canvas == null)
                continue;

            pair.Value.Apply(canvas);
            VRPointer.Detach(canvas);
            restored++;
        }

        Converted.Clear();

        if (restored > 0)
            Plugin.Log.LogInfo($"[PeakVR] {restored} in-game menu canvas(es) returned to the screen");
    }

    private static Canvas FindActiveCanvas(out bool foreground)
    {
        foreground = false;

        var shown = CanvasOf(MenuWindowShowPatch.Current());
        if (shown != null)
        {
            foreground = true;
            return shown;
        }

        Canvas pauseCanvas = null;

        if (GUIManager.InPauseMenu && PauseMenuField != null && GUIManager.instance != null)
        {
            var pauseMenu = PauseMenuField.GetValue(GUIManager.instance) as GameObject;
            if (pauseMenu != null && pauseMenu.activeInHierarchy)
                pauseCanvas = pauseMenu.GetComponentInParent<Canvas>()?.rootCanvas;
        }

        for (int i = MenuWindow.AllActiveWindows.Count - 1; i >= 0; i--)
        {
            var w = MenuWindow.AllActiveWindows[i];
            if (w == null || !w.isOpen || w.panel == null)
                continue;

            var c = CanvasOf(w);

            if (c == null || c == pauseCanvas)
                continue;

            foreground = pauseCanvas != null;
            return c;
        }

        if (pauseCanvas != null)
        {
            foreground = true;
            return pauseCanvas;
        }

        return null;
    }

    private static Canvas CanvasOf(MenuWindow w)
    {
        if (w == null || w.panel == null)
            return null;

        var c = w.panel.GetComponentInParent<Canvas>();
        if (c == null)
            c = w.panel.GetComponentInChildren<Canvas>(true);

        return c != null ? c.rootCanvas : null;
    }

    private static GameObject GetWheelObject(GUIManager gui)
    {
        if (gui == null || !gui.wheelActive)
            return null;

        if (gui.backpackWheel != null && gui.backpackWheel.gameObject.activeInHierarchy)
            return gui.backpackWheel.gameObject;
        if (gui.emoteWheel != null && gui.emoteWheel.activeInHierarchy)
            return gui.emoteWheel;
        return null;
    }

    private static Canvas GetWheelCanvas(GUIManager gui)
    {
        var wob = GetWheelObject(gui);
        if (wob == null)
            return null;

        var c = wob.GetComponentInParent<Canvas>();
        return c != null ? c.rootCanvas : null;
    }

    private void ConvertToWorld(Canvas canvas)
    {
        var cam = MainCamera.instance != null ? MainCamera.instance.cam : Camera.main;
        if (cam == null)
            return;

        var first = !Converted.ContainsKey(canvas);
        if (first)
            Converted[canvas] = VRCanvasState.Capture(canvas);

        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = cam;

        var kiosk = GetKioskFor(canvas);
        if (kiosk != null)
            PlaceAtKiosk(canvas, kiosk);
        else
            PlaceInFront(canvas);

        if (first)
            Plugin.Log.LogInfo($"[PeakVR] Menu -> world space: {canvas.name}");
    }

    private static Transform GetKioskFor(Canvas canvas)
    {
        var gui = GUIManager.instance;
        if (gui == null || gui.boardingPass == null || !gui.boardingPass.isOpen || gui.boardingPass.kiosk == null)
            return null;

        var panel = gui.boardingPass.panel;
        var bpCanvas = panel != null ? panel.GetComponentInParent<Canvas>() : null;
        if (bpCanvas != null)
            bpCanvas = bpCanvas.rootCanvas;

        return bpCanvas == canvas ? gui.boardingPass.kiosk.transform : null;
    }

    private static void PlaceAtKiosk(Canvas canvas, Transform kiosk)
    {
        var rt = (RectTransform)canvas.transform;
        rt.localScale = Vector3.one * Scale;

        var dir = kiosk.right;
        dir.y = 0f;
        dir = dir.sqrMagnitude < 0.001f ? kiosk.right : dir.normalized;

        var cam = MainCamera.instance != null ? MainCamera.instance.cam : Camera.main;
        if (cam != null)
        {
            var toPlayer = cam.transform.position - kiosk.position;
            toPlayer.y = 0f;
            if (Vector3.Dot(dir, toPlayer) < 0f)
                dir = -dir;
        }

        rt.position = kiosk.position + dir * KioskForward + Vector3.up * KioskUp;
        rt.rotation = Quaternion.LookRotation(-dir, Vector3.up);
    }

    private static void PlaceInFront(Canvas canvas)
    {
        var cam = MainCamera.instance != null ? MainCamera.instance.cam : Camera.main;
        if (cam == null)
            return;

        var head = cam.transform;
        var fwd = head.forward;
        fwd.y = 0f;
        fwd = fwd.sqrMagnitude < 0.001f ? head.forward : fwd.normalized;

        var rt = (RectTransform)canvas.transform;
        rt.localScale = Vector3.one * Scale;
        rt.position = head.position + fwd * Distance;
        rt.rotation = Quaternion.LookRotation(fwd, Vector3.up);
    }

    private static TrackedDeviceGraphicRaycaster EnsureRaycaster(Canvas canvas)
    {
        return VRPointer.Attach(canvas);
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current == null)
            new GameObject("PeakVR EventSystem").AddComponent<EventSystem>();
    }
}

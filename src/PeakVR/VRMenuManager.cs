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

    private Canvas converted;
    private RenderMode savedMode;
    private bool convertedForeground;

    private void Update()
    {
        var gui = GUIManager.instance;
        var hud = gui != null ? gui.hudCanvas : null;

        var menuCanvas = FindActiveCanvas(out var menuIsPause);
        var wheelCanvas = GetWheelCanvas(gui);

        var pointerTarget = menuCanvas != null ? menuCanvas : wheelCanvas;
        var convertTarget = menuCanvas != null
            ? menuCanvas
            : (wheelCanvas != null && wheelCanvas != hud ? wheelCanvas : null);

        if (convertTarget != converted)
        {
            if (converted != null)
                converted.renderMode = savedMode;

            converted = convertTarget;
            convertedForeground = convertTarget != null && convertTarget == menuCanvas && menuIsPause;

            if (converted != null)
                ConvertToWorld(converted);
        }

        if (converted != null)
        {
            UIOverlay.MakeAlwaysVisible(converted, convertedForeground);

            var lc = Character.localCharacter;
            if (lc != null && lc.data.fullyPassedOut)
                PlaceInFront(converted);
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

    private static Canvas FindActiveCanvas(out bool isPauseMenu)
    {
        isPauseMenu = false;

        if (GUIManager.InPauseMenu && PauseMenuField != null && GUIManager.instance != null)
        {
            var pauseMenu = PauseMenuField.GetValue(GUIManager.instance) as GameObject;
            if (pauseMenu != null && pauseMenu.activeInHierarchy)
            {
                var c = pauseMenu.GetComponentInParent<Canvas>();
                if (c != null)
                {
                    isPauseMenu = true;
                    return c.rootCanvas;
                }
            }
        }

        for (int i = MenuWindow.AllActiveWindows.Count - 1; i >= 0; i--)
        {
            var w = MenuWindow.AllActiveWindows[i];
            if (w == null || !w.isOpen || w.panel == null)
                continue;

            var c = w.panel.GetComponentInParent<Canvas>();
            if (c == null)
                c = w.panel.GetComponentInChildren<Canvas>(true);

            if (c != null)
                return c.rootCanvas;

            Plugin.Log.LogWarning($"[PeakVR] {w.GetType().Name} open but no Canvas found");
        }

        return null;
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

        savedMode = canvas.renderMode;
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = cam;

        var kiosk = GetKioskFor(canvas);
        if (kiosk != null)
            PlaceAtKiosk(canvas, kiosk);
        else
            PlaceInFront(canvas);

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

        // The kiosk's screen faces along its RIGHT axis (its forward points straight up), so use that
        // horizontal axis — the panel stands upright, aligned to the kiosk, on the approach side.
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
        var r = canvas.GetComponent<TrackedDeviceGraphicRaycaster>();
        if (r == null)
            r = canvas.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
        return r;
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current == null)
            new GameObject("PeakVR EventSystem").AddComponent<EventSystem>();
    }
}

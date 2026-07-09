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

    private static readonly FieldInfo PauseMenuField =
        typeof(GUIManager).GetField("pauseMenu", BindingFlags.NonPublic | BindingFlags.Instance);

    private Canvas converted;
    private RenderMode savedMode;

    private void Update()
    {
        var gui = GUIManager.instance;
        var hud = gui != null ? gui.hudCanvas : null;

        var menuCanvas = FindActiveCanvas();
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

            if (converted != null)
                ConvertToWorld(converted);
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

    private static Canvas FindActiveCanvas()
    {
        if (GUIManager.InPauseMenu && PauseMenuField != null && GUIManager.instance != null)
        {
            var pauseMenu = PauseMenuField.GetValue(GUIManager.instance) as GameObject;
            if (pauseMenu != null && pauseMenu.activeInHierarchy)
            {
                var c = pauseMenu.GetComponentInParent<Canvas>();
                if (c != null)
                    return c.rootCanvas;
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

    private static Canvas GetWheelCanvas(GUIManager gui)
    {
        if (gui == null || !gui.wheelActive)
            return null;

        GameObject wob = null;
        if (gui.backpackWheel != null && gui.backpackWheel.gameObject.activeInHierarchy)
            wob = gui.backpackWheel.gameObject;
        else if (gui.emoteWheel != null && gui.emoteWheel.activeInHierarchy)
            wob = gui.emoteWheel;

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

        var head = cam.transform;
        var fwd = head.forward;
        fwd.y = 0f;
        fwd = fwd.sqrMagnitude < 0.001f ? head.forward : fwd.normalized;

        var rt = (RectTransform)canvas.transform;
        rt.localScale = Vector3.one * Scale;
        rt.position = head.position + fwd * Distance;
        rt.rotation = Quaternion.LookRotation(fwd, Vector3.up);

        Plugin.Log.LogInfo($"[PeakVR] Menu -> world space: {canvas.name}");
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

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

    private Canvas active;
    private RenderMode savedMode;

    private void Update()
    {
        var canvas = FindActiveCanvas();
        if (canvas == active)
            return;

        if (active != null)
            active.renderMode = savedMode;

        active = canvas;

        if (active != null)
        {
            Activate(active);
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

    private void Activate(Canvas canvas)
    {
        if (EventSystem.current == null)
            new GameObject("PeakVR EventSystem").AddComponent<EventSystem>();

        var cam = MainCamera.instance != null ? MainCamera.instance.cam : Camera.main;
        if (cam == null)
            return;

        savedMode = canvas.renderMode;
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = cam;

        var raycaster = canvas.GetComponent<TrackedDeviceGraphicRaycaster>();
        if (raycaster == null)
            raycaster = canvas.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();

        var head = cam.transform;
        var fwd = head.forward;
        fwd.y = 0f;
        fwd = fwd.sqrMagnitude < 0.001f ? head.forward : fwd.normalized;

        var rt = (RectTransform)canvas.transform;
        rt.localScale = Vector3.one * Scale;
        rt.position = head.position + fwd * Distance;
        rt.rotation = Quaternion.LookRotation(fwd, Vector3.up);

        VRPointer.Canvas = canvas;
        VRPointer.Raycaster = raycaster;
        VRHands.SetPointersActive(true);

        Plugin.Log.LogInfo($"[PeakVR] Menu -> world space: {canvas.name}");
    }
}

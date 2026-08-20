using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace PeakVR;

[DefaultExecutionOrder(120)]
internal class VRMenuPopup : MonoBehaviour
{
    private const float Scale = 0.0025f;
    private const float Distance = 3f;
    private const float Height = 0.4f;

    private const int RefreshInterval = 30;

    private static readonly Dictionary<Canvas, VRCanvasState> converted = new();

    private static Canvas active;
    private int frame;

    private void LateUpdate()
    {
        if (!Plugin.VrEnabled)
            return;

        var cam = MainCamera.instance != null ? MainCamera.instance.cam : Camera.main;
        if (cam == null)
            return;

        if (MenuCanvasPatch.MenuCanvas != null)
            MenuCanvasPatch.MenuCanvas.worldCamera = cam;

        var popup = FindPopup();

        if (popup == null)
        {
            if (active != null)
            {
                RestorePointer();
                active = null;
            }
            else if (VRPointer.Canvas != MenuCanvasPatch.MenuCanvas)
            {
                RestorePointer();
            }
            return;
        }

        if (popup != active)
        {
            Convert(popup, cam);
            active = popup;
            frame = 0;
            return;
        }

        if (VRPointer.Canvas != popup)
            PointAt(popup);

        if (++frame % RefreshInterval == 0)
        {
            UIOverlay.SweepForegroundLayer(popup);
            UIOverlay.MakeAlwaysVisible(popup, true);
        }
    }

    private static Canvas FindPopup()
    {
        foreach (var c in Object.FindObjectsByType<Canvas>(UnityEngine.FindObjectsSortMode.None))
        {
            if (IsPopup(c))
                return c;
        }
        return null;
    }

    private static bool IsPopup(Canvas c)
    {
        if (c == null || !c.isActiveAndEnabled)
            return false;
        if (c == MenuCanvasPatch.MenuCanvas)
            return false;
        if (c.transform.root.name.StartsWith("PeakVR"))
            return false;
        if (c.GetComponentInParent<LoadingScreen>() != null)
            return false;
        if (!VRCanvasOwner.IsGame(c))
            return false;

        if (c.GetComponentInParent<Zorro.UI.Modal.Modal>() != null)
            return Zorro.UI.Modal.Modal.IsOpen;

        return c.renderMode == RenderMode.ScreenSpaceOverlay || converted.ContainsKey(c);
    }

    private static void Convert(Canvas c, Camera cam)
    {
        if (c.renderMode != RenderMode.WorldSpace)
        {
            converted[c] = VRCanvasState.Capture(c);
            c.renderMode = RenderMode.WorldSpace;
            c.worldCamera = cam;
            UIOverlay.MakeAlwaysVisible(c, true);
        }

        var rt = (RectTransform)c.transform;
        var menu = MenuCanvasPatch.MenuCanvas;

        if (menu != null)
        {
            var mt = menu.transform;
            rt.position = mt.position;
            rt.rotation = mt.rotation;
            rt.localScale = mt.localScale;
        }
        else
        {
            var head = cam.transform;
            var fwd = head.forward;
            fwd.y = 0f;
            fwd = fwd.sqrMagnitude < 0.001f ? head.forward : fwd.normalized;

            rt.localScale = Vector3.one * Scale;
            rt.position = head.position + fwd * Distance + Vector3.up * Height;
            rt.rotation = Quaternion.LookRotation(fwd, Vector3.up);
        }

        PointAt(c);
        Plugin.Log.LogInfo($"[PeakVR] Menu popup '{c.transform.root.name}' -> world space");
    }

    private static void PointAt(Canvas c)
    {
        var raycaster = VRPointer.Attach(c);

        if (EventSystem.current == null)
            new GameObject("PeakVR EventSystem").AddComponent<EventSystem>();

        VRPointer.Canvas = c;
        VRPointer.Raycaster = raycaster;
    }

    private static void RestorePointer()
    {
        VRPointer.Canvas = MenuCanvasPatch.MenuCanvas;
        VRPointer.Raycaster = MenuCanvasPatch.MenuRaycaster;
    }

    public static void RestoreAll()
    {
        var restored = 0;

        foreach (var pair in converted)
        {
            var c = pair.Key;
            if (c == null)
                continue;

            pair.Value.Apply(c);
            VRPointer.Detach(c);
            restored++;
        }

        converted.Clear();
        active = null;

        if (restored > 0)
            Plugin.Log.LogInfo($"[PeakVR] {restored} dialog canvas(es) returned to the screen");
    }
}

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

    private Canvas active;

    private void LateUpdate()
    {
        if (!Plugin.VrEnabled)
            return;

        var cam = MainCamera.instance != null ? MainCamera.instance.cam : Camera.main;
        if (cam == null)
            return;

        var popup = FindPopup();

        if (popup == null)
        {
            if (active != null)
            {
                RestorePointer();
                active = null;
            }
            return;
        }

        if (popup != active)
        {
            Convert(popup, cam);
            active = popup;
        }
        else if (VRPointer.Canvas != popup)
        {
            PointAt(popup);
        }
    }

    private static Canvas FindPopup()
    {
        foreach (var c in Object.FindObjectsByType<Canvas>(UnityEngine.FindObjectsSortMode.None))
        {
            if (!c.isActiveAndEnabled)
                continue;
            if (c == MenuCanvasPatch.MenuCanvas)
                continue;
            if (c.transform.root.name.StartsWith("PeakVR"))
                continue;

            return c;
        }
        return null;
    }

    private static void Convert(Canvas c, Camera cam)
    {
        if (c.renderMode != RenderMode.WorldSpace)
        {
            c.renderMode = RenderMode.WorldSpace;
            c.worldCamera = cam;
            UIOverlay.MakeAlwaysVisible(c, true);
        }

        var head = cam.transform;
        var fwd = head.forward;
        fwd.y = 0f;
        fwd = fwd.sqrMagnitude < 0.001f ? head.forward : fwd.normalized;

        var rt = (RectTransform)c.transform;
        rt.localScale = Vector3.one * Scale;
        rt.position = head.position + fwd * Distance + Vector3.up * Height;
        rt.rotation = Quaternion.LookRotation(fwd, Vector3.up);

        PointAt(c);
        Plugin.Log.LogInfo($"[PeakVR] Menu popup '{c.transform.root.name}' -> world space");
    }

    private static void PointAt(Canvas c)
    {
        var raycaster = c.GetComponent<TrackedDeviceGraphicRaycaster>();
        if (raycaster == null)
            raycaster = c.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();

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
}

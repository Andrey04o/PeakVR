using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace PeakVR;

internal static class VRPointer
{
    public static Canvas Canvas;
    public static TrackedDeviceGraphicRaycaster Raycaster;

    // Written every frame by VRLaser.
    public static GameObject Target;

    public static TrackedDeviceGraphicRaycaster Attach(Canvas canvas)
    {
        if (canvas == null)
            return null;

        var raycaster = canvas.GetComponent<TrackedDeviceGraphicRaycaster>();
        if (raycaster == null)
            raycaster = canvas.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();

        DisableScreenRaycaster(canvas);
        return raycaster;
    }

    public static void Detach(Canvas canvas)
    {
        if (canvas == null)
            return;

        var raycaster = canvas.GetComponent<TrackedDeviceGraphicRaycaster>();
        if (raycaster != null)
            Object.Destroy(raycaster);

        foreach (var screen in canvas.GetComponents<GraphicRaycaster>())
            if (screen != null)
                screen.enabled = true;
    }

    public static void DisableScreenRaycaster(Canvas canvas)
    {
        if (canvas == null)
            return;

        foreach (var screen in canvas.GetComponents<GraphicRaycaster>())
            if (screen != null && screen.enabled)
                screen.enabled = false;
    }
}

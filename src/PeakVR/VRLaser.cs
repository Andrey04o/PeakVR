using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace PeakVR;

internal class VRLaser : MonoBehaviour
{
    public LineRenderer line;
    public InputAction trigger;

    private const float MaxDistance = 8f;

    private GameObject hovered;
    private GameObject pressTarget;
    private bool wasPressed;

    private readonly List<RaycastResult> results = new();

    private void Update()
    {
        var canvas = MenuCanvasPatch.MenuCanvas;
        var raycaster = MenuCanvasPatch.MenuRaycaster;
        if (canvas == null || raycaster == null)
            return;

        var origin = transform.position;
        var dir = transform.forward;

        var eventData = new TrackedDeviceEventData(EventSystem.current)
        {
            layerMask = ~0,
            rayPoints = new List<Vector3> { origin, origin + dir * MaxDistance }
        };

        results.Clear();
        raycaster.Raycast(eventData, results);

        var hitGo = results.Count > 0 ? results[0].gameObject : null;

        UpdateBeam(canvas);

        var enterTarget = hitGo != null ? ExecuteEvents.GetEventHandler<IPointerEnterHandler>(hitGo) : null;
        if (enterTarget != hovered)
        {
            if (hovered != null)
                ExecuteEvents.Execute(hovered, eventData, ExecuteEvents.pointerExitHandler);
            hovered = enterTarget;
            if (hovered != null)
                ExecuteEvents.Execute(hovered, eventData, ExecuteEvents.pointerEnterHandler);
        }

        var pressed = trigger.IsPressed();

        if (pressed && !wasPressed)
        {
            pressTarget = hitGo != null ? ExecuteEvents.GetEventHandler<IPointerClickHandler>(hitGo) : null;
            eventData.pointerPress = pressTarget;
            if (pressTarget != null)
                ExecuteEvents.Execute(pressTarget, eventData, ExecuteEvents.pointerDownHandler);
        }
        else if (!pressed && wasPressed)
        {
            var upTarget = hitGo != null ? ExecuteEvents.GetEventHandler<IPointerClickHandler>(hitGo) : null;
            if (pressTarget != null)
                ExecuteEvents.Execute(pressTarget, eventData, ExecuteEvents.pointerUpHandler);
            if (upTarget != null && upTarget == pressTarget)
                ExecuteEvents.Execute(upTarget, eventData, ExecuteEvents.pointerClickHandler);
            pressTarget = null;
        }

        wasPressed = pressed;
    }

    private void UpdateBeam(Canvas canvas)
    {
        var rt = (RectTransform)canvas.transform;
        var plane = new Plane(-rt.forward, rt.position);
        var ray = new Ray(transform.position, transform.forward);

        if (plane.Raycast(ray, out var d))
            line.SetPosition(1, transform.InverseTransformPoint(ray.GetPoint(d)));
        else
            line.SetPosition(1, Vector3.forward * 5f);
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace PeakVR;

internal class VRLaser : MonoBehaviour
{
    public LineRenderer line;
    public InputAction trigger;

    private const float MaxDistance = 8f;
    private const float ReticlePixels = 36f;

    private GameObject hovered;
    private GameObject pressTarget;
    private bool wasPressed;

    private Image reticle;
    private Canvas reticleCanvas;

    private readonly List<RaycastResult> results = new();

    private void OnDisable()
    {
        if (reticle != null)
            reticle.gameObject.SetActive(false);
    }

    private void Update()
    {
        var canvas = VRPointer.Canvas;
        var raycaster = VRPointer.Raycaster;
        if (canvas == null || raycaster == null)
            return;

        EnsureReticle(canvas);

        var origin = transform.position;
        var dir = transform.forward;

        var onPanel = TryGetPanelHit(canvas, origin, dir, out var hitPoint);
        line.SetPosition(1, onPanel ? transform.InverseTransformPoint(hitPoint) : Vector3.forward * 5f);
        UpdateReticle(canvas, onPanel, hitPoint);

        var eventData = new TrackedDeviceEventData(EventSystem.current)
        {
            layerMask = ~0,
            rayPoints = new List<Vector3> { origin, origin + dir * MaxDistance }
        };

        results.Clear();
        raycaster.Raycast(eventData, results);
        var hitGo = results.Count > 0 ? results[0].gameObject : null;

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

    private void EnsureReticle(Canvas canvas)
    {
        if (PeakAssets.Reticle == null)
            return;

        if (reticle == null)
        {
            var go = new GameObject("PeakVR Reticle");
            reticle = go.AddComponent<Image>();
            reticle.sprite = PeakAssets.Reticle;
            reticle.raycastTarget = false;
            reticle.rectTransform.sizeDelta = new Vector2(ReticlePixels, ReticlePixels);
            reticle.gameObject.SetActive(false);
        }

        if (reticleCanvas != canvas)
        {
            reticle.rectTransform.SetParent(canvas.transform, false);
            reticleCanvas = canvas;
        }
    }

    private void UpdateReticle(Canvas canvas, bool onPanel, Vector3 hitPoint)
    {
        if (reticle == null)
            return;

        if (!onPanel)
        {
            reticle.gameObject.SetActive(false);
            return;
        }

        var local = canvas.transform.InverseTransformPoint(hitPoint);
        reticle.rectTransform.localPosition = new Vector3(local.x, local.y, 0f);
        reticle.rectTransform.SetAsLastSibling();
        reticle.gameObject.SetActive(true);
    }

    private bool TryGetPanelHit(Canvas canvas, Vector3 origin, Vector3 dir, out Vector3 hitPoint)
    {
        var rt = (RectTransform)canvas.transform;
        var plane = new Plane(-rt.forward, rt.position);
        var ray = new Ray(origin, dir);

        if (plane.Raycast(ray, out var d))
        {
            hitPoint = ray.GetPoint(d);
            return true;
        }

        hitPoint = default;
        return false;
    }
}

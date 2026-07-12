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
    private GameObject dragTarget;
    private bool dragging;
    private bool wasPressed;
    private Vector2 pressScreenPos;
    private RaycastResult pressRaycast;

    private Image reticle;
    private Canvas reticleCanvas;

    private readonly List<RaycastResult> results = new();
    private readonly List<TrackedDeviceGraphicRaycaster> raycasters = new();

    private void OnDisable()
    {
        if (reticle != null)
            reticle.gameObject.SetActive(false);
    }

    private void Update()
    {
        var canvas = VRPointer.Canvas;
        if (canvas == null)
            return;

        EnsureReticle(canvas);
        GatherRaycasters(canvas);

        var origin = transform.position;
        var dir = transform.forward;

        var onPanel = TryGetPanelHit(canvas, origin, dir, out var hitPoint);
        line.SetPosition(1, onPanel ? transform.InverseTransformPoint(hitPoint) : Vector3.forward * 5f);
        UpdateReticle(canvas, onPanel, hitPoint);

        var eventData = new TrackedDeviceEventData(EventSystem.current)
        {
            layerMask = ~0,
            rayPoints = new List<Vector3> { origin, origin + dir * MaxDistance },
            button = PointerEventData.InputButton.Left
        };

        var hasHit = RaycastTopmost(eventData, out var hit);
        var hitGo = hasHit ? hit.gameObject : null;

        eventData.pointerCurrentRaycast = hit;
        eventData.position = hasHit ? hit.screenPosition : pressScreenPos;

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
            pressScreenPos = eventData.position;
            pressRaycast = hit;

            eventData.pressPosition = pressScreenPos;
            eventData.pointerPressRaycast = hit;

            pressTarget = hitGo != null ? ExecuteEvents.GetEventHandler<IPointerClickHandler>(hitGo) : null;
            dragTarget = hitGo != null ? ExecuteEvents.GetEventHandler<IDragHandler>(hitGo) : null;
            dragging = false;

            eventData.pointerPress = pressTarget;
            eventData.pointerDrag = dragTarget;
            eventData.useDragThreshold = true;

            if (pressTarget != null)
                ExecuteEvents.Execute(pressTarget, eventData, ExecuteEvents.pointerDownHandler);
            if (dragTarget != null)
                ExecuteEvents.Execute(dragTarget, eventData, ExecuteEvents.initializePotentialDrag);
        }
        else if (pressed && wasPressed)
        {
            eventData.pressPosition = pressScreenPos;
            eventData.pointerPressRaycast = pressRaycast;
            eventData.pointerPress = pressTarget;
            eventData.pointerDrag = dragTarget;
            eventData.delta = eventData.position - pressScreenPos;

            if (dragTarget != null && !dragging)
            {
                var moved = (eventData.position - pressScreenPos).sqrMagnitude;
                var threshold = EventSystem.current.pixelDragThreshold;
                if (!eventData.useDragThreshold || moved >= threshold * threshold)
                {
                    dragging = true;
                    eventData.dragging = true;
                    ExecuteEvents.Execute(dragTarget, eventData, ExecuteEvents.beginDragHandler);
                }
            }

            if (dragging && dragTarget != null)
                ExecuteEvents.Execute(dragTarget, eventData, ExecuteEvents.dragHandler);
        }
        else if (!pressed && wasPressed)
        {
            eventData.pointerPressRaycast = pressRaycast;

            if (dragging && dragTarget != null)
                ExecuteEvents.Execute(dragTarget, eventData, ExecuteEvents.endDragHandler);

            if (pressTarget != null)
                ExecuteEvents.Execute(pressTarget, eventData, ExecuteEvents.pointerUpHandler);

            var upTarget = hitGo != null ? ExecuteEvents.GetEventHandler<IPointerClickHandler>(hitGo) : null;
            if (!dragging && upTarget != null && upTarget == pressTarget)
                ExecuteEvents.Execute(upTarget, eventData, ExecuteEvents.pointerClickHandler);

            pressTarget = null;
            dragTarget = null;
            dragging = false;
        }

        wasPressed = pressed;
    }

    private bool RaycastTopmost(TrackedDeviceEventData eventData, out RaycastResult hit)
    {
        foreach (var rc in raycasters)
        {
            results.Clear();
            rc.Raycast(eventData, results);
            if (results.Count > 0)
            {
                hit = results[0];
                return true;
            }
        }

        hit = default;
        return false;
    }

    private void GatherRaycasters(Canvas canvas)
    {
        raycasters.Clear();

        var root = canvas.rootCanvas != null ? canvas.rootCanvas : canvas;

        foreach (var c in root.GetComponentsInChildren<Canvas>(false))
        {
            if (c == null || !c.isActiveAndEnabled)
                continue;

            var tdgr = c.GetComponent<TrackedDeviceGraphicRaycaster>();
            if (tdgr == null)
            {
                if (c != root && c.GetComponent<GraphicRaycaster>() == null)
                    continue;

                tdgr = c.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
            }

            raycasters.Add(tdgr);
        }

        raycasters.Sort((a, b) =>
            ((Canvas)b.GetComponent(typeof(Canvas))).sortingOrder
            .CompareTo(((Canvas)a.GetComponent(typeof(Canvas))).sortingOrder));
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

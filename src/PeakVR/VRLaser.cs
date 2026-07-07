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
    private const float ReticleScale = 0.03f;

    private GameObject hovered;
    private GameObject pressTarget;
    private bool wasPressed;

    private Transform reticle;

    private readonly List<RaycastResult> results = new();

    private void Awake()
    {
        if (PeakAssets.Reticle == null)
            return;

        var go = new GameObject("PeakVR Reticle");
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = PeakAssets.Reticle;
        reticle = go.transform;
        reticle.gameObject.SetActive(false);
    }

    private void Update()
    {
        var canvas = MenuCanvasPatch.MenuCanvas;
        var raycaster = MenuCanvasPatch.MenuRaycaster;
        var cam = MainCamera.instance != null ? MainCamera.instance.cam : Camera.main;

        if (canvas == null || raycaster == null)
            return;

        var origin = transform.position;
        var dir = transform.forward;

        var onPanel = TryGetPanelHit(canvas, origin, dir, out var hitPoint);
        line.SetPosition(1, onPanel ? transform.InverseTransformPoint(hitPoint) : Vector3.forward * 5f);
        UpdateReticle(onPanel, hitPoint, cam);

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

    private void UpdateReticle(bool onPanel, Vector3 hitPoint, Camera cam)
    {
        if (reticle == null)
            return;

        if (!onPanel || cam == null)
        {
            reticle.gameObject.SetActive(false);
            return;
        }

        reticle.gameObject.SetActive(true);
        reticle.position = hitPoint - transform.forward * 0.01f;
        reticle.rotation = Quaternion.LookRotation(reticle.position - cam.transform.position, Vector3.up);
        reticle.localScale = Vector3.one * Vector3.Distance(cam.transform.position, hitPoint) * ReticleScale;
    }
}

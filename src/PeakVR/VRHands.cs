using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;

namespace PeakVR;

internal static class VRHands
{
    private static Material handMat;
    private static Material laserMat;

    private static VRLaser leftLaser;
    private static VRLaser rightLaser;

    private static LineRenderer interactRay;
    private static Material interactRayMat;
    private static bool menuPointersOn;

    public static Transform Left { get; private set; }
    public static Transform Right { get; private set; }

    public static void Create(Transform rig)
    {
        if (Left != null)
            return;

        Left = CreateHand(rig, "LeftHand", "PeakVR IG Left Hand", out leftLaser);
        Right = CreateHand(rig, "RightHand", "PeakVR IG Right Hand", out rightLaser);
        interactRay = CreateInteractRay(Right);
    }

    public static void SetPointersActive(bool on)
    {
        menuPointersOn = on;

        if (leftLaser == null || rightLaser == null)
            return;

        leftLaser.enabled = on;
        leftLaser.line.enabled = on;
        rightLaser.enabled = on;
        rightLaser.line.enabled = on;

        if (on && interactRay != null)
            interactRay.enabled = false;
    }

    public static void DrawInteractRay(bool hovering, float length)
    {
        if (interactRay == null)
            return;

        if (menuPointersOn)
        {
            if (interactRay.enabled)
                interactRay.enabled = false;
            return;
        }

        interactRay.enabled = true;
        interactRay.SetPosition(1, Vector3.forward * length);

        var col = hovering ? new Color(0.3f, 1f, 0.4f) : new Color(0.35f, 0.75f, 1f);
        if (interactRayMat.HasProperty("_BaseColor"))
            interactRayMat.SetColor("_BaseColor", col);
        else
            interactRayMat.color = col;
    }

    private static LineRenderer CreateInteractRay(Transform hand)
    {
        var obj = new GameObject("PeakVR Interact Ray");
        obj.transform.SetParent(hand, false);

        var line = obj.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.widthMultiplier = 0.004f;
        line.numCapVertices = 4;
        line.positionCount = 2;
        line.SetPosition(0, Vector3.zero);
        line.SetPosition(1, Vector3.forward * 2.5f);
        line.startColor = line.endColor = Color.white;

        var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
        interactRayMat = new Material(shader);
        line.material = interactRayMat;
        line.enabled = false;
        return line;
    }

    private static Transform CreateHand(Transform rig, string hand, string name, out VRLaser laser)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(rig, false);

        var pos = new InputAction($"{hand} HandPos", InputActionType.Value,
            $"<XRController>{{{hand}}}/pointer/position", expectedControlType: "Vector3");
        var rot = new InputAction($"{hand} HandRot", InputActionType.Value,
            $"<XRController>{{{hand}}}/pointer/rotation", expectedControlType: "Quaternion");
        pos.Enable();
        rot.Enable();

        var driver = obj.AddComponent<TrackedPoseDriver>();
        driver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
        driver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
        driver.positionInput = new InputActionProperty(pos);
        driver.rotationInput = new InputActionProperty(rot);

        var line = obj.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.widthMultiplier = 0.005f;
        line.numCapVertices = 4;
        line.positionCount = 2;
        line.SetPosition(0, Vector3.zero);
        line.SetPosition(1, Vector3.forward * 5f);
        line.startColor = line.endColor = Color.cyan;
        line.material = CreateLaserMaterial();
        line.enabled = false;

        var trigger = new InputAction($"{hand} PointerTrigger", InputActionType.Button,
            $"<XRController>{{{hand}}}/triggerPressed");
        trigger.Enable();

        laser = obj.AddComponent<VRLaser>();
        laser.line = line;
        laser.trigger = trigger;
        laser.enabled = false;

        if (PeakAssets.Controller != null)
        {
            var model = Object.Instantiate(PeakAssets.Controller, obj.transform);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            handMat ??= new Material(Shader.Find("Universal Render Pipeline/Lit"))
            {
                color = new Color(0.12f, 0.12f, 0.13f)
            };

            foreach (var r in model.GetComponentsInChildren<Renderer>())
                r.sharedMaterial = handMat;
        }

        Plugin.Log.LogInfo($"[PeakVR] Created in-game {name}");
        return obj.transform;
    }

    private static Material CreateLaserMaterial()
    {
        if (laserMat != null)
            return laserMat;

        var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
        laserMat = new Material(shader);

        if (laserMat.HasProperty("_BaseColor"))
            laserMat.SetColor("_BaseColor", Color.cyan);
        else
            laserMat.color = Color.cyan;

        return laserMat;
    }
}

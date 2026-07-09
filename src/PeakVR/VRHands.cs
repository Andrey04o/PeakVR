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

    public static Transform Left { get; private set; }
    public static Transform Right { get; private set; }

    public static void Create(Transform rig)
    {
        if (Left != null)
            return;

        Left = CreateHand(rig, "LeftHand", "PeakVR IG Left Hand", out leftLaser);
        Right = CreateHand(rig, "RightHand", "PeakVR IG Right Hand", out rightLaser);
    }

    public static void SetPointersActive(bool on)
    {
        if (leftLaser == null || rightLaser == null)
            return;

        leftLaser.enabled = on;
        leftLaser.line.enabled = on;
        rightLaser.enabled = on;
        rightLaser.line.enabled = on;
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

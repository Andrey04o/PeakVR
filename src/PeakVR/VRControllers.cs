using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;

namespace PeakVR;

internal static class VRControllers
{
    public static void CreateLasers(Transform rig)
    {
        if (GameObject.Find("PeakVR Left Hand") != null)
            return;

        CreateHand(rig, "LeftHand", "PeakVR Left Hand");
        CreateHand(rig, "RightHand", "PeakVR Right Hand");
    }

    private static void CreateHand(Transform rig, string hand, string name)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(rig, false);

        var posAction = new InputAction($"{hand} Position", InputActionType.Value,
            $"<XRController>{{{hand}}}/pointer/position", expectedControlType: "Vector3");
        var rotAction = new InputAction($"{hand} Rotation", InputActionType.Value,
            $"<XRController>{{{hand}}}/pointer/rotation", expectedControlType: "Quaternion");
        posAction.Enable();
        rotAction.Enable();

        var driver = obj.AddComponent<TrackedPoseDriver>();
        driver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
        driver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
        driver.positionInput = new InputActionProperty(posAction);
        driver.rotationInput = new InputActionProperty(rotAction);

        var line = obj.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.widthMultiplier = 0.005f;
        line.numCapVertices = 4;
        line.positionCount = 2;
        line.SetPosition(0, Vector3.zero);
        line.SetPosition(1, Vector3.forward * 5f);
        line.startColor = line.endColor = Color.cyan;
        line.material = CreateLaserMaterial();

        var trigger = new InputAction($"{hand} Trigger", InputActionType.Button,
            $"<XRController>{{{hand}}}/triggerPressed");
        trigger.Enable();

        var laser = obj.AddComponent<VRLaser>();
        laser.line = line;
        laser.trigger = trigger;

        Plugin.Log.LogInfo($"[PeakVR] Created {name}");
    }

    private static Material CreateLaserMaterial()
    {
        var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
        var mat = new Material(shader);

        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", Color.cyan);
        else
            mat.color = Color.cyan;

        return mat;
    }
}

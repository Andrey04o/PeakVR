using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;

namespace PeakVR;

internal static class VRHands
{
    private static Material handMat;

    public static Transform Left { get; private set; }
    public static Transform Right { get; private set; }

    public static void Create(Transform rig)
    {
        if (Left != null)
            return;

        Left = CreateHand(rig, "LeftHand", "PeakVR IG Left Hand");
        Right = CreateHand(rig, "RightHand", "PeakVR IG Right Hand");
    }

    private static Transform CreateHand(Transform rig, string hand, string name)
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
}

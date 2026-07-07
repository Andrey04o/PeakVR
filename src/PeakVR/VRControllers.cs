using UnityEngine;
using TrackedPoseDriver = UnityEngine.SpatialTracking.TrackedPoseDriver;
using TrackedPose = UnityEngine.SpatialTracking.TrackedPoseDriver.TrackedPose;

namespace PeakVR;

internal static class VRControllers
{
    public static void CreateLasers(Transform rig)
    {
        if (GameObject.Find("PeakVR Left Hand") != null)
            return;

        CreateHand(rig, TrackedPose.LeftPose, "PeakVR Left Hand");
        CreateHand(rig, TrackedPose.RightPose, "PeakVR Right Hand");
    }

    private static void CreateHand(Transform rig, TrackedPose pose, string name)
    {
        var hand = new GameObject(name);
        hand.transform.SetParent(rig, false);

        var driver = hand.AddComponent<TrackedPoseDriver>();
        driver.SetPoseSource(TrackedPoseDriver.DeviceType.GenericXRDevice, pose);
        driver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
        driver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;

        var line = hand.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.widthMultiplier = 0.005f;
        line.numCapVertices = 4;
        line.positionCount = 2;
        line.SetPosition(0, Vector3.zero);
        line.SetPosition(1, Vector3.forward * 5f);
        line.startColor = line.endColor = Color.cyan;
        line.material = CreateLaserMaterial();

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

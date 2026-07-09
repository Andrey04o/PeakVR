using HarmonyLib;
using UnityEngine;
using TrackedPoseDriver = UnityEngine.SpatialTracking.TrackedPoseDriver;

namespace PeakVR;

[HarmonyPatch(typeof(MainCameraMovement), "Start")]
internal static class InGameCameraPatch
{
    [HarmonyPostfix]
    private static void Postfix(MainCameraMovement __instance)
    {
        var cam = __instance.GetComponent<Camera>();
        if (cam == null || cam.GetComponent<TrackedPoseDriver>() != null)
            return;

        cam.stereoTargetEye = StereoTargetEyeMask.Both;
        cam.nearClipPlane = 0.05f;

        var rig = new GameObject("PeakVR InGame Rig");
        rig.transform.SetPositionAndRotation(cam.transform.position, Quaternion.identity);
        rig.transform.localScale = Vector3.one * VRHeadRig.HandScale;

        cam.transform.SetParent(rig.transform, false);
        cam.transform.localPosition = Vector3.zero;
        cam.transform.localRotation = Quaternion.identity;

        var driver = cam.gameObject.AddComponent<TrackedPoseDriver>();
        driver.SetPoseSource(TrackedPoseDriver.DeviceType.GenericXRDevice, TrackedPoseDriver.TrackedPose.Center);
        driver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
        driver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;

        rig.AddComponent<VRHeadRig>();

        VRHands.Create(rig.transform);

        Plugin.Log.LogInfo("[PeakVR] In-game VR camera rig created");
    }
}

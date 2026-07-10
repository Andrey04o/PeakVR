using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using TrackedPoseDriver = UnityEngine.SpatialTracking.TrackedPoseDriver;

namespace PeakVR;

[HarmonyPatch(typeof(MainCamera), nameof(MainCamera.Awake))]
internal static class MainCameraPatch
{
    private static readonly string[] MenuScenes = { "Title", "MainMenu", "Pretitle" };

    [HarmonyPostfix]
    private static void Postfix(MainCamera __instance)
    {
        var sceneName = SceneManager.GetActiveScene().name;
        if (System.Array.IndexOf(MenuScenes, sceneName) < 0)
            return;

        var cam = __instance.GetComponent<Camera>();
        if (cam == null || cam.GetComponent<TrackedPoseDriver>() != null)
            return;

        cam.stereoTargetEye = StereoTargetEyeMask.Both;
        cam.nearClipPlane = 0.05f;

        var rig = new GameObject("PeakVR Camera Rig");
        rig.transform.SetPositionAndRotation(cam.transform.position, cam.transform.rotation);

        cam.transform.SetParent(rig.transform, false);
        cam.transform.localPosition = Vector3.zero;
        cam.transform.localRotation = Quaternion.identity;

        var driver = cam.gameObject.AddComponent<TrackedPoseDriver>();
        driver.SetPoseSource(TrackedPoseDriver.DeviceType.GenericXRDevice, TrackedPoseDriver.TrackedPose.Center);
        driver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
        driver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;

        cam.gameObject.AddComponent<VRStereoCulling>();

        VRControllers.CreateLasers(rig.transform);

        Plugin.Log.LogInfo($"[PeakVR] Menu VR camera ready in '{sceneName}', rig at {rig.transform.position}");
    }
}

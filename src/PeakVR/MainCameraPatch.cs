using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using TrackedPoseDriver = UnityEngine.SpatialTracking.TrackedPoseDriver;

namespace PeakVR;

[HarmonyPatch(typeof(MainCamera), nameof(MainCamera.Awake))]
internal static class MainCameraPatch
{
    private static readonly string[] MenuScenes = { "Title", "MainMenu", "Pretitle" };

    private const string RigName = "PeakVR Camera Rig";

    private static Transform rig;
    private static Transform cameraParent;
    private static Vector3 cameraLocalPosition;
    private static Quaternion cameraLocalRotation;
    private static float nearClip;
    private static StereoTargetEyeMask targetEye;

    [HarmonyPostfix]
    private static void Postfix(MainCamera __instance)
    {
        if (!Plugin.VrEnabled)
            return;

        Build(__instance, SceneManager.GetActiveScene().name);
    }

    public static void Build(MainCamera instance, string sceneName)
    {
        if (System.Array.IndexOf(MenuScenes, sceneName) < 0 || rig != null)
            return;

        var cam = instance.GetComponent<Camera>();
        if (cam == null || cam.GetComponent<TrackedPoseDriver>() != null)
            return;

        cameraParent = cam.transform.parent;
        cameraLocalPosition = cam.transform.localPosition;
        cameraLocalRotation = cam.transform.localRotation;
        nearClip = cam.nearClipPlane;
        targetEye = cam.stereoTargetEye;

        cam.stereoTargetEye = StereoTargetEyeMask.Both;
        cam.nearClipPlane = 0.05f;

        var go = new GameObject(RigName);
        rig = go.transform;
        rig.SetPositionAndRotation(cam.transform.position, cam.transform.rotation);

        cam.transform.SetParent(rig, false);
        cam.transform.localPosition = Vector3.zero;
        cam.transform.localRotation = Quaternion.identity;

        var driver = cam.gameObject.AddComponent<TrackedPoseDriver>();
        driver.SetPoseSource(TrackedPoseDriver.DeviceType.GenericXRDevice, TrackedPoseDriver.TrackedPose.Center);
        driver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
        driver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;

        VRRender.DisableXRVisibilityMesh();
        VRRender.DisableBrokenAO();
        VRRender.ApplySharpening();
        RenderDiagnostics.ApplyLodBias();

        cam.gameObject.AddComponent<VRStereoCulling>();

        VRControllers.CreateLasers(rig);

        Plugin.Log.LogInfo($"[PeakVR] Menu VR camera ready in '{sceneName}', rig at {rig.position}");
    }

    public static void Teardown()
    {
        if (rig == null)
        {
            var found = GameObject.Find(RigName);
            if (found == null)
                return;
            rig = found.transform;
        }

        var mainCam = MainCamera.instance != null ? MainCamera.instance.GetComponent<Camera>() : null;
        var cam = mainCam != null && mainCam.transform.IsChildOf(rig) ? mainCam : rig.GetComponentInChildren<Camera>(true);

        rig.gameObject.SetActive(false);

        if (cam != null)
        {
            Object.Destroy(cam.GetComponent<TrackedPoseDriver>());
            Object.Destroy(cam.GetComponent<VRStereoCulling>());

            cam.stereoTargetEye = targetEye;
            cam.nearClipPlane = nearClip;

            cam.transform.SetParent(cameraParent, false);
            cam.transform.localPosition = cameraLocalPosition;
            cam.transform.localRotation = cameraLocalRotation;
        }

        Object.Destroy(rig.gameObject);
        rig = null;
        cameraParent = null;

        Plugin.Log.LogInfo("[PeakVR] Menu VR camera rig removed");
    }
}

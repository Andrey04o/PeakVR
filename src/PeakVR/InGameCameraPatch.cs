using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;

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

        var posAction = new InputAction("HeadPosition", InputActionType.Value,
            "<XRHMD>/centerEyePosition", expectedControlType: "Vector3");
        var rotAction = new InputAction("HeadRotation", InputActionType.Value,
            "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion");
        posAction.Enable();
        rotAction.Enable();

        var driver = cam.gameObject.AddComponent<TrackedPoseDriver>();
        driver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
        driver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
        driver.positionInput = new InputActionProperty(posAction);
        driver.rotationInput = new InputActionProperty(rotAction);

        cam.gameObject.AddComponent<VRStereoCulling>();
        cam.gameObject.AddComponent<VRTunneling>();

        rig.AddComponent<VRHeadRig>();
        rig.AddComponent<VRMenuManager>();
        rig.AddComponent<VRInteractPrompt>();
        rig.AddComponent<VRShoulderTwist>();
        rig.AddComponent<VRHeadRoll>();

        VRHands.Create(rig.transform);

        RenderDiagnostics.Apply();
        RenderDiagnostics.ApplyLodBias();
        RenderDiagnostics.ScheduleScan();

        Plugin.Log.LogInfo("[PeakVR] In-game VR camera rig created");
    }
}

using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;

namespace PeakVR;

[HarmonyPatch(typeof(MainCameraMovement), "Start")]
internal static class InGameCameraPatch
{
    private const string RigName = "PeakVR InGame Rig";

    private static Transform rig;
    private static Transform cameraParent;
    private static Vector3 cameraLocalPosition;
    private static Quaternion cameraLocalRotation;
    private static float nearClip;
    private static StereoTargetEyeMask targetEye;

    [HarmonyPostfix]
    private static void Postfix(MainCameraMovement __instance)
    {
        if (!Plugin.VrEnabled)
            return;

        Build(__instance);
    }

    public static void Build(MainCameraMovement movement)
    {
        var cam = movement.GetComponent<Camera>();
        if (cam == null || cam.GetComponent<TrackedPoseDriver>() != null || rig != null)
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
        rig.SetPositionAndRotation(cam.transform.position, Quaternion.identity);
        rig.localScale = Vector3.one * VRHeadRig.HandScale;

        cam.transform.SetParent(rig, false);
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

        VRRender.DisableXRVisibilityMesh();
        VRRender.DisableBrokenAO();

        cam.gameObject.AddComponent<VRStereoCulling>();
        cam.gameObject.AddComponent<VRTunneling>();
        cam.gameObject.AddComponent<VRBinoculars>();

        go.AddComponent<VRHeadRig>();
        go.AddComponent<VRMenuManager>();
        go.AddComponent<VRInteractPrompt>();
        go.AddComponent<VRShoulderTwist>();
        go.AddComponent<VRHeadRoll>();
        go.AddComponent<VRItemAim>();
        go.AddComponent<VRItemCollision>();
        go.AddComponent<VRBazookaHold>();
        go.AddComponent<VRNetSync>();
        go.AddComponent<VRControllerHud>();
        go.AddComponent<VREmoteWheel>();

        PeakTextChatPatch.Resolve();
        go.AddComponent<VRChatButton>();

        VRHands.Create(rig);

        RenderDiagnostics.ApplyLodBias();
        VRRender.ApplyFarPlane();
        VRRender.ApplySharpening();
        VRFoveation.Apply();
        RenderDiagnostics.ScheduleScan();
        VRFoliage.ScheduleScan();
        VRHazardLayer.ScheduleScan();

        if (Plugin.DebugButtons || Plugin.Config.EnableVerboseLogging.Value)
        {
            UrpDiagnostics.DumpOnce();
            ForegroundUI.LogUiLayerRenderers();
        }

        ForceKeyboardMouseScheme();

        Plugin.Log.LogInfo("[PeakVR] In-game VR camera rig created");
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

        var cam = rig.GetComponentInChildren<Camera>(true);

        // Destroy() only takes effect at the end of the frame, and a mode switch runs from Update — so
        // without this the rig's LateUpdates would fire once more over half-restored state. The camera
        // is reparented out below, which makes it live again in the same frame.
        rig.gameObject.SetActive(false);

        // The rig-borne components put the HUD and the interaction prompts where they are, so they get
        // to unwind their own moves before the GameObject that carries them goes away.
        foreach (var restorable in rig.GetComponentsInChildren<IVRRestorable>(true))
            restorable.RestoreForFlat();

        if (cam != null)
        {
            Object.Destroy(cam.GetComponent<TrackedPoseDriver>());
            Object.Destroy(cam.GetComponent<VRStereoCulling>());
            Object.Destroy(cam.GetComponent<VRTunneling>());
            Object.Destroy(cam.GetComponent<VRBinoculars>());

            cam.stereoTargetEye = targetEye;
            cam.nearClipPlane = nearClip;

            cam.transform.SetParent(cameraParent, false);
            cam.transform.localPosition = cameraLocalPosition;
            cam.transform.localRotation = cameraLocalRotation;
        }

        Object.Destroy(rig.gameObject);
        rig = null;
        cameraParent = null;

        Plugin.Log.LogInfo("[PeakVR] In-game VR camera rig removed");
    }

    // Force the game's control scheme to Keyboard&Mouse once at level start so the HUD stops
    // showing gamepad button prompts (our VR input otherwise reads as an Unknown scheme).
    private static void ForceKeyboardMouseScheme()
    {
        try
        {
            var playerInput = Object.FindObjectOfType<PlayerInput>();
            if (playerInput == null || Keyboard.current == null)
                return;

            if (Mouse.current != null)
                playerInput.SwitchCurrentControlScheme("Keyboard&Mouse", Keyboard.current, Mouse.current);
            else
                playerInput.SwitchCurrentControlScheme("Keyboard&Mouse", Keyboard.current);

            Plugin.Log.LogInfo("[PeakVR] Forced control scheme to Keyboard&Mouse");
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"[PeakVR] Could not force Keyboard&Mouse scheme: {e.Message}");
        }
    }
}

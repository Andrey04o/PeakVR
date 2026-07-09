using UnityEngine;
using UnityEngine.InputSystem;

namespace PeakVR;

[DefaultExecutionOrder(1000)]
internal class VRHeadRig : MonoBehaviour
{
    public static float HandScale = 0.95f;

    private const float ForwardOffset = 0f;
    private const float SnapAngle = 45f;
    private const float SnapThreshold = 0.7f;
    private const float SnapReleaseThreshold = 0.3f;
    private const float ScaleRate = 0.15f;

    private static readonly Vector3[] RotPresets =
    {
        new Vector3(0f, 0f, 0f), new Vector3(0f, 180f, 0f), new Vector3(180f, 0f, 0f),
        new Vector3(0f, 0f, 180f), new Vector3(90f, 0f, 0f), new Vector3(-90f, 0f, 0f),
        new Vector3(0f, 90f, 0f), new Vector3(0f, -90f, 0f), new Vector3(90f, 180f, 0f),
        new Vector3(-90f, 180f, 0f), new Vector3(0f, 180f, 180f), new Vector3(90f, 0f, 180f),
    };

    private Camera cam;
    private Vector3 hmdOffset;
    private float turnYaw;
    private bool snapReady = true;

    private InputAction scaleDown;
    private InputAction scaleUp;
    private InputAction cycleRot;
    private bool cycleReady = true;
    private int rotIndex = 9;
    private float sinceLog;

    private void Awake()
    {
        cam = GetComponentInChildren<Camera>();

        scaleDown = new InputAction("ScaleDown", InputActionType.Button, "<XRController>{LeftHand}/gripPressed");
        scaleUp = new InputAction("ScaleUp", InputActionType.Button, "<XRController>{RightHand}/gripPressed");
        cycleRot = new InputAction("CycleRot", InputActionType.Button, "<XRController>{LeftHand}/primaryButton");
        scaleDown.Enable();
        scaleUp.Enable();
        cycleRot.Enable();
    }

    private void Update()
    {
        if (cam != null)
            hmdOffset = cam.transform.localPosition;

        HandleSnapTurn();
        HandleScaleTuning();
        HandleRotationCycle();
    }

    private void HandleRotationCycle()
    {
        if (cycleRot.IsPressed() && cycleReady)
        {
            cycleReady = false;
            rotIndex = (rotIndex + 1) % RotPresets.Length;
            VRArmIKPatch.HandRotationOffset = Quaternion.Euler(RotPresets[rotIndex]);
            Plugin.Log.LogInfo($"[PeakVR] Hand rotation preset {rotIndex} = {RotPresets[rotIndex]}");
        }
        else if (!cycleRot.IsPressed())
        {
            cycleReady = true;
        }
    }

    private void HandleSnapTurn()
    {
        if (VRControls.TurnStick == null)
            return;

        var x = VRControls.TurnStick.ReadValue<Vector2>().x;

        if (snapReady && Mathf.Abs(x) > SnapThreshold)
        {
            turnYaw += Mathf.Sign(x) * SnapAngle;
            snapReady = false;
        }
        else if (Mathf.Abs(x) < SnapReleaseThreshold)
        {
            snapReady = true;
        }
    }

    private void HandleScaleTuning()
    {
        var delta = (scaleUp.IsPressed() ? 1f : 0f) - (scaleDown.IsPressed() ? 1f : 0f);
        if (delta == 0f)
            return;

        HandScale = Mathf.Clamp(HandScale + delta * ScaleRate * Time.deltaTime, 0.3f, 1.6f);

        sinceLog += Time.deltaTime;
        if (sinceLog > 0.3f)
        {
            sinceLog = 0f;
            Plugin.Log.LogInfo($"[PeakVR] HandScale = {HandScale:F3}");
        }
    }

    private void LateUpdate()
    {
        var character = Character.localCharacter;
        if (character == null || cam == null)
            return;

        transform.localScale = Vector3.one * HandScale;
        transform.rotation = Quaternion.Euler(0f, turnYaw, 0f);
        transform.position = character.GetCameraPos(ForwardOffset) - transform.rotation * (hmdOffset * HandScale);
    }
}

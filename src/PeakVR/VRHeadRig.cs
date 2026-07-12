using UnityEngine;

namespace PeakVR;

[DefaultExecutionOrder(1000)]
internal class VRHeadRig : MonoBehaviour
{
    public static float HandScale = 1f;

    private const float ForwardOffset = 0f;
    private const float SnapThreshold = 0.7f;
    private const float SnapReleaseThreshold = 0.3f;
    private const float SmoothDeadzone = 0.15f;

    private Camera cam;
    private Vector3 hmdOffset;
    private float turnYaw;
    private bool snapReady = true;

    private void Awake()
    {
        cam = GetComponentInChildren<Camera>();
    }

    private void Update()
    {
        if (cam != null)
            hmdOffset = cam.transform.localPosition;

        RenderDiagnostics.Tick(cam);
        HandleTurn();
    }

    private void HandleTurn()
    {
        if (VRControls.TurnStick == null)
            return;

        var x = VRControls.TurnStick.ReadValue<Vector2>().x;

        if (Plugin.Config.SmoothTurn.Value)
        {
            if (Mathf.Abs(x) > SmoothDeadzone)
                turnYaw += x * Plugin.Config.SmoothTurnSpeed.Value * Time.deltaTime;
            return;
        }

        if (snapReady && Mathf.Abs(x) > SnapThreshold)
        {
            turnYaw += Mathf.Sign(x) * Plugin.Config.SnapTurnAngle.Value;
            snapReady = false;
        }
        else if (Mathf.Abs(x) < SnapReleaseThreshold)
        {
            snapReady = true;
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

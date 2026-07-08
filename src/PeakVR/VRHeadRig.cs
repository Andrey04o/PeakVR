using UnityEngine;

namespace PeakVR;

[DefaultExecutionOrder(1000)]
internal class VRHeadRig : MonoBehaviour
{
    private const float ForwardOffset = 0f;
    private const float SnapAngle = 45f;
    private const float SnapThreshold = 0.7f;
    private const float SnapReleaseThreshold = 0.3f;

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

        HandleSnapTurn();
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

    private void LateUpdate()
    {
        var character = Character.localCharacter;
        if (character == null || cam == null)
            return;

        transform.rotation = Quaternion.Euler(0f, turnYaw, 0f);
        transform.position = character.GetCameraPos(ForwardOffset) - transform.rotation * hmdOffset;
    }
}

using UnityEngine;

namespace PeakVR;

[DefaultExecutionOrder(1000)]
internal class VRHeadRig : MonoBehaviour
{
    public static float HandScale = 1f;

    private const float SnapThreshold = 0.7f;
    private const float SnapReleaseThreshold = 0.3f;
    private const float SmoothDeadzone = 0.15f;

    private const float StickDeadzone = 0.1f;
    private const float ForceMultiplier = 8f;
    private const float MaxGap = 0.75f;
    private const float MinGap = 0.02f;
    private const float RecenterSpeed = 2f;

    public static bool RoomMoving;

    private Camera cam;
    private Vector3 hmdOffset;
    private float turnYaw;
    private bool snapReady = true;

    private Vector2 originOffset;
    private Vector2 prevAnchor;
    private bool hasPrev;

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

        var anchor = character.GetCameraPos(0f);
        var scaledHmd = transform.rotation * (hmdOffset * HandScale);
        var anchorXZ = new Vector2(anchor.x, anchor.z);

        CompensateOrigin(anchorXZ, new Vector2(scaledHmd.x, scaledHmd.z));

        transform.position = new Vector3(
            anchor.x + originOffset.x,
            anchor.y - scaledHmd.y,
            anchor.z + originOffset.y);

        RoomScale(character, new Vector2(scaledHmd.x, scaledHmd.z));
    }

    private void CompensateOrigin(Vector2 anchorXZ, Vector2 hmdXZ)
    {
        if (!hasPrev)
        {
            prevAnchor = anchorXZ;
            originOffset = -hmdXZ;
            hasPrev = true;
            return;
        }

        var move = anchorXZ - prevAnchor;
        prevAnchor = anchorXZ;

        var camXZ = anchorXZ + originOffset + hmdXZ;

        if (Vector2.Distance(anchorXZ + new Vector2(move.x, 0f), camXZ) < Vector2.Distance(anchorXZ, camXZ))
            originOffset.x -= move.x;

        if (Vector2.Distance(anchorXZ + new Vector2(0f, move.y), camXZ) < Vector2.Distance(anchorXZ, camXZ))
            originOffset.y -= move.y;
    }

    private void RoomScale(Character character, Vector2 hmdXZ)
    {
        RoomMoving = false;

        var gap = originOffset + hmdXZ;
        var gapMag = gap.magnitude;

        var stick = VRControls.MoveStick != null ? VRControls.MoveStick.ReadValue<Vector2>().magnitude : 0f;
        if (stick > StickDeadzone)
        {
            originOffset = Vector2.MoveTowards(originOffset, originOffset - gap, RecenterSpeed * Time.deltaTime);
            return;
        }

        if (gapMag < MinGap)
            return;

        if (gapMag > MaxGap)
        {
            originOffset -= gap;
            return;
        }

        if (VRPointer.Canvas != null || !character.data.isGrounded)
            return;

        var force = character.refs.movement != null ? character.refs.movement.movementForce : 10f;
        character.AddForce(new Vector3(gap.x, 0f, gap.y) * (force * ForceMultiplier));
        RoomMoving = true;
    }
}

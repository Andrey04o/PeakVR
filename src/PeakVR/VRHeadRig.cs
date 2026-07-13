using UnityEngine;
using UnityEngine.InputSystem;

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

    private const float MoveDeadzone = 0.12f;
    private const float WalkInputGain = 0.6f;
    private const float MaxWalkInput = 0.5f;

    private const float CrouchRatio = 0.5f;
    private const float CrouchExitRatio = 0.6f;

    private const float ClampDown = 0.7f;
    private const float ClampUp = 0.3f;
    private const float RefRate = 1.5f;

    public static bool RoomMoving;
    public static Vector2 RoomInput;
    public static bool Crouching;

    private Camera cam;
    private Vector3 hmdOffset;
    private float turnYaw;
    private bool snapReady = true;

    private Vector2 originOffset;
    private Vector2 prevAnchor;
    private bool hasPrev;

    private Vector2 prevHmdRawXZ;
    private bool physInit;
    private Vector2 physVel;

    private float hmdRef;
    private float standingHmdY;
    private bool heightCalibrated;
    private bool resetHeight;

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

        if (Keyboard.current != null && Keyboard.current.f5Key.wasPressedThisFrame)
            resetHeight = true;
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

        UpdatePhysVel();
        CompensateOrigin(anchorXZ, new Vector2(scaledHmd.x, scaledHmd.z));

        transform.position = new Vector3(
            anchor.x + originOffset.x,
            ComputeRigY(anchor),
            anchor.z + originOffset.y);

        RoomScale(character, new Vector2(scaledHmd.x, scaledHmd.z));
        HandleCrouch();
    }

    private float ComputeRigY(Vector3 anchor)
    {
        if (!heightCalibrated || resetHeight)
        {
            standingHmdY = hmdOffset.y;
            hmdRef = hmdOffset.y;
            heightCalibrated = true;
            resetHeight = false;
        }
        else
        {
            hmdRef = Mathf.Lerp(hmdRef, hmdOffset.y, RefRate * Time.deltaTime);
        }

        var offset = Mathf.Clamp(hmdOffset.y - hmdRef, -ClampDown, ClampUp);
        return anchor.y + offset - hmdOffset.y;
    }

    private void HandleCrouch()
    {
        if (!heightCalibrated || standingHmdY < 0.1f)
            return;

        var ratio = hmdOffset.y / standingHmdY;
        if (ratio < CrouchRatio)
            Crouching = true;
        else if (ratio > CrouchExitRatio)
            Crouching = false;
    }

    private void UpdatePhysVel()
    {
        var raw = new Vector2(hmdOffset.x, hmdOffset.z);
        physVel = physInit ? (raw - prevHmdRawXZ) / Mathf.Max(Time.deltaTime, 1e-4f) : Vector2.zero;
        prevHmdRawXZ = raw;
        physInit = true;
    }

    private void ComputeWalkInput(Character character)
    {
        if (physVel.magnitude < MoveDeadzone)
            return;

        var world = transform.rotation * new Vector3(physVel.x, 0f, physVel.y);
        var look = character.data.lookDirection;
        look.y = 0f;
        if (look.sqrMagnitude < 1e-4f)
            return;
        look.Normalize();

        var right = new Vector3(look.z, 0f, -look.x);
        var input = new Vector2(Vector3.Dot(world, right), Vector3.Dot(world, look));

        RoomInput = Vector2.ClampMagnitude(input * WalkInputGain, MaxWalkInput);
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
        RoomInput = Vector2.zero;

        var gap = originOffset + hmdXZ;
        var gapMag = gap.magnitude;

        var stick = VRControls.MoveStick != null ? VRControls.MoveStick.ReadValue<Vector2>().magnitude : 0f;
        if (stick > StickDeadzone)
        {
            originOffset = Vector2.MoveTowards(originOffset, originOffset - gap, RecenterSpeed * Time.deltaTime);
            ComputeWalkInput(character);
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

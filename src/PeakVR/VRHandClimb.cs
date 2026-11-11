using System.Collections.Generic;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;
using UnityEngine.InputSystem;
using Zorro.Core;

namespace PeakVR;

internal class VRClimbHand
{
    public bool IsLeft;
    public bool IsGripping;
    public bool IsGripHeld;
    public bool IsInputConsumed;
    public float AutoGrabLeft;
    public Collider GripCollider;
    public Vector3 AnchorLocal;
    public Vector3 Normal;
    public Vector3 Residual;
    public Vector3 ShoulderToTorso;
    public bool HasShoulder;
    public Vector3 PalmLast;
    public bool IsPalmLastValid;
    public bool IsRope;
    public Rope Rope;
    public float RopePercent;

    public Transform Controller => IsLeft ? VRHands.Left : VRHands.Right;
    public InputAction Grip => IsLeft ? VRControls.LeftGrip : VRControls.RightGrip;

    public bool HasAnchor => IsGripping && (IsRope ? Rope != null : GripCollider != null);

    public Vector3 GetAnchor()
    {
        if (IsRope)
            return Rope.climbingAPI.GetPosition(RopePercent);
        return GripCollider.transform.TransformPoint(AnchorLocal);
    }
}

internal static class VRHandClimb
{
    private static readonly Vector3 ShoulderOffsetLeft = new(-0.18f, -0.2f, 0f);
    private static readonly Vector3 ShoulderOffsetRight = new(0.18f, -0.2f, 0f);
    private static readonly Vector3 PalmOffset = new(0f, -0.02f, 0.05f);

    private const float PalmThickness = 0.025f;
    private const float SphereRadius = 0.05f;
    private const float SphereDistance = 0.25f;
    private const float SphereBackoff = 0.08f;
    private const float RayDistance = 0.6f;
    private const float ResidualMax = 0.15f;
    private const float ResidualRelax = 0.1f;
    private const float AutoGrabTime = 1f;
    private const float SpeedMax = 6f;
    private const float VelocityGain = 30f;
    private const float FollowTime = 0.08f;
    private const float BodyNormalDistance = 0.4f;
    private const float FloorAngle = 50f;
    private const float SlideSpeed = 0.6f;
    private const float SlideProbe = 0.15f;
    private const float InputSpeed = 1.5f;
    private const float HandFollowTime = 0.05f;
    private const float HandSpeedMax = 8f;
    private const float HandGain = 25f;
    private const float RopeRadius = 0.1f;
    private const float RopeUpSpeed = 0.3f;
    private const float RopeTurnSpeed = 360f;
    private const float HandsAcrossMin = 0.15f;
    private const float HandsAcrossFull = 0.4f;

    private static Vector3 ropeForward = Vector3.forward;

    private static readonly Collider[] Overlaps = new Collider[16];

    private const float MoveSoundDistance = 0.45f;

    private static Character soundOwner;
    private static SFX_PlayOneShot soundGrab;
    private static SFX_PlayOneShot soundMoveA;
    private static SFX_PlayOneShot soundMoveB;
    private static float moveDistance;
    private static bool isMoveSoundB;

    public static readonly VRClimbHand Left = new() { IsLeft = true };
    public static readonly VRClimbHand Right = new() { IsLeft = false };

    private static readonly RaycastHit[] Hits = new RaycastHit[16];

    public static bool Enabled => Plugin.VrEnabled && Plugin.Config != null && Plugin.Config.HandClimbing.Value;

    public static bool IsHolding => (Left.IsGripping && !Left.IsRope) || (Right.IsGripping && !Right.IsRope);

    public static bool IsHoldingRope => (Left.IsGripping && Left.IsRope) || (Right.IsGripping && Right.IsRope);

    public static bool IsHoldingAny => Left.IsGripping || Right.IsGripping;

    public static bool IsConsumed(bool isLeft)
    {
        return (isLeft ? Left : Right).IsInputConsumed;
    }

    public static void UpdateGrips(Character c, bool playerMovementActive)
    {
        if (IsHolding && ShouldLetGo(c))
        {
            DropWallHands();
            SendStop(c);
        }

        if (IsHoldingRope && !IsRopeValid(c))
        {
            DropRopeHands();
            if (c.data.isRopeClimbing)
                c.refs.ropeHandling.Stop();
        }

        UpdateHand(c, Left, playerMovementActive);
        UpdateHand(c, Right, playerMovementActive);
    }

    private static void UpdateHand(Character c, VRClimbHand hand, bool playerMovementActive)
    {
        if (hand.Grip == null || hand.Controller == null)
            return;

        bool pressed = hand.Grip.IsPressed() && Enabled && playerMovementActive && !VRKeyboard.IsOpen;
        if (!hand.IsGripHeld && pressed)
        {
            hand.IsGripHeld = true;
            hand.AutoGrabLeft = AutoGrabTime;
            hand.IsInputConsumed = TryGrab(c, hand);
        }
        else if (hand.IsGripHeld && !pressed)
        {
            hand.IsGripHeld = false;
            hand.IsInputConsumed = false;
            Release(c, hand);
        }
        else if (hand.IsGripHeld && !hand.IsGripping && hand.AutoGrabLeft > 0f)
        {
            hand.AutoGrabLeft -= Time.deltaTime;
            if (TryGrab(c, hand))
                hand.IsInputConsumed = true;
        }

        if (hand.IsGripping && !hand.HasAnchor)
            Release(c, hand);

        if (hand.HasShoulder)
        {
            hand.PalmLast = GetVirtualPalm(hand, GetShoulder(c, hand, c.Center));
            hand.IsPalmLastValid = true;
        }
    }

    private static bool ShouldLetGo(Character c)
    {
        CharacterData data = c.data;
        if (data.dead || data.fullyPassedOut || data.passedOut)
            return true;
        if (data.isRopeClimbing || data.isVineClimbing || data.currentClimbHandle != null)
            return true;
        if (data.carrierCondor != null || data.isCarried)
            return true;
        if (data.currentRagdollControll < 0.25f)
            return true;
        return VRControls.RightPrimary != null && VRControls.RightPrimary.WasPressedThisFrame();
    }

    private static bool IsRopeValid(Character c)
    {
        CharacterData data = c.data;
        if (data.dead || data.fullyPassedOut || data.passedOut)
            return false;
        if (!data.isRopeClimbing || data.heldRope == null)
            return false;
        Rope held = GetHeldRope();
        return held != null && held == data.heldRope;
    }

    private static Rope GetHeldRope()
    {
        if (Left.IsGripping && Left.IsRope && Left.Rope != null)
            return Left.Rope;
        if (Right.IsGripping && Right.IsRope && Right.Rope != null)
            return Right.Rope;
        return null;
    }

    private static void DropWallHands()
    {
        if (Left.IsGripping && !Left.IsRope)
            DropHand(Left);
        if (Right.IsGripping && !Right.IsRope)
            DropHand(Right);
    }

    private static void DropRopeHands()
    {
        if (Left.IsGripping && Left.IsRope)
            DropHand(Left);
        if (Right.IsGripping && Right.IsRope)
            DropHand(Right);
    }

    private static Quaternion GetBodyYaw(Character c)
    {
        if (IsHoldingRope && c.data.isRopeClimbing)
            return Quaternion.LookRotation(ropeForward, Vector3.up);

        Vector3 flat = c.data.lookDirection_Flat;
        if (flat.sqrMagnitude < 1e-4f)
            return Quaternion.identity;
        return Quaternion.LookRotation(flat, Vector3.up);
    }

    private static Vector3 GetShoulder(Character c, VRClimbHand hand, Vector3 torso)
    {
        return torso + GetBodyYaw(c) * hand.ShoulderToTorso;
    }

    public static void StoreShoulders(Character c, Vector3 shoulderLeft, Vector3 shoulderRight)
    {
        if (IsHolding && Left.HasShoulder && Right.HasShoulder)
            return;

        Vector3 torso = c.Center;
        Quaternion yawInverse = Quaternion.Inverse(GetBodyYaw(c));
        Left.ShoulderToTorso = yawInverse * (shoulderLeft - torso);
        Right.ShoulderToTorso = yawInverse * (shoulderRight - torso);
        Left.HasShoulder = true;
        Right.HasShoulder = true;
    }

    public static bool TryGetWristTarget(bool isLeft, Vector3 hipShift, out Vector3 wrist)
    {
        VRClimbHand hand = isLeft ? Left : Right;
        wrist = default;
        if (!hand.HasAnchor || hand.Controller == null)
            return false;

        wrist = GetWrist(hand) + hipShift;
        return true;
    }

    private static Vector3 GetWrist(VRClimbHand hand)
    {
        return hand.GetAnchor() - hand.Controller.rotation * PalmOffset;
    }

    private static void PinHand(Character c, VRClimbHand hand)
    {
        if (!hand.HasAnchor || hand.Controller == null)
            return;

        Rigidbody rig = c.GetBodypartRig(hand.IsLeft ? BodypartType.Hand_L : BodypartType.Hand_R);
        if (rig == null || rig.isKinematic)
            return;

        Vector3 error = GetWrist(hand) - rig.position;
        Vector3 velocityTarget = Vector3.ClampMagnitude(error / HandFollowTime, HandSpeedMax);
        rig.AddForce((velocityTarget - rig.linearVelocity) * HandGain, ForceMode.Acceleration);
    }

    private static void FindSounds(Character c)
    {
        if (soundOwner == c)
            return;

        soundOwner = c;
        soundGrab = null;
        soundMoveA = null;
        soundMoveB = null;
        SFX_PlayOneShot[] sounds = c.GetComponentsInChildren<SFX_PlayOneShot>(true);
        for (int i = 0; i < sounds.Length; i++)
        {
            string name = sounds[i].gameObject.name;
            if (name == "SFX Climb Grab")
                soundGrab = sounds[i];
            else if (name == "SFX Climb Move (1)")
                soundMoveA = sounds[i];
            else if (name == "SFX Climb Move (2)")
                soundMoveB = sounds[i];
        }
    }

    private static void PlayGrabSound(Character c)
    {
        FindSounds(c);
        if (soundGrab != null)
            soundGrab.Play();
    }

    private static void UpdateMoveSound(Character c, float dt)
    {
        moveDistance += c.data.avarageVelocity.magnitude * dt;
        if (moveDistance < MoveSoundDistance)
            return;

        moveDistance = 0f;
        FindSounds(c);
        SFX_PlayOneShot sound = isMoveSoundB ? soundMoveB : soundMoveA;
        isMoveSoundB = !isMoveSoundB;
        if (sound != null)
            sound.Play();
    }

    private static Vector3 GetVirtualPalm(VRClimbHand hand, Vector3 shoulder)
    {
        Transform cam = MainCamera.instance.cam.transform;
        Vector3 offset = cam.rotation * (hand.IsLeft ? ShoulderOffsetLeft : ShoulderOffsetRight);
        Vector3 wrist = shoulder + VRArmIKPatch.ArmScale * (hand.Controller.position - cam.position - offset);
        return wrist + hand.Controller.rotation * PalmOffset;
    }

    private static Vector3 GetPalmDirection(VRClimbHand hand)
    {
        return hand.IsLeft ? hand.Controller.right : -hand.Controller.right;
    }

    private static bool CanStart(Character c)
    {
        CharacterData data = c.data;
        if (data.dead || data.fullyPassedOut || data.passedOut)
            return false;
        if (data.isRopeClimbing || data.isVineClimbing || data.currentClimbHandle != null)
            return false;
        if (data.carrierCondor != null || data.isCarried)
            return false;
        if (!IsHolding && !data.isClimbing && !c.refs.climbing.CanClimb())
            return false;
        return true;
    }

    private static bool IsOwnCollider(Character c, Collider col)
    {
        if (col.isTrigger)
            return true;
        return c.refs.ragdoll.colliderList.Contains(col);
    }

    private static bool CastBest(Character c, Vector3 origin, Vector3 direction, float radius, float distance,
        out RaycastHit best)
    {
        best = default;
        int mask = HelperFunctions.terrainMapMask;
        int count = radius > 0f
            ? Physics.SphereCastNonAlloc(origin, radius, direction, Hits, distance, mask, QueryTriggerInteraction.Ignore)
            : Physics.RaycastNonAlloc(origin, direction, Hits, distance, mask, QueryTriggerInteraction.Ignore);

        float distanceBest = float.MaxValue;
        bool isFound = false;
        for (int i = 0; i < count; i++)
        {
            RaycastHit h = Hits[i];
            if (h.collider == null || IsOwnCollider(c, h.collider))
                continue;
            if (h.distance <= 0f && h.point == Vector3.zero)
                continue;
            if (h.distance >= distanceBest)
                continue;
            distanceBest = h.distance;
            best = h;
            isFound = true;
        }

        return isFound;
    }

    private static bool CastSweep(Character c, VRClimbHand hand, Vector3 palm, out RaycastHit hit)
    {
        hit = default;
        if (!hand.IsPalmLastValid)
            return false;

        Vector3 sweep = palm - hand.PalmLast;
        float distance = sweep.magnitude;
        if (distance < 1e-3f)
            return false;

        return CastBest(c, hand.PalmLast, sweep / distance, SphereRadius, distance, out hit);
    }

    private static bool TryGrab(Character c, VRClimbHand hand)
    {
        if (!hand.HasShoulder || MainCamera.instance == null)
            return false;

        Vector3 shoulder = GetShoulder(c, hand, c.Center);
        Vector3 palm = GetVirtualPalm(hand, shoulder);
        if (TryGrabRope(c, hand, palm))
            return true;
        if (IsHoldingRope || !CanStart(c))
            return false;

        Vector3 palmDirection = GetPalmDirection(hand);
        Vector3 toPalm = palm - shoulder;
        Transform controller = hand.Controller;

        RaycastHit hit = default;
        bool isFound = toPalm.sqrMagnitude > 1e-6f && CastBest(c, shoulder, toPalm.normalized, SphereRadius, toPalm.magnitude, out hit);
        if (!isFound)
            isFound = CastSweep(c, hand, palm, out hit);
        if (!isFound)
            isFound = CastBest(c, palm - palmDirection * SphereBackoff, palmDirection, SphereRadius, SphereBackoff + SphereDistance, out hit);
        if (!isFound)
            isFound = CastBest(c, palm, controller.forward, 0f, RayDistance, out hit);
        if (!isFound)
            isFound = CastBest(c, palm, controller.forward, SphereRadius, RayDistance, out hit);
        if (!isFound)
            return false;

        Vector3 anchor = hit.point + hit.normal * PalmThickness;
        hand.IsRope = false;
        hand.Rope = null;
        hand.GripCollider = hit.collider;
        hand.AnchorLocal = hit.collider.transform.InverseTransformPoint(anchor);
        hand.Normal = hit.normal;
        hand.Residual = Vector3.ClampMagnitude(anchor - palm, ResidualMax);
        hand.IsGripping = true;

        if (!Left.IsGripping || !Right.IsGripping)
            StartClimb(c, anchor);
        else
            PlayGrabSound(c);
        return true;
    }

    private static bool TryGrabRope(Character c, VRClimbHand hand, Vector3 palm)
    {
        CharacterData data = c.data;
        if (data.dead || data.fullyPassedOut || data.passedOut)
            return false;
        if (data.isVineClimbing || data.currentClimbHandle != null || data.carrierCondor != null || data.isCarried)
            return false;
        if (c.OutOfStamina())
            return false;
        if (!FindRope(c, hand, palm, out Rope rope, out float percent))
            return false;

        Rope held = GetHeldRope();
        if (held != null && held != rope)
            return false;
        if (data.isRopeClimbing && data.heldRope != rope)
            return false;

        PhotonView ropeView = rope.GetComponentInParent<PhotonView>();
        if (ropeView == null)
            return false;

        if (!IsHoldingRope)
            ropeForward = GetFlatLook(c);

        hand.IsRope = true;
        hand.Rope = rope;
        hand.RopePercent = percent;
        hand.GripCollider = null;
        hand.Normal = Vector3.zero;
        Vector3 anchor = hand.GetAnchor();
        hand.Residual = Vector3.ClampMagnitude(anchor - palm, ResidualMax);
        hand.IsGripping = true;

        if (IsHolding)
        {
            DropWallHands();
            SendStop(c);
        }

        if (!data.isRopeClimbing)
        {
            c.refs.items.EquipSlot(Optionable<byte>.None);
            int index = Mathf.RoundToInt(percent * Mathf.Max(0, rope.SegmentCount - 1));
            c.refs.view.RPC("GrabRopeRpc", RpcTarget.All, ropeView, index);
            data.ropePercent = percent;
        }
        return true;
    }

    private static bool FindRope(Character c, VRClimbHand hand, Vector3 palm, out Rope rope, out float percent)
    {
        rope = null;
        percent = 0f;
        float distanceBest = RopeRadius * 2f;

        int count = Physics.OverlapSphereNonAlloc(palm, RopeRadius, Overlaps, ~0, QueryTriggerInteraction.Collide);
        for (int i = 0; i < count; i++)
            CheckRope(c, Overlaps[i], palm, ref rope, ref percent, ref distanceBest);
        if (rope != null || !hand.IsPalmLastValid)
            return rope != null;

        Vector3 sweep = palm - hand.PalmLast;
        float distance = sweep.magnitude;
        if (distance < 1e-3f)
            return false;

        Vector3 direction = sweep / distance;
        count = Physics.SphereCastNonAlloc(hand.PalmLast, RopeRadius, direction, Hits, distance, ~0, QueryTriggerInteraction.Collide);
        for (int i = 0; i < count; i++)
        {
            Vector3 center = hand.PalmLast + direction * Hits[i].distance;
            CheckRope(c, Hits[i].collider, center, ref rope, ref percent, ref distanceBest);
        }
        return rope != null;
    }

    private static void CheckRope(Character c, Collider col, Vector3 point, ref Rope rope, ref float percent, ref float distanceBest)
    {
        if (col == null)
            return;
        RopeSegment segment = col.GetComponentInParent<RopeSegment>();
        if (segment == null || segment.rope == null || !segment.rope.isClimbable)
            return;

        float angle = segment.GetAngle();
        float angleMax = c.refs.ropeHandling.maxRopeAngle * 0.6f;
        if (angle >= angleMax && 180f - angle >= angleMax)
            return;

        if (!GetClosestPercent(segment.rope, point, out float p, out float d))
            return;
        if (d >= distanceBest)
            return;

        distanceBest = d;
        rope = segment.rope;
        percent = p;
    }

    private static bool GetClosestPercent(Rope rope, Vector3 point, out float percent, out float distance)
    {
        percent = 0f;
        distance = float.MaxValue;
        List<Transform> segments = rope.GetRopeSegments();
        int n = segments.Count;
        if (n < 3)
            return false;

        for (int k = 1; k < n - 1; k++)
        {
            Vector3 a = segments[k].position;
            Vector3 ab = segments[k + 1].position - a;
            float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector3.Dot(point - a, ab) / ab.sqrMagnitude) : 0f;
            float d = Vector3.Distance(point, a + ab * t);
            if (d >= distance)
                continue;
            distance = d;
            percent = (k + t) / (n - 1);
        }
        return true;
    }

    private static Vector3 GetFlatLook(Character c)
    {
        Vector3 look = c.data.lookDirection_Flat;
        look.y = 0f;
        if (look.sqrMagnitude < 1e-4f)
            return Vector3.forward;
        return look.normalized;
    }

    private static Vector3 GetHandsForward(Character c)
    {
        Vector3 look = GetFlatLook(c);
        if (VRHands.Left == null || VRHands.Right == null)
            return look;

        Vector3 across = Vector3.ProjectOnPlane(VRHands.Right.position - VRHands.Left.position, Vector3.up);
        float weight = Mathf.InverseLerp(HandsAcrossMin, HandsAcrossFull, across.magnitude);
        if (weight <= 0f)
            return look;

        Vector3 perpendicular = Vector3.Cross(across.normalized, Vector3.up);
        Vector3 forward = look * (1f - weight) + perpendicular * weight;
        if (forward.sqrMagnitude < 1e-4f)
            return perpendicular;
        return forward.normalized;
    }

    public static void DriveRope(Character c)
    {
        Rope rope = c.data.heldRope;
        if (rope == null || rope.climbingAPI == null)
            return;

        RopeClimbingAPI api = rope.climbingAPI;
        float dt = Time.fixedDeltaTime;
        Vector3 torso = c.TorsoPos();
        Vector3 delta = Vector3.zero;
        int count = 0;
        if (Left.IsRope && Left.HasAnchor && Left.Rope == rope && Left.HasShoulder)
        {
            delta += GetHandDelta(c, Left, torso);
            count++;
        }
        if (Right.IsRope && Right.HasAnchor && Right.Rope == rope && Right.HasShoulder)
        {
            delta += GetHandDelta(c, Right, torso);
            count++;
        }
        if (count == 0)
            return;
        delta /= count;

        Vector3 velocityTarget = Vector3.ClampMagnitude(delta / FollowTime, SpeedMax);
        c.AddForce((velocityTarget - c.data.avarageVelocity) * VelocityGain);

        if (GetClosestPercent(rope, torso + delta, out float percent, out float distance))
            c.data.ropePercent = percent;

        Vector3 forwardTarget = GetHandsForward(c);
        ropeForward = Vector3.RotateTowards(ropeForward, forwardTarget, RopeTurnSpeed * Mathf.Deg2Rad * dt, 0f);
        ropeForward = Vector3.ProjectOnPlane(ropeForward, Vector3.up).normalized;
        if (ropeForward.sqrMagnitude < 1e-4f)
            ropeForward = forwardTarget;
        c.data.ropeClimbNormal = -ropeForward;
        c.data.ropeClimbWorldNormal = c.data.ropeClimbNormal;
        c.data.ropeClimbWorldUp = api.GetUp(c.data.ropePercent);

        if (Vector3.Dot(c.data.avarageVelocity, Vector3.up) > RopeUpSpeed)
        {
            CharacterRopeHandling handling = c.refs.ropeHandling;
            c.UseStamina(Mathf.Max(0f, handling.staminaUsageUp - handling.staminaUsage) * dt);
        }

        PinHand(c, Left);
        PinHand(c, Right);
        Left.Residual = Vector3.MoveTowards(Left.Residual, Vector3.zero, ResidualRelax * dt);
        Right.Residual = Vector3.MoveTowards(Right.Residual, Vector3.zero, ResidualRelax * dt);
    }

    private static void StartClimb(Character c, Vector3 anchor)
    {
        if (c.data.isClimbing)
            c.data.isClimbing = false;
        c.refs.items.EquipSlot(Optionable<byte>.None);
        Vector3 normal = GetBodyNormal(c);
        c.data.climbNormal = normal;
        c.data.climbPos = c.TorsoPos() - normal * BodyNormalDistance;
        c.data.sinceGrounded = 0f;
        c.refs.view.RPC("StartClimbRpc", RpcTarget.Others, c.data.climbPos, normal);
    }

    private static void SendStop(Character c)
    {
        c.refs.view.RPC("StopClimbingRpc", RpcTarget.Others, 0f);
    }

    private static Vector3 GetBodyNormal(Character c)
    {
        Vector3 normal = Vector3.zero;
        Vector3 anchors = Vector3.zero;
        int count = 0;
        if (Left.IsGripping && Left.GripCollider != null)
        {
            normal += Left.Normal;
            anchors += Left.GetAnchor();
            count++;
        }
        if (Right.IsGripping && Right.GripCollider != null)
        {
            normal += Right.Normal;
            anchors += Right.GetAnchor();
            count++;
        }
        if (count == 0)
            return c.data.climbNormal;

        normal.Normalize();
        if (Vector3.Angle(normal, Vector3.up) >= FloorAngle)
            return normal;

        Vector3 toAnchor = Vector3.ProjectOnPlane(anchors / count - c.TorsoPos(), Vector3.up);
        if (toAnchor.sqrMagnitude < 1e-4f)
            toAnchor = c.data.lookDirection_Flat;
        return -toAnchor.normalized;
    }

    private static void Release(Character c, VRClimbHand hand)
    {
        if (!hand.IsGripping)
            return;

        bool isRope = hand.IsRope;
        DropHand(hand);
        if (isRope)
        {
            if (!IsHoldingRope && c.data.isRopeClimbing)
                c.refs.ropeHandling.Stop();
        }
        else if (!IsHolding)
            SendStop(c);
    }

    private static void DropHand(VRClimbHand hand)
    {
        hand.IsGripping = false;
        hand.GripCollider = null;
        hand.IsRope = false;
        hand.Rope = null;
        hand.AutoGrabLeft = 0f;
    }

    public static void ReleaseAll()
    {
        Character local = Character.localCharacter;
        if (IsHolding && local != null)
            SendStop(local);
        if (IsHoldingRope && local != null && local.data.isRopeClimbing)
            local.refs.ropeHandling.Stop();
        DropHand(Left);
        DropHand(Right);
        Left.IsGripHeld = false;
        Right.IsGripHeld = false;
        Left.IsInputConsumed = false;
        Right.IsInputConsumed = false;
        Left.HasShoulder = false;
        Right.HasShoulder = false;
        Left.IsPalmLastValid = false;
        Right.IsPalmLastValid = false;
        soundOwner = null;
        soundGrab = null;
        soundMoveA = null;
        soundMoveB = null;
        moveDistance = 0f;
    }

    public static void DriveBody(Character c)
    {
        float dt = Time.fixedDeltaTime;
        c.data.isGrounded = false;
        c.data.sinceClimb = 0f;
        if (!c.OutOfStamina())
            c.data.sinceGrounded = 0f;
        else
        {
            SlideHand(c, Left, dt);
            SlideHand(c, Right, dt);
            if (!IsHolding)
                return;
        }

        Vector3 torso = c.TorsoPos();
        Vector3 delta = Vector3.zero;
        int count = 0;
        if (Left.IsGripping && Left.GripCollider != null && Left.HasShoulder)
        {
            delta += GetHandDelta(c, Left, torso);
            count++;
        }
        if (Right.IsGripping && Right.GripCollider != null && Right.HasShoulder)
        {
            delta += GetHandDelta(c, Right, torso);
            count++;
        }
        if (count == 0)
            return;

        delta /= count;
        Vector3 velocityTarget = Vector3.ClampMagnitude(delta / FollowTime, SpeedMax);
        Vector3 velocity = c.data.avarageVelocity;
        c.AddForce((velocityTarget - velocity) * VelocityGain);

        Vector3 normal = GetBodyNormal(c);
        c.data.climbNormal = normal;
        c.data.climbPos = torso + delta - normal * BodyNormalDistance;

        UseStamina(c, velocity.magnitude, normal, dt);
        UpdateMoveSound(c, dt);

        PinHand(c, Left);
        PinHand(c, Right);

        Left.Residual = Vector3.MoveTowards(Left.Residual, Vector3.zero, ResidualRelax * dt);
        Right.Residual = Vector3.MoveTowards(Right.Residual, Vector3.zero, ResidualRelax * dt);
    }

    private static void UseStamina(Character c, float speed, Vector3 normal, float dt)
    {
        CharacterClimbing climbing = c.refs.climbing;
        float usage = Mathf.Lerp(climbing.minStaminaUsage, climbing.maxStaminaUsage, Mathf.Clamp01(speed / InputSpeed));
        float angle = Vector3.Angle(Vector3.up, normal);
        usage *= Mathf.Lerp(0.2f, 1f, Mathf.InverseLerp(40f, 60f, angle));
        usage *= c.data.staminaMod;
        c.UseStamina(usage * dt);
    }

    private static Vector3 GetHandDelta(Character c, VRClimbHand hand, Vector3 torso)
    {
        Vector3 palm = GetVirtualPalm(hand, GetShoulder(c, hand, torso));
        return hand.GetAnchor() - palm - hand.Residual;
    }

    private static void SlideHand(Character c, VRClimbHand hand, float dt)
    {
        if (!hand.IsGripping || hand.GripCollider == null)
            return;

        Vector3 down = Vector3.ProjectOnPlane(Vector3.down, hand.Normal);
        if (down.sqrMagnitude < 1e-4f)
            return;

        Vector3 anchor = hand.GetAnchor() + down.normalized * (SlideSpeed * dt);
        Vector3 origin = anchor + hand.Normal * SlideProbe;
        if (!Physics.Raycast(origin, -hand.Normal, out RaycastHit hit, SlideProbe * 2f,
                HelperFunctions.terrainMapMask, QueryTriggerInteraction.Ignore))
        {
            Release(c, hand);
            return;
        }

        anchor = hit.point + hit.normal * PalmThickness;
        hand.GripCollider = hit.collider;
        hand.AnchorLocal = hit.collider.transform.InverseTransformPoint(anchor);
        hand.Normal = hit.normal;
    }

    public static bool IsLocalHolding(Character c)
    {
        return IsHolding && c != null && c == Character.localCharacter && Plugin.VrEnabled;
    }
}

[HarmonyPatch(typeof(CharacterInput), "Sample")]
internal static class HandClimbGripPatch
{
    [HarmonyPriority(Priority.First)]
    [HarmonyPostfix]
    private static void Postfix(CharacterInput __instance, bool playerMovementActive)
    {
        if (!Plugin.VrEnabled)
            return;

        Character local = Character.localCharacter;
        if (local == null || local.input != __instance || VRControls.RightGrip == null)
            return;

        VRHandClimb.UpdateGrips(local, playerMovementActive);
    }
}

[HarmonyPatch(typeof(CharacterInput), "Sample")]
internal static class HandClimbInputPatch
{
    [HarmonyPriority(Priority.Last)]
    [HarmonyPostfix]
    private static void Postfix(CharacterInput __instance)
    {
        if (!Plugin.VrEnabled || !VRHandClimb.IsHoldingAny)
            return;

        Character local = Character.localCharacter;
        if (local == null || local.input != __instance)
            return;

        __instance.usePrimaryWasPressed = false;
        __instance.usePrimaryIsPressed = false;
        __instance.usePrimaryWasReleased = false;
        __instance.movementInput = Vector2.zero;
        __instance.crouchIsPressed = false;
    }
}

[HarmonyPatch(typeof(CharacterClimbing), "FixedUpdate")]
internal static class HandClimbBodyPatch
{
    [HarmonyPostfix]
    private static void Postfix(CharacterClimbing __instance)
    {
        if (!Plugin.VrEnabled || !VRHandClimb.IsHolding)
            return;

        Character local = Character.localCharacter;
        if (local == null || local.refs.climbing != __instance)
            return;

        VRHandClimb.DriveBody(local);
    }
}

[HarmonyPatch(typeof(CharacterRopeHandling), "Climbing")]
internal static class HandClimbRopePatch
{
    [HarmonyPrefix]
    private static bool Prefix(CharacterRopeHandling __instance)
    {
        if (!Plugin.VrEnabled || !VRHandClimb.IsHoldingRope)
            return true;

        Character local = Character.localCharacter;
        if (local == null || local.refs.ropeHandling != __instance || local.data.heldRope == null)
            return true;

        VRHandClimb.DriveRope(local);
        return false;
    }
}

[HarmonyPatch(typeof(Character), nameof(Character.CheckGravity))]
internal static class HandClimbGravityPatch
{
    [HarmonyPrefix]
    private static bool Prefix(Character __instance, ref bool __result)
    {
        if (!VRHandClimb.IsLocalHolding(__instance))
            return true;

        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(CharacterMovement), "DoGroundChecks")]
internal static class HandClimbGroundPatch
{
    [HarmonyPrefix]
    private static bool Prefix(CharacterMovement __instance, ref bool __result)
    {
        if (!VRHandClimb.IsHolding || Character.localCharacter == null || Character.localCharacter.refs.movement != __instance)
            return true;

        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(ClimbSFX), "Update")]
internal static class HandClimbSoundPatch
{
    [HarmonyPrefix]
    private static void Prefix(Character ___character, out bool __state)
    {
        __state = false;
        if (___character == null || ___character.data.isClimbing || !VRHandClimb.IsLocalHolding(___character))
            return;

        ___character.data.isClimbing = true;
        __state = true;
    }

    [HarmonyPostfix]
    private static void Postfix(Character ___character, bool __state)
    {
        if (__state)
            ___character.data.isClimbing = false;
    }
}

[HarmonyPatch(typeof(CharacterSyncer), nameof(CharacterSyncer.GetDataToWrite))]
internal static class HandClimbSyncPatch
{
    [HarmonyPostfix]
    private static void Postfix(CharacterSyncer __instance, ref CharacterSyncData __result)
    {
        Character local = Character.localCharacter;
        if (local == null || !VRHandClimb.IsLocalHolding(local) || __instance.gameObject != local.gameObject)
            return;

        __result.isClimbing = true;
        __result.isGrounded = false;
        __result.climbPos = local.data.climbPos;
    }
}

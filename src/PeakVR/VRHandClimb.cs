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
    public float Percent;
    public bool IsVine;
    public JungleVine Vine;
    public bool IsHandle;
    public ClimbHandle Handle;

    public Transform Controller => IsLeft ? VRHands.Left : VRHands.Right;
    public InputAction Grip => IsLeft ? VRControls.LeftGrip : VRControls.RightGrip;

    public bool IsObject => IsRope || IsVine || IsHandle;

    public bool HasAnchor
    {
        get
        {
            if (!IsGripping)
                return false;
            if (IsRope)
                return Rope != null;
            if (IsVine)
                return Vine != null && Vine.colliderRoot != null;
            if (IsHandle)
                return Handle != null;
            return GripCollider != null;
        }
    }

    public UnityEngine.Object HeldObject
    {
        get
        {
            if (IsRope)
                return Rope;
            if (IsVine)
                return Vine;
            if (IsHandle)
                return Handle;
            return null;
        }
    }

    public Vector3 GetAnchor()
    {
        if (IsRope)
            return Rope.climbingAPI.GetPosition(Percent);
        if (IsVine)
            return Vine.GetPosition(Percent);
        if (IsHandle)
            return Handle.transform.TransformPoint(AnchorLocal);
        return GripCollider.transform.TransformPoint(AnchorLocal);
    }

    public void ClearKind()
    {
        IsRope = false;
        Rope = null;
        IsVine = false;
        Vine = null;
        IsHandle = false;
        Handle = null;
        GripCollider = null;
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
    private const float VineReach = 0.3f;
    private const float VineHangDistance = 1f;
    private const float VineIdleStamina = 0.005f;
    private const float VinePercentMin = 0.01f;
    private const float VinePercentMax = 0.99f;
    private const int VineTypeChain = 1;
    private const float HandleRadius = 0.12f;
    private const float HandleReach = 0.3f;

    private static Vector3 bodyForward = Vector3.forward;

    private static readonly Collider[] Overlaps = new Collider[16];
    private static readonly List<MeshRenderer> HandleRenderers = new();

    private static readonly AccessTools.FieldRef<CharacterVineClimbing, float> AttachVel =
        AccessTools.FieldRefAccess<CharacterVineClimbing, float>("attachVel");

    private const float MoveSoundDistance = 0.45f;

    private static Character soundOwner;
    private static SFX_PlayOneShot soundGrab;
    private static SFX_PlayOneShot soundMoveA;
    private static SFX_PlayOneShot soundMoveB;
    private static SFX_PlayOneShot soundWallStart;
    private static GameObject surfaceSounds;
    private static float moveDistance;
    private static bool isMoveSoundB;
    private static readonly SFX_PlayOneShot[] SoundsRope = new SFX_PlayOneShot[3];
    private static readonly SFX_PlayOneShot[] SoundsVine = new SFX_PlayOneShot[3];
    private static readonly SFX_PlayOneShot[] SoundsChain = new SFX_PlayOneShot[3];
    private static int indexRopeSound;

    public static readonly VRClimbHand Left = new() { IsLeft = true };
    public static readonly VRClimbHand Right = new() { IsLeft = false };

    private static readonly RaycastHit[] Hits = new RaycastHit[16];

    public static bool Enabled => Plugin.VrEnabled && Plugin.Config != null && Plugin.Config.HandClimbing.Value;

    public static bool HasWallHand => (Left.IsGripping && !Left.IsObject) || (Right.IsGripping && !Right.IsObject);

    public static bool IsHolding => HasWallHand && !IsHoldingObject;

    public static bool IsHoldingRope => (Left.IsGripping && Left.IsRope) || (Right.IsGripping && Right.IsRope);

    public static bool IsHoldingVine => (Left.IsGripping && Left.IsVine) || (Right.IsGripping && Right.IsVine);

    public static bool IsHoldingHandle => (Left.IsGripping && Left.IsHandle) || (Right.IsGripping && Right.IsHandle);

    public static bool IsHoldingObject => (Left.IsGripping && Left.IsObject) || (Right.IsGripping && Right.IsObject);

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

        if (IsHoldingObject && !IsHangValid(c))
        {
            DropHand(Left);
            DropHand(Right);
            StopHang(c);
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

    private static bool IsHangValid(Character c)
    {
        CharacterData data = c.data;
        if (data.dead || data.fullyPassedOut || data.passedOut)
            return false;
        UnityEngine.Object hang = GetVanillaHang(c);
        if (hang == null || !IsHeldByHand(hang))
            return false;
        return data.currentClimbHandle == null || data.currentClimbHandle.hanger == c;
    }

    private static UnityEngine.Object GetVanillaHang(Character c)
    {
        CharacterData data = c.data;
        if (data.isRopeClimbing)
            return data.heldRope;
        if (data.isVineClimbing)
            return data.heldVine;
        if (data.currentClimbHandle != null)
            return data.currentClimbHandle;
        return null;
    }

    private static bool IsHeldByHand(UnityEngine.Object held)
    {
        if (held == null)
            return false;
        if (Left.HasAnchor && Left.HeldObject == held)
            return true;
        return Right.HasAnchor && Right.HeldObject == held;
    }

    private static VRClimbHand GetObjectHand(VRClimbHand except)
    {
        VRClimbHand best = null;
        if (Left != except && Left.HasAnchor && Left.IsObject)
            best = Left;
        if (Right != except && Right.HasAnchor && Right.IsObject && (best == null || GetPriority(Right) > GetPriority(best)))
            best = Right;
        return best;
    }

    private static int GetPriority(VRClimbHand hand)
    {
        if (hand.IsHandle)
            return 3;
        if (hand.IsRope)
            return 2;
        if (hand.IsVine)
            return 1;
        return 0;
    }

    private static int GetHangPriority(Character c)
    {
        CharacterData data = c.data;
        if (data.currentClimbHandle != null)
            return 3;
        if (data.isRopeClimbing)
            return 2;
        if (data.isVineClimbing)
            return 1;
        return 0;
    }

    private static void StopHang(Character c)
    {
        CharacterData data = c.data;
        if (data.isRopeClimbing)
            c.refs.ropeHandling.Stop();
        else if (data.isVineClimbing)
            c.refs.vineClimbing.Stop();
        else if (data.currentClimbHandle != null)
            c.refs.climbing.CancelHandle(false);
    }

    private static bool StartHang(Character c, VRClimbHand hand, bool wasWall)
    {
        CharacterData data = c.data;
        PhotonView objectView = null;
        if (hand.IsRope)
            objectView = hand.Rope.GetComponentInParent<PhotonView>();
        else if (hand.IsVine)
            objectView = hand.Vine.photonView;
        else if (hand.IsHandle && (hand.Handle.hanger == null || hand.Handle.hanger == c))
            objectView = hand.Handle.GetComponent<PhotonView>();
        if (objectView == null)
            return false;

        if (wasWall)
            SendStop(c);
        c.refs.items.EquipSlot(Optionable<byte>.None);

        if (hand.IsRope)
        {
            int index = Mathf.RoundToInt(hand.Percent * Mathf.Max(0, hand.Rope.SegmentCount - 1));
            c.refs.view.RPC("GrabRopeRpc", RpcTarget.All, objectView, index);
            data.ropePercent = hand.Percent;
        }
        else if (hand.IsVine)
        {
            c.refs.view.RPC("GrabVineRpc", RpcTarget.All, objectView, hand.Vine.GetIndexFromPercentage(hand.Percent));
            data.vinePercent = Mathf.Clamp(hand.Percent, VinePercentMin, VinePercentMax);
            AttachVel(c.refs.vineClimbing) = 0f;
        }
        else
            objectView.RPC("RPCA_Hang", RpcTarget.All, c.refs.view);
        return true;
    }

    private static void PlayObjectSound(Character c, VRClimbHand hand)
    {
        if (hand.IsRope)
            PlayRopeSound(c);
        else if (hand.IsVine)
            PlayVineSound(c, hand.Vine);
        else
            PlayGrabSound(c);
    }

    private static bool FinishObjectGrab(Character c, VRClimbHand hand, Vector3 palm, bool wasWall)
    {
        hand.Normal = Vector3.zero;
        hand.Residual = Vector3.ClampMagnitude(hand.GetAnchor() - palm, ResidualMax);
        hand.IsGripping = true;

        bool isHanging = GetVanillaHang(c) != null;
        if (isHanging && GetPriority(hand) <= GetHangPriority(c))
        {
            PlayObjectSound(c, hand);
            return true;
        }

        if (isHanging)
            StopHang(c);
        else
            bodyForward = GetFlatLook(c);

        if (StartHang(c, hand, wasWall))
        {
            if (hand.IsHandle)
                PlayGrabSound(c);
            return true;
        }

        DropHand(hand);
        VRClimbHand other = GetObjectHand(null);
        if (other != null && !StartHang(c, other, false))
            DropHand(other);
        if (!IsHoldingObject && HasWallHand)
            StartClimb(c, Vector3.zero);
        return false;
    }

    private static void DropWallHands()
    {
        if (Left.IsGripping && !Left.IsObject)
            DropHand(Left);
        if (Right.IsGripping && !Right.IsObject)
            DropHand(Right);
    }

    private static bool IsHanging(Character c)
    {
        return IsHoldingObject && GetVanillaHang(c) != null;
    }

    public static bool TryGetHangRotation(Character c, out Quaternion rotation)
    {
        rotation = Quaternion.identity;
        CharacterData data = c.data;
        if (!IsHanging(c) || data.isRopeClimbing)
            return false;
        rotation = Quaternion.LookRotation(bodyForward, Vector3.up);
        return true;
    }

    private static Quaternion GetBodyYaw(Character c)
    {
        if (IsHanging(c))
            return Quaternion.LookRotation(bodyForward, Vector3.up);

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
        soundWallStart = null;
        RestoreSurfaceSounds();
        surfaceSounds = null;
        for (int i = 0; i < SoundsRope.Length; i++)
        {
            SoundsRope[i] = null;
            SoundsVine[i] = null;
            SoundsChain[i] = null;
        }

        SFX_PlayOneShot[] sounds = c.GetComponentsInChildren<SFX_PlayOneShot>(true);
        for (int i = 0; i < sounds.Length; i++)
        {
            string name = sounds[i].gameObject.name;
            Transform parent = sounds[i].transform.parent;
            if (name == "SFX Start Climb" && parent != null && parent.name == "Surface")
            {
                soundWallStart = sounds[i];
                surfaceSounds = parent.gameObject;
            }
            else if (name == "SFX Climb Grab")
                soundGrab = sounds[i];
            else if (name == "SFX Climb Move (1)")
                soundMoveA = sounds[i];
            else if (name == "SFX Climb Move (2)")
                soundMoveB = sounds[i];
            else if (parent != null && parent.name == "Rope" && name.StartsWith("SFX Rope Move "))
                StoreSound(SoundsRope, name.Substring("SFX Rope Move ".Length), 0, sounds[i]);
            else if (parent != null && parent.name == "Vine" && name.StartsWith("SFX Rope Move "))
                StoreSound(SoundsVine, name.Substring("SFX Rope Move ".Length), 0, sounds[i]);
            else if (parent != null && parent.name == "Chain" && name.StartsWith("SFX Chain (") && name.EndsWith(")"))
                StoreSound(SoundsChain, name.Substring("SFX Chain (".Length, name.Length - "SFX Chain (".Length - 1), 1, sounds[i]);
        }
    }

    public static void MuteSurfaceSounds(Character c)
    {
        FindSounds(c);
        if (surfaceSounds != null && surfaceSounds.activeSelf)
            surfaceSounds.SetActive(false);
    }

    public static void RestoreSurfaceSounds()
    {
        if (surfaceSounds == null)
            return;

        if (!surfaceSounds.activeSelf)
        {
            Transform root = surfaceSounds.transform;
            for (int i = 0; i < root.childCount; i++)
            {
                GameObject child = root.GetChild(i).gameObject;
                if (child.name.StartsWith("SFX Climb "))
                    child.SetActive(false);
            }
            surfaceSounds.SetActive(true);
        }
    }

    private static void StoreSound(SFX_PlayOneShot[] target, string number, int first, SFX_PlayOneShot sound)
    {
        if (!int.TryParse(number, out int index))
            return;
        index -= first;
        if (index >= 0 && index < target.Length)
            target[index] = sound;
    }

    private static void PlayVineSound(Character c, JungleVine vine)
    {
        FindSounds(c);
        SFX_PlayOneShot[] sounds = vine != null && vine.vineType == VineTypeChain ? SoundsChain : SoundsVine;
        SFX_PlayOneShot sound = sounds[indexRopeSound];
        indexRopeSound = (indexRopeSound + 1) % sounds.Length;
        if (sound != null)
            sound.Play();
    }

    private static void UpdateVineSound(Character c, float dt)
    {
        moveDistance += c.data.avarageVelocity.magnitude * dt;
        if (moveDistance < MoveSoundDistance)
            return;

        moveDistance = 0f;
        PlayVineSound(c, c.data.heldVine);
    }

    private static void PlayWallStartSound(Character c)
    {
        FindSounds(c);
        if (soundWallStart != null)
            soundWallStart.Play();
        else if (soundGrab != null)
            soundGrab.Play();
    }

    private static void PlayRopeSound(Character c)
    {
        FindSounds(c);
        SFX_PlayOneShot sound = SoundsRope[indexRopeSound];
        indexRopeSound = (indexRopeSound + 1) % SoundsRope.Length;
        if (sound != null)
            sound.Play();
    }

    private static void UpdateRopeSound(Character c, float dt)
    {
        moveDistance += c.data.avarageVelocity.magnitude * dt;
        if (moveDistance < MoveSoundDistance)
            return;

        moveDistance = 0f;
        PlayRopeSound(c);
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
        if (TryGrabVine(c, hand, palm))
            return true;
        if (TryGrabHandle(c, hand, palm))
            return true;
        if (!IsHoldingObject && !CanStart(c))
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
        hand.ClearKind();
        hand.GripCollider = hit.collider;
        hand.AnchorLocal = hit.collider.transform.InverseTransformPoint(anchor);
        hand.Normal = hit.normal;
        hand.Residual = Vector3.ClampMagnitude(anchor - palm, ResidualMax);
        hand.IsGripping = true;

        if (!IsHoldingObject && (!Left.IsGripping || !Right.IsGripping))
            StartClimb(c, anchor);
        else if (IsHoldingObject)
            PlayWallStartSound(c);
        else
            PlayGrabSound(c);
        return true;
    }

    private static bool TryGrabRope(Character c, VRClimbHand hand, Vector3 palm)
    {
        CharacterData data = c.data;
        if (data.dead || data.fullyPassedOut || data.passedOut)
            return false;
        if (data.carrierCondor != null || data.isCarried)
            return false;
        if (c.OutOfStamina())
            return false;
        if (!FindRope(c, hand, palm, out Rope rope, out float percent))
            return false;

        bool wasWall = IsHolding;
        hand.ClearKind();
        hand.IsRope = true;
        hand.Rope = rope;
        hand.Percent = percent;
        return FinishObjectGrab(c, hand, palm, wasWall);
    }

    private static bool TryGrabVine(Character c, VRClimbHand hand, Vector3 palm)
    {
        CharacterData data = c.data;
        if (data.dead || data.fullyPassedOut || data.passedOut)
            return false;
        if (data.carrierCondor != null || data.isCarried)
            return false;
        if (c.OutOfStamina())
            return false;
        if (!FindVine(hand, palm, out JungleVine vine, out float percent))
            return false;

        bool wasWall = IsHolding;
        hand.ClearKind();
        hand.IsVine = true;
        hand.Vine = vine;
        hand.Percent = percent;
        return FinishObjectGrab(c, hand, palm, wasWall);
    }

    private static bool FindVine(VRClimbHand hand, Vector3 palm, out JungleVine vine, out float percent)
    {
        vine = null;
        percent = 0f;
        float distanceBest = VineReach;

        int count = Physics.OverlapSphereNonAlloc(palm, RopeRadius, Overlaps, ~0, QueryTriggerInteraction.Collide);
        for (int i = 0; i < count; i++)
            CheckVine(Overlaps[i], palm, ref vine, ref percent, ref distanceBest);
        if (vine != null || !hand.IsPalmLastValid)
            return vine != null;

        Vector3 sweep = palm - hand.PalmLast;
        float distance = sweep.magnitude;
        if (distance < 1e-3f)
            return false;

        Vector3 direction = sweep / distance;
        count = Physics.SphereCastNonAlloc(hand.PalmLast, RopeRadius, direction, Hits, distance, ~0, QueryTriggerInteraction.Collide);
        for (int i = 0; i < count; i++)
        {
            Vector3 center = hand.PalmLast + direction * Hits[i].distance;
            CheckVine(Hits[i].collider, center, ref vine, ref percent, ref distanceBest);
        }
        return vine != null;
    }

    private static void CheckVine(Collider col, Vector3 point, ref JungleVine vine, ref float percent, ref float distanceBest)
    {
        if (col == null)
            return;
        JungleVine candidate = col.GetComponentInParent<JungleVine>();
        if (candidate == null || candidate.colliderType != JungleVine.ColliderType.Capsule || candidate.colliderRoot == null)
            return;
        if (col.transform.parent != candidate.colliderRoot)
            return;

        if (!GetClosestVinePercent(candidate, point, out float p, out float d))
            return;
        if (d >= distanceBest)
            return;

        distanceBest = d;
        vine = candidate;
        percent = p;
    }

    private static bool GetClosestVinePercent(JungleVine vine, Vector3 point, out float percent, out float distance)
    {
        percent = 0f;
        distance = float.MaxValue;
        Transform root = vine.colliderRoot;
        int n = root.childCount;
        if (n < 2)
            return false;

        for (int k = 0; k < n - 1; k++)
        {
            Vector3 a = root.GetChild(k).position;
            Vector3 ab = root.GetChild(k + 1).position - a;
            float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector3.Dot(point - a, ab) / ab.sqrMagnitude) : 0f;
            float d = Vector3.Distance(point, a + ab * t);
            if (d >= distance)
                continue;
            distance = d;
            percent = (k + t) / (n - 1);
        }
        return true;
    }

    private static bool TryGrabHandle(Character c, VRClimbHand hand, Vector3 palm)
    {
        CharacterData data = c.data;
        if (data.dead || data.fullyPassedOut || data.passedOut)
            return false;
        if (data.carrierCondor != null || data.isCarried)
            return false;
        if (!FindHandle(c, hand, palm, out ClimbHandle handle, out Vector3 point))
            return false;

        bool wasWall = IsHolding;
        hand.ClearKind();
        hand.IsHandle = true;
        hand.Handle = handle;
        hand.AnchorLocal = handle.transform.InverseTransformPoint(point);
        return FinishObjectGrab(c, hand, palm, wasWall);
    }

    private static bool FindHandle(Character c, VRClimbHand hand, Vector3 palm, out ClimbHandle handle, out Vector3 point)
    {
        handle = null;
        point = palm;
        float distanceBest = HandleReach;

        int count = Physics.OverlapSphereNonAlloc(palm, HandleRadius, Overlaps, ~0, QueryTriggerInteraction.Collide);
        for (int i = 0; i < count; i++)
            CheckHandle(c, Overlaps[i], palm, ref handle, ref point, ref distanceBest);
        if (handle != null || !hand.IsPalmLastValid)
            return handle != null;

        Vector3 sweep = palm - hand.PalmLast;
        float distance = sweep.magnitude;
        if (distance < 1e-3f)
            return false;

        Vector3 direction = sweep / distance;
        count = Physics.SphereCastNonAlloc(hand.PalmLast, HandleRadius, direction, Hits, distance, ~0, QueryTriggerInteraction.Collide);
        for (int i = 0; i < count; i++)
        {
            Vector3 center = hand.PalmLast + direction * Hits[i].distance;
            CheckHandle(c, Hits[i].collider, center, ref handle, ref point, ref distanceBest);
        }
        return handle != null;
    }

    private static void CheckHandle(Character c, Collider col, Vector3 palm, ref ClimbHandle handle, ref Vector3 point,
        ref float distanceBest)
    {
        if (col == null)
            return;
        ClimbHandle candidate = col.GetComponentInParent<ClimbHandle>();
        if (candidate == null || candidate == handle)
            return;
        if (candidate.hanger != null && candidate.hanger != c)
            return;

        Vector3 p = GetHandlePoint(candidate, palm);
        float d = Vector3.Distance(palm, p);
        if (d >= distanceBest)
            return;

        distanceBest = d;
        handle = candidate;
        point = p;
    }

    private static Vector3 GetHandlePoint(ClimbHandle handle, Vector3 palm)
    {
        Vector3 best = handle.transform.position;
        float distanceBest = Vector3.Distance(palm, best);
        handle.GetComponentsInChildren(false, HandleRenderers);
        for (int i = 0; i < HandleRenderers.Count; i++)
        {
            MeshRenderer meshRenderer = HandleRenderers[i];
            if (!meshRenderer.enabled)
                continue;
            Vector3 p = meshRenderer.bounds.ClosestPoint(palm);
            float d = Vector3.Distance(palm, p);
            if (d >= distanceBest)
                continue;
            distanceBest = d;
            best = p;
        }
        HandleRenderers.Clear();
        return best;
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
        if (!GetDriveDelta(c, torso, out Vector3 delta))
            return;

        ApplyServo(c, delta);

        if (GetClosestPercent(rope, torso + delta, out float percent, out float distance))
            c.data.ropePercent = percent;

        TurnBody(c, dt);
        c.data.ropeClimbNormal = -bodyForward;
        c.data.ropeClimbWorldNormal = c.data.ropeClimbNormal;
        c.data.ropeClimbWorldUp = api.GetUp(c.data.ropePercent);

        if (Vector3.Dot(c.data.avarageVelocity, Vector3.up) > RopeUpSpeed)
        {
            CharacterRopeHandling handling = c.refs.ropeHandling;
            c.UseStamina(Mathf.Max(0f, handling.staminaUsageUp - handling.staminaUsage) * dt);
        }

        UpdateRopeSound(c, dt);
        FinishHang(c, dt);
    }

    public static void DriveVine(Character c)
    {
        JungleVine vine = c.data.heldVine;
        if (vine == null || vine.colliderRoot == null)
            return;

        CharacterVineClimbing vineClimbing = c.refs.vineClimbing;
        AttachVel(vineClimbing) = 0f;

        float dt = Time.fixedDeltaTime;
        Vector3 torso = c.TorsoPos();
        if (!GetDriveDelta(c, torso, out Vector3 delta))
            return;

        ApplyServo(c, delta);

        if (GetClosestVinePercent(vine, torso + delta + Vector3.up * VineHangDistance, out float percent, out float distance))
            c.data.vinePercent = Mathf.Clamp(percent, VinePercentMin, VinePercentMax);

        TurnBody(c, dt);

        if (c.data.avarageVelocity.magnitude > RopeUpSpeed)
            c.UseStamina(Mathf.Max(0f, vineClimbing.staminaUsage - VineIdleStamina) * dt);

        UpdateVineSound(c, dt);
        FinishHang(c, dt);
    }

    public static void DriveHandle(Character c)
    {
        ClimbHandle handle = c.data.currentClimbHandle;
        if (handle == null)
            return;

        c.data.sinceGrounded = 0f;
        float dt = Time.fixedDeltaTime;
        Vector3 torso = c.TorsoPos();
        if (!GetDriveDelta(c, torso, out Vector3 delta))
            return;

        ApplyServo(c, delta);
        TurnBody(c, dt);
        FinishHang(c, dt);
    }

    private static bool GetDriveDelta(Character c, Vector3 torso, out Vector3 delta)
    {
        delta = Vector3.zero;
        int count = 0;
        if (IsDriving(Left))
        {
            delta += GetHandDelta(c, Left, torso);
            count++;
        }
        if (IsDriving(Right))
        {
            delta += GetHandDelta(c, Right, torso);
            count++;
        }
        if (count == 0)
            return false;
        delta /= count;
        return true;
    }

    private static void ApplyServo(Character c, Vector3 delta)
    {
        Vector3 velocityTarget = Vector3.ClampMagnitude(delta / FollowTime, SpeedMax);
        c.AddForce((velocityTarget - c.data.avarageVelocity) * VelocityGain);
    }

    private static void TurnBody(Character c, float dt)
    {
        Vector3 forwardTarget = GetHandsForward(c);
        bodyForward = Vector3.RotateTowards(bodyForward, forwardTarget, RopeTurnSpeed * Mathf.Deg2Rad * dt, 0f);
        bodyForward = Vector3.ProjectOnPlane(bodyForward, Vector3.up).normalized;
        if (bodyForward.sqrMagnitude < 1e-4f)
            bodyForward = forwardTarget;
    }

    private static void FinishHang(Character c, float dt)
    {
        PinHand(c, Left);
        PinHand(c, Right);
        Left.Residual = Vector3.MoveTowards(Left.Residual, Vector3.zero, ResidualRelax * dt);
        Right.Residual = Vector3.MoveTowards(Right.Residual, Vector3.zero, ResidualRelax * dt);
    }

    private static bool IsDriving(VRClimbHand hand)
    {
        return hand.HasAnchor && hand.HasShoulder;
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

        bool isObject = hand.IsObject;
        DropHand(hand);
        if (isObject)
        {
            if (IsHeldByHand(GetVanillaHang(c)))
                return;

            StopHang(c);
            VRClimbHand other = GetObjectHand(null);
            if (other != null)
            {
                if (StartHang(c, other, false))
                    return;
                DropHand(other);
            }
            if (HasWallHand)
                StartClimb(c, Vector3.zero);
        }
        else if (!IsHoldingObject && !HasWallHand)
            SendStop(c);
    }

    private static void DropHand(VRClimbHand hand)
    {
        hand.IsGripping = false;
        hand.ClearKind();
        hand.AutoGrabLeft = 0f;
    }

    public static void ReleaseAll()
    {
        Character local = Character.localCharacter;
        if (IsHolding && local != null)
            SendStop(local);
        if (IsHoldingObject && local != null)
            StopHang(local);
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
        RestoreSurfaceSounds();
        surfaceSounds = null;
        VRClimbPose.Clear();
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

    public static Vector2 GetClimbAnimInput(Character c)
    {
        Vector3 normal = c.data.climbNormal;
        if (normal.sqrMagnitude < 1e-4f)
            return Vector2.zero;

        Vector3 right = Vector3.Cross(Vector3.up, -normal);
        Vector3 up = Vector3.ProjectOnPlane(Vector3.up, normal);
        if (right.sqrMagnitude < 1e-4f || up.sqrMagnitude < 1e-4f)
            return Vector2.zero;

        Vector3 velocity = c.data.avarageVelocity;
        float x = Mathf.Clamp(Vector3.Dot(velocity, right.normalized) / InputSpeed, -1f, 1f);
        float y = Mathf.Clamp(Vector3.Dot(velocity, up.normalized) / InputSpeed, -1f, 1f);
        return new Vector2(x, y);
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

        if (!VRHandClimb.IsHoldingHandle)
            return;

        __instance.interactWasPressed = false;
        __instance.interactIsPressed = false;
        __instance.interactWasReleased = false;
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

[HarmonyPatch(typeof(CharacterAnimations), "Update")]
internal static class HandClimbAnimationPatch
{
    private static readonly int ClimbSurface = Animator.StringToHash("Climb Surface");
    private static readonly int InputX = Animator.StringToHash("Input X");
    private static readonly int InputY = Animator.StringToHash("Input Y");

    [HarmonyPostfix]
    private static void Postfix(CharacterAnimations __instance)
    {
        Character local = Character.localCharacter;
        if (local == null || local.refs.animations != __instance)
            return;
        if (!VRHandClimb.IsLocalHolding(local))
        {
            VRHandClimb.RestoreSurfaceSounds();
            return;
        }

        VRHandClimb.MuteSurfaceSounds(local);
        Animator animator = local.refs.animator;
        Vector2 input = VRHandClimb.GetClimbAnimInput(local);
        animator.SetBool(ClimbSurface, true);
        animator.SetFloat(InputX, input.x, 0.125f, Time.deltaTime);
        animator.SetFloat(InputY, input.y, 0.125f, Time.deltaTime);
    }
}

[HarmonyPatch(typeof(Interaction), nameof(Interaction.DoInteractableRaycasts))]
internal static class HandClimbInteractPatch
{
    [HarmonyPostfix]
    private static void Postfix(ref IInteractible interactableResult)
    {
        if (!VRHandClimb.Enabled || interactableResult == null)
            return;
        if (interactableResult is RopeSegment || interactableResult is JungleVine || interactableResult is ClimbHandle)
            interactableResult = null;
    }
}

[HarmonyPatch(typeof(CharacterVineClimbing), "Climbing")]
internal static class HandClimbVinePatch
{
    [HarmonyPrefix]
    private static bool Prefix(CharacterVineClimbing __instance)
    {
        if (!Plugin.VrEnabled || !VRHandClimb.IsHoldingVine)
            return true;

        Character local = Character.localCharacter;
        if (local == null || local.refs.vineClimbing != __instance || local.data.heldVine == null)
            return true;

        VRHandClimb.DriveVine(local);
        return false;
    }
}

[HarmonyPatch(typeof(CharacterClimbing), "HandleClimbHandle")]
internal static class HandClimbHandlePatch
{
    [HarmonyPrefix]
    private static bool Prefix(CharacterClimbing __instance)
    {
        if (!Plugin.VrEnabled || !VRHandClimb.IsHoldingHandle)
            return true;

        Character local = Character.localCharacter;
        if (local == null || local.refs.climbing != __instance || local.data.currentClimbHandle == null)
            return true;

        VRHandClimb.DriveHandle(local);
        return false;
    }
}

[HarmonyPatch(typeof(Character), "SetRotation")]
internal static class HandClimbRotationPatch
{
    [HarmonyPostfix]
    private static void Postfix(Character __instance)
    {
        if (!Plugin.VrEnabled || __instance != Character.localCharacter || __instance.data.carrier != null)
            return;
        if (!VRHandClimb.TryGetHangRotation(__instance, out Quaternion rotation))
            return;

        __instance.refs.rigCreator.transform.rotation = rotation;
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

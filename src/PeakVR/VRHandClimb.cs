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

    public Transform Controller => IsLeft ? VRHands.Left : VRHands.Right;
    public InputAction Grip => IsLeft ? VRControls.LeftGrip : VRControls.RightGrip;

    public Vector3 GetAnchor()
    {
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

    private static FootStepPlayer footSteps;
    private static Character footStepsOwner;

    public static readonly VRClimbHand Left = new() { IsLeft = true };
    public static readonly VRClimbHand Right = new() { IsLeft = false };

    private static readonly RaycastHit[] Hits = new RaycastHit[16];

    public static bool Enabled => Plugin.VrEnabled && Plugin.Config != null && Plugin.Config.HandClimbing.Value;

    public static bool IsHolding => Left.IsGripping || Right.IsGripping;

    public static bool IsConsumed(bool isLeft)
    {
        return (isLeft ? Left : Right).IsInputConsumed;
    }

    public static void UpdateGrips(Character c, bool playerMovementActive)
    {
        if (IsHolding && ShouldLetGo(c))
        {
            DropHand(Left);
            DropHand(Right);
            SendStop(c);
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

        if (hand.IsGripping && hand.GripCollider == null)
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

    private static Quaternion GetBodyYaw(Character c)
    {
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
        if (!hand.IsGripping || hand.GripCollider == null || hand.Controller == null)
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
        if (!hand.IsGripping || hand.GripCollider == null || hand.Controller == null)
            return;

        Rigidbody rig = c.GetBodypartRig(hand.IsLeft ? BodypartType.Hand_L : BodypartType.Hand_R);
        if (rig == null || rig.isKinematic)
            return;

        Vector3 error = GetWrist(hand) - rig.position;
        Vector3 velocityTarget = Vector3.ClampMagnitude(error / HandFollowTime, HandSpeedMax);
        rig.AddForce((velocityTarget - rig.linearVelocity) * HandGain, ForceMode.Acceleration);
    }

    private static void PlayGrabSound(Character c, Collider col, Vector3 position)
    {
        if (footSteps == null || footStepsOwner != c)
        {
            footSteps = c.GetComponentInChildren<FootStepPlayer>(true);
            footStepsOwner = c;
        }
        if (footSteps == null || footSteps.surfaceLookup == null)
            return;

        footSteps.surfaceLookup.PlayStep(position, GetSurfaceIndex(footSteps, col));
    }

    private static int GetSurfaceIndex(FootStepPlayer steps, Collider col)
    {
        MeshRenderer renderer = col.GetComponent<MeshRenderer>();
        if (renderer == null || renderer.sharedMaterial == null)
            return 0;

        string name = renderer.sharedMaterial.name.Replace(" (Instance)", "");
        if (IsNamed(steps.beachSand, name) || IsNamed(steps.desertSand, name))
            return 1;
        if (IsNamed(steps.beachRock, name))
            return 2;
        if (IsAnyNamed(steps.jungleGrass, name))
            return 3;
        if (IsNamed(steps.jungleRock, name))
            return 4;
        if (IsNamed(steps.iceRock, name))
            return 5;
        if (IsNamed(steps.iceSnow, name))
            return 6;
        if (IsAnyNamed(steps.metal, name))
            return 7;
        if (IsAnyNamed(steps.wood, name))
            return 8;
        if (IsNamed(steps.volcanoRock, name))
            return 9;
        return 0;
    }

    private static bool IsNamed(Material material, string name)
    {
        return material != null && material.name == name;
    }

    private static bool IsAnyNamed(Material[] materials, string name)
    {
        if (materials == null)
            return false;
        for (int i = 0; i < materials.Length; i++)
        {
            if (IsNamed(materials[i], name))
                return true;
        }
        return false;
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
        if (!CanStart(c))
            return false;

        Vector3 shoulder = GetShoulder(c, hand, c.Center);
        Vector3 palm = GetVirtualPalm(hand, shoulder);
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
        hand.GripCollider = hit.collider;
        hand.AnchorLocal = hit.collider.transform.InverseTransformPoint(anchor);
        hand.Normal = hit.normal;
        hand.Residual = Vector3.ClampMagnitude(anchor - palm, ResidualMax);
        hand.IsGripping = true;

        if (!Left.IsGripping || !Right.IsGripping)
            StartClimb(c, anchor);
        else
            PlayGrabSound(c, hit.collider, anchor);
        return true;
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

        DropHand(hand);
        if (!IsHolding)
            SendStop(c);
    }

    private static void DropHand(VRClimbHand hand)
    {
        hand.IsGripping = false;
        hand.GripCollider = null;
        hand.AutoGrabLeft = 0f;
    }

    public static void ReleaseAll()
    {
        Character local = Character.localCharacter;
        if (IsHolding && local != null)
            SendStop(local);
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
        footSteps = null;
        footStepsOwner = null;
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
        if (!Plugin.VrEnabled || !VRHandClimb.IsHolding)
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

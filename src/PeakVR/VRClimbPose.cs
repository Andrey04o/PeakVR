using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace PeakVR;

internal class VRPoseBone
{
    public Transform Real;
    public Transform Mirror;
    public Quaternion Open;
    public Quaternion Closed;
    public Quaternion Like;
    public bool IsIndex;
    public bool IsThumb;
}

internal class VRFingerState
{
    public float CurlIndex;
    public float CurlGrip;
    public float CurlThumb;
    public float WeightFree;
    public float WeightLike;
}

internal static class VRClimbPose
{
    private const string ClipFallName = "A_Scout_Fall_Neutral";
    private const string ClipWallName = "A_Scout_Climb_Idle";
    private const string ClipHoldName = "A_Scout_Hands";
    private const string ClipLikeName = "A_Scout_Emote_ThumbsUp";
    private const float HoldClosedTime = 0.5f;
    private const float LikeTime = 0.5f;
    private const float LikePress = 0.8f;
    private const float CurlSpeed = 12f;
    private const float BlendTime = 0.2f;
    private const float LegWallAngle = 35f;
    private const float LegNearAngle = 65f;
    private const float RopeFootDrop = 0.9f;
    private const float RopeLegStrength = 0.7f;
    private const float LegFarAngle = 0f;
    private const float LegAimStrength = 0.8f;
    private const int LegLeanSteps = 4;
    private const float LegClearance = 0.08f;

    private static readonly BodypartType[] LegParts =
    {
        BodypartType.Hip_L, BodypartType.Leg_L, BodypartType.Knee_L, BodypartType.Foot_L,
        BodypartType.Hip_R, BodypartType.Leg_R, BodypartType.Knee_R, BodypartType.Foot_R
    };

    private static readonly string[] FingerNames = { "Hand_Upper_", "Thumb_", "Index_", "Middle_", "Ring_", "Pinky_" };

    private static Character owner;
    private static GameObject mirrorRoot;
    private static AnimationClip clipFall;
    private static AnimationClip clipWall;
    private static AnimationClip clipHold;
    private static AnimationClip clipLike;
    private static Transform legLeft;
    private static Transform footLeft;
    private static Transform legRight;
    private static Transform footRight;
    private static readonly List<VRPoseBone> Legs = new();
    private static readonly List<VRPoseBone> FingersLeft = new();
    private static readonly List<VRPoseBone> FingersRight = new();
    private static readonly Dictionary<Transform, Transform> MirrorMap = new();

    private static float weightLegs;
    private static float weightWallLeft;
    private static float weightWallRight;
    private static readonly VRFingerState StateLeft = new();
    private static readonly VRFingerState StateRight = new();
    private static float poseTime;
    private static Vector3 legInto;
    private static Vector3 legForward = Vector3.forward;
    private static float weightRope;
    private static Vector3 ropeFoot;
    private static Vector3 animOffset;

    public static void Apply(Character c)
    {
        bool isAny = VRHandClimb.IsHoldingAny;
        bool isLegs = UpdateInto(c);
        bool isRope = UpdateRope(c);
        float step = Time.fixedDeltaTime / BlendTime;
        weightLegs = Mathf.MoveTowards(weightLegs, isLegs ? 1f : 0f, step);
        weightRope = Mathf.MoveTowards(weightRope, isRope ? 1f : 0f, step);
        weightWallLeft = Mathf.MoveTowards(weightWallLeft, isAny && IsWallHand(VRHandClimb.Left) ? 1f : 0f, step);
        weightWallRight = Mathf.MoveTowards(weightWallRight, isAny && IsWallHand(VRHandClimb.Right) ? 1f : 0f, step);

        if (!Prepare(c))
            return;

        float stepCurl = Time.fixedDeltaTime * CurlSpeed;
        UpdateFingerState(StateLeft, true, VRHandClimb.Left, VRLeftHand.Carried != null, step, stepCurl);
        UpdateFingerState(StateRight, false, VRHandClimb.Right, c.data.currentItem != null, step, stepCurl);
        ApplyFingers(FingersLeft, StateLeft);
        ApplyFingers(FingersRight, StateRight);

        if (weightLegs <= 0f && weightRope <= 0f && weightWallLeft <= 0f && weightWallRight <= 0f)
        {
            poseTime = 0f;
            return;
        }

        poseTime += Time.fixedDeltaTime;
        animOffset = Vector3.zero;
        if (c.refs.animationHipTransform != null && c.refs.hip != null)
            animOffset = c.refs.hip.Rig.position - c.refs.animationHipTransform.position;

        if (weightRope > 0f)
            AimLegsToRope();

        if (weightLegs > 0f && clipFall != null)
        {
            Sample(clipFall, true);
            BlendBones(Legs, weightLegs);
            LeanLegs();
        }

        if ((weightWallLeft > 0f || weightWallRight > 0f) && clipWall != null)
        {
            Sample(clipWall, false);
            BlendBones(FingersLeft, weightWallLeft);
            BlendBones(FingersRight, weightWallRight);
        }
    }

    private static void UpdateFingerState(VRFingerState state, bool isLeft, VRClimbHand hand, bool isHolding, float step,
        float stepCurl)
    {
        float grip = VRControls.GripValue(isLeft);
        float trigger = VRControls.TriggerValue(isLeft);
        bool isTouched = VRControls.IsThumbTouched(isLeft);
        bool isFree = !hand.IsGripping;
        bool isLike = isFree && !isHolding && !isTouched && grip >= LikePress && trigger >= LikePress;
        state.CurlIndex = Mathf.MoveTowards(state.CurlIndex, trigger, stepCurl);
        state.CurlGrip = Mathf.MoveTowards(state.CurlGrip, isHolding ? 1f : grip, stepCurl);
        state.CurlThumb = Mathf.MoveTowards(state.CurlThumb, isTouched ? 1f : 0f, stepCurl);
        state.WeightFree = Mathf.MoveTowards(state.WeightFree, isFree ? 1f : 0f, step);
        state.WeightLike = Mathf.MoveTowards(state.WeightLike, isLike ? 1f : 0f, stepCurl);
    }

    private static void ApplyFingers(List<VRPoseBone> bones, VRFingerState state)
    {
        if (state.WeightFree <= 0f)
            return;

        for (int i = 0; i < bones.Count; i++)
        {
            VRPoseBone bone = bones[i];
            if (bone.Real == null)
                continue;
            float curl = bone.IsIndex ? state.CurlIndex : bone.IsThumb ? state.CurlThumb : state.CurlGrip;
            Quaternion target = Quaternion.Slerp(bone.Open, bone.Closed, curl);
            if (state.WeightLike > 0f)
                target = Quaternion.Slerp(target, bone.Like, state.WeightLike);
            bone.Real.localRotation = Quaternion.Slerp(bone.Real.localRotation, target, state.WeightFree);
        }
    }

    private static void CachePoses()
    {
        if (clipHold != null)
        {
            clipHold.SampleAnimation(mirrorRoot, 0f);
            StorePose(FingersLeft, 0);
            StorePose(FingersRight, 0);
            clipHold.SampleAnimation(mirrorRoot, clipHold.length * HoldClosedTime);
            StorePose(FingersLeft, 1);
            StorePose(FingersRight, 1);
        }
        else
        {
            StorePose(FingersLeft, 0);
            StorePose(FingersRight, 0);
            StorePose(FingersLeft, 1);
            StorePose(FingersRight, 1);
        }

        if (clipLike == null)
        {
            CopyLike(FingersLeft);
            CopyLike(FingersRight);
            return;
        }

        clipLike.SampleAnimation(mirrorRoot, clipLike.length * LikeTime);
        Dictionary<string, Quaternion> left = new();
        for (int i = 0; i < FingersLeft.Count; i++)
        {
            VRPoseBone bone = FingersLeft[i];
            bone.Like = bone.Mirror.localRotation;
            left[GetFingerKey(bone.Mirror.name)] = bone.Like;
        }
        for (int i = 0; i < FingersRight.Count; i++)
        {
            VRPoseBone bone = FingersRight[i];
            bone.Like = left.TryGetValue(GetFingerKey(bone.Mirror.name), out Quaternion q)
                ? new Quaternion(q.x, -q.y, -q.z, q.w)
                : bone.Open;
        }
    }

    private static void StorePose(List<VRPoseBone> bones, int slot)
    {
        for (int i = 0; i < bones.Count; i++)
        {
            VRPoseBone bone = bones[i];
            if (slot == 0)
                bone.Open = bone.Mirror.localRotation;
            else
                bone.Closed = bone.Mirror.localRotation;
        }
    }

    private static void CopyLike(List<VRPoseBone> bones)
    {
        for (int i = 0; i < bones.Count; i++)
            bones[i].Like = bones[i].Open;
    }

    private static string GetFingerKey(string name)
    {
        return name.Replace("_L", "").Replace("_R", "");
    }

    public static void Clear()
    {
        if (mirrorRoot != null)
            Object.Destroy(mirrorRoot);
        mirrorRoot = null;
        owner = null;
        clipFall = null;
        clipWall = null;
        clipHold = null;
        clipLike = null;
        legLeft = null;
        footLeft = null;
        legRight = null;
        footRight = null;
        Legs.Clear();
        FingersLeft.Clear();
        FingersRight.Clear();
        MirrorMap.Clear();
        weightLegs = 0f;
        weightWallLeft = 0f;
        weightWallRight = 0f;
        ResetState(StateLeft);
        ResetState(StateRight);
        poseTime = 0f;
        legInto = Vector3.zero;
        legForward = Vector3.forward;
        weightRope = 0f;
        ropeFoot = Vector3.zero;
    }

    private static void ResetState(VRFingerState state)
    {
        state.CurlIndex = 0f;
        state.CurlGrip = 0f;
        state.CurlThumb = 0f;
        state.WeightFree = 0f;
        state.WeightLike = 0f;
    }

    private static bool IsWallHand(VRClimbHand hand)
    {
        return hand.IsGripping && !hand.IsObject;
    }

    private static void Sample(AnimationClip clip, bool isLooped)
    {
        float length = clip.length;
        float time = isLooped && length > 0f ? poseTime % length : 0f;
        clip.SampleAnimation(mirrorRoot, time);
    }

    private static void BlendBones(List<VRPoseBone> bones, float weight)
    {
        if (weight <= 0f)
            return;

        for (int i = 0; i < bones.Count; i++)
        {
            VRPoseBone bone = bones[i];
            if (bone.Real == null)
                continue;
            bone.Real.localRotation = Quaternion.Slerp(bone.Real.localRotation, bone.Mirror.localRotation, weight);
        }
    }

    private static bool UpdateInto(Character c)
    {
        if (!VRHandClimb.IsLocalHolding(c))
            return false;

        Vector3 normal = c.data.climbNormal;
        if (normal.sqrMagnitude > 1e-4f)
            legInto = -normal.normalized;
        Vector3 forward = Vector3.ProjectOnPlane(c.data.lookDirection_Flat, Vector3.up);
        if (forward.sqrMagnitude > 1e-4f)
            legForward = forward.normalized;
        return true;
    }

    private static bool UpdateRope(Character c)
    {
        if (!VRHandClimb.TryGetRopeLine(c, out Vector3 point, out Vector3 up))
            return false;

        ropeFoot = point - up.normalized * RopeFootDrop;
        return true;
    }

    private static void AimLegsToRope()
    {
        if (legLeft == null || legRight == null || footLeft == null || footRight == null)
            return;

        AimLeg(legLeft, footLeft, ropeFoot - (legLeft.position + animOffset), RopeLegStrength * weightRope);
        AimLeg(legRight, footRight, ropeFoot - (legRight.position + animOffset), RopeLegStrength * weightRope);
    }

    private static void AimLeg(Transform leg, Transform foot, Vector3 aim, float strength)
    {
        Vector3 current = foot.position - leg.position;
        if (current.sqrMagnitude < 1e-6f || aim.sqrMagnitude < 1e-6f)
            return;

        current.Normalize();
        Vector3 target = Vector3.Slerp(current, aim.normalized, strength);
        leg.rotation = Quaternion.FromToRotation(current, target) * leg.rotation;
    }

    private static void LeanLegs()
    {
        if (legLeft == null || legRight == null)
            return;

        float side = 0f;
        Vector3 across = legLeft.position - legRight.position;
        Vector3 flat = Vector3.ProjectOnPlane(legInto, Vector3.up);
        if (flat.sqrMagnitude > 1e-4f && across.sqrMagnitude > 1e-6f)
            side = Vector3.Dot(across.normalized, flat.normalized) * Mathf.Clamp01(flat.magnitude * 2f);
        LeanLeg(legLeft, footLeft, GetLegAngle(side));
        LeanLeg(legRight, footRight, GetLegAngle(-side));
    }

    private static float GetLegAngle(float side)
    {
        if (side >= 0f)
            return Mathf.Lerp(LegWallAngle, LegNearAngle, side);
        return Mathf.Lerp(LegWallAngle, LegFarAngle, -side);
    }

    private static void LeanLeg(Transform leg, Transform foot, float angle)
    {
        if (leg == null || foot == null)
            return;

        Vector3 dir = foot.position - leg.position;
        float length = dir.magnitude;
        if (length < 1e-3f)
            return;

        Vector3 aim = Vector3.down;
        Vector3 axis = Vector3.Cross(Vector3.down, legInto);
        if (axis.sqrMagnitude < 1e-4f)
            axis = Vector3.Cross(Vector3.down, legForward);
        if (legInto.sqrMagnitude > 1e-4f && axis.sqrMagnitude > 1e-4f)
        {
            axis.Normalize();
            float span = Vector3.Angle(Vector3.down, legInto);
            float total = Mathf.Min(angle * span / 90f, span);
            for (int i = LegLeanSteps; i > 0; i--)
            {
                Vector3 test = Quaternion.AngleAxis(total * i / LegLeanSteps, axis) * Vector3.down;
                if (Physics.Raycast(leg.position + animOffset, test, length + LegClearance, HelperFunctions.terrainMapMask,
                        QueryTriggerInteraction.Ignore))
                    continue;

                aim = test;
                break;
            }
        }

        AimLeg(leg, foot, aim, LegAimStrength * weightLegs);
    }

    private static bool Prepare(Character c)
    {
        if (owner == c && mirrorRoot != null)
            return true;

        Clear();

        Animator animator = c.refs.animator;
        CharacterRagdoll ragdoll = c.refs.ragdoll;
        if (animator == null || ragdoll == null || animator.runtimeAnimatorController == null)
            return false;

        AnimationClip[] clips = animator.runtimeAnimatorController.animationClips;
        for (int i = 0; i < clips.Length; i++)
        {
            AnimationClip clip = clips[i];
            if (clip == null)
                continue;
            if (clip.name == ClipFallName)
                clipFall = clip;
            else if (clip.name == ClipWallName)
                clipWall = clip;
            else if (clip.name == ClipHoldName)
                clipHold = clip;
            else if (clip.name == ClipLikeName)
                clipLike = clip;
        }

        owner = c;
        mirrorRoot = new GameObject("PeakVR Pose Mirror");
        mirrorRoot.hideFlags = HideFlags.HideAndDontSave;
        Object.DontDestroyOnLoad(mirrorRoot);
        Animator mirrorAnimator = mirrorRoot.AddComponent<Animator>();
        mirrorAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        MirrorMap[animator.transform] = mirrorRoot.transform;

        for (int i = 0; i < LegParts.Length; i++)
        {
            if (!ragdoll.partDict.TryGetValue(LegParts[i], out Bodypart part) || part == null)
                continue;
            AddBone(Legs, part.transform, animator.transform);
        }

        legLeft = GetPart(ragdoll, BodypartType.Leg_L);
        footLeft = GetPart(ragdoll, BodypartType.Foot_L);
        legRight = GetPart(ragdoll, BodypartType.Leg_R);
        footRight = GetPart(ragdoll, BodypartType.Foot_R);

        AddFingers(FingersLeft, GetPart(ragdoll, BodypartType.Hand_L), "_L", animator.transform);
        AddFingers(FingersRight, GetPart(ragdoll, BodypartType.Hand_R), "_R", animator.transform);
        CachePoses();

        MirrorMap.Clear();
        return true;
    }

    private static Transform GetPart(CharacterRagdoll ragdoll, BodypartType type)
    {
        if (ragdoll.partDict.TryGetValue(type, out Bodypart part) && part != null)
            return part.transform;
        return null;
    }

    private static void AddFingers(List<VRPoseBone> bones, Transform hand, string side, Transform root)
    {
        if (hand == null)
            return;

        Transform[] children = hand.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            Transform child = children[i];
            if (child == hand || !IsSideName(child.name, side) || !IsFingerName(child.name))
                continue;
            AddBone(bones, child, root);
            if (bones.Count == 0 || bones[bones.Count - 1].Real != child)
                continue;
            bones[bones.Count - 1].IsIndex = child.name.StartsWith("Index_");
            bones[bones.Count - 1].IsThumb = child.name.StartsWith("Thumb_");
        }
    }

    private static bool IsSideName(string name, string side)
    {
        if (name.EndsWith("_end"))
            return false;
        return name.EndsWith(side) || name.Contains(side + "_");
    }

    private static bool IsFingerName(string name)
    {
        for (int i = 0; i < FingerNames.Length; i++)
        {
            if (name.StartsWith(FingerNames[i]))
                return true;
        }
        return false;
    }

    private static void AddBone(List<VRPoseBone> bones, Transform real, Transform root)
    {
        Transform mirror = GetMirror(real, root);
        if (mirror == null)
            return;
        bones.Add(new VRPoseBone { Real = real, Mirror = mirror });
    }

    private static Transform GetMirror(Transform real, Transform root)
    {
        if (real == null)
            return null;
        if (MirrorMap.TryGetValue(real, out Transform found))
            return found;
        if (real == root)
            return mirrorRoot.transform;

        Transform parent = GetMirror(real.parent, root);
        if (parent == null)
            return null;

        GameObject go = new GameObject(real.name);
        go.hideFlags = HideFlags.HideAndDontSave;
        Transform mirror = go.transform;
        mirror.SetParent(parent, false);
        mirror.localPosition = real.localPosition;
        mirror.localRotation = real.localRotation;
        mirror.localScale = real.localScale;
        MirrorMap[real] = mirror;
        return mirror;
    }
}

[HarmonyPatch(typeof(CharacterAnimations), nameof(CharacterAnimations.ConfigureIK))]
internal static class ClimbPosePatch
{
    [HarmonyPostfix]
    private static void Postfix(CharacterAnimations __instance)
    {
        if (!Plugin.VrEnabled)
            return;

        Character local = Character.localCharacter;
        if (local == null || local.refs.animations != __instance || local.data.dead)
            return;

        VRClimbPose.Apply(local);
    }
}

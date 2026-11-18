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

internal class VRClimbPose
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
    private const float NetBlendSpeed = 20f;
    private const float LegWallAngle = 35f;
    private const float LegNearAngle = 65f;
    private const float RopeFootDrop = 0.9f;
    private const float RopeLegStrength = 0.7f;
    private const float LegFarAngle = 0f;
    private const float LegAimStrength = 0.8f;
    private const int LegLeanSteps = 4;
    private const float LegClearance = 0.08f;
    private const int StateSize = 14;
    private const int PruneCount = 16;

    private static readonly BodypartType[] LegParts =
    {
        BodypartType.Hip_L, BodypartType.Leg_L, BodypartType.Knee_L, BodypartType.Foot_L,
        BodypartType.Hip_R, BodypartType.Leg_R, BodypartType.Knee_R, BodypartType.Foot_R
    };

    private static readonly string[] FingerNames = { "Hand_Upper_", "Thumb_", "Index_", "Middle_", "Ring_", "Pinky_" };

    private static readonly Dictionary<Character, VRClimbPose> Poses = new();

    private GameObject mirrorRoot;
    private AnimationClip clipFall;
    private AnimationClip clipWall;
    private AnimationClip clipHold;
    private AnimationClip clipLike;
    private Transform legLeft;
    private Transform footLeft;
    private Transform legRight;
    private Transform footRight;
    private readonly List<VRPoseBone> legs = new();
    private readonly List<VRPoseBone> fingersLeft = new();
    private readonly List<VRPoseBone> fingersRight = new();
    private readonly Dictionary<Transform, Transform> mirrorMap = new();

    private float weightLegs;
    private float weightWallLeft;
    private float weightWallRight;
    private readonly VRFingerState stateLeft = new();
    private readonly VRFingerState stateRight = new();
    private float poseTime;
    private Vector3 legInto;
    private Vector3 legForward = Vector3.forward;
    private float weightRope;
    private Vector3 ropeFoot;
    private Vector3 animOffset;

    public static void ApplyLocal(Character c)
    {
        Get(c).StepLocal(c);
    }

    public static void ApplyRemote(Character c, byte[] state, Vector3 into, Vector3 ropeOffset)
    {
        if (state == null || state.Length < StateSize)
            return;

        Get(c).StepRemote(c, state, into, ropeOffset);
    }

    public static bool TryGetNetState(Character c, out byte[] state, out Vector3 into, out Vector3 ropeOffset)
    {
        state = null;
        into = Vector3.zero;
        ropeOffset = Vector3.zero;
        if (c == null || !Poses.TryGetValue(c, out VRClimbPose pose))
            return false;

        state = new byte[StateSize];
        WriteHand(state, 0, pose.stateLeft, pose.weightWallLeft);
        WriteHand(state, 6, pose.stateRight, pose.weightWallRight);
        state[12] = Encode(pose.weightLegs);
        state[13] = Encode(pose.weightRope);
        if (pose.weightLegs > 0f)
            into = pose.legInto;
        if (pose.weightRope > 0f && c.refs.hip != null && c.refs.hip.Rig != null)
            ropeOffset = pose.ropeFoot - c.refs.hip.Rig.position;
        return true;
    }

    public static bool TryGetRemoteWall(Character c, out Vector3 normal)
    {
        normal = Vector3.zero;
        if (c == null || !Poses.TryGetValue(c, out VRClimbPose pose))
            return false;
        if (pose.weightWallLeft <= 0f && pose.weightWallRight <= 0f || pose.legInto.sqrMagnitude < 0.5f)
            return false;

        normal = -pose.legInto;
        return true;
    }

    public static void Clear()
    {
        foreach (KeyValuePair<Character, VRClimbPose> entry in Poses)
            entry.Value.Reset();
        Poses.Clear();
    }

    private static VRClimbPose Get(Character c)
    {
        if (Poses.TryGetValue(c, out VRClimbPose pose))
            return pose;

        if (Poses.Count >= PruneCount)
            Prune();
        pose = new VRClimbPose();
        Poses[c] = pose;
        return pose;
    }

    private static void Prune()
    {
        List<Character> gone = new();
        foreach (KeyValuePair<Character, VRClimbPose> entry in Poses)
        {
            if (entry.Key == null)
                gone.Add(entry.Key);
        }

        foreach (Character character in gone)
        {
            Poses[character].Reset();
            Poses.Remove(character);
        }
    }

    private static void WriteHand(byte[] state, int index, VRFingerState hand, float weightWall)
    {
        state[index] = Encode(hand.CurlIndex);
        state[index + 1] = Encode(hand.CurlGrip);
        state[index + 2] = Encode(hand.CurlThumb);
        state[index + 3] = Encode(hand.WeightFree);
        state[index + 4] = Encode(hand.WeightLike);
        state[index + 5] = Encode(weightWall);
    }

    private static byte Encode(float value)
    {
        return (byte)Mathf.RoundToInt(Mathf.Clamp01(value) * 255f);
    }

    private static float Decode(byte value)
    {
        return value / 255f;
    }

    private void StepLocal(Character c)
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
        UpdateFingerState(stateLeft, true, VRHandClimb.Left, VRHandItems.LeftItem != null, step, stepCurl);
        UpdateFingerState(stateRight, false, VRHandClimb.Right,
            c.data.currentItem != null || VRHandItems.RightLoose != null, step, stepCurl);
        Pose(c);
    }

    private void StepRemote(Character c, byte[] state, Vector3 into, Vector3 ropeOffset)
    {
        if (!Prepare(c))
            return;

        float step = Time.fixedDeltaTime * NetBlendSpeed;
        ReadHand(state, 0, stateLeft, ref weightWallLeft, step);
        ReadHand(state, 6, stateRight, ref weightWallRight, step);
        weightLegs = Mathf.MoveTowards(weightLegs, Decode(state[12]), step);
        weightRope = Mathf.MoveTowards(weightRope, Decode(state[13]), step);

        if (into.sqrMagnitude > 1e-4f)
            legInto = into.normalized;
        Vector3 forward = Vector3.ProjectOnPlane(c.data.lookDirection_Flat, Vector3.up);
        if (forward.sqrMagnitude > 1e-4f)
            legForward = forward.normalized;
        if (ropeOffset.sqrMagnitude > 1e-6f && c.refs.hip != null && c.refs.hip.Rig != null)
            ropeFoot = c.refs.hip.Rig.position + ropeOffset;

        Pose(c);
    }

    private static void ReadHand(byte[] state, int index, VRFingerState hand, ref float weightWall, float step)
    {
        hand.CurlIndex = Mathf.MoveTowards(hand.CurlIndex, Decode(state[index]), step);
        hand.CurlGrip = Mathf.MoveTowards(hand.CurlGrip, Decode(state[index + 1]), step);
        hand.CurlThumb = Mathf.MoveTowards(hand.CurlThumb, Decode(state[index + 2]), step);
        hand.WeightFree = Mathf.MoveTowards(hand.WeightFree, Decode(state[index + 3]), step);
        hand.WeightLike = Mathf.MoveTowards(hand.WeightLike, Decode(state[index + 4]), step);
        weightWall = Mathf.MoveTowards(weightWall, Decode(state[index + 5]), step);
    }

    private void Pose(Character c)
    {
        ApplyFingers(fingersLeft, stateLeft);
        ApplyFingers(fingersRight, stateRight);

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
            BlendBones(legs, weightLegs);
            LeanLegs();
        }

        if ((weightWallLeft > 0f || weightWallRight > 0f) && clipWall != null)
        {
            Sample(clipWall, false);
            BlendBones(fingersLeft, weightWallLeft);
            BlendBones(fingersRight, weightWallRight);
        }
    }

    private static void UpdateFingerState(VRFingerState state, bool isLeft, VRClimbHand hand, bool isHolding, float step,
        float stepCurl)
    {
        float grip = VRControls.GripValue(isLeft);
        float trigger = VRControls.TriggerValue(isLeft);
        bool isTouched = VRControls.IsThumbTouched(isLeft);
        bool isFree = !hand.IsGripping && Plugin.Config.FingerPoses.Value;
        bool isSliding = isHolding && VRHandItems.IsSliding(isLeft);
        bool isLike = isFree && !isHolding && !isTouched && grip >= LikePress && trigger >= LikePress;
        state.CurlIndex = Mathf.MoveTowards(state.CurlIndex, trigger, stepCurl);
        state.CurlGrip = Mathf.MoveTowards(state.CurlGrip, isHolding && !isSliding ? 1f : grip, stepCurl);
        state.CurlThumb = Mathf.MoveTowards(state.CurlThumb, isTouched || isSliding ? 1f : 0f, stepCurl);
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

    private void CachePoses()
    {
        if (clipHold != null)
        {
            clipHold.SampleAnimation(mirrorRoot, 0f);
            StorePose(fingersLeft, 0);
            StorePose(fingersRight, 0);
            clipHold.SampleAnimation(mirrorRoot, clipHold.length * HoldClosedTime);
            StorePose(fingersLeft, 1);
            StorePose(fingersRight, 1);
        }
        else
        {
            StorePose(fingersLeft, 0);
            StorePose(fingersRight, 0);
            StorePose(fingersLeft, 1);
            StorePose(fingersRight, 1);
        }

        if (clipLike == null)
        {
            CopyLike(fingersLeft);
            CopyLike(fingersRight);
            return;
        }

        clipLike.SampleAnimation(mirrorRoot, clipLike.length * LikeTime);
        Dictionary<string, Quaternion> left = new();
        for (int i = 0; i < fingersLeft.Count; i++)
        {
            VRPoseBone bone = fingersLeft[i];
            bone.Like = bone.Mirror.localRotation;
            left[GetFingerKey(bone.Mirror.name)] = bone.Like;
        }
        for (int i = 0; i < fingersRight.Count; i++)
        {
            VRPoseBone bone = fingersRight[i];
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

    private void Reset()
    {
        if (mirrorRoot != null)
            Object.Destroy(mirrorRoot);
        mirrorRoot = null;
        clipFall = null;
        clipWall = null;
        clipHold = null;
        clipLike = null;
        legLeft = null;
        footLeft = null;
        legRight = null;
        footRight = null;
        legs.Clear();
        fingersLeft.Clear();
        fingersRight.Clear();
        mirrorMap.Clear();
        weightLegs = 0f;
        weightWallLeft = 0f;
        weightWallRight = 0f;
        ResetState(stateLeft);
        ResetState(stateRight);
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

    private void Sample(AnimationClip clip, bool isLooped)
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

    private bool UpdateInto(Character c)
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

    private bool UpdateRope(Character c)
    {
        if (!VRHandClimb.TryGetRopeLine(c, out Vector3 point, out Vector3 up))
            return false;

        ropeFoot = point - up.normalized * RopeFootDrop;
        return true;
    }

    private void AimLegsToRope()
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

    private void LeanLegs()
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

    private void LeanLeg(Transform leg, Transform foot, float angle)
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

    private bool Prepare(Character c)
    {
        if (mirrorRoot != null)
            return true;

        Reset();

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

        mirrorRoot = new GameObject("PeakVR Pose Mirror");
        mirrorRoot.hideFlags = HideFlags.HideAndDontSave;
        Object.DontDestroyOnLoad(mirrorRoot);
        Animator mirrorAnimator = mirrorRoot.AddComponent<Animator>();
        mirrorAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        mirrorMap[animator.transform] = mirrorRoot.transform;

        for (int i = 0; i < LegParts.Length; i++)
        {
            if (!ragdoll.partDict.TryGetValue(LegParts[i], out Bodypart part) || part == null)
                continue;
            AddBone(legs, part.transform, animator.transform);
        }

        legLeft = GetPart(ragdoll, BodypartType.Leg_L);
        footLeft = GetPart(ragdoll, BodypartType.Foot_L);
        legRight = GetPart(ragdoll, BodypartType.Leg_R);
        footRight = GetPart(ragdoll, BodypartType.Foot_R);

        AddFingers(fingersLeft, GetPart(ragdoll, BodypartType.Hand_L), "_L", animator.transform);
        AddFingers(fingersRight, GetPart(ragdoll, BodypartType.Hand_R), "_R", animator.transform);
        CachePoses();

        mirrorMap.Clear();
        return true;
    }

    private static Transform GetPart(CharacterRagdoll ragdoll, BodypartType type)
    {
        if (ragdoll.partDict.TryGetValue(type, out Bodypart part) && part != null)
            return part.transform;
        return null;
    }

    private void AddFingers(List<VRPoseBone> bones, Transform hand, string side, Transform root)
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

    private void AddBone(List<VRPoseBone> bones, Transform real, Transform root)
    {
        Transform mirror = GetMirror(real, root);
        if (mirror == null)
            return;
        bones.Add(new VRPoseBone { Real = real, Mirror = mirror });
    }

    private Transform GetMirror(Transform real, Transform root)
    {
        if (real == null)
            return null;
        if (mirrorMap.TryGetValue(real, out Transform found))
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
        mirrorMap[real] = mirror;
        return mirror;
    }
}

[HarmonyPatch(typeof(CharacterAnimations), nameof(CharacterAnimations.ConfigureIK))]
internal static class ClimbPosePatch
{
    [HarmonyPostfix]
    private static void Postfix(CharacterAnimations __instance)
    {
        Character c = __instance.character;
        if (c == null || c.refs == null || c.refs.animations != __instance || c.data == null || c.data.dead)
            return;

        if (c == Character.localCharacter)
        {
            if (Plugin.VrEnabled)
                VRClimbPose.ApplyLocal(c);
            return;
        }

        if (VRNetworking.TryGetClimbPose(c, out byte[] state, out Vector3 into, out Vector3 ropeOffset))
            VRClimbPose.ApplyRemote(c, state, into, ropeOffset);
    }
}

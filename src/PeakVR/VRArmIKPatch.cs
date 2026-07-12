using HarmonyLib;
using UnityEngine;
using UnityEngine.Animations.Rigging;

namespace PeakVR;

[HarmonyPatch(typeof(CharacterAnimations))]
internal static class VRArmIKPatch
{
    public static Quaternion HandRotationOffset = Quaternion.Euler(-90f, 180f, 0f);
    public static float ArmScale = 1.089f;

    private static readonly Vector3 ShoulderOffsetLeft = new(-0.18f, -0.2f, 0f);
    private static readonly Vector3 ShoulderOffsetRight = new(0.18f, -0.2f, 0f);
    private static readonly Vector3 ElbowSeedLocal = new(0f, -0.7f, -0.5f);
    private const float ElbowHintDistance = 0.3f;
    private const float HandInfluence = 0.35f;
    private const float MinElbowAngle = 40f;

    [HarmonyPatch("HandleIK")]
    [HarmonyPostfix]
    private static void HandleIKPostfix(CharacterAnimations __instance)
    {
        if (!ShouldDrive(__instance, out var c))
            return;

        c.refs.ikRig.weight = 1f;
        c.refs.ikLeft.weight = 1f;
        c.refs.ikRight.weight = 1f;
    }

    [HarmonyPatch(nameof(CharacterAnimations.ConfigureIK))]
    [HarmonyPostfix]
    private static void ConfigureIKPostfix(CharacterAnimations __instance)
    {
        if (!ShouldDrive(__instance, out var c))
            return;

        var refs = c.refs;
        if (refs.ikLeft.data.root == null || refs.ikRight.data.root == null)
            return;

        var cam = MainCamera.instance.cam.transform;
        var shoulderL = cam.rotation * ShoulderOffsetLeft;
        var shoulderR = cam.rotation * ShoulderOffsetRight;

        var targetL = refs.ikLeft.data.root.position + ArmScale * (VRHands.Left.position - cam.position - shoulderL);
        var targetR = refs.ikRight.data.root.position + ArmScale * (VRHands.Right.position - cam.position - shoulderR);

        targetL = ClampMinElbowBend(refs.ikLeft, targetL);
        targetR = ClampMinElbowBend(refs.ikRight, targetR);

        refs.IKHandTargetLeft.position = targetL;
        refs.IKHandTargetRight.position = targetR;

        refs.IKHandTargetLeft.rotation = VRHands.Left.rotation * HandRotationOffset;
        refs.IKHandTargetRight.rotation = VRHands.Right.rotation * HandRotationOffset;

        SetElbowHint(refs.ikLeft, targetL, VRHands.Left.rotation);
        SetElbowHint(refs.ikRight, targetR, VRHands.Right.rotation);

        refs.ikRig.weight = 1f;
        refs.ikLeft.weight = 1f;
        refs.ikRight.weight = 1f;
    }

    private static Vector3 ClampMinElbowBend(TwoBoneIKConstraint ik, Vector3 target)
    {
        var root = ik.data.root;
        var mid = ik.data.mid;
        var tip = ik.data.tip;
        if (root == null || mid == null || tip == null)
            return target;

        var upper = Vector3.Distance(root.position, mid.position);
        var fore = Vector3.Distance(mid.position, tip.position);
        if (upper < 1e-4f || fore < 1e-4f)
            return target;

        var cos = Mathf.Cos(MinElbowAngle * Mathf.Deg2Rad);
        var minDist = Mathf.Sqrt(Mathf.Max(0f, upper * upper + fore * fore - 2f * upper * fore * cos));

        var toHand = target - root.position;
        var dist = toHand.magnitude;
        if (dist >= minDist || dist < 1e-4f)
            return target;

        return root.position + toHand * (minDist / dist);
    }

    private static void SetElbowHint(TwoBoneIKConstraint ik, Vector3 handPos, Quaternion ctrlRot)
    {
        var hint = ik.data.hint;
        if (hint == null)
            return;

        var shoulderPos = ik.data.root.position;
        var axis = handPos - shoulderPos;
        if (axis.sqrMagnitude < 1e-4f)
            return;
        axis.Normalize();

        var downPole = Vector3.ProjectOnPlane(Vector3.down, axis);
        if (downPole.sqrMagnitude < 1e-4f)
            downPole = Vector3.ProjectOnPlane(Vector3.back, axis);

        var handPole = Vector3.ProjectOnPlane(ctrlRot * ElbowSeedLocal, axis);
        if (handPole.sqrMagnitude < 1e-4f)
            handPole = downPole;

        var pole = Vector3.Slerp(downPole.normalized, handPole.normalized, HandInfluence);

        hint.position = (shoulderPos + handPos) * 0.5f + pole.normalized * ElbowHintDistance;
    }

    private static bool ShouldDrive(CharacterAnimations anim, out Character c)
    {
        c = Character.localCharacter;
        if (c == null || VRHands.Left == null || VRHands.Right == null)
            return false;
        if (c.refs.animations != anim)
            return false;
        if (c.refs.IKHandTargetLeft == null || c.refs.ikRig == null ||
            c.refs.ikLeft == null || c.refs.ikRight == null)
            return false;
        return true;
    }
}

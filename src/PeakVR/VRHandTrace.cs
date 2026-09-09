using UnityEngine;

namespace PeakVR;

internal static class VRHandTrace
{
    private const float Interval = 2f;

    private static float sendTimer;
    private static float recvTimer;

    public static void Sent(Character c, Vector3 leftTarget, Vector3 rightTarget)
    {
        if (!Ready() || (sendTimer += Time.deltaTime) < Interval)
            return;

        sendTimer = 0f;
        Write("SENT", c, leftTarget, rightTarget);
    }

    public static void Received(Character c, Vector3 leftTarget, Vector3 rightTarget)
    {
        if (!Ready() || (recvTimer += Time.deltaTime) < Interval)
            return;

        recvTimer = 0f;
        Write("RECV", c, leftTarget, rightTarget);
    }

    private static bool Ready() =>
        Plugin.Config != null && Plugin.Config.EnableVerboseLogging.Value;

    private static void Write(string tag, Character c, Vector3 leftTarget, Vector3 rightTarget)
    {
        var refs = c != null ? c.refs : null;
        if (refs == null || refs.ikLeft == null || refs.ikRight == null)
            return;

        var root = c.transform;

        Plugin.Log.LogInfo($"[PeakVR][Hands] {tag} '{c.characterName}' local target L{V(leftTarget)} R{V(rightTarget)}" +
            $" | WORLD target L{V(root.TransformPoint(leftTarget))} R{V(root.TransformPoint(rightTarget))}" +
            $" ikTarget L{W(refs.IKHandTargetLeft)} R{W(refs.IKHandTargetRight)}" +
            $" shoulder L{W(refs.ikLeft.data.root)} R{W(refs.ikRight.data.root)}" +
            $" hand L{W(refs.ikLeft.data.tip)} R{W(refs.ikRight.data.tip)}" +
            $" hint L{W(refs.ikLeft.data.hint)} R{W(refs.ikRight.data.hint)}" +
            $" | root {V(root.position)} rot {V(root.rotation.eulerAngles)}" +
            $" | weights {refs.ikRig.weight:F2}/{refs.ikLeft.weight:F2}/{refs.ikRight.weight:F2}" +
            $" | L pos{refs.ikLeft.data.targetPositionWeight:F2} rot{refs.ikLeft.data.targetRotationWeight:F2}" +
            $" hint{refs.ikLeft.data.hintWeight:F2} bound={Bound(refs.ikLeft, refs.IKHandTargetLeft)}" +
            $" | R bound={Bound(refs.ikRight, refs.IKHandTargetRight)}");

        Plugin.Log.LogInfo($"[PeakVR][Hands] {tag} '{c.characterName}' ROT" +
            $" targetL{E(refs.IKHandTargetLeft)} handL{E(refs.ikLeft.data.tip)}" +
            $" deltaL{Gap(refs.IKHandTargetLeft, refs.ikLeft.data.tip)}" +
            $" | targetR{E(refs.IKHandTargetRight)} handR{E(refs.ikRight.data.tip)}" +
            $" deltaR{Gap(refs.IKHandTargetRight, refs.ikRight.data.tip)}" +
            $" | shootRoll={ShootableAim.RollEnabled && ShootableAim.IsShootableHeld()}" +
            $" item={(c.data != null && c.data.currentItem != null ? c.data.currentItem.name : "none")}");
    }

    private static string W(Transform bone) => bone == null ? "(null)" : V(bone.position);

    private static string E(Transform bone) => bone == null ? "(null)" : V(bone.rotation.eulerAngles);

    private static string Gap(Transform a, Transform b) =>
        a == null || b == null ? "(n/a)" : $"{Quaternion.Angle(a.rotation, b.rotation):F1}deg";

    private static string Bound(UnityEngine.Animations.Rigging.TwoBoneIKConstraint ik, Transform expected)
    {
        var target = ik.data.target;
        if (target == null)
            return "NO-TARGET";

        return target == expected ? "ok" : $"OTHER({target.name})";
    }

    private static string V(Vector3 v) => $"({v.x:F2},{v.y:F2},{v.z:F2})";
}

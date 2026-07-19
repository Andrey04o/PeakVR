using System.Collections.Generic;
using UnityEngine;

namespace PeakVR;

[DefaultExecutionOrder(2001)]
internal class VRNetReceiver : MonoBehaviour
{
    private const float SmoothSpeed = 18f;
    private const float OutlierMax = 5f;
    private const int MaxOutliers = 3;

    private class Smooth
    {
        public float headRoll;
        public Vector3 leftPos;
        public Quaternion leftRot;
        public Vector3 rightPos;
        public Quaternion rightRot;
        public bool init;

        public int outlierCount;
        public Vector3 lastOutlierLeft;

        // Non-compounding head-roll state.
        public Quaternion lastHeadOutput;
        public Vector3 lastAxis;
        public float lastRoll;
        public bool rollInit;
    }

    private readonly Dictionary<int, Smooth> smoothing = new();
    private readonly List<int> stale = new();

    private void Update()
    {
        VRNetworking.EnsureRegistered();
    }

    private void LateUpdate()
    {
        if (VRNetworking.Remotes.Count == 0)
        {
            if (smoothing.Count > 0)
                smoothing.Clear();
            return;
        }

        var local = Character.localCharacter;
        stale.Clear();
        var t = Time.deltaTime * SmoothSpeed;

        foreach (var kv in VRNetworking.Remotes)
        {
            var pose = kv.Value;
            pose.sinceReceived += Time.deltaTime;
            VRNetworking.Remotes[kv.Key] = pose;

            if (pose.sinceReceived > VRNetworking.StaleTime)
            {
                stale.Add(kv.Key);
                continue;
            }

            if (!PlayerHandler.TryGetCharacter(kv.Key, out var character))
                continue;
            if (character == null || character == local || character.refs.head == null)
                continue;

            if (!smoothing.TryGetValue(kv.Key, out var s))
            {
                s = new Smooth();
                smoothing[kv.Key] = s;
            }

            if (!s.init)
            {
                s.headRoll = pose.headRoll;
                s.leftPos = pose.leftPos;
                s.leftRot = pose.leftRot;
                s.rightPos = pose.rightPos;
                s.rightRot = pose.rightRot;
                s.init = true;
            }
            else
            {
                s.headRoll = Mathf.LerpAngle(s.headRoll, pose.headRoll, t);

                var outlier = (pose.leftPos - s.leftPos).magnitude > OutlierMax
                    || (pose.rightPos - s.rightPos).magnitude > OutlierMax;

                if (outlier)
                {
                    if (s.outlierCount > 0 && (pose.leftPos - s.lastOutlierLeft).magnitude < OutlierMax)
                        s.outlierCount++;
                    else
                        s.outlierCount = 1;
                    s.lastOutlierLeft = pose.leftPos;

                    if (s.outlierCount >= MaxOutliers)
                    {
                        s.outlierCount = 0;
                        s.leftPos = pose.leftPos;
                        s.leftRot = pose.leftRot;
                        s.rightPos = pose.rightPos;
                        s.rightRot = pose.rightRot;
                    }
                }
                else
                {
                    s.outlierCount = 0;
                    s.leftPos = Vector3.Lerp(s.leftPos, pose.leftPos, t);
                    s.leftRot = Quaternion.Slerp(s.leftRot, pose.leftRot, t);
                    s.rightPos = Vector3.Lerp(s.rightPos, pose.rightPos, t);
                    s.rightRot = Quaternion.Slerp(s.rightRot, pose.rightRot, t);
                }
            }

            if (pose.hasHands)
                ApplyHands(character, s);

            ApplyHeadRoll(character, s);
        }

        foreach (var key in stale)
        {
            VRNetworking.Remotes.Remove(key);
            smoothing.Remove(key);
        }
    }

    private static void ApplyHands(Character character, Smooth s)
    {
        var refs = character.refs;
        if (refs.IKHandTargetLeft == null || refs.IKHandTargetRight == null
            || refs.ikRig == null || refs.ikLeft == null || refs.ikRight == null)
            return;

        var root = character.transform;

        refs.IKHandTargetLeft.position = root.TransformPoint(s.leftPos);
        refs.IKHandTargetLeft.rotation = root.rotation * s.leftRot;
        refs.IKHandTargetRight.position = root.TransformPoint(s.rightPos);
        refs.IKHandTargetRight.rotation = root.rotation * s.rightRot;

        refs.ikRig.weight = 1f;
        refs.ikLeft.weight = 1f;
        refs.ikRight.weight = 1f;
    }

    private static void ApplyHeadRoll(Character character, Smooth s)
    {
        var axis = character.data.lookDirection;
        if (axis.sqrMagnitude < 1e-4f)
            return;
        axis.Normalize();

        var head = character.refs.head.transform;

        var baseRot = head.rotation;
        if (s.rollInit && Quaternion.Angle(head.rotation, s.lastHeadOutput) < 1f)
            baseRot = Quaternion.AngleAxis(-s.lastRoll, s.lastAxis) * head.rotation;

        var output = Quaternion.AngleAxis(s.headRoll, axis) * baseRot;
        head.rotation = output;

        s.lastHeadOutput = output;
        s.lastAxis = axis;
        s.lastRoll = s.headRoll;
        s.rollInit = true;
    }
}

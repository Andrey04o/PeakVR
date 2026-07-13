using System.Collections.Generic;
using UnityEngine;

namespace PeakVR;

[DefaultExecutionOrder(2001)]
internal class VRNetReceiver : MonoBehaviour
{
    private const float SmoothSpeed = 18f;

    private class Smooth
    {
        public float headRoll;
        public Quaternion frameRot;
        public Vector3 leftPos;
        public Quaternion leftRot;
        public Vector3 rightPos;
        public Quaternion rightRot;
        public bool init;
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
                s.frameRot = pose.frameRot;
                s.leftPos = pose.leftPos;
                s.leftRot = pose.leftRot;
                s.rightPos = pose.rightPos;
                s.rightRot = pose.rightRot;
                s.init = true;
            }
            else
            {
                s.headRoll = Mathf.LerpAngle(s.headRoll, pose.headRoll, t);
                s.frameRot = Quaternion.Slerp(s.frameRot, pose.frameRot, t);
                s.leftPos = Vector3.Lerp(s.leftPos, pose.leftPos, t);
                s.leftRot = Quaternion.Slerp(s.leftRot, pose.leftRot, t);
                s.rightPos = Vector3.Lerp(s.rightPos, pose.rightPos, t);
                s.rightRot = Quaternion.Slerp(s.rightRot, pose.rightRot, t);
            }

            ApplyHeadRoll(character, s.headRoll);

            if (pose.hasHands)
                ApplyHands(character, s);
        }

        foreach (var key in stale)
        {
            VRNetworking.Remotes.Remove(key);
            smoothing.Remove(key);
        }
    }

    private static void ApplyHeadRoll(Character character, float roll)
    {
        var axis = character.data.lookDirection;
        if (axis.sqrMagnitude < 1e-4f)
            return;
        axis.Normalize();

        var head = character.refs.head.transform;
        head.rotation = Quaternion.AngleAxis(roll, axis) * head.rotation;
    }

    private static void ApplyHands(Character character, Smooth s)
    {
        var refs = character.refs;
        if (refs.IKHandTargetLeft == null || refs.IKHandTargetRight == null
            || refs.ikRig == null || refs.ikLeft == null || refs.ikRight == null)
            return;

        var origin = refs.head.transform.position;

        refs.IKHandTargetLeft.position = origin + s.frameRot * s.leftPos;
        refs.IKHandTargetLeft.rotation = s.frameRot * s.leftRot;
        refs.IKHandTargetRight.position = origin + s.frameRot * s.rightPos;
        refs.IKHandTargetRight.rotation = s.frameRot * s.rightRot;

        refs.ikRig.weight = 1f;
        refs.ikLeft.weight = 1f;
        refs.ikRight.weight = 1f;
    }
}

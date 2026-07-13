using System.Collections.Generic;
using UnityEngine;

namespace PeakVR;

[DefaultExecutionOrder(2001)]
internal class VRNetReceiver : MonoBehaviour
{
    private const float StaleTime = 1f;
    private const float SmoothSpeed = 15f;

    private readonly List<int> stale = new();

    private void Update()
    {
        VRNetworking.EnsureRegistered();
    }

    private void LateUpdate()
    {
        if (VRNetworking.Remotes.Count == 0)
            return;

        var local = Character.localCharacter;
        stale.Clear();

        foreach (var kv in VRNetworking.Remotes)
        {
            var pose = kv.Value;
            pose.sinceReceived += Time.deltaTime;
            VRNetworking.Remotes[kv.Key] = pose;

            if (pose.sinceReceived > StaleTime)
            {
                stale.Add(kv.Key);
                continue;
            }

            if (!PlayerHandler.TryGetCharacter(kv.Key, out var character))
                continue;
            if (character == null || character == local || character.refs.head == null)
                continue;

            ApplyHeadRoll(character, pose.headRoll);

            if (pose.hasHands)
                ApplyHands(character, pose);
        }

        foreach (var key in stale)
            VRNetworking.Remotes.Remove(key);
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

    private static void ApplyHands(Character character, VRNetworking.RemotePose pose)
    {
        var refs = character.refs;
        if (refs.IKHandTargetLeft == null || refs.IKHandTargetRight == null
            || refs.ikRig == null || refs.ikLeft == null || refs.ikRight == null)
            return;

        VRNetworking.GetFrame(character, out var origin, out var frameRot);

        var worldLeftPos = origin + frameRot * pose.leftPos;
        var worldLeftRot = frameRot * pose.leftRot;
        var worldRightPos = origin + frameRot * pose.rightPos;
        var worldRightRot = frameRot * pose.rightRot;

        var t = Time.deltaTime * SmoothSpeed;

        refs.IKHandTargetLeft.position = Vector3.Lerp(refs.IKHandTargetLeft.position, worldLeftPos, t);
        refs.IKHandTargetLeft.rotation = Quaternion.Slerp(refs.IKHandTargetLeft.rotation, worldLeftRot, t);
        refs.IKHandTargetRight.position = Vector3.Lerp(refs.IKHandTargetRight.position, worldRightPos, t);
        refs.IKHandTargetRight.rotation = Quaternion.Slerp(refs.IKHandTargetRight.rotation, worldRightRot, t);

        refs.ikRig.weight = 1f;
        refs.ikLeft.weight = 1f;
        refs.ikRight.weight = 1f;
    }
}

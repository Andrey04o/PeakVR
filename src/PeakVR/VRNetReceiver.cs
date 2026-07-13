using System.Collections.Generic;
using UnityEngine;

namespace PeakVR;

[DefaultExecutionOrder(2001)]
internal class VRNetReceiver : MonoBehaviour
{
    private const float StaleTime = 1f;

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
}

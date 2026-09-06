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
        public Vector3 leftHint;
        public Vector3 rightHint;
        public Vector3 leftShoulder;
        public Vector3 rightShoulder;
        public bool hasShoulders;
        public bool init;

        public int outlierCount;
        public Vector3 lastOutlierLeft;

        // Non-compounding head-roll state.
        public Quaternion lastHeadOutput;
        public Vector3 lastAxis;
        public float lastRoll;
        public bool rollInit;
    }

    private class IkWeights
    {
        public float rig;
        public float left;
        public float right;
    }

    private readonly Dictionary<int, Smooth> smoothing = new();
    private readonly List<int> stale = new();
    private static readonly Dictionary<Character, IkWeights> IkOriginals = new();
    private static readonly Dictionary<Character, Smooth> Poses = new();
    public static readonly Dictionary<Character, float> RemoteRolls = new();

    public static void ApplyTo(Character character)
    {
        if (character != null && Poses.TryGetValue(character, out var s))
            ApplyHands(character, s);
    }

    public static void Drop(int actor)
    {
        if (!PlayerHandler.TryGetCharacter(actor, out var gone) || gone == null)
        {
            Plugin.Log.LogWarning($"[PeakVR][Net] actor {actor} left VR but no Character was found — " +
                "their head tilt and hand IK could not be restored");
            return;
        }

        RemoteRolls.Remove(gone);
        Poses.Remove(gone);
        HeadTiltPatch.RestoreFor(gone);
        VRHandDebug.Hide(gone);

        Plugin.Log.LogInfo($"[PeakVR][Net] '{gone.characterName}' is no longer VR — restoring head tilt and hand IK");

        try
        {
            RestoreIK(gone);
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogError($"[PeakVR][Net] restoring hand IK for '{gone.characterName}' failed: {e}");
        }
    }


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

            s.hasShoulders = pose.hasShoulders;

            if (!s.init)
            {
                s.headRoll = pose.headRoll;
                s.leftPos = pose.leftPos;
                s.leftRot = pose.leftRot;
                s.rightPos = pose.rightPos;
                s.rightRot = pose.rightRot;
                s.leftHint = pose.leftHint;
                s.rightHint = pose.rightHint;
                s.leftShoulder = pose.leftShoulder;
                s.rightShoulder = pose.rightShoulder;
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
                        s.leftHint = pose.leftHint;
                        s.rightHint = pose.rightHint;
                        s.leftShoulder = pose.leftShoulder;
                        s.rightShoulder = pose.rightShoulder;
                    }
                }
                else
                {
                    s.outlierCount = 0;
                    s.leftPos = Vector3.Lerp(s.leftPos, pose.leftPos, t);
                    s.leftRot = Quaternion.Slerp(s.leftRot, pose.leftRot, t);
                    s.rightPos = Vector3.Lerp(s.rightPos, pose.rightPos, t);
                    s.rightRot = Quaternion.Slerp(s.rightRot, pose.rightRot, t);
                    s.leftHint = Vector3.Lerp(s.leftHint, pose.leftHint, t);
                    s.rightHint = Vector3.Lerp(s.rightHint, pose.rightHint, t);
                    s.leftShoulder = Vector3.Lerp(s.leftShoulder, pose.leftShoulder, t);
                    s.rightShoulder = Vector3.Lerp(s.rightShoulder, pose.rightShoulder, t);
                }
            }

            if (pose.hasHands)
                Poses[character] = s;

            RemoteRolls[character] = s.headRoll;
        }

        foreach (var key in stale)
        {
            VRNetworking.Remotes.Remove(key);
            smoothing.Remove(key);
            Drop(key);
        }
    }

    private static void RestoreIK(Character character)
    {
        var refs = character.refs;
        if (refs == null)
            return;

        if (IkOriginals.TryGetValue(character, out var original))
        {
            IkOriginals.Remove(character);

            if (refs.ikRig != null)
                refs.ikRig.weight = original.rig;
            if (refs.ikLeft != null)
                refs.ikLeft.weight = original.left;
            if (refs.ikRight != null)
                refs.ikRight.weight = original.right;
        }

        if (refs.animations != null)
            refs.animations.ConfigureIK();
    }


    private static void ApplyHands(Character character, Smooth s)
    {
        var refs = character.refs;
        if (refs.IKHandTargetLeft == null || refs.IKHandTargetRight == null
            || refs.ikRig == null || refs.ikLeft == null || refs.ikRight == null)
            return;

        if (!IkOriginals.ContainsKey(character))
            IkOriginals[character] = new IkWeights
            {
                rig = refs.ikRig.weight,
                left = refs.ikLeft.weight,
                right = refs.ikRight.weight
            };

        var root = character.transform;

        var leftPos = s.leftPos;
        var rightPos = s.rightPos;
        var leftHint = s.leftHint;
        var rightHint = s.rightHint;

        if (s.hasShoulders && refs.ikLeft.data.root != null && refs.ikRight.data.root != null)
        {
            var leftShoulder = root.InverseTransformPoint(refs.ikLeft.data.root.position);
            var rightShoulder = root.InverseTransformPoint(refs.ikRight.data.root.position);

            leftPos = leftShoulder + (s.leftPos - s.leftShoulder);
            rightPos = rightShoulder + (s.rightPos - s.rightShoulder);
            leftHint = leftShoulder + (s.leftHint - s.leftShoulder);
            rightHint = rightShoulder + (s.rightHint - s.rightShoulder);
        }

        var worldLeft = root.TransformPoint(leftPos);
        var worldRight = root.TransformPoint(rightPos);

        refs.IKHandTargetLeft.position = worldLeft;
        refs.IKHandTargetLeft.rotation = root.rotation * s.leftRot;
        refs.IKHandTargetRight.position = worldRight;
        refs.IKHandTargetRight.rotation = root.rotation * s.rightRot;

        refs.ikRig.weight = 1f;
        refs.ikLeft.weight = 1f;
        refs.ikRight.weight = 1f;

        if (refs.ikLeft.data.hint != null)
            refs.ikLeft.data.hint.position = root.TransformPoint(leftHint);
        if (refs.ikRight.data.hint != null)
            refs.ikRight.data.hint.position = root.TransformPoint(rightHint);

        VRArmIKPatch.ForceConstraintWeights(refs.ikLeft);
        VRArmIKPatch.ForceConstraintWeights(refs.ikRight);

        VRHandTrace.Received(character, leftPos, rightPos);
        VRHandDebug.Show(character, worldLeft, worldRight);
    }

}

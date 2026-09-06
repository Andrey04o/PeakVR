using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace PeakVR;

[HarmonyPatch(typeof(Bodypart), nameof(Bodypart.SaveAnimationData))]
internal static class HeadTiltPatch
{
    private const float MinRoll = 0.05f;
    private const float NeckSpring = 3000f;
    private const float NeckDamper = 120f;
    private const float NeckMaxForce = 10000f;

    private class NeckState
    {
        public ConfigurableJointMotion angularX;
        public ConfigurableJointMotion angularY;
        public ConfigurableJointMotion angularZ;
        public RotationDriveMode driveMode;
        public JointDrive slerpDrive;
        public readonly List<(Collider head, Collider body)> uncoupled = new();
    }

    private static readonly Dictionary<Bodypart, NeckState> widened = new();
    private static readonly Dictionary<Character, Bodypart> heads = new();

    public static void RestoreFor(Character character)
    {
        if (character != null && heads.TryGetValue(character, out var head) && head != null)
            RestoreNeck(head);
    }

    [HarmonyPostfix]
    private static void Postfix(Bodypart __instance)
    {
        var profile = VRProfile.Begin();
        PostfixBody(__instance);
        VRProfile.End(VRProfile.HeadTilt, profile);
    }

    private static void PostfixBody(Bodypart __instance)
    {
        if (__instance == null || __instance.partType != BodypartType.Head)
            return;

        var character = __instance.character;
        if (character == null)
            return;

        float roll;
        if (character.IsLocal)
        {
            if (!Plugin.VrEnabled)
            {
                RestoreNeck(__instance);
                return;
            }

            roll = VRHeadRoll.LocalRoll;
        }
        else if (!VRNetReceiver.RemoteRolls.TryGetValue(character, out roll))
        {
            RestoreNeck(__instance);
            return;
        }

        if (character.data.fullyPassedOut)
            return;

        if (Mathf.Abs(roll) < MinRoll)
            return;

        var axis = character.data.lookDirection;
        if (axis.sqrMagnitude < 1e-4f)
            return;
        axis.Normalize();

        FreeNeck(__instance);

        var tilt = Quaternion.AngleAxis(roll, axis);

        __instance.targetForward = tilt * __instance.targetForward;
        __instance.targetUp = tilt * __instance.targetUp;

        var parent = __instance.transform.parent;
        if (parent == null)
            return;

        __instance.targetRotation =
            Quaternion.Inverse(parent.rotation) * tilt * parent.rotation * __instance.targetRotation;
    }

    private static void FreeNeck(Bodypart head)
    {
        if (widened.ContainsKey(head))
            return;

        var joint = head.GetComponent<ConfigurableJoint>();
        if (joint == null)
            return;

        var state = new NeckState
        {
            angularX = joint.angularXMotion,
            angularY = joint.angularYMotion,
            angularZ = joint.angularZMotion,
            driveMode = joint.rotationDriveMode,
            slerpDrive = joint.slerpDrive
        };
        widened[head] = state;
        if (head.character != null)
            heads[head.character] = head;
        Log(head, "neck freed for VR head tilt");

        joint.angularXMotion = ConfigurableJointMotion.Free;
        joint.angularYMotion = ConfigurableJointMotion.Free;
        joint.angularZMotion = ConfigurableJointMotion.Free;

        joint.rotationDriveMode = RotationDriveMode.Slerp;
        joint.slerpDrive = Strengthen(joint.slerpDrive);

        IgnoreBodyCollisions(head, state);
    }

    private static void RestoreNeck(Bodypart head)
    {
        if (!widened.TryGetValue(head, out var state))
            return;

        widened.Remove(head);
        if (head.character != null)
            heads.Remove(head.character);

        var joint = head.GetComponent<ConfigurableJoint>();
        if (joint != null)
        {
            joint.angularXMotion = state.angularX;
            joint.angularYMotion = state.angularY;
            joint.angularZMotion = state.angularZ;
            joint.rotationDriveMode = state.driveMode;
            joint.slerpDrive = state.slerpDrive;
        }

        foreach (var pair in state.uncoupled)
            if (pair.head != null && pair.body != null)
                Physics.IgnoreCollision(pair.head, pair.body, false);

        Log(head, $"neck restored ({state.uncoupled.Count} collision pairs re-coupled)");
        state.uncoupled.Clear();
    }

    private static void Log(Bodypart head, string message)
    {
        var name = head.character != null ? head.character.characterName : "?";
        Plugin.Log.LogInfo($"[PeakVR][HeadTilt] '{name}': {message}");
    }

    private static void IgnoreBodyCollisions(Bodypart head, NeckState state)
    {
        var character = head.character;
        if (character == null)
            return;

        var headColliders = head.GetComponentsInChildren<Collider>(true);

        foreach (var part in character.GetComponentsInChildren<Bodypart>(true))
        {
            if (part == null || part == head || IsHand(part.partType))
                continue;

            foreach (var bc in part.GetComponents<Collider>())
            {
                if (bc == null || bc.transform.IsChildOf(head.transform))
                    continue;

                foreach (var hc in headColliders)
                {
                    if (hc == null || hc == bc || Physics.GetIgnoreCollision(hc, bc))
                        continue;

                    Physics.IgnoreCollision(hc, bc, true);
                    state.uncoupled.Add((hc, bc));
                }
            }
        }
    }

    private static bool IsHand(BodypartType type) =>
        type == BodypartType.Hand_L || type == BodypartType.Hand_R;

    private static JointDrive Strengthen(JointDrive drive)
    {
        drive.positionSpring = Mathf.Max(drive.positionSpring, NeckSpring);
        drive.positionDamper = Mathf.Max(drive.positionDamper, NeckDamper);
        drive.maximumForce = Mathf.Max(drive.maximumForce, NeckMaxForce);
        return drive;
    }
}

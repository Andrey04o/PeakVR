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

    private static readonly HashSet<Bodypart> widened = new();

    [HarmonyPostfix]
    private static void Postfix(Bodypart __instance)
    {
        if (__instance == null || __instance.partType != BodypartType.Head)
            return;

        var character = __instance.character;
        if (character == null || character.data.fullyPassedOut)
            return;

        float roll;
        if (character.IsLocal)
        {
            if (!Plugin.VrEnabled)
                return;

            roll = VRHeadRoll.LocalRoll;
        }
        else if (!VRNetReceiver.RemoteRolls.TryGetValue(character, out roll))
        {
            return;
        }

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
        if (!widened.Add(head))
            return;

        var joint = head.GetComponent<ConfigurableJoint>();
        if (joint == null)
            return;

        joint.angularXMotion = ConfigurableJointMotion.Free;
        joint.angularYMotion = ConfigurableJointMotion.Free;
        joint.angularZMotion = ConfigurableJointMotion.Free;

        joint.rotationDriveMode = RotationDriveMode.Slerp;
        joint.slerpDrive = Strengthen(joint.slerpDrive);

        IgnoreBodyCollisions(head);
    }

    private static void IgnoreBodyCollisions(Bodypart head)
    {
        var character = head.character;
        if (character == null)
            return;

        var headColliders = head.GetComponentsInChildren<Collider>(true);
        var bodyColliders = character.GetComponentsInChildren<Collider>(true);

        foreach (var hc in headColliders)
        {
            if (hc == null)
                continue;

            foreach (var bc in bodyColliders)
            {
                if (bc == null || bc == hc || bc.transform.IsChildOf(head.transform) || IsHand(bc))
                    continue;

                Physics.IgnoreCollision(hc, bc, true);
            }
        }
    }

    private static bool IsHand(Collider collider)
    {
        var part = collider.GetComponentInParent<Bodypart>();
        if (part == null)
            return false;

        return part.partType == BodypartType.Hand_L || part.partType == BodypartType.Hand_R;
    }

    private static JointDrive Strengthen(JointDrive drive)
    {
        drive.positionSpring = Mathf.Max(drive.positionSpring, NeckSpring);
        drive.positionDamper = Mathf.Max(drive.positionDamper, NeckDamper);
        drive.maximumForce = Mathf.Max(drive.maximumForce, NeckMaxForce);
        return drive;
    }
}

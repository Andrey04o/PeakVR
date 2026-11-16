using System.Collections.Generic;
using UnityEngine;

namespace PeakVR;

internal static class VRHandJoint
{
    private const float SlideFriction = 1.5f;

    private static readonly List<Joint> Joints = new List<Joint>();

    public static Joint AttachRigid(Rigidbody hand, Item item)
    {
        var joint = hand.gameObject.AddComponent<FixedJoint>();
        joint.connectedBody = item.rig;
        return joint;
    }

    public static Joint Attach(Rigidbody hand, Item item, bool alreadyHeld)
    {
        if (!alreadyHeld && VRFollowHold.IsFollow(item) && Character.localCharacter != null
            && hand == Character.localCharacter.GetBodypartRig(BodypartType.Hand_R))
            return null;

        return AttachRigid(hand, item);
    }

    public static Joint Ensure(Rigidbody hand, Item item, bool isSliding)
    {
        if (hand == null || item == null || item.rig == null)
            return null;

        Joints.Clear();
        hand.GetComponents(Joints);

        Joint current = null;
        foreach (Joint joint in Joints)
        {
            if (joint != null && joint.connectedBody == item.rig)
            {
                current = joint;
                break;
            }
        }

        if (current == null)
            return null;

        bool hasLine = VRGripProfiles.TryGetLine(item, out Vector3 start, out Vector3 end);
        bool isWanted = isSliding && hasLine;
        if ((current is ConfigurableJoint) == isWanted)
            return current;

        foreach (Joint joint in Joints)
        {
            if (joint != null && joint.connectedBody == item.rig)
                Object.DestroyImmediate(joint);
        }

        return isWanted ? AttachSlide(hand, item, start, end) : AttachRigid(hand, item);
    }

    public static void DestroyItemJoints(Rigidbody hand)
    {
        if (hand == null)
            return;

        Joints.Clear();
        hand.GetComponents(Joints);
        foreach (Joint joint in Joints)
        {
            if (joint != null && joint.connectedBody != null && joint.connectedBody.GetComponent<Item>() != null)
                Object.Destroy(joint);
        }
    }

    private static Joint AttachSlide(Rigidbody hand, Item item, Vector3 start, Vector3 end)
    {
        Rigidbody rig = item.rig;
        Quaternion inverse = Quaternion.Inverse(rig.rotation);
        Vector3 handLocal = inverse * (hand.position - rig.position);

        Vector3 line = end - start;
        float length = line.magnitude;
        Vector3 axis = line / length;
        float along = Vector3.Dot(handLocal - start, axis);
        Vector3 offset = handLocal - (start + axis * along);

        Vector3 axisHand = Quaternion.Inverse(hand.rotation) * (rig.rotation * axis);
        Vector3 side = Mathf.Abs(axisHand.y) < 0.9f ? Vector3.up : Vector3.right;

        ConfigurableJoint joint = hand.gameObject.AddComponent<ConfigurableJoint>();
        joint.autoConfigureConnectedAnchor = false;
        joint.connectedBody = rig;
        joint.anchor = Vector3.zero;
        joint.connectedAnchor = (start + end) * 0.5f + offset;
        joint.axis = axisHand;
        joint.secondaryAxis = Vector3.Cross(axisHand, side).normalized;
        joint.xMotion = ConfigurableJointMotion.Limited;
        joint.yMotion = ConfigurableJointMotion.Locked;
        joint.zMotion = ConfigurableJointMotion.Locked;
        joint.angularXMotion = ConfigurableJointMotion.Locked;
        joint.angularYMotion = ConfigurableJointMotion.Locked;
        joint.angularZMotion = ConfigurableJointMotion.Locked;
        joint.linearLimit = new SoftJointLimit { limit = length * 0.5f };
        joint.xDrive = new JointDrive
        {
            positionSpring = 0f,
            positionDamper = SlideFriction * rig.mass,
            maximumForce = float.MaxValue
        };
        joint.enableCollision = false;
        return joint;
    }

    public static bool HeldByOtherHand(Item item, bool right)
    {
        if (item == null)
            return false;

        if (right)
            return VRHandItems.LeftItem == item;

        var local = Character.localCharacter;
        return local != null && local.data != null && local.data.currentItem == item;
    }
}

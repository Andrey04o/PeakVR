using UnityEngine;

namespace PeakVR;

internal static class VRHandJoint
{
    public static Joint AttachRigid(Rigidbody hand, Item item)
    {
        var joint = hand.gameObject.AddComponent<FixedJoint>();
        joint.connectedBody = item.rig;
        return joint;
    }

    public static Joint Attach(Rigidbody hand, Item item, bool alreadyHeld)
    {
        return AttachRigid(hand, item);
    }

    public static bool HeldByOtherHand(Item item, bool right)
    {
        if (item == null)
            return false;

        if (right)
            return VRLeftHand.Carried == item;

        var local = Character.localCharacter;
        return local != null && local.data != null && local.data.currentItem == item;
    }
}

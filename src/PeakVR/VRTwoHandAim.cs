using UnityEngine;

namespace PeakVR;

internal static class VRTwoHandAim
{
    private const float BlendTime = 0.2f;
    private const float MinGripSpan = 0.05f;

    private static Item item;
    private static bool isValid;
    private static Vector3 posRight;
    private static Vector3 posLeft;
    private static Quaternion rotRight = Quaternion.identity;
    private static Quaternion rotLeft = Quaternion.identity;
    private static float weight;
    private static Quaternion lastRight = Quaternion.identity;
    private static Quaternion lastLeft = Quaternion.identity;
    private static Vector3 lastOffsetLeft;

    public static void Apply(Character c, Vector3 targetRight, ref Vector3 targetLeft,
        ref Quaternion rightRot, ref Quaternion leftRot)
    {
        Item held = c.data != null ? c.data.currentItem : null;
        bool isTwoHand = held != null && held == VRHandItems.LeftItem && held.rig != null;

        if (isTwoHand && held != item)
            Capture(c, held);

        if (!isTwoHand)
            item = null;

        if (!isTwoHand || !isValid)
        {
            if (weight <= 0f)
                return;

            weight = Mathf.MoveTowards(weight, 0f, Time.deltaTime / BlendTime);
            rightRot = Quaternion.Slerp(rightRot, lastRight, weight);
            leftRot = Quaternion.Slerp(leftRot, lastLeft, weight);
            targetLeft = Vector3.Lerp(targetLeft, targetRight + lastOffsetLeft, weight);
            return;
        }

        weight = Mathf.MoveTowards(weight, 1f, Time.deltaTime / BlendTime);

        Quaternion itemRot = rightRot * Quaternion.Inverse(rotRight);
        Vector3 gripAxis = itemRot * (posLeft - posRight);
        Vector3 aimAxis = targetLeft - targetRight;
        if (aimAxis.sqrMagnitude > 1e-4f)
            itemRot = Quaternion.FromToRotation(gripAxis, aimAxis) * itemRot;

        lastRight = itemRot * rotRight;
        lastLeft = itemRot * rotLeft;
        lastOffsetLeft = itemRot * (posLeft - posRight);

        rightRot = Quaternion.Slerp(rightRot, lastRight, weight);
        leftRot = Quaternion.Slerp(leftRot, lastLeft, weight);
        targetLeft = Vector3.Lerp(targetLeft, targetRight + lastOffsetLeft, weight);
    }

    private static void Capture(Character c, Item held)
    {
        item = held;
        isValid = false;

        Rigidbody handRight = c.GetBodypartRig(BodypartType.Hand_R);
        Rigidbody handLeft = c.GetBodypartRig(BodypartType.Hand_L);
        if (handRight == null || handLeft == null)
            return;

        Quaternion inverse = Quaternion.Inverse(held.rig.rotation);
        Vector3 origin = held.rig.position;
        posRight = inverse * (handRight.position - origin);
        posLeft = inverse * (handLeft.position - origin);
        rotRight = inverse * handRight.rotation;
        rotLeft = inverse * handLeft.rotation;
        isValid = Vector3.Distance(posRight, posLeft) >= MinGripSpan;
    }
}

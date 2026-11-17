using HarmonyLib;
using UnityEngine;

namespace PeakVR;

internal static class VRRopeTwist
{
    public const float DegreesPerSegment = 45f;
    private const float MaxStepDegrees = 90f;

    private static RopeSpool lastSpool;
    private static bool lastIsLeft;
    private static Quaternion lastRotation;

    public static float Pending { get; private set; }

    public static bool Enabled => Plugin.VrEnabled && VRHandItems.Enabled;

    public static void Step(RopeSpool spool)
    {
        Pending = 0f;
        Character local = Character.localCharacter;
        Item item = spool.item;
        if (!Enabled || local == null || local.data == null || item == null || !spool.photonView.IsMine || spool.rope == null)
        {
            Reset();
            return;
        }

        bool isRight = item == local.data.currentItem;
        bool isLeft = !isRight && item == VRHandItems.LeftItem;
        Transform hand = isRight ? VRHands.Right : isLeft ? VRHands.Left : null;
        RopeTier tier = item.GetComponent<RopeTier>();
        if (hand == null || hand.parent == null || !VRHandItems.IsGripHolding(local, item)
            || (tier != null && tier.LookingToPlaceAnchor))
        {
            Reset();
            return;
        }

        Quaternion rotation = hand.parent.localRotation * hand.localRotation;
        if (lastSpool != spool || lastIsLeft != isLeft)
        {
            lastSpool = spool;
            lastIsLeft = isLeft;
            lastRotation = rotation;
            return;
        }

        Vector3 forward = rotation * Vector3.forward;
        Vector3 upBefore = Vector3.ProjectOnPlane(lastRotation * Vector3.up, forward);
        Vector3 upNow = Vector3.ProjectOnPlane(rotation * Vector3.up, forward);
        lastRotation = rotation;
        if (upBefore.sqrMagnitude < 0.0001f || upNow.sqrMagnitude < 0.0001f)
            return;

        float angle = Vector3.SignedAngle(upBefore, upNow, forward);
        if (Mathf.Abs(angle) > MaxStepDegrees)
            return;

        Pending = -angle / DegreesPerSegment;
    }

    public static void Reset()
    {
        lastSpool = null;
        Pending = 0f;
    }
}

[HarmonyPatch(typeof(RopeSpool), "FixedUpdate")]
internal static class RopeTwistPatch
{
    [HarmonyPrefix]
    private static void Prefix(RopeSpool __instance)
    {
        if (!VRRopeTwist.Enabled || __instance.rope == null || !__instance.photonView.IsMine)
            return;

        VRRopeTwist.Step(__instance);
        if (VRRopeTwist.Pending != 0f)
            __instance.Segments += VRRopeTwist.Pending;
    }
}

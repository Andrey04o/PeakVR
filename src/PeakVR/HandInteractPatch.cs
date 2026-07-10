using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace PeakVR;

[HarmonyPatch(typeof(Interaction), "DoInteractableRaycasts")]
internal static class HandInteractPatch
{
    private static readonly FieldInfo DistanceField = AccessTools.Field(typeof(Interaction), "distance");
    private static readonly FieldInfo AreaField = AccessTools.Field(typeof(Interaction), "area");

    private static readonly RaycastHit[] LineBuffer = new RaycastHit[32];
    private static readonly RaycastHit[] SphereBuffer = new RaycastHit[32];

    [HarmonyPostfix]
    private static void Postfix(Interaction __instance, ref IInteractible interactableResult)
    {
        if (VRHands.Right == null)
            return;

        var local = Character.localCharacter;
        if (local == null)
            return;

        if (!(interactableResult is ClimbHandle))
            interactableResult = FindHandInteractable(__instance, local);

        var hovering = interactableResult != null && !(interactableResult is ClimbHandle);
        VRHands.DrawInteractRay(hovering, RayLength(__instance, local));
    }

    private static bool ValidHandTarget(RaycastHit h, Character local, out IInteractible interactible)
    {
        interactible = null;
        if (h.collider == null || local.refs.ragdoll.colliderList.Contains(h.collider))
            return false;

        var candidate = h.collider.GetComponentInParent<IInteractible>();
        if (candidate == null || candidate is ClimbHandle || !candidate.IsInteractible(local))
            return false;

        if (candidate is Item item && item == local.data.currentItem)
            return false;

        interactible = candidate;
        return true;
    }

    private static float RayLength(Interaction interaction, Character local)
    {
        var t = VRHands.Right;
        var distance = DistanceField != null ? (float)DistanceField.GetValue(interaction) : 3f;
        var mask = (int)HelperFunctions.GetMask(HelperFunctions.LayerType.AllPhysical);
        var count = Physics.RaycastNonAlloc(t.position, t.forward, LineBuffer, distance, mask,
            QueryTriggerInteraction.Ignore);

        var nearest = distance;
        for (var i = 0; i < count; i++)
        {
            var h = LineBuffer[i];
            if (h.collider == null || h.distance >= nearest)
                continue;
            if (local.refs.ragdoll.colliderList.Contains(h.collider))
                continue;
            nearest = h.distance;
        }

        return nearest;
    }

    private static IInteractible FindHandInteractable(Interaction interaction, Character local)
    {
        var t = VRHands.Right;
        var origin = t.position;
        var dir = t.forward;

        var distance = DistanceField != null ? (float)DistanceField.GetValue(interaction) : 3f;
        var mask = (int)HelperFunctions.GetMask(HelperFunctions.LayerType.AllPhysical);

        var lineCount = Physics.RaycastNonAlloc(origin, dir, LineBuffer, distance, mask,
            QueryTriggerInteraction.Collide);

        IInteractible best = null;
        var bestDist = float.MaxValue;
        for (var i = 0; i < lineCount; i++)
        {
            var h = LineBuffer[i];
            if (h.distance >= bestDist || !ValidHandTarget(h, local, out var candidate))
                continue;
            best = candidate;
            bestDist = h.distance;
        }

        if (best != null)
            return best;

        var area = AreaField != null ? (float)AreaField.GetValue(interaction) : 0.3f;
        var count = Physics.SphereCastNonAlloc(origin + dir * (area / 2f), area, dir, SphereBuffer,
            distance, mask, QueryTriggerInteraction.Collide);

        var bestAngle = float.MaxValue;
        for (var i = 0; i < count; i++)
        {
            var h = SphereBuffer[i];
            if (!ValidHandTarget(h, local, out var candidate))
                continue;
            var angle = Vector3.Angle(h.point - origin, dir);
            if (angle >= bestAngle)
                continue;
            best = candidate;
            bestAngle = angle;
        }

        return best;
    }
}

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

    private const float LineStartOffset = 0.1f;
    private const float MinHitDistance = 0.0001f;

    // Contact point of whichever cast acquired the current target, so the line can end exactly where
    // we touched it rather than at the object's centre.
    private static IInteractible hitTarget;
    private static Vector3 hitPoint;
    private static int hitFrame = -1;

    public static IInteractible RightTarget { get; private set; }
    public static IInteractible LeftTarget { get; private set; }

    [HarmonyPostfix]
    private static void Postfix(Interaction __instance, ref IInteractible interactableResult)
    {
        if (VRHands.Right == null)
            return;

        var local = Character.localCharacter;
        if (local == null)
            return;

        var climbing = interactableResult is ClimbHandle;

        RightTarget = climbing ? interactableResult : FindHandInteractable(__instance, local, true);
        LeftTarget = VRGrab.LeftHandEnabled && !VRControllerHud.LeftPointing
            ? FindHandInteractable(__instance, local, false)
            : null;

        if (!climbing)
            interactableResult = Arbitrate(local);

        DrawLine(__instance, local, true, RightTarget);

        if (VRGrab.LeftHandEnabled)
            DrawLine(__instance, local, false, LeftTarget);
        else
            VRHands.SetInteractRay(false, false, default, default, default);
    }

    private static IInteractible Arbitrate(Character local)
    {
        var left = Held(local, LeftTarget) ? null : LeftTarget;

        var acting = VRLeftHand.ActingHand;
        if (acting.HasValue)
            return acting.Value ? RightTarget : left;

        if (left == null)
            return RightTarget;
        if (RightTarget == null)
            return left;

        return Nearer(local, RightTarget, left);
    }

    private static bool Held(Character local, IInteractible target)
    {
        return target is Item item && local.data != null && item == local.data.currentItem;
    }

    private static IInteractible Nearer(Character local, IInteractible right, IInteractible left)
    {
        if (!VRAim.TryRight(out var rightOrigin, out _) || !VRAim.TryLeft(out var leftOrigin, out _))
            return right;

        var rightDist = right is Component rc && rc != null
            ? Vector3.Distance(rc.transform.position, rightOrigin) : float.MaxValue;
        var leftDist = left is Component lc && lc != null
            ? Vector3.Distance(lc.transform.position, leftOrigin) : float.MaxValue;

        return leftDist < rightDist ? left : right;
    }

    private static void DrawLine(Interaction interaction, Character local, bool right, IInteractible target)
    {
        var hovering = target != null && !(target is ClimbHandle);

        var show = VRLine.ShouldShow(Plugin.Config.InteractionLine.Value, hovering);
        var aimed = right ? VRAim.TryRight(out var origin, out var dir) : VRAim.TryLeft(out origin, out dir);

        if (!show || !aimed)
        {
            VRHands.SetInteractRay(right, false, default, default, default);
            return;
        }

        Vector3 end;
        if (hovering && Plugin.Config.AimAtObjectCenter.Value && TryGetCenter(target, origin, out var center))
            end = center;
        else
            end = origin + dir * RayLength(interaction, local, right);

        var lineStart = origin + dir * LineStartOffset;

        var color = hovering ? VRLine.CharacterColor() : new Color(0.35f, 0.75f, 1f);
        VRHands.SetInteractRay(right, true, lineStart, end, color);
    }

    // The line ends at the CENTRE of the hovered interactable, as before. The only change is which
    // collider counts: a chain is many segment colliders, and taking the first one parked the line at
    // the start of the chain no matter where you aimed. Pick the segment we actually touched (or the
    // nearest one) and centre on that. Single-collider objects — every normal item — are unaffected.
    private static bool TryGetCenter(IInteractible interactable, Vector3 origin, out Vector3 center)
    {
        center = default;
        if (interactable is not Component comp || comp == null)
            return false;

        var aim = hitFrame == Time.frameCount && ReferenceEquals(hitTarget, interactable) ? hitPoint : origin;

        Collider best = null;
        var bestSqr = float.MaxValue;

        foreach (var col in comp.GetComponentsInChildren<Collider>())
        {
            if (col == null || !col.enabled)
                continue;

            var sqr = col.bounds.SqrDistance(aim);
            if (sqr >= bestSqr)
                continue;

            bestSqr = sqr;
            best = col;
        }

        center = best != null ? best.bounds.center : comp.transform.position;
        return true;
    }

    private static bool ValidHandTarget(RaycastHit h, Character local, bool right, out IInteractible interactible)
    {
        interactible = null;
        if (h.collider == null || local.refs.ragdoll.colliderList.Contains(h.collider))
            return false;

        var candidate = h.collider.GetComponentInParent<IInteractible>();
        if (candidate == null || candidate is ClimbHandle || !candidate.IsInteractible(local))
            return false;

        if (right && candidate is Item item && item == local.data.currentItem)
            return false;

        interactible = candidate;
        return true;
    }

    private static float RayLength(Interaction interaction, Character local, bool right)
    {
        var distance = DistanceField != null ? (float)DistanceField.GetValue(interaction) : 3f;
        var aimed = right ? VRAim.TryRight(out var origin, out var dir) : VRAim.TryLeft(out origin, out dir);
        if (!aimed)
            return distance;

        var mask = (int)HelperFunctions.GetMask(HelperFunctions.LayerType.AllPhysical);
        var count = Physics.RaycastNonAlloc(origin, dir, LineBuffer, distance, mask,
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

    private static IInteractible FindHandInteractable(Interaction interaction, Character local, bool right)
    {
        var aimed = right ? VRAim.TryRight(out var origin, out var dir) : VRAim.TryLeft(out origin, out dir);
        if (!aimed)
            return null;

        var distance = DistanceField != null ? (float)DistanceField.GetValue(interaction) : 3f;
        var mask = (int)HelperFunctions.GetMask(HelperFunctions.LayerType.AllPhysical);

        var lineCount = Physics.RaycastNonAlloc(origin, dir, LineBuffer, distance, mask,
            QueryTriggerInteraction.Collide);

        IInteractible best = null;
        var bestDist = float.MaxValue;
        var bestPoint = Vector3.zero;
        var bestPointValid = false;
        for (var i = 0; i < lineCount; i++)
        {
            var h = LineBuffer[i];
            if (h.distance >= bestDist || !ValidHandTarget(h, local, right, out var candidate))
                continue;
            best = candidate;
            bestDist = h.distance;
            bestPoint = h.point;
            bestPointValid = h.distance > MinHitDistance;
        }

        if (best != null)
        {
            Remember(best, bestPoint, bestPointValid);
            return best;
        }

        var area = AreaField != null ? (float)AreaField.GetValue(interaction) : 0.3f;
        var count = Physics.SphereCastNonAlloc(origin + dir * (area / 2f), area, dir, SphereBuffer,
            distance, mask, QueryTriggerInteraction.Collide);

        var bestAngle = float.MaxValue;
        for (var i = 0; i < count; i++)
        {
            var h = SphereBuffer[i];
            if (!ValidHandTarget(h, local, right, out var candidate))
                continue;
            var angle = Vector3.Angle(h.point - origin, dir);
            if (angle >= bestAngle)
                continue;
            best = candidate;
            bestAngle = angle;
            bestPoint = h.point;
            bestPointValid = h.distance > MinHitDistance;
        }

        if (best != null)
            Remember(best, bestPoint, bestPointValid);

        return best;
    }

    // A cast that starts already overlapping a collider reports distance 0 and point (0,0,0). Taking
    // that as a contact point picks whichever collider sits nearest the world origin — which on a
    // chain reads as "always snaps back to the start". Treat it as no contact instead.
    private static void Remember(IInteractible target, Vector3 point, bool valid)
    {
        if (!valid)
        {
            hitFrame = -1;
            return;
        }

        hitTarget = target;
        hitPoint = point;
        hitFrame = Time.frameCount;
    }
}

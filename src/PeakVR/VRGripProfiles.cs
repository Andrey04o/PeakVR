using System.Collections.Generic;
using UnityEngine;

namespace PeakVR;

internal enum VRGripType
{
    Auto,
    Snap,
    Line
}

internal class VRGripProfile
{
    public VRGripType Type = VRGripType.Auto;
    public bool IsAimed;
    public bool IsDeployable;
    public bool HasSnapRight;
    public bool HasSnapLeft;
    public Vector3 SnapRightPosition;
    public Quaternion SnapRightRotation = Quaternion.identity;
    public Vector3 SnapLeftPosition;
    public Quaternion SnapLeftRotation = Quaternion.identity;
    public Vector3 LineStart;
    public Vector3 LineEnd;
    public Vector3 RotationHand;
    public float Radius;
    public float Mass;
}

internal static class VRGripProfiles
{
    public const float GrabRange = 0.3f;
    private const float MaxGripOffset = 0.35f;
    private const float SnapRadius = 0.15f;
    private const float SnapMatchDistance = 0.04f;
    private const float SnapMatchAngle = 15f;
    private const float LineMinLength = 0.3f;
    private const float LineAspect = 2.2f;
    private const float LineInset = 0.05f;

    private static readonly Dictionary<ushort, VRGripProfile> Cache = new Dictionary<ushort, VRGripProfile>();

    public static VRGripProfile Get(Item item)
    {
        if (item == null)
            return null;

        if (Cache.TryGetValue(item.itemID, out VRGripProfile profile))
            return profile;

        profile = Build(item);
        Cache[item.itemID] = profile;
        Log($"profile '{item.name}' id={item.itemID} type={profile.Type} aimed={profile.IsAimed} "
            + $"deployable={profile.IsDeployable} radius={profile.Radius:F2} mass={profile.Mass:F2}");
        return profile;
    }

    public static string Capture(Item item, bool isLeft, Vector3 handPos, Quaternion handRot,
        out Vector3 dir, out Quaternion rot)
    {
        VRGripProfile profile = Get(item);
        Transform root = item.transform;
        Quaternion inv = Quaternion.Inverse(root.rotation);

        Vector3 nearest = ClosestPoint(item, handPos);
        bool isFar = Vector3.Distance(nearest, handPos) > GrabRange;
        Vector3 handLocal = inv * ((isFar ? nearest : handPos) - root.position);

        dir = handLocal;
        rot = inv * handRot;

        bool hasSnap = TryGetSnap(profile, isLeft, out Vector3 snapPos, out Quaternion snapRot);

        if (profile.Type == VRGripType.Snap && hasSnap
            && (profile.IsAimed || isFar || Vector3.Distance(handLocal, snapPos) <= SnapRadius))
        {
            dir = snapPos;
            rot = snapRot;
            return "snap";
        }

        if (profile.Type == VRGripType.Line)
        {
            dir = ClampToLine(profile, handLocal);
            return isFar ? "line far" : "line";
        }

        if (hasSnap)
        {
            float limit = Mathf.Max(MaxGripOffset, profile.Radius + GrabRange);
            dir = snapPos + Vector3.ClampMagnitude(handLocal - snapPos, limit);
        }

        return isFar ? "auto far" : "auto";
    }

    public static Vector3 ClosestPoint(Item item, Vector3 point)
    {
        Vector3 best = point;
        float bestSqr = float.MaxValue;

        foreach (Collider col in item.GetComponentsInChildren<Collider>(true))
        {
            if (col == null || !col.enabled || col.isTrigger)
                continue;

            Vector3 candidate = col is MeshCollider mesh && !mesh.convex
                ? col.bounds.ClosestPoint(point)
                : col.ClosestPoint(point);

            float sqr = (candidate - point).sqrMagnitude;
            if (sqr >= bestSqr)
                continue;

            bestSqr = sqr;
            best = candidate;
        }

        return bestSqr == float.MaxValue ? item.transform.position : best;
    }

    public static bool IsAtSnap(Item item, Transform hand, bool isLeft)
    {
        if (item == null || hand == null)
            return false;

        VRGripProfile profile = Get(item);
        if (!TryGetSnap(profile, isLeft, out Vector3 snapPos, out Quaternion snapRot))
            return false;

        Quaternion inv = Quaternion.Inverse(item.transform.rotation);
        Vector3 local = inv * (hand.position - item.transform.position);
        Quaternion localRot = inv * hand.rotation;

        return Vector3.Distance(local, snapPos) <= SnapMatchDistance
            && Quaternion.Angle(localRot, snapRot) <= SnapMatchAngle;
    }

    public static bool TryGetHandRotation(Item item, Transform hand, out Quaternion offset)
    {
        offset = Quaternion.identity;

        VRGripProfile profile = Get(item);
        if (profile == null || profile.RotationHand == Vector3.zero)
            return false;

        if (!profile.IsAimed && !IsAtSnap(item, hand, false))
            return false;

        offset = Quaternion.Euler(profile.RotationHand);
        return true;
    }

    private static bool TryGetSnap(VRGripProfile profile, bool isLeft, out Vector3 pos, out Quaternion rot)
    {
        if (isLeft && profile.HasSnapLeft)
        {
            pos = profile.SnapLeftPosition;
            rot = profile.SnapLeftRotation;
            return true;
        }

        pos = profile.SnapRightPosition;
        rot = profile.SnapRightRotation;
        return profile.HasSnapRight;
    }

    private static Vector3 ClampToLine(VRGripProfile profile, Vector3 local)
    {
        Vector3 line = profile.LineEnd - profile.LineStart;
        float length = line.magnitude;
        if (length < 1e-4f)
            return local;

        Vector3 axis = line / length;
        float along = Vector3.Dot(local - profile.LineStart, axis);
        float clamped = Mathf.Clamp(along, 0f, length);
        return local + axis * (clamped - along);
    }

    private static VRGripProfile Build(Item item)
    {
        VRGripProfile profile = new VRGripProfile();
        Transform root = item.transform;
        Quaternion inv = Quaternion.Inverse(root.rotation);

        Transform right = root.Find("Hand_R");
        if (right != null)
        {
            profile.HasSnapRight = true;
            profile.SnapRightPosition = inv * (right.position - root.position);
            profile.SnapRightRotation = inv * right.rotation;
        }

        Transform left = root.Find("Hand_L");
        if (left != null)
        {
            profile.HasSnapLeft = true;
            profile.SnapLeftPosition = inv * (left.position - root.position);
            profile.SnapLeftRotation = inv * left.rotation;
        }

        profile.IsAimed = IsAimed(item);
        profile.IsDeployable = IsDeployable(item);
        profile.Mass = item.rig != null ? item.rig.mass : 0f;

        if (item.UIData != null && item.UIData.isShootable)
            profile.RotationHand = ShootableAim.RotationOffset;

        bool hasBounds = TryGetLocalBounds(item, inv, out Bounds bounds);
        profile.Radius = hasBounds ? bounds.extents.magnitude : 0f;

        if ((profile.IsAimed || profile.IsDeployable) && profile.HasSnapRight)
            profile.Type = VRGripType.Snap;
        else if (hasBounds && TryBuildLine(bounds, profile))
            profile.Type = VRGripType.Line;

        return profile;
    }

    private static bool TryBuildLine(Bounds bounds, VRGripProfile profile)
    {
        Vector3 size = bounds.size;
        int axis = 0;
        if (size.y > size[axis])
            axis = 1;
        if (size.z > size[axis])
            axis = 2;

        float longest = size[axis];
        float second = 0f;
        for (int i = 0; i < 3; i++)
        {
            if (i != axis && size[i] > second)
                second = size[i];
        }

        if (longest < LineMinLength || longest < second * LineAspect)
            return false;

        Vector3 direction = Vector3.zero;
        direction[axis] = 1f;

        float half = longest * 0.5f;
        float reach = half - Mathf.Min(LineInset, half * 0.2f);
        profile.LineStart = bounds.center - direction * reach;
        profile.LineEnd = bounds.center + direction * reach;
        return true;
    }

    private static bool TryGetLocalBounds(Item item, Quaternion inv, out Bounds bounds)
    {
        bounds = new Bounds();
        bool found = false;
        Vector3 origin = item.transform.position;

        foreach (Collider col in item.GetComponentsInChildren<Collider>(true))
        {
            if (col == null || !col.enabled || col.isTrigger)
                continue;

            if (!TryGetColliderBox(col, out Vector3 center, out Vector3 size))
                continue;

            Transform t = col.transform;
            Vector3 half = size * 0.5f;

            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = center + new Vector3(
                    (i & 1) == 0 ? -half.x : half.x,
                    (i & 2) == 0 ? -half.y : half.y,
                    (i & 4) == 0 ? -half.z : half.z);
                Vector3 local = inv * (t.TransformPoint(corner) - origin);

                if (!found)
                {
                    bounds = new Bounds(local, Vector3.zero);
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(local);
                }
            }
        }

        return found;
    }

    private static bool TryGetColliderBox(Collider col, out Vector3 center, out Vector3 size)
    {
        center = Vector3.zero;
        size = Vector3.zero;

        switch (col)
        {
            case BoxCollider box:
                center = box.center;
                size = box.size;
                return true;
            case SphereCollider sphere:
                center = sphere.center;
                size = Vector3.one * (sphere.radius * 2f);
                return true;
            case CapsuleCollider capsule:
                center = capsule.center;
                size = Vector3.one * (capsule.radius * 2f);
                size[capsule.direction] = Mathf.Max(capsule.height, capsule.radius * 2f);
                return true;
            case MeshCollider mesh when mesh.sharedMesh != null:
                center = mesh.sharedMesh.bounds.center;
                size = mesh.sharedMesh.bounds.size;
                return true;
            default:
                return false;
        }
    }

    private static bool IsAimed(Item item)
    {
        if (item.UIData != null && item.UIData.isShootable)
            return true;

        if (item.GetComponentInChildren<Peak.Action_Antizooka>(true) != null)
            return true;

        if (item.GetComponentInChildren<Peak.Action_RaycastSpawnSomething>(true) != null)
            return true;

        return item.GetComponentInChildren<Action_ShowBinocularOverlay>(true) != null;
    }

    private static bool IsDeployable(Item item)
    {
        return item.GetComponentInChildren<Constructable>(true) != null
            || item.GetComponentInChildren<ClimbingSpikeComponent>(true) != null
            || item.GetComponentInChildren<RopeSpool>(true) != null
            || item.GetComponentInChildren<RopeTier>(true) != null
            || item.GetComponentInChildren<Action_ConstructableScoutCannonScroll>(true) != null;
    }

    public static bool IsUseAllowed(Character character)
    {
        if (!VRHandItems.Enabled || character == null || character.data == null)
            return true;

        Item item = character.data.currentItem;
        if (item == null)
            return true;

        if (!VRHandItems.IsGripHolding(character, item))
            return false;

        VRGripProfile profile = Get(item);
        if (profile == null || !profile.IsDeployable)
            return true;

        Rigidbody hand = character.GetBodypartRig(BodypartType.Hand_R);
        return hand != null && IsAtSnap(item, hand.transform, false);
    }

    private static void Log(string message)
    {
        if (Plugin.Config != null && Plugin.Config.EnableVerboseLogging.Value)
            Plugin.Log.LogInfo($"[PeakVR][Grip] {message}");
    }
}

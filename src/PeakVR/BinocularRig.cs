using UnityEngine;

namespace PeakVR;

internal static class BinocularRig
{
    public static bool HasRig { get; private set; }

    public static Vector3 ScopePos { get; private set; }
    public static Quaternion ScopeRot { get; private set; } = Quaternion.identity;
    public static Vector3 ScopeScale { get; private set; } = Vector3.one;

    public static Vector3 GripPos { get; private set; }
    public static Quaternion GripRot { get; private set; } = Quaternion.identity;

    private static bool loaded;

    public static void Load()
    {
        if (loaded)
            return;
        loaded = true;

        var prefab = PeakAssets.BinocularsRig;
        if (prefab == null)
            return;

        var scope = prefab.transform.Find("Scope");
        var grip = prefab.transform.Find("Grip");
        if (scope == null || grip == null)
        {
            Plugin.Log.LogWarning("[PeakVR] BinocularsRig needs child transforms named 'Scope' and 'Grip'");
            return;
        }

        ScopePos = scope.localPosition;
        ScopeRot = scope.localRotation;
        ScopeScale = scope.localScale;
        GripPos = grip.localPosition;
        GripRot = grip.localRotation;
        HasRig = true;

        Plugin.Log.LogInfo($"[PeakVR] Binoculars rig: scope pos={ScopePos} scale={ScopeScale} grip pos={GripPos}");
    }

    public static void ApplyHold(Transform item, Transform hand)
    {
        if (hand == null)
            return;

        ApplyHold(item, hand.position, hand.rotation);
    }

    public static void ApplyHold(Transform item, Vector3 handPos, Quaternion handRot)
    {
        if (item == null || !HasRig)
            return;

        var rotation = handRot * Quaternion.Inverse(GripRot);
        item.SetPositionAndRotation(handPos - rotation * GripPos, rotation);
    }

    public static Quaternion ControllerRotationFromIkTarget(Quaternion ikRotation)
    {
        return ikRotation * Quaternion.Inverse(VRArmIKPatch.HandRotationOffset);
    }
}

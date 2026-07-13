using UnityEngine;

namespace PeakVR;

internal static class ItemAim
{
    public static bool Enabled = true;

    public static Transform Get()
    {
        var fallback = MainCamera.instance != null ? MainCamera.instance.transform : null;

        if (!Enabled || VRHands.Right == null)
            return fallback;

        var character = Character.localCharacter;
        if (character == null || character.data.currentItem == null)
            return fallback;

        return VRHands.Right;
    }

    public static Ray GetMiddleScreenRay()
    {
        var t = Get();
        if (t == null)
            return new Ray(Vector3.zero, Vector3.forward);
        return new Ray(t.position, t.forward);
    }
}

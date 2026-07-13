using UnityEngine;

namespace PeakVR;

internal static class ShootableAim
{
    public static bool RollEnabled = true;
    public static Vector3 RotationOffset = new(5f, 0f, -45f);

    public static bool IsShootableHeld()
    {
        var c = Character.localCharacter;
        var item = c != null ? c.data.currentItem : null;
        return item != null && item.UIData.isShootable;
    }
}

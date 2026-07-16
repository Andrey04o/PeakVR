using UnityEngine;

namespace PeakVR;

// Central aim source for the hands. Direction is always the controller's aim (pointer) forward.
// Origin is the controller position, unless controllers are hidden (T3), in which case the origin
// moves to the character's hand bone (wrist) so the ray appears to come from the visible hand while
// keeping the precise controller aim direction.
internal static class VRAim
{
    public static bool TryRight(out Vector3 origin, out Vector3 direction)
        => TryHand(VRHands.Right, BodypartType.Hand_R, out origin, out direction);

    public static bool TryLeft(out Vector3 origin, out Vector3 direction)
        => TryHand(VRHands.Left, BodypartType.Hand_L, out origin, out direction);

    private static bool TryHand(Transform controller, BodypartType palm, out Vector3 origin, out Vector3 direction)
    {
        origin = default;
        direction = default;

        if (controller == null)
            return false;

        direction = controller.forward;
        origin = controller.position;

        if (VRControllerVisibility.Hidden)
        {
            var ch = Character.localCharacter;
            if (ch != null)
            {
                var bone = ch.GetBodypartRig(palm);
                if (bone != null)
                    origin = bone.transform.position;
            }
        }

        return true;
    }
}

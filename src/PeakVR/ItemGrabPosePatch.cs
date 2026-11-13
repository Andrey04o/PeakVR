using HarmonyLib;

namespace PeakVR;

[HarmonyPatch(typeof(Item), "Interact")]
internal static class ItemGrabPosePatch
{
    [HarmonyPrefix]
    private static bool Prefix(Item __instance, Character interactor)
    {
        if (interactor == null || interactor != Character.localCharacter || VRHands.Right == null)
            return true;

        VRGrab.Capture(__instance);
        return !VRGrab.TryLivePickup(interactor, __instance);
    }
}

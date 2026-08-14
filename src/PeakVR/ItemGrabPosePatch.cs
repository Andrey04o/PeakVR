using HarmonyLib;

namespace PeakVR;

[HarmonyPatch(typeof(Item), "Interact")]
internal static class ItemGrabPosePatch
{
    [HarmonyPrefix]
    private static void Prefix(Item __instance, Character interactor)
    {
        if (interactor != null && interactor == Character.localCharacter && VRHands.Right != null)
            VRGrab.Capture(__instance);
    }
}

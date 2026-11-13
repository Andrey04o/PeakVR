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

        return VRHandItems.RunVanillaInteract(interactor, __instance);
    }
}

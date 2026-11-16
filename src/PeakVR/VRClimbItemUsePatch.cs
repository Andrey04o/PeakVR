using HarmonyLib;

namespace PeakVR;

[HarmonyPatch]
internal static class VRClimbItemUsePatch
{
    private const float ClimbUseScale = 2f;

    [HarmonyPatch(typeof(Item), nameof(Item.ContinueUsePrimary))]
    [HarmonyPrefix]
    private static void PrimaryPrefix(Item __instance, out float __state)
    {
        __state = Scale(__instance);
    }

    [HarmonyPatch(typeof(Item), nameof(Item.ContinueUsePrimary))]
    [HarmonyFinalizer]
    private static void PrimaryFinalizer(Item __instance, float __state)
    {
        Restore(__instance, __state);
    }

    [HarmonyPatch(typeof(Item), nameof(Item.ContinueUseSecondary))]
    [HarmonyPrefix]
    private static void SecondaryPrefix(Item __instance, out float __state)
    {
        __state = Scale(__instance);
    }

    [HarmonyPatch(typeof(Item), nameof(Item.ContinueUseSecondary))]
    [HarmonyFinalizer]
    private static void SecondaryFinalizer(Item __instance, float __state)
    {
        Restore(__instance, __state);
    }

    private static float Scale(Item item)
    {
        if (!Plugin.VrEnabled || !IsLocalClimbing(item))
            return -1f;

        float original = item.usingTimePrimary;
        item.usingTimePrimary = original * ClimbUseScale;
        return original;
    }

    private static void Restore(Item item, float original)
    {
        if (original >= 0f && item != null)
            item.usingTimePrimary = original;
    }

    private static bool IsLocalClimbing(Item item)
    {
        Character local = Character.localCharacter;
        if (item == null || local == null || local.data == null)
            return false;

        if (item != local.data.currentItem && item != VRHandItems.LeftItem)
            return false;

        return VRHandClimb.IsHoldingAny || local.data.isClimbingAnything;
    }
}

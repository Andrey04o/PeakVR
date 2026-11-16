using HarmonyLib;

namespace PeakVR;

[HarmonyPatch(typeof(BackpackWheel))]
internal static class VRBackpackPickPatch
{
    private static bool isLeftWheel;

    [HarmonyPatch(nameof(BackpackWheel.InitWheel))]
    [HarmonyPostfix]
    private static void InitWheelPostfix()
    {
        isLeftWheel = Plugin.VrEnabled && VRHandItems.Enabled && VRHandItems.ActingHand == false;
    }

    [HarmonyPatch(nameof(BackpackWheel.Choose))]
    [HarmonyPrefix]
    private static void ChoosePrefix(BackpackWheel __instance)
    {
        if (!isLeftWheel || !__instance.chosenSlice.IsSome)
            return;

        BackpackWheelSlice.SliceData slice = __instance.chosenSlice.Value;
        if (slice.isBackpackWear || slice.isStashSlice || slice.isJetpackFuelSlice)
            return;

        if (!slice.backpackReference.GetVisuals().TryGetSpawnedItem(slice.slotID, out Item item))
            return;

        VRHandItems.SetBypass(item);
        VRHandItems.ExpectLeftTake(Character.localCharacter, item);
    }

    [HarmonyPatch(nameof(BackpackWheel.Choose))]
    [HarmonyFinalizer]
    private static void ChooseFinalizer()
    {
        VRHandItems.SetBypass(null);
    }
}

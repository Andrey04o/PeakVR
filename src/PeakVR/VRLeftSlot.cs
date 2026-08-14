using HarmonyLib;

namespace PeakVR;

internal static class VRLeftSlot
{
    public const byte SlotId = 251;

    private static ItemSlot slot;
    private static Player owner;

    public static ItemSlot For(Player player)
    {
        if (player == null || player != owner)
            return null;

        return slot;
    }

    public static int Weight => slot != null && slot.prefab != null ? slot.prefab.CarryWeight : 0;

    public static void Fill(Character character, Item item)
    {
        var player = character != null ? character.player : null;
        if (player == null || item == null)
            return;

        if (!ItemDatabase.TryGetItem(item.itemID, out var prefab))
            return;

        owner = player;
        slot ??= new ItemSlot(SlotId);
        slot.SetItem(prefab, item.data);

        character.refs.afflictions?.UpdateWeight();
    }

    public static void Empty(Character character)
    {
        slot?.EmptyOut();

        if (character != null && character.refs != null)
            character.refs.afflictions?.UpdateWeight();
    }
}

[HarmonyPatch(typeof(Player), "GetItemSlot")]
internal static class LeftSlotLookupPatch
{
    [HarmonyPrefix]
    private static bool Prefix(Player __instance, byte slotID, ref ItemSlot __result)
    {
        if (slotID != VRLeftSlot.SlotId)
            return true;

        __result = VRLeftSlot.For(__instance);
        return false;
    }
}

[HarmonyPatch(typeof(CharacterAfflictions), "UpdateWeight")]
internal static class LeftSlotWeightPatch
{
    [HarmonyPostfix]
    private static void Postfix(CharacterAfflictions __instance)
    {
        if (__instance.character != Character.localCharacter)
            return;

        var weight = VRLeftSlot.Weight;
        if (weight <= 0)
            return;

        __instance.SetStatus(CharacterAfflictions.STATUSTYPE.Weight,
            __instance.GetCurrentStatus(CharacterAfflictions.STATUSTYPE.Weight) + 0.025f * weight);
    }
}

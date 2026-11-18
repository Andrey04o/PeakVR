using HarmonyLib;

namespace PeakVR;

internal static class VRHandHolder
{
    public static Character GetHolder(Item item)
    {
        if (item == null || item.holderCharacter != null)
            return null;

        Character local = Character.localCharacter;
        if (local != null && Plugin.VrEnabled && VRHandItems.Enabled
            && (item == VRHandItems.LeftItem || item == VRHandItems.RightLoose)
            && VRHandItems.IsGripHolding(local, item))
            return local;

        return VRRemoteGrip.GetHolder(item);
    }

    public static Item FindOpenParasol(Character character)
    {
        Item item = GetHandItem(character, true);
        if (IsOpenParasol(item))
            return item;

        item = GetHandItem(character, false);
        return IsOpenParasol(item) ? item : null;
    }

    public static Item FindShade(Character character)
    {
        Item item = GetHandItem(character, true);
        if (IsShade(item))
            return item;

        item = GetHandItem(character, false);
        return IsShade(item) ? item : null;
    }

    public static bool IsOpenParasol(Item item)
    {
        return item != null && item.TryGetComponent(out Parasol parasol) && parasol.isOpen;
    }

    public static bool IsShade(Item item)
    {
        return item != null && (item.gliderHold || IsOpenParasol(item));
    }

    public static Item GetHandItem(Character character, bool isLeft)
    {
        if (character == null || character.data == null)
            return null;

        Item item;
        if (character.IsLocal)
        {
            if (!Plugin.VrEnabled || !VRHandItems.Enabled)
                return null;
            item = isLeft ? VRHandItems.LeftItem : VRHandItems.RightLoose;
            if (item == null || !VRHandItems.IsGripHolding(character, item))
                return null;
        }
        else
        {
            item = VRRemoteGrip.GetItem(character, isLeft);
        }

        return item != null && item != character.data.currentItem ? item : null;
    }

    public static bool Lend(Item item)
    {
        if (item == null || item.overrideHolderCharacter != null)
            return false;

        Character holder = GetHolder(item);
        if (holder == null)
            return false;

        item.overrideHolderCharacter = holder;
        return true;
    }

    public static void Return(Item item, bool isLent)
    {
        if (isLent && item != null)
            item.overrideHolderCharacter = null;
    }
}

[HarmonyPatch(typeof(Glider), "FixedUpdate")]
internal static class HandGliderPatch
{
    [HarmonyPrefix]
    private static void Prefix(Glider __instance, out bool __state)
    {
        __state = VRHandHolder.Lend(__instance.item);
    }

    [HarmonyFinalizer]
    private static void Finalizer(Glider __instance, bool __state)
    {
        VRHandHolder.Return(__instance.item, __state);
    }
}

[HarmonyPatch(typeof(Parasol), "FixedUpdate")]
internal static class HandParasolPatch
{
    [HarmonyPrefix]
    private static void Prefix(Parasol __instance, out bool __state)
    {
        __state = VRHandHolder.Lend(__instance.item);
    }

    [HarmonyFinalizer]
    private static void Finalizer(Parasol __instance, bool __state)
    {
        VRHandHolder.Return(__instance.item, __state);
    }
}

[HarmonyPatch(typeof(Parasol), "ToggleOpenRPC")]
internal static class HandParasolTogglePatch
{
    [HarmonyPrefix]
    private static void Prefix(Parasol __instance, out bool __state)
    {
        __state = VRHandHolder.Lend(__instance.item);
    }

    [HarmonyFinalizer]
    private static void Finalizer(Parasol __instance, bool __state)
    {
        VRHandHolder.Return(__instance.item, __state);
    }
}

[HarmonyPatch(typeof(CharacterAfflictions), nameof(CharacterAfflictions.AddSunHeat))]
internal static class HandShadePatch
{
    private static Item saved;

    [HarmonyPrefix]
    private static void Prefix(CharacterAfflictions __instance, out bool __state)
    {
        __state = false;
        Character character = __instance.character;
        if (character == null || character.data == null || VRHandHolder.IsShade(character.data.currentItem))
            return;

        Item shade = VRHandHolder.FindShade(character);
        if (shade == null)
            return;

        saved = character.data._currentitem;
        character.data._currentitem = shade;
        __state = true;
    }

    [HarmonyFinalizer]
    private static void Finalizer(CharacterAfflictions __instance, bool __state)
    {
        if (__state)
            __instance.character.data._currentitem = saved;
        saved = null;
    }
}

[HarmonyPatch(typeof(WindChillZone), "AddWindForceToCharacter")]
internal static class HandParasolWindPatch
{
    private static Item saved;

    [HarmonyPrefix]
    private static void Prefix(Character character, out bool __state)
    {
        __state = false;
        if (character == null || character.data == null || VRHandHolder.IsOpenParasol(character.data.currentItem))
            return;

        Item parasol = VRHandHolder.FindOpenParasol(character);
        if (parasol == null)
            return;

        saved = character.data._currentitem;
        character.data._currentitem = parasol;
        __state = true;
    }

    [HarmonyFinalizer]
    private static void Finalizer(Character character, bool __state)
    {
        if (__state)
            character.data._currentitem = saved;
        saved = null;
    }
}

using HarmonyLib;
using Photon.Pun;
using Zorro.Core.Serizalization;

namespace PeakVR;

[HarmonyPatch(typeof(Player), nameof(Player.AddItem))]
internal static class ClimbPickupPatch
{
    [HarmonyPrefix]
    private static bool Prefix(Player __instance, ushort itemID, ref ItemInstanceData instanceData, ref ItemSlot slot,
        ref bool __result)
    {
        Character character = __instance.character;
        if (!PhotonNetwork.IsMasterClient || character == null || character.data == null
            || !character.data.isClimbingAnything || !IsHandPlayer(character)
            || !__instance.tempFullSlot.IsEmpty() || !ItemDatabase.TryGetItem(itemID, out Item prefab)
            || prefab is Backpack)
            return true;

        for (int i = 0; i < __instance.itemSlots.Length; i++)
        {
            if (__instance.itemSlots[i].IsEmpty())
                return true;
        }

        if (instanceData == null)
        {
            instanceData = new ItemInstanceData(System.Guid.NewGuid());
            ItemInstanceDataHandler.AddInstanceData(instanceData);
        }

        __instance.tempFullSlot.SetItem(prefab, instanceData);
        slot = __instance.tempFullSlot;
        InventorySyncData sync = new InventorySyncData(__instance.itemSlots, __instance.backpackSlot, __instance.tempFullSlot);
        __instance.view.RPC("SyncInventoryRPC", RpcTarget.Others, IBinarySerializable.ToManagedArray(sync), false);
        __result = true;
        return false;
    }

    private static bool IsHandPlayer(Character character)
    {
        if (character == Character.localCharacter)
            return Plugin.VrEnabled && VRHandItems.Enabled && VRHands.Right != null;

        return VRNetworking.IsActiveRemote(character);
    }
}

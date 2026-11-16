using System.Collections.Generic;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;
using Zorro.Core;

namespace PeakVR;

internal static class VRBackpackHands
{
    private const float ReachOwn = 0.3f;
    private const float ReachWorn = 0.18f;
    private const float ReachGround = 0.22f;
    private const float SearchRange = 3f;
    private const float GroundRefresh = 0.5f;
    private const float PendingTimeout = 3f;

    private struct Zone
    {
        public BackpackVisuals Visuals;
        public BackpackReference Reference;
        public Backpack Item;
        public CharacterBackpackHandler Handler;
        public int SlotCount;
        public float Reach;
        public string Name;
    }

    private static readonly List<Zone> Zones = new List<Zone>();
    private static readonly List<Backpack> GroundPacks = new List<Backpack>();
    private static int zonesFrame = -1;
    private static float groundTime = -1f;

    private static VRBackpackStashTarget rightStash;
    private static VRBackpackStashTarget leftStash;

    private static bool isPending;
    private static Zone pendingZone;
    private static byte pendingSlot;
    private static ushort pendingItemId;
    private static float pendingSince;

    public static bool Enabled => Plugin.VrEnabled && VRHandItems.Enabled;

    public static bool IsPackTarget(IInteractible target)
    {
        if (target is VRBackpackStashTarget)
            return true;

        return target is Item item && item != null && item.itemState == ItemState.InBackpack;
    }

    public static IInteractible FindTarget(Character local, bool isRight)
    {
        if (!Enabled || local == null || local.data == null)
            return null;

        Transform hand = isRight ? VRHands.Right : VRHands.Left;
        if (hand == null)
            return null;

        Item held = GetHeld(local, isRight);
        if (held != null)
        {
            if (!TryFindStash(local, isRight, held, out Zone zone, out byte slot))
                return null;

            VRBackpackStashTarget target = GetStashTarget(isRight);
            target.Set(zone.Name, held, zone.Visuals.backpackSlots[slot]);
            return target;
        }

        if (VRHandItems.IsBusy(local, !isRight))
            return null;

        return FindSlotItem(local, hand.position);
    }

    public static bool TryTake(Character local, Item item, bool isLeft)
    {
        if (!Enabled || item == null || item.itemState != ItemState.InBackpack)
            return false;

        VRHandItems.SetBypass(item);
        if (isLeft)
            VRHandItems.ExpectLeftPick(local);
        item.Interact(local);
        VRHandItems.SetBypass(null);
        Log($"{(isLeft ? "left" : "right")} takes '{item.name}' from a backpack");
        return true;
    }

    public static bool TryStashRight(Character local, Item held)
    {
        if (!Enabled || held == null || held != local.data.currentItem || held == VRHandItems.LeftItem)
            return false;

        if (!TryFindStash(local, true, held, out Zone zone, out byte slot))
            return false;

        if (zone.Item != null)
            zone.Item.Stash(local, slot);
        else
            zone.Handler.StashInBackpack(local, slot);

        Log($"right stashed '{held.name}' into {zone.Name} slot {slot}");
        return true;
    }

    public static bool TryFindLeftStash(Character local, Item held)
    {
        return Enabled && held != null && held != local.data.currentItem
            && TryFindStash(local, false, held, out _, out _);
    }

    public static void StashLeft(Character local, Item item)
    {
        if (!TryFindStash(local, false, item, out Zone zone, out byte slot, true))
            return;

        isPending = true;
        pendingZone = zone;
        pendingSlot = slot;
        pendingItemId = item.itemID;
        pendingSince = Time.time;

        VRHandItems.SetBypass(item);
        item.Interact(local);
        VRHandItems.SetBypass(null);
        Log($"left stashing '{item.name}' into {zone.Name} slot {slot}");
    }

    public static bool TryCompleteStash(CharacterItems items, byte slotID)
    {
        if (!isPending)
            return false;

        isPending = false;
        Character local = Character.localCharacter;
        if (local == null || items.character != local || Time.time - pendingSince > PendingTimeout
            || !pendingZone.Reference.exists)
            return false;

        ItemSlot slot = local.player.GetItemSlot(slotID);
        if (slot == null || slot.IsEmpty() || slot.prefab == null || slot.prefab.itemID != pendingItemId)
            return false;

        BackpackData data = pendingZone.Reference.GetData();
        int target = data.itemSlots[pendingSlot].IsEmpty() ? pendingSlot : FirstEmpty(data, pendingZone.SlotCount);
        if (target < 0)
            return false;

        PhotonView playerView = local.player.GetComponent<PhotonView>();
        string rpc = pendingZone.Item != null ? "RPCAddItemToBackpack" : "RPCAddItemToCharacterBackpack";
        pendingZone.Reference.view.RPC(rpc, RpcTarget.All, playerView, slotID, (byte)target);
        local.player.EmptySlot(Optionable<byte>.Some(slotID));
        local.refs.afflictions.UpdateWeight();
        items.RefreshAllCharacterCarryWeight();
        Log($"left stash done into {pendingZone.Name} slot {target}");
        return true;
    }

    private static Item GetHeld(Character local, bool isRight)
    {
        Item right = local.data.currentItem;
        Item left = VRHandItems.LeftItem;
        if (right != null && right == left)
            return null;

        if (isRight)
            return right != null && VRHandItems.IsGripHolding(local, right) ? right : null;

        return left;
    }

    private static bool TryFindStash(Character local, bool isRight, Item held, out Zone best, out byte bestSlot,
        bool isAnyDistance = false)
    {
        best = default;
        bestSlot = 0;

        if (held == null || held is Backpack)
            return false;

        Transform hand = isRight ? VRHands.Right : VRHands.Left;
        if (hand == null)
            return false;

        Vector3 point = hand.position;
        float bestDist = float.MaxValue;
        bool isFound = false;
        List<Zone> zones = GetZones(local);

        for (int i = 0; i < zones.Count; i++)
        {
            Zone zone = zones[i];
            if (zone.Item == held)
                continue;

            float reach = isAnyDistance ? zone.Reach * 2f : zone.Reach;
            if (NearestSlot(zone, point) > reach)
                continue;

            BackpackData data = zone.Reference.GetData();
            for (int k = 0; k < zone.SlotCount; k++)
            {
                if (!data.itemSlots[k].IsEmpty())
                    continue;

                float dist = Vector3.Distance(point, zone.Visuals.backpackSlots[k].position);
                if (dist >= bestDist)
                    continue;

                bestDist = dist;
                best = zone;
                bestSlot = (byte)k;
                isFound = true;
            }
        }

        return isFound;
    }

    private static Item FindSlotItem(Character local, Vector3 point)
    {
        List<Zone> zones = GetZones(local);
        Item best = null;
        float bestDist = float.MaxValue;

        for (int i = 0; i < zones.Count; i++)
        {
            Zone zone = zones[i];
            for (int k = 0; k < zone.SlotCount; k++)
            {
                if (!zone.Visuals.TryGetSpawnedItem((byte)k, out Item item) || item.itemState != ItemState.InBackpack)
                    continue;

                float dist = Vector3.Distance(point, zone.Visuals.backpackSlots[k].position);
                if (dist > zone.Reach || dist >= bestDist)
                    continue;

                bestDist = dist;
                best = item;
            }
        }

        return best;
    }

    private static float NearestSlot(Zone zone, Vector3 point)
    {
        float nearest = float.MaxValue;
        for (int k = 0; k < zone.SlotCount; k++)
        {
            float dist = Vector3.Distance(point, zone.Visuals.backpackSlots[k].position);
            if (dist < nearest)
                nearest = dist;
        }

        return nearest;
    }

    private static int FirstEmpty(BackpackData data, int slotCount)
    {
        for (int k = 0; k < slotCount; k++)
        {
            if (data.itemSlots[k].IsEmpty())
                return k;
        }

        return -1;
    }

    private static List<Zone> GetZones(Character local)
    {
        if (zonesFrame == Time.frameCount)
            return Zones;

        zonesFrame = Time.frameCount;
        Zones.Clear();

        Vector3 center = local.Center;
        for (int i = 0; i < Character.AllCharacters.Count; i++)
        {
            Character character = Character.AllCharacters[i];
            if (character == null || character.player == null || character.refs == null
                || character.refs.backpackHandler == null || character.data == null || character.data.dead)
                continue;

            bool isOwn = character == local;
            if (!isOwn && Vector3.Distance(character.Center, center) > SearchRange)
                continue;

            AddWorn(character, isOwn);
        }

        RefreshGround();
        for (int i = 0; i < GroundPacks.Count; i++)
        {
            Backpack pack = GroundPacks[i];
            if (pack == null || pack.itemState != ItemState.Ground
                || Vector3.Distance(pack.transform.position, center) > SearchRange)
                continue;

            ItemBackpackVisuals visuals = pack.GetComponent<ItemBackpackVisuals>();
            if (visuals == null || visuals.backpackSlots == null)
                continue;

            Zones.Add(new Zone
            {
                Visuals = visuals,
                Reference = BackpackReference.GetFromBackpackItem(pack),
                Item = pack,
                SlotCount = SlotCount(visuals, pack.slotCount, pack.GetData<BackpackData>(DataEntryKey.BackpackData)),
                Reach = ReachGround,
                Name = pack.GetItemName()
            });
        }

        return Zones;
    }

    private static void AddWorn(Character character, bool isOwn)
    {
        BackpackSlot slot = character.player.backpackSlot;
        if (slot == null || slot.backpackType == BackpackSlot.BackpackType.None
            || slot.backpackType == BackpackSlot.BackpackType.Rocketpack)
            return;

        CharacterItems items = character.refs.items;
        if (items != null && items.currentSelectedSlot.IsSome && items.currentSelectedSlot.Value == 3)
            return;

        CharacterBackpackHandler handler = character.refs.backpackHandler;
        BackpackOnBackVisuals visuals = handler.activeBackpackVisuals;
        if (visuals == null || visuals.backpackSlots == null)
            return;

        BackpackReference reference = BackpackReference.GetFromEquippedBackpack(character);
        Zones.Add(new Zone
        {
            Visuals = visuals,
            Reference = reference,
            Handler = handler,
            SlotCount = SlotCount(visuals, visuals.slotCount, reference.GetData()),
            Reach = isOwn ? ReachOwn : ReachWorn,
            Name = visuals.GetName()
        });
    }

    private static int SlotCount(BackpackVisuals visuals, int count, BackpackData data)
    {
        int result = Mathf.Min(count, visuals.backpackSlots.Length);
        if (data != null && data.itemSlots != null)
            result = Mathf.Min(result, data.itemSlots.Length);
        return Mathf.Max(result, 0);
    }

    private static void RefreshGround()
    {
        if (groundTime >= 0f && Time.time - groundTime < GroundRefresh)
            return;

        groundTime = Time.time;
        GroundPacks.Clear();
        for (int i = 0; i < Item.ALL_ITEMS.Count; i++)
        {
            if (Item.ALL_ITEMS[i] is Backpack pack && pack != null)
                GroundPacks.Add(pack);
        }
    }

    private static VRBackpackStashTarget GetStashTarget(bool isRight)
    {
        VRBackpackStashTarget target = isRight ? rightStash : leftStash;
        if (target != null)
            return target;

        GameObject go = new GameObject(isRight ? "PeakVR Stash Target Right" : "PeakVR Stash Target Left");
        Object.DontDestroyOnLoad(go);
        target = go.AddComponent<VRBackpackStashTarget>();

        if (isRight)
            rightStash = target;
        else
            leftStash = target;

        return target;
    }

    private static void Log(string message)
    {
        if (Plugin.Config != null && Plugin.Config.EnableVerboseLogging.Value)
            Plugin.Log.LogInfo($"[PeakVR][Backpack] {message}");
    }
}

internal class VRBackpackStashTarget : MonoBehaviour, IInteractible
{
    private string packName = "";
    private Item held;
    private Transform slot;

    public void Set(string name, Item item, Transform slotTransform)
    {
        packName = name;
        held = item;
        slot = slotTransform;
        if (slot != null)
            transform.position = slot.position;
    }

    public bool IsInteractible(Character interactor)
    {
        return true;
    }

    public void Interact(Character interactor)
    {
    }

    public void HoverEnter()
    {
    }

    public void HoverExit()
    {
    }

    public Vector3 Center()
    {
        return slot != null ? slot.position : transform.position;
    }

    public Transform GetTransform()
    {
        return transform;
    }

    public string GetInteractionText()
    {
        string itemName = held != null ? held.GetItemName() : "";
        return LocalizedText.GetText("STASHITEM").Replace("#", itemName);
    }

    public string GetName()
    {
        return packName;
    }
}

[HarmonyPatch(typeof(CharacterItems), nameof(CharacterItems.OnPickupAccepted))]
internal static class BackpackStashPickupPatch
{
    [HarmonyPrefix]
    private static bool Prefix(CharacterItems __instance, byte slotID)
    {
        return !VRBackpackHands.TryCompleteStash(__instance, slotID);
    }
}

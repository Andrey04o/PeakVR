using System.Collections.Generic;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;
using Zorro.Core;

namespace PeakVR;

internal struct BackpackZone
{
    public BackpackVisuals Visuals;
    public BackpackReference Reference;
    public Backpack Item;
    public CharacterBackpackHandler Handler;
    public int SlotCount;
    public float Reach;
    public string Name;
}

internal struct BackpackSlotTarget
{
    public bool IsFound;
    public bool IsStash;
    public bool IsLine;
    public BackpackZone Zone;
    public byte Slot;
    public Item Held;
    public Item SlotItem;
    public Vector3 Probe;

    public Transform SlotTransform => Zone.Visuals.backpackSlots[Slot];
}

internal struct BackpackMark
{
    public Transform Slot;
    public Item Ghost;
    public Item Outline;
}

internal static class VRBackpackHands
{
    private const float ReachWorn = 0.18f;
    private const float ReachPack = 0.2f;
    private const float SlotTouch = 0.12f;
    private const float LineStashRadius = 0.2f;
    private const float LineStashSpread = 0.12f;
    private const float LineTakeRadius = 0.25f;
    private const float LineTakeSpread = 0.15f;
    private const float LineRange = 3f;
    private const float TakeOffBehind = 0.02f;
    private const float TakeOffAbove = 0.1f;
    private const float TakeOffWidth = 0.35f;
    private const float SearchRange = 3f;
    private const float GroundRefresh = 0.5f;
    private const float PendingTimeout = 3f;
    private const float TakeCooldown = 0.3f;
    private const float ShoulderBehind = -0.05f;
    private const float ShoulderBelow = -0.15f;
    private const float ShoulderWidth = 0.45f;
    private const float ShoulderRange = 0.7f;
    private const byte WornSlot = 3;

    private static readonly List<BackpackZone> Zones = new List<BackpackZone>();
    private static readonly List<Backpack> GroundPacks = new List<Backpack>();
    private static readonly List<Collider> HeldColliders = new List<Collider>();
    private static readonly Collider[] OverlapBuffer = new Collider[32];
    private static Bounds heldBounds;

    private const float IgnoreNear = 0.15f;
    private const float IgnoreClear = 0.02f;
    private static readonly List<Collider> ItemBuffer = new List<Collider>();
    private static readonly List<Collider> PackBuffer = new List<Collider>();
    private static readonly List<Item> IgnoredItems = new List<Item>();
    private static readonly List<Backpack> IgnoredPacks = new List<Backpack>();
    private static int zonesFrame = -1;
    private static float groundTime = -1f;

    private static BackpackSlotTarget rightTarget;
    private static BackpackSlotTarget leftTarget;
    private static int rightFrame = -1;
    private static int leftFrame = -1;

    private static VRBackpackStashTarget rightStash;
    private static VRBackpackStashTarget leftStash;

    private static bool isPending;
    private static BackpackZone pendingZone;
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
        BackpackSlotTarget target = default;
        if (Enabled && local != null && local.data != null)
            target = Find(local, isRight, GetHeld(local, isRight));

        if (isRight)
        {
            rightTarget = target;
            rightFrame = Time.frameCount;
        }
        else
        {
            leftTarget = target;
            leftFrame = Time.frameCount;
        }

        if (!target.IsFound)
            return null;

        if (!target.IsStash)
            return target.SlotItem;

        VRBackpackStashTarget stash = GetStashTarget(isRight);
        stash.Set(target.Zone.Name, target.Held, target.SlotTransform);
        return stash;
    }

    public static bool TryGetLineEnd(bool isRight, out Vector3 end)
    {
        end = Vector3.zero;
        BackpackSlotTarget target = GetTarget(isRight);
        if (!target.IsFound || !target.IsLine)
            return false;

        end = target.SlotTransform.position;
        return true;
    }

    public static void CollectMarks(Character local, List<BackpackMark> marks)
    {
        if (!Enabled || local == null || local.data == null)
            return;

        AddMarks(local, true, marks);
        AddMarks(local, false, marks);
    }

    public static bool TryTake(Character local, Item item, bool isLeft)
    {
        if (!Enabled || item == null || item.itemState != ItemState.InBackpack)
            return false;

        CharacterItems items = local.refs != null ? local.refs.items : null;
        float lastEquip = items != null ? items.lastEquippedSlotTime : 0f;
        if (items != null && lastEquip <= Time.time)
            items.lastEquippedSlotTime = Mathf.Min(lastEquip, Time.time - TakeCooldown);

        VRHandItems.SetBypass(item);
        if (isLeft)
            VRHandItems.ExpectLeftTake(local, item);
        item.Interact(local);
        VRHandItems.SetBypass(null);

        if (items != null)
            items.lastEquippedSlotTime = lastEquip;
        Log($"{(isLeft ? "left" : "right")} takes '{item.name}' from a backpack");
        return true;
    }

    public static bool TryStashRight(Character local, Item held)
    {
        if (!Enabled || held == null || held != local.data.currentItem || held == VRHandItems.LeftItem)
            return false;

        BackpackSlotTarget target = Find(local, true, held);
        if (!target.IsFound || !target.IsStash)
            return false;

        if (target.Zone.Item != null)
            target.Zone.Item.Stash(local, target.Slot);
        else
            target.Zone.Handler.StashInBackpack(local, target.Slot);

        Log($"right stashed '{held.name}' into {target.Zone.Name} slot {target.Slot}{(target.IsLine ? " by line" : "")}");
        return true;
    }

    public static bool TryGetLeftStash(Character local, Item held, out BackpackSlotTarget target)
    {
        target = default;
        if (!Enabled || held == null || held == local.data.currentItem)
            return false;

        target = Find(local, false, held);
        return target.IsFound && target.IsStash;
    }

    public static void StashLeft(Character local, Item item, BackpackSlotTarget target)
    {
        isPending = true;
        pendingZone = target.Zone;
        pendingSlot = target.Slot;
        pendingItemId = item.itemID;
        pendingSince = Time.time;

        VRHandItems.SetBypass(item);
        item.Interact(local);
        VRHandItems.SetBypass(null);
        Log($"left stashing '{item.name}' into {target.Zone.Name} slot {target.Slot}{(target.IsLine ? " by line" : "")}");
    }

    public static bool TryWearRight(Character local, Item held)
    {
        if (!Enabled || held is not Backpack || held != local.data.currentItem || held == VRHandItems.LeftItem)
            return false;

        CharacterItems items = local.refs.items;
        if (items == null || !items.currentSelectedSlot.IsSome || items.currentSelectedSlot.Value != WornSlot
            || VRHands.Right == null || !IsBehindShoulder(local, VRHands.Right.position))
            return false;

        items.currentSelectedSlot = Optionable<byte>.None;
        items.lastEquippedSlotTime = Time.time;
        local.photonView.RPC("EquipSlotRpc", RpcTarget.All, -1, -1);
        local.refs.afflictions.UpdateWeight();
        Log($"right put '{held.name}' on the back");
        return true;
    }

    public static bool CanWearLoose(Character local, Item held, bool isLeft, out bool isSwap)
    {
        isSwap = false;
        Transform hand = isLeft ? VRHands.Left : VRHands.Right;
        if (!Enabled || held is not Backpack || held == local.data.currentItem || local.player == null
            || local.refs == null || hand == null || !IsBehindShoulder(local, hand.position))
            return false;

        if (local.player.backpackSlot.IsEmpty())
            return true;

        CharacterItems items = local.refs.items;
        isSwap = isLeft && local.data.currentItem is Backpack && items != null && items.currentSelectedSlot.IsSome
            && items.currentSelectedSlot.Value == WornSlot;
        if (!isSwap)
            Log($"{(isLeft ? "left" : "right")} does not wear '{held.name}': already wearing a pack");

        return isSwap;
    }

    public static void Wear(Character local, Item item)
    {
        if (item is not Backpack pack)
            return;

        CharacterItems items = local.refs != null ? local.refs.items : null;
        if (items != null && local.data.currentItem == null && items.currentSelectedSlot.IsSome
            && items.currentSelectedSlot.Value == WornSlot)
            items.currentSelectedSlot = Optionable<byte>.None;

        VRHandItems.SetBypass(item);
        pack.Wear(local);
        VRHandItems.SetBypass(null);
        Log($"wearing '{item.name}'");
    }

    public static bool TryTakeOff(Character local, bool isRight)
    {
        if (!Enabled || local == null || local.player == null || local.refs == null || local.player.backpackSlot.IsEmpty())
            return false;

        CharacterItems items = local.refs.items;
        if (items == null || local.data.currentItem is Backpack)
            return false;

        Transform hand = isRight ? VRHands.Right : VRHands.Left;
        if (hand == null || !IsOverShoulder(local, hand.position))
            return false;

        Item right = local.data.currentItem;
        if (isRight && right != null)
            return false;

        if (!isRight && right != null && (right.UIData == null || !right.UIData.canPocket))
        {
            Log($"left can't take the pack off: '{right.name}' in the right hand can't be pocketed");
            return false;
        }

        if (!isRight)
            VRHandItems.ExpectLeftPick(local);
        items.EquipSlot(Optionable<byte>.Some(WornSlot));
        Log($"{(isRight ? "right" : "left")} takes the pack off the back");
        return true;
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

    public static void StepCollisions(Character local)
    {
        if (!Enabled || local == null || local.data == null)
        {
            RestoreCollisions();
            return;
        }

        Item right = local.data.currentItem;
        Item left = VRHandItems.LeftItem;

        for (int i = IgnoredItems.Count - 1; i >= 0; i--)
        {
            Item item = IgnoredItems[i];
            Backpack pack = IgnoredPacks[i];
            if (item == null || pack == null || item.itemState == ItemState.InBackpack || pack.itemState == ItemState.InBackpack)
            {
                RemoveIgnored(i, item != null && pack != null);
                continue;
            }

            bool isHeld = item == right || item == left;
            float gap = Gap(item, pack);
            if (gap <= (isHeld ? IgnoreNear : IgnoreClear))
                continue;

            RemoveIgnored(i, true);
            Log($"'{item.name}' collides with '{pack.name}' again");
        }

        AddIgnored(local, right);
        if (left != right)
            AddIgnored(local, left);
    }

    private static void AddIgnored(Character local, Item item)
    {
        if (item == null || item is Backpack)
            return;

        List<BackpackZone> zones = GetZones(local);
        for (int i = 0; i < zones.Count; i++)
        {
            Backpack pack = zones[i].Item;
            if (pack == null || IsIgnored(item, pack) || Gap(item, pack) > IgnoreNear)
                continue;

            SetIgnored(item, pack, true);
            IgnoredItems.Add(item);
            IgnoredPacks.Add(pack);
            Log($"'{item.name}' passes through '{pack.name}'");
        }
    }

    private static bool IsIgnored(Item item, Backpack pack)
    {
        for (int i = 0; i < IgnoredItems.Count; i++)
        {
            if (IgnoredItems[i] == item && IgnoredPacks[i] == pack)
                return true;
        }

        return false;
    }

    private static void RemoveIgnored(int index, bool isRestore)
    {
        if (isRestore)
            SetIgnored(IgnoredItems[index], IgnoredPacks[index], false);

        IgnoredItems.RemoveAt(index);
        IgnoredPacks.RemoveAt(index);
    }

    private static void RestoreCollisions()
    {
        for (int i = IgnoredItems.Count - 1; i >= 0; i--)
            RemoveIgnored(i, IgnoredItems[i] != null && IgnoredPacks[i] != null);
    }

    private static void SetIgnored(Item item, Backpack pack, bool isIgnored)
    {
        item.GetComponentsInChildren(true, ItemBuffer);
        pack.GetComponentsInChildren(true, PackBuffer);
        for (int i = 0; i < ItemBuffer.Count; i++)
        {
            for (int k = 0; k < PackBuffer.Count; k++)
            {
                if (ItemBuffer[i] != null && PackBuffer[k] != null)
                    Physics.IgnoreCollision(ItemBuffer[i], PackBuffer[k], isIgnored);
            }
        }

        ItemBuffer.Clear();
        PackBuffer.Clear();
    }

    private static float Gap(Item item, Backpack pack)
    {
        if (!TryGetSolidBounds(item, ItemBuffer, out Bounds a) || !TryGetSolidBounds(pack, PackBuffer, out Bounds b))
            return float.MaxValue;

        float dx = Mathf.Max(0f, Mathf.Max(a.min.x - b.max.x, b.min.x - a.max.x));
        float dy = Mathf.Max(0f, Mathf.Max(a.min.y - b.max.y, b.min.y - a.max.y));
        float dz = Mathf.Max(0f, Mathf.Max(a.min.z - b.max.z, b.min.z - a.max.z));
        return Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private static bool TryGetSolidBounds(Component owner, List<Collider> buffer, out Bounds bounds)
    {
        bounds = default;
        bool isFound = false;
        owner.GetComponentsInChildren(false, buffer);
        for (int i = 0; i < buffer.Count; i++)
        {
            Collider col = buffer[i];
            if (col == null || !col.enabled || col.isTrigger)
                continue;

            if (!isFound)
            {
                bounds = col.bounds;
                isFound = true;
            }
            else
            {
                bounds.Encapsulate(col.bounds);
            }
        }

        buffer.Clear();
        return isFound;
    }

    private static BackpackSlotTarget GetTarget(bool isRight)
    {
        if (isRight)
            return rightFrame == Time.frameCount ? rightTarget : default;

        return leftFrame == Time.frameCount ? leftTarget : default;
    }

    private static Item GetHeld(Character local, bool isRight)
    {
        Item right = local.data.currentItem;
        Item left = VRHandItems.LeftItem;
        if (right != null && right == left)
            return null;

        if (isRight && right == null)
            return VRHandItems.RightLoose;

        if (isRight)
            return VRHandItems.IsGripHolding(local, right) ? right : null;

        return left;
    }

    private static bool TryGetProbe(Character local, bool isRight, Item held, out Vector3 probe)
    {
        probe = Vector3.zero;
        if (held != null)
        {
            probe = held.Center();
            return true;
        }

        Rigidbody bone = local.GetBodypartRig(isRight ? BodypartType.Hand_R : BodypartType.Hand_L);
        if (bone == null)
            return false;

        probe = bone.position;
        return true;
    }

    private static Vector3 GetController(bool isRight, Vector3 fallback)
    {
        Transform hand = isRight ? VRHands.Right : VRHands.Left;
        return hand != null ? hand.position : fallback;
    }

    private static BackpackSlotTarget Find(Character local, bool isRight, Item held)
    {
        BackpackSlotTarget result = default;
        if (held is Backpack)
            return result;

        if (!TryGetProbe(local, isRight, held, out Vector3 probe))
            return result;

        Vector3 controller = GetController(isRight, probe);
        bool isStash = held != null;
        bool hasSurface = isStash && CollectHeld(held);
        result.IsStash = isStash;
        result.Held = held;
        result.Probe = probe;

        List<BackpackZone> zones = GetZones(local);
        float bestDist = float.MaxValue;
        for (int i = 0; i < zones.Count; i++)
        {
            BackpackZone zone = zones[i];
            if (!IsAlive(zone) || (zone.Item != null && zone.Item == held))
                continue;

            BackpackData data = zone.Reference.GetData();
            for (int k = 0; k < zone.SlotCount; k++)
            {
                if (!IsUsable(zone, data, k, isStash, out Item slotItem))
                    continue;

                Vector3 slot = zone.Visuals.backpackSlots[k].position;
                float dist = Vector3.Distance(controller, slot);
                bool isNear = dist <= zone.Reach;
                if (isStash)
                {
                    bool isTouching = hasSurface ? IsTouching(slot) : Vector3.Distance(probe, slot) <= SlotTouch;
                    if (isTouching)
                    {
                        isNear = true;
                        dist = Mathf.Min(dist, Vector3.Distance(probe, slot));
                    }
                }
                else
                {
                    float bone = Vector3.Distance(probe, slot);
                    if (bone <= zone.Reach)
                    {
                        isNear = true;
                        dist = Mathf.Min(dist, bone);
                    }
                }

                if (!isNear || dist >= bestDist)
                    continue;

                bestDist = dist;
                SetFound(ref result, zone, k, slotItem, false);
            }
        }

        if (result.IsFound)
            return result;

        bool isAimed = isRight ? VRAim.TryRight(out Vector3 origin, out Vector3 dir) : VRAim.TryLeft(out origin, out dir);
        if (!isAimed)
            return result;

        float radius = isStash ? LineStashRadius : LineTakeRadius;
        float spread = isStash ? LineStashSpread : LineTakeSpread;
        float bestSlope = float.MaxValue;
        for (int i = 0; i < zones.Count; i++)
        {
            BackpackZone zone = zones[i];
            if (!IsAlive(zone) || (zone.Item != null && zone.Item == held))
                continue;

            BackpackData data = zone.Reference.GetData();
            for (int k = 0; k < zone.SlotCount; k++)
            {
                if (!IsUsable(zone, data, k, isStash, out Item slotItem))
                    continue;

                Vector3 toSlot = zone.Visuals.backpackSlots[k].position - origin;
                float along = Vector3.Dot(toSlot, dir);
                if (along <= 0.01f || along > LineRange)
                    continue;

                float off = (toSlot - dir * along).magnitude;
                float slope = off / along;
                if (off > Mathf.Max(radius, along * spread) || slope >= bestSlope)
                    continue;

                bestSlope = slope;
                SetFound(ref result, zone, k, slotItem, true);
            }
        }

        return result;
    }

    private static bool CollectHeld(Item held)
    {
        held.GetComponentsInChildren(false, HeldColliders);
        bool isFound = false;
        for (int i = HeldColliders.Count - 1; i >= 0; i--)
        {
            Collider col = HeldColliders[i];
            if (col == null || !col.enabled || col.isTrigger)
            {
                HeldColliders.RemoveAt(i);
                continue;
            }

            if (!isFound)
            {
                heldBounds = col.bounds;
                isFound = true;
            }
            else
            {
                heldBounds.Encapsulate(col.bounds);
            }
        }

        return isFound;
    }

    private static bool IsTouching(Vector3 point)
    {
        if (heldBounds.SqrDistance(point) > SlotTouch * SlotTouch)
            return false;

        int count = Physics.OverlapSphereNonAlloc(point, SlotTouch, OverlapBuffer, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            if (HeldColliders.Contains(OverlapBuffer[i]))
                return true;
        }

        return false;
    }

    private static bool IsAlive(BackpackZone zone)
    {
        return zone.Visuals != null && (zone.Item != null || zone.Handler != null);
    }

    private static void SetFound(ref BackpackSlotTarget result, BackpackZone zone, int slot, Item slotItem, bool isLine)
    {
        result.IsFound = true;
        result.IsLine = isLine;
        result.Zone = zone;
        result.Slot = (byte)slot;
        result.SlotItem = slotItem;
    }

    private static bool IsUsable(BackpackZone zone, BackpackData data, int slot, bool isStash, out Item slotItem)
    {
        slotItem = null;
        if (isStash)
            return data != null && data.itemSlots != null && data.itemSlots[slot].IsEmpty();

        return zone.Visuals.TryGetSpawnedItem((byte)slot, out slotItem) && slotItem.itemState == ItemState.InBackpack;
    }

    private static void AddMarks(Character local, bool isRight, List<BackpackMark> marks)
    {
        BackpackSlotTarget target = GetTarget(isRight);
        if (!target.IsFound || !IsAlive(target.Zone))
            return;

        marks.Add(new BackpackMark
        {
            Slot = target.SlotTransform,
            Ghost = target.IsStash ? target.Held : null,
            Outline = target.IsStash ? null : target.SlotItem
        });
    }

    private static bool IsBehindShoulder(Character local, Vector3 point)
    {
        return IsInShoulderZone(local, point, ShoulderBehind, ShoulderBelow, ShoulderWidth);
    }

    private static bool IsOverShoulder(Character local, Vector3 point)
    {
        return IsInShoulderZone(local, point, TakeOffBehind, TakeOffAbove, TakeOffWidth);
    }

    private static bool IsInShoulderZone(Character local, Vector3 point, float minBehind, float minAbove, float maxWidth)
    {
        Bodypart torso = local.GetBodypart(BodypartType.Torso);
        if (torso == null)
            return false;

        Vector3 back = -local.data.lookDirection_Flat;
        back.y = 0f;
        if (back.sqrMagnitude < 0.0001f)
            return false;
        back.Normalize();

        Vector3 offset = point - torso.transform.position;
        float behind = Vector3.Dot(offset, back);
        Vector3 side = offset - back * behind;
        side.y = 0f;

        return behind > minBehind && offset.y > minAbove && side.magnitude < maxWidth
            && offset.magnitude < ShoulderRange;
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

    private static List<BackpackZone> GetZones(Character local)
    {
        if (zonesFrame == Time.frameCount)
            return Zones;

        zonesFrame = Time.frameCount;
        Zones.Clear();

        Vector3 center = local.Center;
        for (int i = 0; i < Character.AllCharacters.Count; i++)
        {
            Character character = Character.AllCharacters[i];
            if (character == null || character == local || character.player == null || character.refs == null
                || character.refs.backpackHandler == null || character.data == null || character.data.dead)
                continue;

            if (Vector3.Distance(character.Center, center) > SearchRange)
                continue;

            AddWorn(character);
        }

        if (local.data.currentItem is Backpack held && held.itemState == ItemState.Held)
            AddPack(held);

        RefreshGround();
        for (int i = 0; i < GroundPacks.Count; i++)
        {
            Backpack pack = GroundPacks[i];
            if (pack == null || pack.itemState != ItemState.Ground
                || Vector3.Distance(pack.transform.position, center) > SearchRange)
                continue;

            AddPack(pack);
        }

        return Zones;
    }

    private static void AddPack(Backpack pack)
    {
        ItemBackpackVisuals visuals = pack.GetComponent<ItemBackpackVisuals>();
        if (visuals == null || visuals.backpackSlots == null)
            return;

        Zones.Add(new BackpackZone
        {
            Visuals = visuals,
            Reference = BackpackReference.GetFromBackpackItem(pack),
            Item = pack,
            SlotCount = SlotCount(visuals, pack.slotCount, pack.GetData<BackpackData>(DataEntryKey.BackpackData)),
            Reach = ReachPack,
            Name = pack.GetItemName()
        });
    }

    private static void AddWorn(Character character)
    {
        BackpackSlot slot = character.player.backpackSlot;
        if (slot == null || slot.backpackType == BackpackSlot.BackpackType.None
            || slot.backpackType == BackpackSlot.BackpackType.Rocketpack)
            return;

        CharacterItems items = character.refs.items;
        if (items != null && items.currentSelectedSlot.IsSome && items.currentSelectedSlot.Value == WornSlot)
            return;

        CharacterBackpackHandler handler = character.refs.backpackHandler;
        BackpackOnBackVisuals visuals = handler.activeBackpackVisuals;
        if (visuals == null || visuals.backpackSlots == null)
            return;

        BackpackReference reference = BackpackReference.GetFromEquippedBackpack(character);
        Zones.Add(new BackpackZone
        {
            Visuals = visuals,
            Reference = reference,
            Handler = handler,
            SlotCount = SlotCount(visuals, visuals.slotCount, reference.GetData()),
            Reach = ReachWorn,
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
        return LocalizedText.GetText("STASHITEM").Replace("#", "").Trim();
    }

    public string GetName()
    {
        return packName;
    }
}

[HarmonyPatch(typeof(Backpack), "Update")]
internal static class LeftBackpackMeshPatch
{
    [HarmonyPostfix]
    private static void Postfix(Backpack __instance)
    {
        if ((__instance != VRHandItems.LeftItem && __instance != VRHandItems.RightLoose)
            || __instance.backpackType == BackpackSlot.BackpackType.Rocketpack
            || __instance.groundMesh == null || __instance.heldMesh == null)
            return;

        __instance.groundMesh.SetActive(false);
        __instance.heldMesh.SetActive(true);
    }
}

[HarmonyPatch(typeof(CharacterItems), nameof(CharacterItems.OnPickupAccepted))]
internal static class BackpackStashPickupPatch
{
    [HarmonyPrefix]
    private static bool Prefix(CharacterItems __instance, byte slotID)
    {
        if (VRBackpackHands.TryCompleteStash(__instance, slotID))
            return false;

        return !VRHandItems.TryCompleteLeftTake(__instance, slotID);
    }
}

using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PeakVR;

internal static class VRItemUse
{
    private const float TouchMargin = 0.05f;

    private static readonly FieldInfo BestCharacterField = AccessTools.Field(typeof(Interaction), "bestCharacter");
    private static readonly Collider[] OverlapBuffer = new Collider[32];
    private static readonly List<Collider> ColliderBuffer = new List<Collider>();

    private static int characterMask = -1;
    private static Item leftUsing;
    private static bool isLeftSecondary;
    private static Item clearLater;
    private static bool isProgressLeft;
    private static readonly Dictionary<ushort, bool> SpikeCache = new Dictionary<ushort, bool>();
    private static Item spikeItem;
    private static float spikeTick;
    private static RaycastHit spikeHit;
    private static GameObject spikePreview;
    private static readonly Dictionary<ushort, bool> RopeCache = new Dictionary<ushort, bool>();
    private static readonly Dictionary<ushort, bool> HookCache = new Dictionary<ushort, bool>();
    private static readonly AccessTools.FieldRef<RescueHook, bool> HookPullingRef =
        AccessTools.FieldRefAccess<RescueHook, bool>("isPulling");
    private static readonly AccessTools.FieldRef<RescueHook, Character> HookTargetRef =
        AccessTools.FieldRefAccess<RescueHook, Character>("targetPlayer");
    private static readonly MethodInfo LetGoMethod = AccessTools.Method(typeof(RescueHook), "RPCA_LetGo");
    private static Item ropeItem;
    private static bool isRopePressed;

    public static CharacterInteractible RightFriend { get; private set; }
    public static CharacterInteractible LeftFriend { get; private set; }

    public static float LeftProgress
    {
        get
        {
            if (spikeItem != null && spikeItem.usingTimePrimary > 0f)
                return Mathf.Clamp01(spikeTick / spikeItem.usingTimePrimary);

            if (ropeItem != null && ropeItem.overrideForceProgress)
                return Mathf.Clamp01(ropeItem.overrideProgress);

            if (leftUsing == null || !leftUsing.shouldShowCastProgress)
                return 0f;

            return leftUsing.progress;
        }
    }

    public static bool IsProgressLeft(Character local)
    {
        if (LeftProgress > 0f)
        {
            isProgressLeft = true;
            return true;
        }

        Item right = local != null && local.data != null ? local.data.currentItem : null;
        if (right != null && right.shouldShowCastProgress && right.progress > 0f)
            isProgressLeft = false;

        return isProgressLeft;
    }

    public static bool IsLeftOwnItem(Character local)
    {
        Item left = VRHandItems.LeftItem;
        return left != null && local != null && local.data != null && left != local.data.currentItem;
    }

    public static void UpdateFriends(Interaction interaction, Character local)
    {
        Item right = local.data != null ? local.data.currentItem : null;
        Item left = VRHandItems.LeftItem;

        RightFriend = right != null && right.canUseOnFriend ? FindFriend(local, right) : null;

        if (left != null && left == right)
            LeftFriend = RightFriend;
        else
            LeftFriend = left != null && left.canUseOnFriend ? FindFriend(local, left) : null;

        if (BestCharacterField != null)
            BestCharacterField.SetValue(interaction, RightFriend);

        if (RightFriend != null)
            interaction.currentHovered = RightFriend;
    }

    public static void StepLeft(Character local, InputAction action, bool isAllowed)
    {
        StepClearLater(local);

        Item item = VRHandItems.LeftItem;
        RopeTier tier = item != null && IsLeftOwnItem(local) ? GetRope(item) : null;
        if (tier != null)
        {
            StopUse();
            StopSpike();
            if (ropeItem != null && ropeItem != item)
                StopRope();

            bool isRopeAllowed = isAllowed && CanInput(local);
            StepRope(local, item, tier, isRopeAllowed && action.IsPressed());
            return;
        }

        if ((leftUsing != null && leftUsing != item) || (spikeItem != null && spikeItem != item))
            Stop();

        if (item == null || !IsLeftOwnItem(local) || !isAllowed || !CanInput(local))
        {
            Stop();
            return;
        }

        ClimbingSpikeComponent spike = GetSpike(item);
        if (spike != null)
        {
            StopUse();
            StepSpike(local, item, spike, action);
            return;
        }

        StopSpike();

        if (!IsLeftUsable(item))
        {
            StopUse();
            return;
        }

        RefreshHook(local, item);

        bool isWasPressed = action.WasPressedThisFrame();
        bool isPressed = action.IsPressed();
        bool isWasReleased = action.WasReleasedThisFrame();

        if (leftUsing == null)
        {
            if (!isWasPressed)
                return;

            leftUsing = item;
            isLeftSecondary = LeftFriend != null;
            item.overrideHolderCharacter = local;
        }

        object saved = BestCharacterField != null ? BestCharacterField.GetValue(Interaction.instance) : null;
        if (BestCharacterField != null)
            BestCharacterField.SetValue(Interaction.instance, LeftFriend);

        Character savedHolder = item.trueHolderCharacter;
        item.holderCharacter = local;
        ItemAim.ActiveItem = item;

        if (isLeftSecondary)
        {
            if (isPressed && item.CanUseSecondary())
            {
                item.StartUseSecondary();
                item.ContinueUseSecondary();
            }
            if (isWasReleased || (item.isUsingSecondary && !item.CanUseSecondary()))
                item.CancelUseSecondary();
        }
        else
        {
            if (isWasPressed && item.CanUsePrimary())
                item.StartUsePrimary();
            if (isPressed && item.CanUsePrimary())
                item.ContinueUsePrimary();
            if (isWasReleased || (item.isUsingPrimary && !item.CanUsePrimary()))
                item.CancelUsePrimary();
        }

        ItemAim.ActiveItem = null;
        item.holderCharacter = savedHolder;

        if (BestCharacterField != null)
            BestCharacterField.SetValue(Interaction.instance, saved);

        SetUseAnimation(local, item, item.isUsingPrimary && item.castProgress < 1f);

        if (!isPressed)
            StopUse();
    }

    public static void Stop()
    {
        StopUse();
        StopSpike();
        StopRope();
    }

    public static void StepIdle(Character local)
    {
        Stop();
        StepClearLater(local);
    }

    public static bool IsSpike(Item item)
    {
        return GetSpike(item) != null;
    }

    public static bool IsLeftPlacement(Item item)
    {
        return IsSpike(item) || GetRope(item) != null;
    }

    private static RopeTier GetRope(Item item)
    {
        if (item == null)
            return null;

        if (!RopeCache.TryGetValue(item.itemID, out bool isRope))
        {
            isRope = item.GetComponent<RopeTier>() != null;
            RopeCache[item.itemID] = isRope;
        }

        return isRope ? item.GetComponent<RopeTier>() : null;
    }

    private static void StepRope(Character local, Item item, RopeTier tier, bool isPressed)
    {
        ropeItem = item;
        bool isWasReleased = isRopePressed && !isPressed;
        isRopePressed = isPressed;

        RopeSpool spool = item.GetComponent<RopeSpool>();
        CharacterInput input = local.input;
        ItemState savedState = item.itemState;
        Character savedHolder = item.trueHolderCharacter;
        bool savedPressed = input.usePrimaryIsPressed;
        bool savedReleased = input.usePrimaryWasReleased;

        item.itemState = ItemState.Held;
        item.holderCharacter = local;
        input.usePrimaryIsPressed = isPressed;
        input.usePrimaryWasReleased = isWasReleased;
        ItemAim.ActiveItem = item;

        if (spool != null)
            spool.Update();
        tier.Update();

        ItemAim.ActiveItem = null;
        input.usePrimaryIsPressed = savedPressed;
        input.usePrimaryWasReleased = savedReleased;
        item.holderCharacter = savedHolder;
        item.itemState = savedState;
    }

    private static void StopRope()
    {
        Item item = ropeItem;
        ropeItem = null;
        isRopePressed = false;
        ClearRopeHold(item);
    }

    public static void ClearRopeHold(Item item)
    {
        if (item == null || item.itemState == ItemState.Held || item.photonView == null || !item.photonView.IsMine)
            return;

        RopeSpool spool = item.GetComponent<RopeSpool>();
        if (spool != null && spool.ropeInstance != null)
        {
            PhotonNetwork.Destroy(spool.ropeInstance);
            spool.ropeInstance = null;
            spool.rope = null;
        }

        RopeTier tier = item.GetComponent<RopeTier>();
        if (tier == null)
            return;

        if (tier.ropeAnchor != null)
            Object.DestroyImmediate(tier.ropeAnchor.gameObject);
        tier.ropeAnchor = null;
        tier.releaseCheck = false;
        item.overrideForceProgress = false;
        item.overrideProgress = 0f;
    }

    private static ClimbingSpikeComponent GetSpike(Item item)
    {
        if (item == null)
            return null;

        if (!SpikeCache.TryGetValue(item.itemID, out bool isSpike))
        {
            isSpike = item.GetComponentInChildren<ClimbingSpikeComponent>(true) != null;
            SpikeCache[item.itemID] = isSpike;
        }

        return isSpike ? item.GetComponentInChildren<ClimbingSpikeComponent>(true) : null;
    }

    private static void StepSpike(Character local, Item item, ClimbingSpikeComponent spike, InputAction action)
    {
        bool isClimbing = local.data.isClimbingAnything || VRHandClimb.IsHoldingAny;

        if (spikeItem == null)
        {
            if (!action.WasPressedThisFrame())
                return;

            float startDistance = isClimbing ? spike.climbingSpikeStartDistance : spike.climbingSpikeStartDistanceGrounded;
            if (!ItemAim.TryGetAim(item, out Vector3 origin, out Vector3 direction)
                || !Physics.Raycast(origin, direction, out spikeHit, startDistance,
                    HelperFunctions.GetMask(HelperFunctions.LayerType.TerrainMap)))
            {
                Log($"left piton: no wall within {startDistance:F1}m");
                return;
            }

            spikeItem = item;
            spikeTick = 0f;
            if (spikePreview == null && spike.climbingSpikePreviewPrefab != null)
                spikePreview = Object.Instantiate(spike.climbingSpikePreviewPrefab);
            if (spikePreview != null)
                spikePreview.transform.SetPositionAndRotation(spikeHit.point,
                    Quaternion.LookRotation(-spikeHit.normal, Vector3.up));
        }

        if (!action.IsPressed())
        {
            StopSpike();
            return;
        }

        float previewDistance = isClimbing ? spike.climbingSpikePreviewDisableDistance
            : spike.climbingSpikePreviewDisableDistanceGrounded;
        if (!ItemAim.TryGetAim(item, out Vector3 aimOrigin, out _)
            || Vector3.Distance(aimOrigin, spikeHit.point) > previewDistance)
        {
            StopSpike();
            return;
        }

        spikeTick += Time.deltaTime;
        if (spikeTick < item.usingTimePrimary)
            return;

        RaycastHit hit = spikeHit;
        StopSpike();
        HammerSpike(local, item, spike, hit);
    }

    private static void HammerSpike(Character local, Item item, ClimbingSpikeComponent spike, RaycastHit hit)
    {
        if (spike.hammeredVersionPrefab == null)
            return;

        GameObject placed = PhotonNetwork.InstantiateItemRoom(spike.hammeredVersionPrefab.gameObject.name, hit.point,
            Quaternion.LookRotation(-hit.normal, Vector3.up), warnIfNotHost: false);
        if (placed == null)
            return;

        item.StartCoroutine(item.ConsumeDelayed(true));
        local.data.lastConsumedItem = Time.time;
        if (GameUtils.instance != null)
            GameUtils.instance.IncrementPermanentItemsPlaced();
    }

    private static void StopSpike()
    {
        spikeItem = null;
        spikeTick = 0f;
        if (spikePreview != null)
            Object.Destroy(spikePreview);
        spikePreview = null;
    }

    private static void StopUse()
    {
        Item item = leftUsing;
        leftUsing = null;
        if (item == null)
            return;

        Character local = Character.localCharacter;
        Character savedHolder = item.trueHolderCharacter;
        if (local != null)
            item.holderCharacter = local;
        ItemAim.ActiveItem = item;

        if (item.isUsingPrimary)
            item.CancelUsePrimary();
        if (item.isUsingSecondary)
            item.CancelUseSecondary();

        ItemAim.ActiveItem = null;
        item.holderCharacter = savedHolder;
        SetUseAnimation(local, item, false);

        if (IsHolderKept(item))
            clearLater = item;
        else
            item.overrideHolderCharacter = null;
    }

    private static void StepClearLater(Character local)
    {
        if (clearLater == null)
        {
            clearLater = null;
            return;
        }

        RescueHook hook = GetHook(clearLater);
        if (hook != null && HookPullingRef(hook) && !IsInHand(local, clearLater))
        {
            Log($"'{clearLater.name}' left the hand while pulling - letting go");
            LetGo(hook);
        }

        if (IsHolderKept(clearLater))
            return;

        clearLater.overrideHolderCharacter = null;
        clearLater = null;
    }

    private static bool IsHolderKept(Item item)
    {
        if (item.consuming && item.gameObject.activeInHierarchy)
            return true;

        RescueHook hook = GetHook(item);
        return hook != null && HookPullingRef(hook);
    }

    private static bool IsInHand(Character local, Item item)
    {
        return item == VRHandItems.LeftItem || (local != null && local.data != null && item == local.data.currentItem);
    }

    private static RescueHook GetHook(Item item)
    {
        if (item == null)
            return null;

        if (!HookCache.TryGetValue(item.itemID, out bool isHook))
        {
            isHook = item.GetComponent<RescueHook>() != null;
            HookCache[item.itemID] = isHook;
        }

        return isHook ? item.GetComponent<RescueHook>() : null;
    }

    private static void RefreshHook(Character local, Item item)
    {
        RescueHook hook = GetHook(item);
        if (hook == null || item.overrideHolderCharacter == local)
            return;

        Character savedHolder = item.trueHolderCharacter;
        item.holderCharacter = local;
        ItemAim.ActiveItem = item;
        hook.Update();
        ItemAim.ActiveItem = null;
        item.holderCharacter = savedHolder;
    }

    private static void LetGo(RescueHook hook)
    {
        if (HookTargetRef(hook) != null && hook.photonView != null)
            hook.photonView.RPC("RPCA_LetGo", RpcTarget.All);
        else if (LetGoMethod != null)
            LetGoMethod.Invoke(hook, null);
    }

    public static void ApplyUseAnimation(Character character)
    {
        if (LeftAnimation == null || character == null || character != Character.localCharacter
            || character.refs == null || character.refs.animator == null)
            return;

        character.refs.animator.SetBool(LeftAnimation, true);
    }

    public static string LeftAnimation { get; private set; }

    private static void SetUseAnimation(Character local, Item item, bool isOn)
    {
        string wanted = null;
        if (isOn && item != null && item.TryGetComponent(out ItemUseFeedback feedback)
            && !string.IsNullOrEmpty(feedback.useAnimation))
            wanted = feedback.useAnimation;

        if (local != null && local.refs != null && local.refs.animator != null)
        {
            if (LeftAnimation != null && LeftAnimation != wanted)
                local.refs.animator.SetBool(LeftAnimation, false);
            if (wanted != null)
                local.refs.animator.SetBool(wanted, true);
        }

        LeftAnimation = wanted;
    }

    private static void Log(string message)
    {
        if (Plugin.Config != null && Plugin.Config.EnableVerboseLogging.Value)
            Plugin.Log.LogInfo($"[PeakVR][ItemUse] {message}");
    }

    private static bool CanInput(Character local)
    {
        return local.data != null && !local.data.passedOut && !local.data.fullyPassedOut && local.CanDoInput();
    }

    private static bool IsLeftUsable(Item item)
    {
        VRGripProfile profile = VRGripProfiles.Get(item);
        return profile == null || !profile.IsDeployable;
    }

    private static CharacterInteractible FindFriend(Character local, Item item)
    {
        if (!TryGetBounds(item, out Bounds bounds))
            return null;

        if (characterMask == -1)
            characterMask = LayerMask.GetMask("Character");

        int count = Physics.OverlapBoxNonAlloc(bounds.center, bounds.extents + Vector3.one * TouchMargin,
            OverlapBuffer, Quaternion.identity, characterMask, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            Collider hit = OverlapBuffer[i];
            Character character = hit != null ? hit.GetComponentInParent<Character>() : null;
            if (character == null || character == local || character.data == null || character.data.dead)
                continue;

            if (character.refs != null && character.refs.interactible != null)
                return character.refs.interactible;
        }

        return null;
    }

    private static bool TryGetBounds(Item item, out Bounds bounds)
    {
        bounds = default;
        bool isFound = false;

        item.GetComponentsInChildren(false, ColliderBuffer);
        for (int i = 0; i < ColliderBuffer.Count; i++)
        {
            Collider col = ColliderBuffer[i];
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

        ColliderBuffer.Clear();
        return isFound;
    }
}

[HarmonyPatch(typeof(ItemAudioManager), "Update")]
internal static class LeftUseAnimationPatch
{
    private static readonly AccessTools.FieldRef<ItemAudioManager, Character> CharacterRef =
        AccessTools.FieldRefAccess<ItemAudioManager, Character>("character");

    [HarmonyPostfix]
    private static void Postfix(ItemAudioManager __instance)
    {
        if (VRItemUse.LeftAnimation == null)
            return;

        VRItemUse.ApplyUseAnimation(CharacterRef(__instance));
    }
}

[HarmonyPatch(typeof(Rope), nameof(Rope.IsActive))]
internal static class LeftRopeActivePatch
{
    [HarmonyPostfix]
    private static void Postfix(Rope __instance, ref bool __result)
    {
        if (__result || !Plugin.VrEnabled || __instance.itemSpool == null)
            return;

        if (__instance.itemSpool == VRHandItems.LeftItem)
            __result = true;
    }
}

[HarmonyPatch(typeof(UI_UseItemProgress), "UpdateFillAmount")]
internal static class LeftUseProgressPatch
{
    [HarmonyPostfix]
    private static void Postfix(UI_UseItemProgress __instance, ref bool __result)
    {
        if (__result || __instance.fill == null)
            return;

        float progress = VRItemUse.LeftProgress;
        if (progress <= 0f)
            return;

        __instance.fill.fillAmount = progress;
        __result = true;
    }
}

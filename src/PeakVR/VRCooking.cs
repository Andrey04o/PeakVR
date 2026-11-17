using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;
using Zorro.Core;

namespace PeakVR;

internal static class VRCooking
{
    private const float ZoneMargin = 0.15f;
    private const float ZoneRadiusMin = 0.35f;
    private const float ZoneBelow = 0.1f;
    private const float ZoneAbove = 0.6f;
    private const float AimRangeDefault = 2f;
    private const float AimNearMargin = 3f;
    private const float LineStartOffset = 0.1f;

    private class FireZone
    {
        public Vector3 Center;
        public float Radius;
        public float Bottom;
        public float Top;
    }

    private class CookHand
    {
        public string Name;
        public bool IsLeft;
        public Item Item;
        public Campfire Fire;
        public Campfire Cooking;
        public float Elapsed;
        public bool IsPressed;
        public bool IsAimed;
        public Vector3 AimPoint;
    }

    private static readonly Dictionary<int, FireZone> Zones = new Dictionary<int, FireZone>();
    private static readonly List<Collider> ColliderBuffer = new List<Collider>();
    private static readonly RaycastHit[] AimBuffer = new RaycastHit[32];
    private static readonly CookHand Left = new CookHand { Name = "left", IsLeft = true };
    private static readonly CookHand Right = new CookHand { Name = "right" };
    private static bool isRightPrompt;

    public static bool Enabled => Plugin.VrEnabled && VRHandItems.Enabled;

    public static float RightProgress => GetProgress(Right);

    public static float LeftProgress => GetProgress(Left);

    public static bool IsOverFire(bool isLeft)
    {
        CookHand hand = isLeft ? Left : Right;
        return hand.Fire != null && hand.Item != null;
    }

    public static bool TryGetLine(bool isRight, out Vector3 start, out Vector3 end)
    {
        start = default;
        end = default;
        CookHand hand = isRight ? Right : Left;
        if (!hand.IsAimed || hand.Fire == null || hand.Item == null)
            return false;

        bool isAimed = isRight ? VRAim.TryRight(out Vector3 origin, out Vector3 dir) : VRAim.TryLeft(out origin, out dir);
        if (!isAimed)
            return false;

        start = origin + dir * LineStartOffset;
        end = hand.AimPoint;
        return true;
    }

    public static void Step(Character local, bool isActive)
    {
        if (!Enabled || local == null || local.data == null)
        {
            Reset();
            return;
        }

        StepHand(Right, GetRightItem(local), VRControls.RightTrigger,
            isActive && !VREmoteWheel.RightTriggerConsumed && !VRChatButton.RightTriggerConsumed);
        StepHand(Left, GetLeftItem(local), VRControls.LeftTrigger,
            isActive && !VRControllerHud.LeftTriggerConsumed);
        SetRightPrompt(IsOverFire(false) && Right.Item == local.data.currentItem);
    }

    public static void Reset()
    {
        Stop(Left);
        Stop(Right);
        Left.Item = null;
        Left.Fire = null;
        Left.IsAimed = false;
        Right.Item = null;
        Right.Fire = null;
        Right.IsAimed = false;
        SetRightPrompt(false);
    }

    private static Item GetRightItem(Character local)
    {
        if (VRHandItems.IsSliding(false))
            return null;

        Item item = local.data.currentItem;
        if (item == null)
            item = VRHandItems.RightLoose;

        return item != null && VRHandItems.IsGripHolding(local, item) ? item : null;
    }

    private static Item GetLeftItem(Character local)
    {
        if (VRHandItems.IsSliding(true) || !VRItemUse.IsLeftOwnItem(local))
            return null;

        Item item = VRHandItems.LeftItem;
        return VRHandItems.IsGripHolding(local, item) ? item : null;
    }

    private static void StepHand(CookHand hand, Item item, InputAction action, bool isAllowed)
    {
        bool isCookable = item != null && IsCookable(item);
        Campfire fire = isCookable ? FindFire(item) : null;
        hand.IsAimed = false;
        if (isCookable && fire == null)
        {
            fire = FindAimedFire(hand, item);
            hand.IsAimed = fire != null;
        }

        if (hand.Cooking != null && (hand.Item != item || fire != hand.Cooking))
            Stop(hand);

        hand.Item = item;
        hand.Fire = fire;

        bool isPressed = action != null && action.IsPressed();
        bool isWasPressed = isPressed && !hand.IsPressed;
        hand.IsPressed = isPressed;

        if (hand.Cooking == null)
        {
            if (isWasPressed && isAllowed && fire != null)
                Begin(hand);
            return;
        }

        if (!isPressed || !isAllowed)
        {
            Stop(hand);
            return;
        }

        hand.Elapsed += Time.deltaTime;
        if (hand.Elapsed >= GetCookTime(hand.Cooking))
            Finish(hand);
    }

    private static bool IsCookable(Item item)
    {
        return item.cooking != null && item.cooking.canBeCooked && !item.consuming && item.photonView != null
            && item.photonView.IsMine;
    }

    private static float GetCookTime(Campfire fire)
    {
        Character local = Character.localCharacter;
        return local != null ? fire.GetInteractTime(local) : fire.cookTime;
    }

    private static float GetProgress(CookHand hand)
    {
        if (hand.Cooking == null || hand.Item == null)
            return 0f;

        float time = GetCookTime(hand.Cooking);
        return time > 0f ? Mathf.Clamp01(hand.Elapsed / time) : 1f;
    }

    private static void Begin(CookHand hand)
    {
        hand.Cooking = hand.Fire;
        hand.Elapsed = 0f;
        hand.Item.cooking.StartCookingVisuals();
        Plugin.Log.LogInfo($"[PeakVR][Cook] {hand.Name} starts cooking '{hand.Item.name}' {(hand.IsAimed ? "aimed at" : "over")} '{hand.Fire.name}'");
    }

    private static void Finish(CookHand hand)
    {
        Item item = hand.Item;
        hand.Cooking = null;
        hand.Elapsed = 0f;

        IntItemData cooked = item.GetData<IntItemData>(DataEntryKey.CookedAmount);
        if (cooked != null && cooked.Value == 0 && Singleton<AchievementManager>.Instance != null)
            Singleton<AchievementManager>.Instance.IncrementSteamStat(STEAMSTATTYPE.MealsCooked, 1);

        item.cooking.FinishCooking();
        if (item.holderCharacter == null && Character.localCharacter != null
            && Character.localCharacter.refs.items.cookSfx != null)
            Character.localCharacter.refs.items.cookSfx.Play(item.transform.position);

        Plugin.Log.LogInfo($"[PeakVR][Cook] {hand.Name} cooked '{item.name}' to {(cooked != null ? cooked.Value : -1)}");
    }

    private static void Stop(CookHand hand)
    {
        if (hand.Cooking == null)
            return;

        hand.Cooking = null;
        hand.Elapsed = 0f;
        if (hand.Item != null && hand.Item.cooking != null)
            hand.Item.cooking.CancelCookingVisuals();
    }

    private static void SetRightPrompt(bool isOn)
    {
        if (isRightPrompt == isOn)
            return;

        isRightPrompt = isOn;
        if (GUIManager.instance != null)
            GUIManager.instance.UpdateItemPrompts();
    }

    private static Campfire FindFire(Item item)
    {
        Vector3 point = item.Center();
        foreach (Campfire fire in Campfire.ALL_CAMPFIRES)
        {
            if (fire == null || !fire.Lit || fire.state == Campfire.FireState.Spent)
                continue;

            FireZone zone = GetZone(fire);
            if (point.y < zone.Bottom || point.y > zone.Top)
                continue;

            Vector3 offset = point - zone.Center;
            offset.y = 0f;
            if (offset.sqrMagnitude <= zone.Radius * zone.Radius)
                return fire;
        }

        return null;
    }

    private static Campfire FindAimedFire(CookHand hand, Item item)
    {
        float range = Interaction.instance != null ? Interaction.instance.distance : AimRangeDefault;
        if (!HasFireNear(item.Center(), range + AimNearMargin))
            return null;

        bool isAimed = hand.IsLeft ? VRAim.TryLeft(out Vector3 origin, out Vector3 dir) : VRAim.TryRight(out origin, out dir);
        Character local = Character.localCharacter;
        if (!isAimed || local == null)
            return null;

        int mask = (int)HelperFunctions.GetMask(HelperFunctions.LayerType.AllPhysical);
        int count = Physics.RaycastNonAlloc(origin, dir, AimBuffer, range, mask, QueryTriggerInteraction.Collide);

        Collider nearest = null;
        float nearestDistance = float.MaxValue;
        Vector3 nearestPoint = Vector3.zero;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = AimBuffer[i];
            Collider col = hit.collider;
            if (col == null || hit.distance >= nearestDistance)
                continue;

            if (local.refs.ragdoll.colliderList.Contains(col) || col.GetComponentInParent<Item>() != null)
                continue;

            if (col.isTrigger && col.GetComponentInParent<Campfire>() == null)
                continue;

            nearest = col;
            nearestDistance = hit.distance;
            nearestPoint = hit.point;
        }

        if (nearest == null)
            return null;

        Campfire fire = nearest.GetComponentInParent<Campfire>();
        if (fire == null || !fire.Lit || fire.state == Campfire.FireState.Spent)
            return null;

        hand.AimPoint = nearestPoint;
        return fire;
    }

    private static bool HasFireNear(Vector3 point, float range)
    {
        float sqrRange = range * range;
        foreach (Campfire fire in Campfire.ALL_CAMPFIRES)
        {
            if (fire != null && fire.Lit && (fire.transform.position - point).sqrMagnitude <= sqrRange)
                return true;
        }

        return false;
    }

    private static FireZone GetZone(Campfire fire)
    {
        int id = fire.GetInstanceID();
        if (Zones.TryGetValue(id, out FireZone zone))
            return zone;

        Vector3 flame = fire.fireParticles != null ? fire.fireParticles.transform.position : fire.transform.position;
        Bounds bounds = new Bounds(flame, Vector3.zero);
        bool isFound = false;

        fire.GetComponentsInChildren(false, ColliderBuffer);
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

        zone = new FireZone
        {
            Center = new Vector3(bounds.center.x, flame.y, bounds.center.z),
            Radius = Mathf.Max(ZoneRadiusMin, Mathf.Max(bounds.extents.x, bounds.extents.z) + ZoneMargin),
            Bottom = Mathf.Min(bounds.min.y, flame.y) - ZoneBelow,
            Top = Mathf.Max(bounds.max.y, flame.y) + ZoneAbove
        };
        Zones[id] = zone;

        Plugin.Log.LogInfo($"[PeakVR][Cook] zone of '{fire.name}': radius {zone.Radius:F2} height {zone.Bottom:F2}..{zone.Top:F2} flame {flame.y:F2} colliders {isFound}");
        return zone;
    }
}

[HarmonyPatch(typeof(GUIManager), nameof(GUIManager.UpdateItemPrompts))]
internal static class CookPromptPatch
{
    [HarmonyPostfix]
    private static void Postfix(GUIManager __instance)
    {
        if (!VRCooking.IsOverFire(false) || __instance.itemPromptMain == null)
            return;

        __instance.itemPromptMain.text = LocalizedText.GetText("COOK");
        if (!__instance.itemPromptMain.gameObject.activeSelf)
            __instance.itemPromptMain.gameObject.SetActive(true);
    }
}

[HarmonyPatch(typeof(UI_UseItemProgress), "UpdateFillAmount")]
internal static class CookProgressPatch
{
    [HarmonyPostfix]
    private static void Postfix(UI_UseItemProgress __instance, ref bool __result)
    {
        float progress = VRCooking.RightProgress;
        if (progress <= 0f || __instance.fill == null)
            return;

        __instance.fill.fillAmount = progress;
        __result = true;
    }
}

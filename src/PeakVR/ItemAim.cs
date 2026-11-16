using System.Collections.Generic;
using UnityEngine;

namespace PeakVR;

internal static class ItemAim
{
    public static bool Enabled = true;

    public static Item ActiveItem;

    private static readonly Dictionary<int, Item> OwnerCache = new Dictionary<int, Item>();
    private static readonly Dictionary<int, Transform> OriginCache = new Dictionary<int, Transform>();

    private static Transform proxy;

    public static Transform Get()
    {
        return Get(null);
    }

    public static Transform Get(object self)
    {
        Transform fallback = MainCamera.instance != null ? MainCamera.instance.transform : null;

        if (!Enabled || !Plugin.VrEnabled)
            return fallback;

        Item item = ResolveItem(self);
        if (!TryGetAim(item, out Vector3 origin, out Vector3 direction))
            return fallback;

        if (proxy == null)
        {
            proxy = new GameObject("PeakVR ItemAim").transform;
            Object.DontDestroyOnLoad(proxy.gameObject);
        }

        proxy.SetPositionAndRotation(origin, Quaternion.LookRotation(direction));
        return proxy;
    }

    public static Ray GetMiddleScreenRay()
    {
        return GetMiddleScreenRay(null);
    }

    public static Ray GetMiddleScreenRay(object self)
    {
        Transform t = Get(self);
        if (t == null)
            return new Ray(Vector3.zero, Vector3.forward);
        return new Ray(t.position, t.forward);
    }

    public static bool IsLocalHeld(Item item, out bool isLeft)
    {
        isLeft = false;
        Character character = Character.localCharacter;
        if (item == null || character == null || character.data == null)
            return false;

        if (item == character.data.currentItem)
            return true;

        isLeft = true;
        return item == VRHandItems.LeftItem;
    }

    public static bool TryGetAim(Item item, out Vector3 origin, out Vector3 direction)
    {
        origin = Vector3.zero;
        direction = Vector3.forward;

        if (!IsLocalHeld(item, out bool isLeft))
            return false;

        VRGripProfile profile = VRGripProfiles.Get(item);
        bool isHandAim = profile != null && profile.IsDeployable;
        bool hasItemAim = !isHandAim && VRGripProfiles.TryGetAim(item, out origin, out direction);
        if (!hasItemAim)
        {
            bool hasHand = isLeft ? VRAim.TryLeft(out origin, out direction) : VRAim.TryRight(out origin, out direction);
            if (!hasHand)
                return false;
        }

        Transform barrel = GetBarrel(item);
        if (barrel != null)
            origin = barrel.position;

        return true;
    }

    private static Item ResolveItem(object self)
    {
        if (ActiveItem != null)
            return ActiveItem;

        if (self is Component component && !(self is CharacterItems))
        {
            int id = component.GetInstanceID();
            if (!OwnerCache.TryGetValue(id, out Item owner) || owner == null)
            {
                owner = component.GetComponentInParent<Item>();
                OwnerCache[id] = owner;
            }

            if (owner != null)
                return owner;
        }

        Character character = Character.localCharacter;
        return character != null && character.data != null ? character.data.currentItem : null;
    }

    private static Transform GetBarrel(Item item)
    {
        int id = item.GetInstanceID();
        if (OriginCache.TryGetValue(id, out Transform barrel))
            return barrel;

        Action_RaycastDart dart = item.GetComponentInChildren<Action_RaycastDart>(true);
        Peak.Action_RaycastSpawnSomething spawner = item.GetComponentInChildren<Peak.Action_RaycastSpawnSomething>(true);

        RopeShooter rope = item.GetComponentInChildren<RopeShooter>(true);
        RescueHook hook = item.GetComponentInChildren<RescueHook>(true);

        if (spawner != null && spawner.rayOrigin != null)
            barrel = spawner.rayOrigin;
        else if (dart != null && dart.spawnTransform != null)
            barrel = dart.spawnTransform;
        else if (rope != null && rope.spawnPoint != null)
            barrel = rope.spawnPoint;
        else if (hook != null && hook.firePoint != null)
            barrel = hook.firePoint;

        OriginCache[id] = barrel;
        return barrel;
    }
}

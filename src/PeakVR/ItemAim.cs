using UnityEngine;

namespace PeakVR;

internal static class ItemAim
{
    public static bool Enabled = true;

    private static Transform proxy;

    public static Transform Get()
    {
        var fallback = MainCamera.instance != null ? MainCamera.instance.transform : null;

        if (!Enabled)
            return fallback;

        var character = Character.localCharacter;
        if (character == null || character.data.currentItem == null)
            return fallback;

        // Blowgun
        var dart = character.data.currentItem.GetComponentInChildren<Action_RaycastDart>();
        if (dart != null && dart.spawnTransform != null)
            return dart.spawnTransform;

        if (!VRAim.TryRight(out var origin, out var dir))
            return fallback;

        if (proxy == null)
        {
            proxy = new GameObject("PeakVR ItemAim").transform;
            Object.DontDestroyOnLoad(proxy.gameObject);
        }

        proxy.SetPositionAndRotation(origin, Quaternion.LookRotation(dir));
        return proxy;
    }

    public static Ray GetMiddleScreenRay()
    {
        var t = Get();
        if (t == null)
            return new Ray(Vector3.zero, Vector3.forward);
        return new Ray(t.position, t.forward);
    }
}

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace PeakVR;

internal static class VRCanvasOwner
{
    private static readonly Dictionary<int, bool> GameCache = new();
    private static HashSet<Assembly> pluginAssemblies;

    public static bool IsOurs(Canvas c)
        => c != null && c.transform.root.name.StartsWith("PeakVR", StringComparison.Ordinal);

    public static bool IsGame(Canvas c)
    {
        if (c == null)
            return false;

        var id = c.GetInstanceID();
        if (GameCache.TryGetValue(id, out var cached) && cached)
            return true;

        var result = false;
        string owner = null;

        foreach (var behaviour in c.transform.root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour == null)
                continue;

            var name = behaviour.GetType().Assembly.GetName().Name;
            if (!name.StartsWith("Assembly-CSharp", StringComparison.OrdinalIgnoreCase)
                && !name.StartsWith("Zorro", StringComparison.OrdinalIgnoreCase)
                && name.IndexOf("PEAKLib", StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            owner = name;
            result = true;
            break;
        }

        if (result && !GameCache.ContainsKey(id))
            Plugin.Log.LogInfo($"[PeakVR] Canvas '{c.transform.root.name}' is game UI (owner: {owner})");

        GameCache[id] = result;
        return result;
    }

    // IsGame scans from transform.root, which is right for Zorro modals but wrong for a mod that
    // parents its own canvas under the game's HUD object (PeakTextChat's TextChatCanvas sits under
    // GUIManager). Scan the canvas's OWN subtree instead: a plugin component with no game component
    // anywhere under it means the canvas is entirely mod-built, whatever its root happens to be.
    public static string ModSubtreeOwner(Canvas c)
    {
        if (c == null)
            return null;

        var plugins = PluginAssemblies();
        if (plugins.Count == 0)
            return null;

        string owner = null;

        foreach (var behaviour in c.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour == null)
                continue;

            var assembly = behaviour.GetType().Assembly;
            var name = assembly.GetName().Name;

            if (name.StartsWith("Assembly-CSharp", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("Zorro", StringComparison.OrdinalIgnoreCase)
                || name.IndexOf("PEAKLib", StringComparison.OrdinalIgnoreCase) >= 0)
                return null;

            if (owner == null && plugins.Contains(assembly))
                owner = name;
        }

        return owner;
    }

    public static string ModOwner(Canvas c)
    {
        if (c == null)
            return null;

        var plugins = PluginAssemblies();
        if (plugins.Count == 0)
            return null;

        foreach (var behaviour in c.transform.root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour == null)
                continue;

            var assembly = behaviour.GetType().Assembly;
            if (plugins.Contains(assembly))
                return assembly.GetName().Name;
        }

        return null;
    }

    private static HashSet<Assembly> PluginAssemblies()
    {
        if (pluginAssemblies != null)
            return pluginAssemblies;

        pluginAssemblies = new HashSet<Assembly>();
        var ours = typeof(VRCanvasOwner).Assembly;

        try
        {
            foreach (var info in BepInEx.Bootstrap.Chainloader.PluginInfos.Values)
            {
                if (info == null || info.Instance == null)
                    continue;

                var assembly = info.Instance.GetType().Assembly;
                if (assembly != ours)
                    pluginAssemblies.Add(assembly);
            }

            Plugin.Log.LogInfo($"[PeakVR] Tracking {pluginAssemblies.Count} other plugin assemblies for canvas ownership");
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"[PeakVR] Could not enumerate plugin assemblies: {e.Message}");
        }

        return pluginAssemblies;
    }
}

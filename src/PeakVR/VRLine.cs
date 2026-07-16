using UnityEngine;

namespace PeakVR;

public enum LineVisibility
{
    Always,
    OnInteract,
    Hidden
}

internal static class VRLine
{
    private static readonly Color Fallback = new(0.3f, 1f, 0.4f);

    // The local character's customization color (set via the passport). Used to tint the
    // interaction line when locked onto something.
    public static Color CharacterColor()
    {
        var c = Character.localCharacter;
        if (c != null && c.refs != null && c.refs.customization != null)
        {
            try
            {
                return c.refs.customization.PlayerColor;
            }
            catch
            {
                // customization not ready yet
            }
        }
        return Fallback;
    }

    public static bool ShouldShow(LineVisibility mode, bool interacting)
    {
        return mode switch
        {
            LineVisibility.Always => true,
            LineVisibility.OnInteract => interacting,
            _ => false
        };
    }
}

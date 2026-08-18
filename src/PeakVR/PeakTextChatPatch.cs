using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PeakVR;

// Soft dependency on PeakTextChat (https://github.com/borealityy/PeakTextChat). Two things it does
// are wrong for VR: it stretches the stamina bar group 1000 units upward to reserve room for the
// chat, and it re-positions the chat box from that group every frame - and the group lives on the
// left wrist here. Both are undone only while VR is on; in flat the mod behaves normally.
internal static class PeakTextChatPatch
{
    private const string DisplayType = "PeakTextChat.TextChatDisplay";

    // Set once VRModHandUI has moved the chat box onto the left wrist, so the mod stops moving it.
    public static bool Claimed;

    public static bool Available => display != null;

    private static System.Type display;
    private static bool resolved;

    // NOT callable from Plugin.Awake: BepInEx loads PeakVR long before PeakTextChat, so the type does
    // not exist yet and TypeByName logs "Could not find type". Called once per level instead.
    public static void Resolve()
    {
        if (resolved || !Plugin.VrEnabled)
            return;

        resolved = true;

        display = AccessTools.TypeByName(DisplayType);
        if (display == null)
            return;

        var target = AccessTools.Method(display, "UpdatePosition");
        if (target == null)
        {
            Plugin.Log.LogWarning("[PeakVR] PeakTextChat found but UpdatePosition is gone; chat may drift on the wrist");
            return;
        }

        new Harmony(Plugin.Id + ".peaktextchat").Patch(target,
            prefix: new HarmonyMethod(AccessTools.Method(typeof(PeakTextChatPatch), nameof(SkipPosition))));
        Plugin.Log.LogInfo("[PeakVR] PeakTextChat detected - its chat box will be placed by PeakVR");
    }

    private static bool SkipPosition() => !Claimed;

    // The mod opens chat on a keyboard key. Reach the same state from the wrist button instead:
    // select its input field and activate it. Everything is reflected because the field, the flag
    // and TMP_InputField itself are all private / version-sensitive.
    public static void OpenChat()
    {
        if (display == null)
            return;

        var instance = AccessTools.Field(display, "instance")?.GetValue(null);
        if (instance == null)
        {
            Plugin.Log.LogWarning("[PeakVR] Chat button pressed but PeakTextChat has no active display");
            return;
        }

        if (AccessTools.Field(display, "usingIMGUI")?.GetValue(instance) is true)
        {
            AccessTools.Field(display, "imguiTyping")?.SetValue(instance, true);
            AccessTools.Field(display, "isBlockingInput")?.SetValue(instance, true);
            Plugin.Log.LogInfo("[PeakVR] Chat opened (PeakTextChat IMGUI mode - the field may need a click to focus)");
            return;
        }

        if (AccessTools.Field(display, "inputField")?.GetValue(instance) is not Component field)
        {
            Plugin.Log.LogWarning("[PeakVR] Chat button pressed but PeakTextChat has no input field");
            return;
        }

        if (EventSystem.current == null)
            new GameObject("PeakVR EventSystem").AddComponent<EventSystem>();

        EventSystem.current.SetSelectedGameObject(field.gameObject, null);
        AccessTools.Method(field.GetType(), "ActivateInputField")?.Invoke(field, null);
        AccessTools.Field(display, "isBlockingInput")?.SetValue(instance, true);
    }
}

// PeakTextChat's own StaminaBar.Start postfix does `parent.offsetMax.y = 1000f` to reserve space
// above the bar. On the wrist canvas that just pushes our stamina bar down. Capture the real value
// before any postfix runs and put it back after them - only the y, so nothing else about the rect
// (which VRControllerHud has already re-anchored) is disturbed.
[HarmonyPatch(typeof(StaminaBar), "Start")]
internal static class StaminaBarRectPatch
{
    private static RectTransform group;
    private static float originalTop;
    private static bool captured;

    [HarmonyPrefix]
    private static void Prefix(StaminaBar __instance)
    {
        captured = false;

        if (!Plugin.VrEnabled || __instance.transform.parent is not RectTransform rt)
            return;

        group = rt;
        originalTop = rt.offsetMax.y;
        captured = true;
    }

    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    private static void Postfix() => Restore();

    public static void Restore()
    {
        if (!captured || group == null)
            return;

        var offset = group.offsetMax;
        if (Mathf.Approximately(offset.y, originalTop))
            return;

        group.offsetMax = new Vector2(offset.x, originalTop);
    }
}

using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PeakVR;

internal static class PeakTextChatPatch
{
    private const string DisplayType = "PeakTextChat.TextChatDisplay";

    public static bool Claimed;

    public static bool Available => display != null;

    private static System.Type display;
    private static bool resolved;

    public static void Resolve()
    {
        if (resolved)
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

        VRKeyboard.AllowOpen();

        EventSystem.current.SetSelectedGameObject(field.gameObject, null);
        AccessTools.Method(field.GetType(), "ActivateInputField")?.Invoke(field, null);
        AccessTools.Field(display, "isBlockingInput")?.SetValue(instance, true);
    }

    public static void KeepBlocking() => SetBlocking(true);

    public static void StopBlocking() => SetBlocking(false);

    private static void SetBlocking(bool blocking)
    {
        if (display == null)
            return;

        var instance = AccessTools.Field(display, "instance")?.GetValue(null);
        if (instance == null)
            return;

        AccessTools.Field(display, "isBlockingInput")?.SetValue(instance, blocking);

        if (!blocking)
            AccessTools.Field(display, "imguiTyping")?.SetValue(instance, false);
    }
}

[HarmonyPatch(typeof(StaminaBar), "Start")]
internal static class StaminaBarRectPatch
{
    private static RectTransform group;
    private static float originalTop;
    private static float moddedTop;
    private static bool captured;

    [HarmonyPrefix]
    private static void Prefix(StaminaBar __instance)
    {
        captured = false;

        if (__instance.transform.parent is not RectTransform rt)
            return;

        group = rt;
        originalTop = rt.offsetMax.y;
        moddedTop = originalTop;
        captured = true;
    }

    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    private static void Postfix()
    {
        if (captured && group != null)
            moddedTop = group.offsetMax.y;

        Apply();
    }

    public static void Apply()
    {
        if (!captured || group == null)
            return;

        var target = Plugin.VrEnabled ? originalTop : moddedTop;
        var offset = group.offsetMax;
        if (Mathf.Approximately(offset.y, target))
            return;

        group.offsetMax = new Vector2(offset.x, target);
    }
}

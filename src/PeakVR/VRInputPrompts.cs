using System.Collections.Generic;
using HarmonyLib;

namespace PeakVR;

// Zorro's InputScheme is a compiled enum (KeyboardMouse / Gamepad / Unknown), so VR can't be
// registered as a real control scheme — anything else maps to Unknown, which makes the game treat us
// as a gamepad. We force Keyboard&Mouse instead (InGameCameraPatch) and rewrite the prompts here:
// both InputIcon and InLineInputPrompts build their text from InputSpriteData.GetSpriteTag, so one
// patch covers the HUD icons and the inline "[INTERACT] to ..." sentences.
//
// Only actions PeakVR actually binds are overridden; anything else keeps the game's keyboard sprite
// so we never show a button that does nothing.
[HarmonyPatch(typeof(InputSpriteData), nameof(InputSpriteData.GetSpriteTag))]
internal static class VRInputPrompts
{
    // The Kenney glyphs are drawn small relative to the surrounding text (most visibly in the
    // guidebook), so scale them up. Percent keeps them proportional wherever they appear.
    private const int GlyphSizePercent = 170;

    // Kenney Meta Quest font code points, per kenney_input_meta_quest_map.txt.
    private const int ButtonA = 0xE008;
    private const int ButtonB = 0xE00A;
    private const int ButtonX = 0xE012;
    private const int ButtonY = 0xE014;
    private const int GripLeft = 0xE016;
    private const int GripRight = 0xE018;
    private const int TriggerLeft = 0xE02E;
    private const int TriggerRight = 0xE030;
    private const int StickLeft = 0xE01A;
    private const int StickLeftPress = 0xE01E;
    private const int StickLeftUp = 0xE020;
    private const int StickLeftDown = 0xE01B;
    private const int StickLeftLeft = 0xE01D;
    private const int StickLeftRight = 0xE01F;
    private const int StickRightPress = 0xE026;
    private const int StickRightUp = 0xE028;
    private const int StickRightDown = 0xE023;
    private const int StickRightVertical = 0xE029;
    private const int StickRightHorizontal = 0xE024;
    private const int ControllerLeft = 0xE000;
    private const int ControllerRight = 0xE002;

    private static readonly Dictionary<InputSpriteData.InputAction, int> Glyphs = new()
    {
        { InputSpriteData.InputAction.Interact, GripRight },
        { InputSpriteData.InputAction.HoldInteract, GripRight },
        { InputSpriteData.InputAction.UsePrimary, TriggerRight },
        { InputSpriteData.InputAction.UseSecondary, TriggerLeft },
        { InputSpriteData.InputAction.Drop, GripLeft },
        { InputSpriteData.InputAction.Throw, GripLeft },

        { InputSpriteData.InputAction.Jump, ButtonA },
        { InputSpriteData.InputAction.Crouch, ButtonX },
        { InputSpriteData.InputAction.DeselectSlot, ButtonB },
        { InputSpriteData.InputAction.Pause, ButtonY },

        { InputSpriteData.InputAction.Sprint, StickLeftPress },
        { InputSpriteData.InputAction.Ping, StickRightPress },

        { InputSpriteData.InputAction.Move, StickLeft },
        { InputSpriteData.InputAction.MoveForward, StickLeftUp },
        { InputSpriteData.InputAction.MoveBackward, StickLeftDown },
        { InputSpriteData.InputAction.MoveLeft, StickLeftLeft },
        { InputSpriteData.InputAction.MoveRight, StickLeftRight },

        { InputSpriteData.InputAction.Scroll, StickRightVertical },
        { InputSpriteData.InputAction.ScrollForward, StickRightUp },
        { InputSpriteData.InputAction.ScrollBackward, StickRightDown },

        // No button for these — you point a controller at the wrist HUD.
        { InputSpriteData.InputAction.Slot1, ControllerLeft },
        { InputSpriteData.InputAction.Slot2, ControllerLeft },
        { InputSpriteData.InputAction.Slot3, ControllerLeft },
        { InputSpriteData.InputAction.Slot4, ControllerLeft },
        { InputSpriteData.InputAction.SlotLeft, ControllerLeft },
        { InputSpriteData.InputAction.SlotRight, ControllerLeft },
        { InputSpriteData.InputAction.Emote, ControllerRight },

        // Looking around is the right stick (snap/smooth turn), not a button.
        { InputSpriteData.InputAction.Aim, StickRightHorizontal },

        // While passed out X / A cycle the spectated player (see InteractionInputPatch).
        { InputSpriteData.InputAction.SpectateLeft, ButtonX },
        { InputSpriteData.InputAction.SpectateRight, ButtonA },
    };

    // Used when the Quest font isn't in the bundle yet, so prompts stay readable instead of showing
    // unrenderable Private-Use-Area boxes.
    private static readonly Dictionary<InputSpriteData.InputAction, string> Labels = new()
    {
        { InputSpriteData.InputAction.Interact, "R Grip" },
        { InputSpriteData.InputAction.HoldInteract, "R Grip" },
        { InputSpriteData.InputAction.UsePrimary, "R Trigger" },
        { InputSpriteData.InputAction.UseSecondary, "L Trigger" },
        { InputSpriteData.InputAction.Drop, "L Grip" },
        { InputSpriteData.InputAction.Throw, "L Grip" },

        { InputSpriteData.InputAction.Jump, "A" },
        { InputSpriteData.InputAction.Crouch, "X" },
        { InputSpriteData.InputAction.DeselectSlot, "B" },
        { InputSpriteData.InputAction.Pause, "Y" },

        { InputSpriteData.InputAction.Sprint, "L Stick" },
        { InputSpriteData.InputAction.Ping, "R Stick" },

        { InputSpriteData.InputAction.Move, "L Stick" },
        { InputSpriteData.InputAction.MoveForward, "L Stick" },
        { InputSpriteData.InputAction.MoveBackward, "L Stick" },
        { InputSpriteData.InputAction.MoveLeft, "L Stick" },
        { InputSpriteData.InputAction.MoveRight, "L Stick" },

        { InputSpriteData.InputAction.Scroll, "R Stick" },
        { InputSpriteData.InputAction.ScrollForward, "R Stick Up" },
        { InputSpriteData.InputAction.ScrollBackward, "R Stick Down" },

        { InputSpriteData.InputAction.Slot1, "Wrist" },
        { InputSpriteData.InputAction.Slot2, "Wrist" },
        { InputSpriteData.InputAction.Slot3, "Wrist" },
        { InputSpriteData.InputAction.Slot4, "Wrist" },
        { InputSpriteData.InputAction.SlotLeft, "Wrist" },
        { InputSpriteData.InputAction.SlotRight, "Wrist" },
        { InputSpriteData.InputAction.Emote, "Wrist" },

        { InputSpriteData.InputAction.Aim, "R Stick" },

        { InputSpriteData.InputAction.SpectateLeft, "X" },
        { InputSpriteData.InputAction.SpectateRight, "A" },
    };

    [HarmonyPostfix]
    private static void Postfix(InputSpriteData.InputAction action, ref string __result)
    {
        if (!Plugin.VrEnabled)
            return;

        if (PeakAssets.QuestFont != null)
        {
            if (Glyphs.TryGetValue(action, out int code))
                __result = $"<font=\"{PeakAssets.QuestFontName}\"><size={GlyphSizePercent}%>{(char)code}</size></font>";
            return;
        }

        if (Labels.TryGetValue(action, out string label))
            __result = label;
    }
}

using System.Collections.Generic;

namespace PeakVR;

internal enum VRKeyAction
{
    Char,
    Backspace,
    Enter,
    Space,
    Shift,
    CapsLock,
    Tab,
    Escape,
    Left,
    Right,
    Clear,
    Close,
    Language,
    Blank
}

internal sealed class VRKeyboardLayoutDef
{
    public readonly string Name;
    public readonly string Display;
    public readonly VRKeyDef[][] Rows;

    public VRKeyboardLayoutDef(string name, string display, VRKeyDef[][] rows)
    {
        Name = name;
        Display = display;
        Rows = rows;
    }
}

internal readonly struct VRKeyDef
{
    public readonly string Label;
    public readonly char Character;
    public readonly char Shifted;
    public readonly VRKeyAction Action;
    public readonly float Width;

    public VRKeyDef(char character)
    {
        Label = character.ToString();
        Character = character;
        Shifted = char.ToUpperInvariant(character);
        Action = VRKeyAction.Char;
        Width = 1f;
    }

    public VRKeyDef(char character, char shifted)
    {
        Label = character.ToString();
        Character = character;
        Shifted = shifted;
        Action = VRKeyAction.Char;
        Width = 1f;
    }

    public VRKeyDef(string label, VRKeyAction action, float width = 1f)
    {
        Label = label;
        Character = '\0';
        Shifted = '\0';
        Action = action;
        Width = width;
    }

    public bool IsLetter => Action == VRKeyAction.Char && char.IsLetter(Character);

    public char Resolve(bool shift, bool caps)
    {
        if (Action != VRKeyAction.Char)
            return '\0';

        if (IsLetter)
            return shift ^ caps ? Shifted : Character;

        return shift ? Shifted : Character;
    }

    public string ResolveLabel(bool shift, bool caps)
    {
        if (Action != VRKeyAction.Char)
            return Label;

        return Resolve(shift, caps).ToString();
    }
}

internal static class VRKeyboardLayout
{
    private const float RowUnits = 15.2f;

    public static readonly VRKeyboardLayoutDef Us = Make("US", "English (US)",
        "`1234567890-=", "~!@#$%^&*()_+",
        "qwertyuiop[]\\", "QWERTYUIOP{}|",
        "asdfghjkl;'", "ASDFGHJKL:\"",
        "zxcvbnm,./", "ZXCVBNM<>?");

    public static readonly VRKeyboardLayoutDef Uk = Make("UK", "English (UK)",
        "`1234567890-=", "¬!\"£$%^&*()_+",
        "qwertyuiop[]#", "QWERTYUIOP{}~",
        "asdfghjkl;'", "ASDFGHJKL:@",
        "\\zxcvbnm,./", "|ZXCVBNM<>?");

    public static readonly VRKeyboardLayoutDef De = Make("DE", "German (QWERTZ)",
        "^1234567890ß´", "°!\"§$%&/()=?`",
        "qwertzuiopü+#", "QWERTZUIOPÜ*'",
        "asdfghjklöä", "ASDFGHJKLÖÄ",
        "<yxcvbnm,.-", ">YXCVBNM;:_");

    public static readonly VRKeyboardLayoutDef Fr = Make("FR", "French (AZERTY)",
        "²&é\"'(-è_çà)=", "~1234567890°+",
        "azertyuiop^$*", "AZERTYUIOP¨£µ",
        "qsdfghjklmù", "QSDFGHJKLM%",
        "<wxcvbn,;:!", ">WXCVBN?./§");

    public static readonly VRKeyboardLayoutDef Es = Make("ES", "Spanish",
        "º1234567890'¡", "ª!\"·$%&/()=?¿",
        "qwertyuiop`+ç", "QWERTYUIOP^*Ç",
        "asdfghjklñ´", "ASDFGHJKLÑ¨",
        "<zxcvbnm,.-", ">ZXCVBNM;:_");

    public static readonly VRKeyboardLayoutDef Ru = Make("RU", "Russian",
        "ё1234567890-=", "Ё!\"№;%:?*()_+",
        "йцукенгшщзхъ\\", "ЙЦУКЕНГШЩЗХЪ/",
        "фывапролджэ", "ФЫВАПРОЛДЖЭ",
        "ячсмитьбю.", "ЯЧСМИТЬБЮ,");

    public static readonly VRKeyboardLayoutDef Ua = Make("UA", "Ukrainian",
        "'1234567890-=", "₴!\"№;%:?*()_+",
        "йцукенгшщзхїґ", "ЙЦУКЕНГШЩЗХЇҐ",
        "фівапролджє", "ФІВАПРОЛДЖЄ",
        "ячсмитьбю.", "ЯЧСМИТЬБЮ,");

    public static readonly VRKeyboardLayoutDef[] All = { Us, Uk, De, Fr, Es, Ru, Ua };

    public static VRKeyDef[][] Numbers => new[]
    {
        new[] { new VRKeyDef('7'), new VRKeyDef('8'), new VRKeyDef('9'), new VRKeyDef("Backspace", VRKeyAction.Backspace, 1.6f) },
        new[] { new VRKeyDef('4'), new VRKeyDef('5'), new VRKeyDef('6'), new VRKeyDef("Clear", VRKeyAction.Clear, 1.6f) },
        new[] { new VRKeyDef('1'), new VRKeyDef('2'), new VRKeyDef('3'), new VRKeyDef("Close", VRKeyAction.Close, 1.6f) },
        new[] { new VRKeyDef('-'), new VRKeyDef('0'), new VRKeyDef('.'), new VRKeyDef("Enter", VRKeyAction.Enter, 1.6f) }
    };

    private static VRKeyboardLayoutDef Make(string name, string display,
        string digits, string digitsShift,
        string top, string topShift,
        string home, string homeShift,
        string bottom, string bottomShift)
    {
        const float leftShift = 2.6f;

        var rows = new[]
        {
            Compose(Pairs(name, digits, digitsShift), null, new VRKeyDef("Backspace", VRKeyAction.Backspace, 2.2f)),
            Compose(Pairs(name, top, topShift), new VRKeyDef("Tab", VRKeyAction.Tab, 1.6f), null),
            Compose(Pairs(name, home, homeShift),
                new VRKeyDef("Caps", VRKeyAction.CapsLock, 1.9f),
                new VRKeyDef("Enter", VRKeyAction.Enter, Remaining(1.9f, home.Length))),
            Compose(Pairs(name, bottom, bottomShift),
                new VRKeyDef("Shift", VRKeyAction.Shift, leftShift),
                new VRKeyDef("Shift", VRKeyAction.Shift, Remaining(leftShift, bottom.Length))),
            BottomRow()
        };

        return new VRKeyboardLayoutDef(name, display, rows);
    }

    private static float Remaining(float used, int keys)
    {
        var left = RowUnits - used - keys;
        return left < 1.4f ? 1.4f : left;
    }

    private static List<VRKeyDef> Pairs(string name, string basic, string shifted)
    {
        var keys = new List<VRKeyDef>();

        if (basic.Length != shifted.Length)
        {
            Plugin.Log.LogWarning($"[PeakVR][Keyboard] layout {name} row mismatch: '{basic}' vs '{shifted}'");
            return keys;
        }

        for (var i = 0; i < basic.Length; i++)
            keys.Add(basic[i] == ' '
                ? new VRKeyDef("", VRKeyAction.Blank)
                : new VRKeyDef(basic[i], shifted[i]));

        return keys;
    }

    private static VRKeyDef[] Compose(List<VRKeyDef> middle, VRKeyDef? first, VRKeyDef? last)
    {
        var row = new List<VRKeyDef>();

        if (first.HasValue)
            row.Add(first.Value);

        row.AddRange(middle);

        if (last.HasValue)
            row.Add(last.Value);

        return row.ToArray();
    }

    private static VRKeyDef[] BottomRow() => new[]
    {
        new VRKeyDef("Esc", VRKeyAction.Escape, 1.6f),
        new VRKeyDef("Lang", VRKeyAction.Language, 1.8f),
        new VRKeyDef("<", VRKeyAction.Left),
        new VRKeyDef(">", VRKeyAction.Right),
        new VRKeyDef("Space", VRKeyAction.Space, 6.4f),
        new VRKeyDef("Clear", VRKeyAction.Clear, 1.6f),
        new VRKeyDef("Close", VRKeyAction.Close, 1.6f)
    };
}

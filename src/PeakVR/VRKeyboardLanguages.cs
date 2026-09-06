using System.Collections.Generic;

namespace PeakVR;

internal static class VRKeyboardLanguages
{
    public static string SettingKey(VRKeyboardLayoutDef layout) => $"Keyboard {layout.Display}";

    public static int EnabledCount()
    {
        var config = Plugin.Config;
        if (config == null)
            return VRKeyboardLayout.All.Length;

        var count = 0;
        foreach (var layout in VRKeyboardLayout.All)
            if (config.KeyboardLayoutEnabled(layout.Name))
                count++;

        return count;
    }

    public static List<VRKeyboardLayoutDef> Enabled()
    {
        var layouts = new List<VRKeyboardLayoutDef>();
        var config = Plugin.Config;

        foreach (var layout in VRKeyboardLayout.All)
            if (config == null || config.KeyboardLayoutEnabled(layout.Name))
                layouts.Add(layout);

        if (layouts.Count == 0)
            layouts.Add(VRKeyboardLayout.Us);

        return layouts;
    }
}

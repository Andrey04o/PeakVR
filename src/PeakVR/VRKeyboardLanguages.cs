using System.Collections.Generic;

namespace PeakVR;

internal static class VRKeyboardLanguages
{
    public static string SettingKey(VRKeyboardLayoutDef layout) => $"Keyboard {layout.Display}";

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

using System;
using System.Collections.Generic;
using System.Linq;
using LCVR;
using SettingsExtender;
using UnityEngine;
using Zorro.Core;
using Zorro.Settings;
using Zorro.Settings.DebugUI;

namespace PeakVR;

internal class OpenXRRuntimeSetting : Setting, IEnumSetting, IExposedSetting
{
    private const string Page = "PeakVR";

    private static List<(string name, string path)> cached;
    private string current = "";

    public OpenXRRuntimeSetting()
    {
        SettingsRegistry.Register(Page);
    }

    private static List<(string name, string path)> Options()
    {
        if (cached != null)
            return cached;

        cached = new List<(string name, string path)> { ("System Default", "") };

        try
        {
            foreach (var rt in OpenXR.GetRuntimes())
                cached.Add((rt.Name ?? "Unknown", rt.Path ?? ""));
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[PeakVR] Failed to enumerate OpenXR runtimes: {ex.Message}");
        }

        return cached;
    }

    public override void Load(ISettingsSaveLoad loader)
    {
        current = Plugin.Config.OpenXRRuntimeFile.Value ?? "";
    }

    public override void Save(ISettingsSaveLoad saver)
    {
    }

    public override void ApplyValue()
    {
    }

    public override GameObject GetSettingUICell()
        => SingletonAsset<InputCellMapper>.Instance.EnumSettingCell;

    public override SettingUI GetDebugUI(ISettingHandler settingHandler)
        => new EnumSettingsUI(this, settingHandler);

    public List<string> GetUnlocalizedChoices()
        => Options().Select(o => o.name).ToList();

    public int GetValue()
    {
        var opts = Options();
        for (var i = 0; i < opts.Count; i++)
            if (opts[i].path == current)
                return i;

        return 0;
    }

    public void SetValue(int v, ISettingHandler settingHandler, bool fromUI)
    {
        var opts = Options();
        var i = v < 0 || v >= opts.Count ? 0 : v;

        current = opts[i].path;
        Plugin.Config.OpenXRRuntimeFile.Value = current;
        Plugin.Log.LogInfo($"[PeakVR] OpenXR runtime set to '{opts[i].name}' — restart the game to apply");
    }

    public string GetDisplayName() => "OpenXR Runtime (restart to apply)";

    public string GetCategory() => SettingsRegistry.GetPageId(Page);
}

using System;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Zorro.Core;
using Zorro.Settings;
using Zorro.Settings.UI;

namespace PeakVR;

// A rebind row, the same shape ModConfig uses for other mods keybinds
internal class VRKeySetting : Setting, IExposedSetting
{
    private readonly string displayName;
    private readonly ConfigEntry<KeyCode> entry;

    private static GameObject cell;

    public VRKeySetting(string displayName, ConfigEntry<KeyCode> entry)
    {
        this.displayName = displayName;
        this.entry = entry;
    }

    public KeyCode Value
    {
        get => entry.Value;
        set => entry.Value = value;
    }

    public override void Load(ISettingsSaveLoad loader) { }

    public override void Save(ISettingsSaveLoad saver) { }

    public override void ApplyValue() { }

    public override Zorro.Settings.DebugUI.SettingUI GetDebugUI(ISettingHandler settingHandler) => null;

    public string GetDisplayName() => displayName;

    public string GetCategory() => "PeakVR";

    public override GameObject GetSettingUICell()
    {
        if (cell != null)
            return cell;

        var source = SingletonAsset<InputCellMapper>.Instance?.FloatSettingCell;
        if (source == null)
            return null;

        cell = UnityEngine.Object.Instantiate(source);
        cell.name = "PeakVRKeyCell";

        var floatUI = cell.GetComponent<FloatSettingUI>();
        var keyUI = cell.AddComponent<VRKeySettingUI>();

        // Stretch the old input box across the row, then turn it into the button.
        var box = floatUI.inputField.GetComponent<RectTransform>();
        box.pivot = new Vector2(0.5f, 0.5f);
        box.offsetMin = new Vector2(20f, -25f);
        box.offsetMax = new Vector2(380f, 25f);

        keyUI.Button = cell.AddComponent<Button>();
        floatUI.inputField.name = "Button";

        UnityEngine.Object.DestroyImmediate(floatUI.inputField.placeholder.gameObject);
        UnityEngine.Object.Destroy(floatUI.inputField);
        UnityEngine.Object.DestroyImmediate(floatUI.slider.gameObject);
        UnityEngine.Object.DestroyImmediate(floatUI);

        var text = keyUI.Button.GetComponentInChildren<TextMeshProUGUI>();
        text.fontSize = text.fontSizeMin = text.fontSizeMax = 22f;
        text.alignment = TextAlignmentOptions.Center;
        keyUI.Label = text;

        UnityEngine.Object.DontDestroyOnLoad(cell);
        return cell;
    }
}

internal class VRKeySettingUI : SettingInputUICell
{
    private static VRKeySettingUI capturing;

    public Button Button;
    public TextMeshProUGUI Label;

    private VRKeySetting setting;

    public override void Setup(Setting target, ISettingHandler handler)
    {
        if (target is not VRKeySetting key || Button == null || Label == null)
            return;

        setting = key;
        RegisterSettingListener(target);
        Label.text = key.Value.ToString();

        Button.onClick.AddListener(() =>
        {
            if (capturing != null)
                return;

            capturing = this;
            Label.text = "PRESS A KEY";
        });
    }

    protected override void OnDestroy()
    {
        if (capturing == this)
            capturing = null;
    }

    private void Update()
    {
        if (capturing != this || Keyboard.current == null)
            return;

        if (Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            Cancel();
            return;
        }

        foreach (var control in Keyboard.current.allKeys)
        {
            if (!control.wasPressedThisFrame)
                continue;

            if (Enum.TryParse<KeyCode>(control.keyCode.ToString(), out var code))
            {
                setting.Value = code;
                Label.text = code.ToString();
                capturing = null;
            }

            return;
        }
    }

    private void Cancel()
    {
        Label.text = setting.Value.ToString();
        capturing = null;
    }
}

using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using PEAKLib.UI;
using PEAKLib.UI.Elements;
using PEAKLib.UI.Elements.Settings;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Zorro.Core;
using Zorro.Settings;
using Zorro.UI;

namespace PeakVR;

internal static class VRSettingsPage
{
    private const string PageName = "VRSettings";
    private const string ButtonName = "Button_VRSettings";

    private static readonly Color BackRed = new(0.5189f, 0.1297f, 0.1718f);
    private static readonly Color ButtonPurple = new(0.404f, 0.208f, 0.588f);
    private static readonly Color Background = new(0f, 0f, 0f, 0.8667f);
    private static readonly Color HeadingColor = new(1f, 0.82f, 0.35f);
    private static readonly Color MutedColor = new(0.74f, 0.74f, 0.78f);

    // Wide enough for "SUB-SECTIONS" - the first tab used to sit on top of it.
    private const float TabLabelWidth = 250f;
    private const float SectionInset = 128f;
    private const float SubsectionInset = 180f;
    private const float TabLabelSize = 26f;

    private const float ContentLeft = 428f;
    private const float ContentRight = 110f;
    private const float ContentTop = 70f;
    private const float ContentBottom = 40f;

    private static Dictionary<string, ConfigEntryBase> entries;

    private static readonly (string Tab, Row[] Rows)[] Layout =
    {
        ("VR", new[]
        {
            Row.Heading("OpenXR"),
            Row.Setting("Start In VR"),
            Row.Setting("OpenXR Runtime"),
            Row.Setting("Mode Switch Hotkey"),
            Row.Setting("Mode Switch Key"),
            Row.Action(ModeButtonLabel, () => VRModeSwitch.Toggle()),
            Row.Info(RuntimeInfo),
            Row.Log(),

            Row.Heading("Controller"),
            Row.Setting("Hide Controllers"),
            Row.Setting("Controller Rotation Offset X"),
            Row.Setting("Controller Rotation Offset Y"),
            Row.Setting("Controller Rotation Offset Z"),

            Row.Heading("Calibration"),
            Row.Setting("Arm Span Scale"),
            Row.Action(() => "Open VR Calibration", VRCalibration.Open),

            Row.Heading("Lasers"),
            Row.Setting("Interaction Line"),
            Row.Setting("HUD Pointer Line"),
            Row.Setting("Aim At Object Center"),

            Row.Heading("Keyboard"),
            Row.Languages(),
            Row.Setting("Key Repeat Delay"),
            Row.Setting("Key Repeat Rate"),

            Row.Heading("Tools"),
            Row.Setting("Verbose Logging"),
            Row.Setting("Reacquire Audio Device"),
        }),

        ("Comfort", new[]
        {
            Row.Setting("Smooth Turn"),
            Row.Setting("Snap Turn Angle"),
            Row.Setting("Smooth Turn Speed"),
            Row.Setting("Movement Tunneling"),
            Row.Setting("Tunneling Strength"),
            Row.Setting("Camera Roll"),
        }),

        ("VR Graphics", new[]
        {
            Row.Heading("Graphics"),
            Row.Setting("LOD Bias"),
            Row.Setting("Make Image Sharper"),
            Row.Setting("Far Plane Culling Distance"),
            Row.Setting("Foliage Draw Distance"),
            Row.Setting("Depth Prepass"),
            Row.Setting("GPU Occlusion Culling"),
            Row.Setting("Foveated Rendering"),
            Row.Setting("Foreground UI"),
            Row.Setting("Foreground UI Over Geometry"),

            Row.Heading("Fixes"),
            Row.Setting("Fix Hazard Rendering"),
            Row.Setting("Fix Per-Eye Object Culling"),
            Row.Setting("Force Disable HBAO"),
            Row.Setting("Fullscreen Loading Cover"),
        }),

        ("Mod Compatibility", new[]
        {
            Row.Setting("Other Mods UI In VR"),
            Row.Setting("sPEAKer UI On Left Hand"),
        }),

        ("Special", new[]
        {
            Row.Setting("Copy Head Rotation"),
            Row.Info(() => "These also apply in flat (non-VR) mode."),
        }),
    };

    private class TabFitter : MonoBehaviour
    {
        private const float Spacing = 20f;
        private const float MinWidth = 96f;
        private const float MaxWidth = 220f;

        private RectTransform self;
        private float lastWidth = -1f;
        private int lastCount = -1;

        private void Awake() => self = (RectTransform)transform;

        private void LateUpdate()
        {
            var tabs = GetComponentsInChildren<LayoutElement>(true);
            var width = self.rect.width;

            if (tabs.Length == 0 || (Mathf.Approximately(width, lastWidth) && tabs.Length == lastCount))
                return;

            lastWidth = width;
            lastCount = tabs.Length;

            var each = (width - Spacing * (tabs.Length - 1)) / tabs.Length - 1f;
            each = Mathf.Clamp(each, MinWidth, MaxWidth);

            foreach (var tab in tabs)
                tab.minWidth = each;
        }
    }

    private class Row
    {
        public string HeadingText;
        public string Key;
        public Func<string> Text;
        public Func<string> Label;
        public Action Click;
        public bool IsLog;
        public bool IsLanguages;

        public static Row Heading(string text) => new() { HeadingText = text };
        public static Row Setting(string key) => new() { Key = key };
        public static Row Info(Func<string> text) => new() { Text = text };
        public static Row Action(Func<string> label, Action click) => new() { Label = label, Click = click };
        public static Row Log() => new() { IsLog = true };
        public static Row Languages() => new() { IsLanguages = true };
    }

    private static readonly List<Setting> Settings = new();

    public static void Register()
    {
        MenuAPI.AddToSettingsMenu(Build);
        Plugin.Log.LogInfo("[PeakVR] VR settings registered (Settings menu)");
    }

    private static void Build(Transform parent)
    {
        if (parent.Find(ButtonName) != null)
            return;

        var mainMenu = parent.GetComponentInParent<MainMenuPageHandler>();
        var pauseMenu = parent.GetComponentInParent<PauseMenuHandler>();

        var parentPage = mainMenu != null
            ? mainMenu.GetPage<MainMenuSettingsPage>() as UIPage
            : pauseMenu != null ? pauseMenu.GetPage<PauseMenuSettingsMenuPage>() : null;

        if (parentPage == null)
        {
            Plugin.Log.LogWarning("[PeakVR] No settings page handler found; VR settings not added");
            return;
        }

        var page = MenuAPI.CreateChildPage(PageName, parentPage);

        // Only the main menu needs one - in the pause menu the world behind must stay visible.
        if (mainMenu != null)
            page.CreateBackground(Background);

        var header = new GameObject("Header")
            .ParentTo(page)
            .AddComponent<PeakElement>()
            .SetAnchorMinMax(new Vector2(0f, 1f))
            .SetPosition(new Vector2(40f, -40f))
            .SetPivot(new Vector2(0f, 1f))
            .SetSize(new Vector2(360f, 100f));

        var title = MenuAPI.CreateText("VR Settings", "HeaderText")
            .SetFontSize(48f)
            .ParentTo(header)
            .ExpandToParent();

        title.TextMesh.fontSizeMax = 48f;
        title.TextMesh.fontSizeMin = 24f;
        title.TextMesh.enableAutoSizing = true;
        title.TextMesh.alignment = TextAlignmentOptions.Left;

        var back = MenuAPI.CreateMenuButton("Back")
            .SetLocalizationIndex("BACK")
            .SetColor(BackRed)
            .ParentTo(page)
            .SetPosition(new Vector2(100f, -160f))
            .SetWidth(120f);

        page.SetBackButton(back.Button);

        var content = new GameObject("Content")
            .AddComponent<PeakElement>()
            .ParentTo(page);

        var contentRt = content.RectTransform;
        contentRt.anchorMin = Vector2.zero;
        contentRt.anchorMax = Vector2.one;
        contentRt.pivot = new Vector2(0.5f, 0.5f);
        contentRt.offsetMin = new Vector2(ContentLeft, ContentBottom);
        contentRt.offsetMax = new Vector2(-ContentRight, -ContentTop);

        TabLabel(content, "SECTIONS", 10f);
        TabLabel(content, "SUB-SECTIONS", -44f);

        var tabStrip = new GameObject("SectionTabs")
            .ParentTo(content)
            .AddComponent<PeakHorizontalTabs>();
        Inset(tabStrip.RectTransform, SectionInset, 0f);

        var subStrip = new GameObject("SubsectionTabs")
            .ParentTo(content)
            .AddComponent<PeakHorizontalTabs>();
        Inset(subStrip.RectTransform, SubsectionInset, -54f);

        var list = MenuAPI.CreateScrollableContent("VRSettingsList")
            .ParentTo(content)
            .ExpandToParent()
            .SetOffsetMax(new Vector2(0f, -118f));

        var runner = list.gameObject.AddComponent<SectionList>();
        runner.Content = list.Content;
        runner.SubStrip = subStrip;
        runner.SubTabs = subStrip.gameObject.AddComponent<SubsectionTabs>();
        runner.SubTabs.List = runner;

        var tabs = tabStrip.gameObject.AddComponent<SectionTabs>();
        tabs.List = runner;

        tabStrip.gameObject.AddComponent<TabFitter>();
        subStrip.gameObject.AddComponent<TabFitter>();

        foreach (var group in Layout)
        {
            var tab = tabStrip.AddTab(group.Tab.ToUpperInvariant());
            var tabButton = tab.AddComponent<SectionTab>();
            tabButton.Section = group.Tab;
            tabButton.text = tab.GetComponentInChildren<TextMeshProUGUI>();
            tabButton.SelectedGraphic = tab.transform.Find("Selected")?.gameObject;
        }

        page.SetOnOpen(() => runner.Refresh());

        var button = MenuAPI.CreatePauseMenuButton("VR SETTINGS")
            .SetColor(ButtonPurple)
            .ParentTo(parent)
            .OnClick(() =>
            {
                var handler = mainMenu as UIPageHandler ?? pauseMenu;
                handler?.TransistionToPage(page, new SetActivePageTransistion());
            });

        button.gameObject.name = ButtonName;
        button.SetWidth(220f);
        button.gameObject.AddComponent<StackUnderSiblings>().Target = button.RectTransform;

        page.gameObject.SetActive(false);
    }

    internal class SectionList : MonoBehaviour
    {
        private const float Stagger = 0.05f;

        public RectTransform Content;
        public PeakHorizontalTabs SubStrip;
        public SubsectionTabs SubTabs;

        private string section;
        private string subsection;
        private readonly List<SettingsUICell> cells = new();

        public void Show(string name)
        {
            section = name;
            subsection = null;

            var groups = Groups(section);

            if (SubStrip != null && SubTabs != null)
            {
                for (var i = SubStrip.Tabs.Count - 1; i >= 0; i--)
                    SubStrip.DeleteTab(SubStrip.Tabs[i].name);

                SubTabs.buttons.Clear();
                SubTabs.selectedButton = null;

                var named = groups.Count > 1 || groups[0].Name != null;
                SubStrip.gameObject.SetActive(named);

                if (named)
                {
                    SubTabs.Muted = true;
                    foreach (var group in groups)
                    {
                        var tab = SubStrip.AddTab(group.Name.ToUpperInvariant());
                                    var button = tab.AddComponent<SubsectionTab>();
                        button.Subsection = group.Name;
                        button.text = tab.GetComponentInChildren<TextMeshProUGUI>();
                        button.SelectedGraphic = tab.transform.Find("Selected")?.gameObject;
                        SubTabs.AddButton(button);
                    }
                    SubTabs.Muted = false;

                    if (SubTabs.buttons.Count > 0)
                    {
                        SubTabs.Select(SubTabs.buttons[0]);
                        return;
                    }
                }
            }

            Refresh();
        }

        public void ShowSubsection(string name)
        {
            subsection = name;
            Refresh();
        }

        public void Refresh()
        {
            if (Content == null || string.IsNullOrEmpty(section))
                return;

            StopAllCoroutines();

            for (var i = Content.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(Content.GetChild(i).gameObject);

            cells.Clear();
            Settings.Clear();

            if (Templates.SettingsCellPrefab == null)
            {
                Plugin.Log.LogWarning("[PeakVR] Settings cell prefab missing - open the main menu once first");
                return;
            }

            foreach (var group in Groups(section))
            {
                if (subsection != null && group.Name != subsection)
                    continue;

                // Options first, explanatory text after them, whatever order the layout declares.
                foreach (var row in group.Rows)
                    if (row.Text == null && !row.IsLog)
                    {
                        var cell = Build(Content, row, this);
                        if (cell != null)
                            cells.Add(cell);
                    }

                foreach (var row in group.Rows)
                    if (row.Text != null || row.IsLog)
                        Build(Content, row, this);
            }

            foreach (var leftover in Unlisted(section))
            {
                var cell = SettingRow(Content, leftover);
                if (cell != null)
                    cells.Add(cell);
            }

            StartCoroutine(FadeInCells());
        }

        private System.Collections.IEnumerator FadeInCells()
        {
            foreach (var cell in cells)
            {
                if (cell == null)
                    continue;

                cell.FadeIn();
                yield return new WaitForSecondsRealtime(Stagger);
            }
        }
    }

    private static List<(string Name, List<Row> Rows)> Groups(string tab)
    {
        var groups = new List<(string Name, List<Row> Rows)>();
        List<Row> current = null;

        foreach (var group in Layout)
        {
            if (group.Tab != tab)
                continue;

            foreach (var row in group.Rows)
            {
                if (row.HeadingText != null)
                {
                    current = new List<Row>();
                    groups.Add((row.HeadingText, current));
                    continue;
                }

                if (current == null)
                {
                    current = new List<Row>();
                    groups.Add((null, current));
                }

                current.Add(row);
            }
        }

        if (groups.Count == 0)
            groups.Add((null, new List<Row>()));

        return groups;
    }

    internal class SubsectionTab : Zorro.UI.TAB_Button
    {
        public string Subsection = "";
        public GameObject SelectedGraphic;

        private void Update()
        {
            if (text != null)
                text.color = Color.Lerp(text.color, Selected ? Color.black : Color.white,
                    Time.unscaledDeltaTime * 7f);

            if (SelectedGraphic != null)
                SelectedGraphic.SetActive(Selected);
        }
    }

    internal class SubsectionTabs : Zorro.UI.TABS<SubsectionTab>
    {
        public SectionList List;

        public bool Muted;

        public override void OnSelected(SubsectionTab button)
        {
            if (!Muted && List != null && button != null)
                List.ShowSubsection(button.Subsection);
        }
    }

    internal class SectionTab : Zorro.UI.TAB_Button
    {
        public string Section = "";
        public GameObject SelectedGraphic;

        private void Update()
        {
            if (text != null)
                text.color = Color.Lerp(text.color, Selected ? Color.black : Color.white,
                    Time.unscaledDeltaTime * 7f);

            if (SelectedGraphic != null)
                SelectedGraphic.SetActive(Selected);
        }
    }

    internal class SectionTabs : Zorro.UI.TABS<SectionTab>
    {
        public SectionList List;

        public override void OnSelected(SectionTab button)
        {
            if (List != null && button != null)
                List.Show(button.Section);
        }
    }

    private static SettingsUICell Build(RectTransform content, Row row, MonoBehaviour host)
    {
        if (row.HeadingText != null)
        {
            HeadingRow(content, row.HeadingText);
            return null;
        }

        if (row.Text != null)
        {
            InfoRow(content, row.Text());
            return null;
        }

        if (row.Click != null)
        {
            ActionRow(content, row, host);
            return null;
        }

        if (row.IsLog)
        {
            LogRow(content);
            return null;
        }

        if (row.IsLanguages)
        {
            LanguageRow(content);
            return null;
        }

        return Entries().TryGetValue(row.Key, out var entry) ? SettingRow(content, entry) : null;
    }

    private static string RuntimeInfo()
    {
        var mode = Plugin.VrEnabled ? "VR" : "Flat";

        var active = LCVR.OpenXR.GetActiveRuntimeName(out var name)
            && LCVR.OpenXR.GetActiveRuntimeVersion(out var major, out var minor, out var patch)
                ? $"{name} ({major}.{minor}.{patch})"
                : Plugin.VrEnabled ? "unknown" : "none - VR is not running";

        var found = string.Join(", ", LCVR.OpenXR.GetRuntimeChoices());

        return $"Mode: {mode}\nActive runtime: {active}\nDetected: {found}";
    }

    private static string ModeButtonLabel() => Plugin.VrEnabled ? "Switch to Flat mode" : "Switch to VR mode";

    private static void Inset(RectTransform strip, float left, float y)
    {
        strip.anchoredPosition = new Vector2(0f, y);
        strip.offsetMin = new Vector2(left, strip.offsetMin.y);
        strip.offsetMax = new Vector2(0f, strip.offsetMax.y);
    }

    private static void TabLabel(PeakElement content, string text, float y)
    {
        var label = MenuAPI.CreateText(text).ParentTo(content);

        label.RectTransform.anchorMin = label.RectTransform.anchorMax = new Vector2(0f, 1f);
        label.RectTransform.pivot = new Vector2(0f, 1f);
        label.RectTransform.sizeDelta = new Vector2(TabLabelWidth, 44f);
        label.RectTransform.anchoredPosition = new Vector2(4f, y);

        label.TextMesh.enableAutoSizing = false;
        label.TextMesh.fontSize = TabLabelSize;
        label.TextMesh.alignment = TextAlignmentOptions.Left;
        label.TextMesh.raycastTarget = false;
    }

    private static void HeadingRow(RectTransform content, string title)
    {
        var rt = NewRow(content, $"Heading_{title}", 62f);

        var label = MenuAPI.CreateText(title.ToUpperInvariant()).ParentTo(rt).ExpandToParent();
        label.SetColor(HeadingColor);

        var tmp = label.TextMesh;
        tmp.enableAutoSizing = false;
        tmp.fontSize = 32f;
        tmp.alignment = TextAlignmentOptions.Left;
        tmp.raycastTarget = false;
    }

    private static void InfoRow(RectTransform content, string text)
    {
        var rt = NewRow(content, "Info", 110f);

        var label = MenuAPI.CreateText(text).ParentTo(rt).ExpandToParent();
        label.SetColor(MutedColor);

        var tmp = label.TextMesh;
        tmp.enableAutoSizing = false;
        tmp.fontSize = 30f;
        tmp.alignment = TextAlignmentOptions.TopLeft;
        tmp.enableWordWrapping = true;
        tmp.raycastTarget = false;
    }

    // A slice of the OpenXR chatter, so a failed start can be diagnosed inside the headset.
    private static void LogRow(RectTransform content)
    {
        VRLogPanel.Build(NewRow(content, "Log", 290f));
    }

    private static void LanguageRow(RectTransform content)
    {
        const float cell = 132f;
        const float gap = 16f;

        var layouts = VRKeyboardLayout.All;
        var rt = NewRow(content, "Languages", cell + gap * 2f);

        var font = Templates.SettingsCellPrefab != null
            ? Templates.SettingsCellPrefab.GetComponentInChildren<TextMeshProUGUI>(true)?.font
            : null;

        for (var i = 0; i < layouts.Length; i++)
        {
            var layout = layouts[i];

            var button = new GameObject($"Lang_{layout.Name}", typeof(RectTransform));
            var buttonRect = (RectTransform)button.transform;
            buttonRect.SetParent(rt, false);
            buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(0f, 0.5f);
            buttonRect.pivot = new Vector2(0f, 0.5f);
            buttonRect.sizeDelta = new Vector2(cell, cell);
            buttonRect.anchoredPosition = new Vector2(20f + i * (cell + gap), 0f);

            var image = button.AddComponent<Image>();
            image.raycastTarget = true;

            var text = Text(buttonRect, font, layout.Name, 40f,
                new Vector2(0f, 26f), new Vector2(cell, 52f));

            var box = new GameObject("Box", typeof(RectTransform));
            var boxRect = (RectTransform)box.transform;
            boxRect.SetParent(buttonRect, false);
            boxRect.anchorMin = boxRect.anchorMax = boxRect.pivot = new Vector2(0.5f, 0.5f);
            boxRect.sizeDelta = new Vector2(34f, 34f);
            boxRect.anchoredPosition = new Vector2(0f, -34f);

            var boxImage = box.AddComponent<Image>();
            boxImage.raycastTarget = false;

            var tick = new GameObject("Tick", typeof(RectTransform));
            var tickRect = (RectTransform)tick.transform;
            tickRect.SetParent(boxRect, false);
            tickRect.anchorMin = tickRect.anchorMax = tickRect.pivot = new Vector2(0.5f, 0.5f);
            tickRect.sizeDelta = new Vector2(34f, 34f);
            tickRect.anchoredPosition = Vector2.zero;

            Stroke(tickRect, new Vector2(-6f, -2f), new Vector2(14f, 5f), 45f);
            Stroke(tickRect, new Vector2(3f, 2f), new Vector2(22f, 5f), -45f);

            button.AddComponent<VRLanguageToggle>().Setup(layout.Name, image, text, boxImage, tick);
        }
    }

    private static void Stroke(RectTransform parent, Vector2 position, Vector2 size, float angle)
    {
        var go = new GameObject("Stroke", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = position;
        rt.localRotation = Quaternion.Euler(0f, 0f, angle);

        var image = go.AddComponent<Image>();
        image.color = VRLanguageToggle.Accent;
        image.raycastTarget = false;
    }

    private static TextMeshProUGUI Text(RectTransform parent, TMP_FontAsset font, string value,
        float size, Vector2 position, Vector2 area)
    {
        var go = new GameObject("Label", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = area;
        rt.anchoredPosition = position;

        var text = go.AddComponent<TextMeshProUGUI>();
        if (font != null)
            text.font = font;
        text.text = value;
        text.fontSize = size;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        return text;
    }

    private static void ActionRow(RectTransform content, Row row, MonoBehaviour host)
    {
        var rt = NewRow(content, "Action", 86f);

        var button = MenuAPI.CreateButton(row.Label()).ParentTo(rt);
        button.RectTransform.anchorMin = button.RectTransform.anchorMax = new Vector2(0f, 0.5f);
        button.RectTransform.pivot = new Vector2(0f, 0.5f);
        button.RectTransform.sizeDelta = new Vector2(420f, 68f);
        button.RectTransform.anchoredPosition = new Vector2(20f, 0f);

        button.OnClick(() =>
        {
            row.Click();

            // Mode switching changes what the info text says, so redraw the tab after it settles.
            if (host is SectionList list)
                list.Refresh();
        });
    }

    private static RectTransform NewRow(RectTransform content, string name, float height)
    {
        var go = new GameObject($"Row_{name}", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(content, false);
        rt.sizeDelta = new Vector2(1360f, height);

        var layout = go.AddComponent<LayoutElement>();
        layout.minHeight = height;
        layout.preferredHeight = height;
        return rt;
    }

    private static Dictionary<string, ConfigEntryBase> Entries()
    {
        if (entries != null)
            return entries;

        entries = new Dictionary<string, ConfigEntryBase>();
        foreach (var pair in Plugin.Config.File)
            if (!IsHidden(pair.Key.Section, pair.Value))
                entries[pair.Key.Key] = pair.Value;

        return entries;
    }

    private static IEnumerable<ConfigEntryBase> Unlisted(string tab)
    {
        if (tab != Layout[Layout.Length - 1].Tab)
            yield break;

        var listed = new HashSet<string>();
        foreach (var group in Layout)
            foreach (var row in group.Rows)
                if (row.Key != null)
                    listed.Add(row.Key);

        foreach (var layout in VRKeyboardLayout.All)
            listed.Add(VRKeyboardLanguages.SettingKey(layout));

        foreach (var pair in Entries())
            if (!listed.Contains(pair.Key))
                yield return pair.Value;
    }

    private static SettingsUICell SettingRow(RectTransform content, ConfigEntryBase entry)
    {
        var setting = Wrap(entry);
        if (setting == null)
            return null;

        setting.Load(null);
        Settings.Add(setting);

        var cell = UnityEngine.Object.Instantiate(Templates.SettingsCellPrefab, content).GetComponent<SettingsUICell>();
        cell.m_text.text = entry.Definition.Key;

        cell.m_text.fontStyle &= ~FontStyles.UpperCase;

        var group = cell.GetComponent<CanvasGroup>();
        if (group != null)
        {
            cell.m_canvasGroup = group;
            group.alpha = 0f;
        }

        if (cell.localizedText != null)
            cell.localizedText.enabled = false;

        UnityEngine.Object.Instantiate(setting.GetSettingUICell(), cell.m_settingsContentParent)
            .GetComponent<SettingInputUICell>()
            .Setup(setting, GameHandler.Instance.SettingsHandler);

        return cell;
    }

    private static Setting Wrap(ConfigEntryBase entry)
    {
        var name = entry.Definition.Key;

        if (entry is ConfigEntry<bool> flag)
            return new GenericBoolSetting(name, (bool)entry.DefaultValue, currentValue: flag.Value,
                saveCallback: value => flag.Value = value);

        if (entry is ConfigEntry<float> number
            && entry.Description?.AcceptableValues is AcceptableValueRange<float> range)
            return new GenericFloatSetting(name, (float)entry.DefaultValue,
                minValue: range.MinValue, maxValue: range.MaxValue, currentValue: number.Value,
                saveCallback: value => number.Value = value);

        if (entry is ConfigEntry<LineVisibility> line)
            return new GenericEnumSetting<LineVisibility>(name, line.Value, (LineVisibility)entry.DefaultValue,
                saveCallback: value => line.Value = value);

        if (entry is ConfigEntry<string> text
            && entry.Description?.AcceptableValues is AcceptableValueList<string> choices)
            return new ChoiceSetting(name, () => text.Value, value => text.Value = value,
                choices.AcceptableValues);

        if (entry is ConfigEntry<KeyCode> hotkey)
            return new VRKeySetting(name, hotkey);

        return null;
    }

    private class ChoiceSetting : Setting, IEnumSetting, IExposedSetting
    {
        private readonly string displayName;
        private readonly Func<string> read;
        private readonly Action<string> write;
        private readonly List<string> choices;

        public ChoiceSetting(string displayName, Func<string> read, Action<string> write, IEnumerable<string> choices)
        {
            this.displayName = displayName;
            this.read = read;
            this.write = write;
            this.choices = new List<string>(choices);
        }

        public override void Load(ISettingsSaveLoad loader) { }

        public override void Save(ISettingsSaveLoad saver) { }

        public override void ApplyValue() { }

        public override Zorro.Settings.DebugUI.SettingUI GetDebugUI(ISettingHandler settingHandler) => null;

        public override GameObject GetSettingUICell() =>
            SingletonAsset<InputCellMapper>.Instance.EnumSettingCell;

        public List<string> GetUnlocalizedChoices() => choices;

        public int GetValue()
        {
            var index = choices.IndexOf(read());
            return index < 0 ? 0 : index;
        }

        public void SetValue(int value, ISettingHandler settingHandler, bool fromUI)
        {
            if (value >= 0 && value < choices.Count)
                write(choices[value]);
        }

        public string GetDisplayName() => displayName;

        public string GetCategory() => "PeakVR";
    }

    private static bool IsHidden(string section, ConfigEntryBase entry)
    {
        if (section == "Internal")
            return true;

        var tags = entry.Description?.Tags;
        if (tags == null)
            return false;

        foreach (var tag in tags)
            if (tag is string text && text == "Hidden")
                return true;

        return false;
    }

    private class StackUnderSiblings : MonoBehaviour
    {
        private const float Gap = 52f;
        private const float FallbackY = -300f;

        public RectTransform Target;

        private void LateUpdate()
        {
            enabled = false;

            if (Target == null || Target.parent == null)
                return;

            var lowest = float.MaxValue;
            var found = false;

            foreach (Transform sibling in Target.parent)
            {
                if (sibling == Target || sibling is not RectTransform rt || !sibling.gameObject.activeSelf)
                    continue;

                if (rt.anchoredPosition.y < lowest)
                {
                    lowest = rt.anchoredPosition.y;
                    found = true;
                }
            }

            Target.anchoredPosition = new Vector2(171f, found ? lowest - Gap : FallbackY);
        }
    }
}

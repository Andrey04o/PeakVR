using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Zorro.Settings;

namespace PeakVR;

internal class VRKeyboard : MonoBehaviour
{
    private const float Unit = 96f;
    private const float Gap = 8f;
    private const float Pad = 18f;

    private const float Distance = 0.85f;
    private const float Drop = 0.40f;
    private const float KeyboardWidth = 1f;
    private const float NumpadWidth = 0.38f;
    private const float ChatLift = 210f;
    private const float ChatScale = 1.8f;
    private const int OpenGraceFrames = 12;
    private const float StickClose = 0.9f;
    private const int RefreshInterval = 30;

    private static VRKeyboard instance;

    public static bool IsOpen => instance != null && instance.open;

    private bool open;
    private bool shift;
    private bool caps;
    private bool numeric;
    private int refocusAttempts;

    private TMP_InputField field;
    private bool savedSelectAll;
    private bool chatLent;
    private bool isChat;
    private string numericBuffer = "";

    private int openedFrame = -1;
    private bool numericFresh;
    private bool clearPending;
    private bool rebuildPending;
    private int refreshTick;
    private TMP_InputField dismissed;

    private List<VRKeyboardLayoutDef> layouts;
    private int layoutIndex;
    private string layoutName;

    private GameObject panel;
    private Canvas canvas;
    private RectTransform panelRect;
    private RectTransform keyRoot;
    private GameObject keyTemplate;
    private TMP_FontAsset font;

    private readonly List<VRKeyboardKey> keys = new();
    private readonly List<TextMeshProUGUI> keyLabels = new();
    private readonly List<VRKeyDef> keyDefs = new();

    public static void Create()
    {
        if (instance != null)
            return;

        var go = new GameObject("PeakVR Keyboard");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<VRKeyboard>();
    }

    public static void Close()
    {
        if (instance != null)
            instance.Hide("closed externally");
    }

    private void Update()
    {
        if (!Plugin.VrEnabled)
        {
            if (open)
                Hide("flat mode");
            return;
        }

        if (clearPending)
            clearPending = !Select(null);

        if (rebuildPending && open)
        {
            rebuildPending = false;
            Rebuild();
            ApplyScale();
        }

        var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        var focused = selected != null ? selected.GetComponent<TMP_InputField>() : null;

        if (focused != null && focused != field)
        {
            if (focused == dismissed && !PressedOn(focused))
                return;

            dismissed = null;
            Attach(focused);
            return;
        }

        if (!open)
            return;

        if (field == null)
        {
            Hide("field destroyed");
            return;
        }


        if (selected != null && selected != field.gameObject)
        {
            Hide($"selection moved to '{selected.name}'");
            return;
        }

        if (isChat)
        {
            PeakTextChatPatch.KeepBlocking();

            if (++refreshTick % RefreshInterval == 0)
            {
                UIOverlay.MakeAlwaysVisible(canvas, UIOverlay.KeyboardQueue);
                UIOverlay.SweepForegroundLayer(canvas);
            }
        }

        if (VRControls.MoveStick != null
            && VRControls.MoveStick.ReadValue<Vector2>().sqrMagnitude >= StickClose * StickClose)
        {
            CloseField();
            return;
        }

        if (field.isFocused)
        {
            refocusAttempts = 0;
            return;
        }

        if (++refocusAttempts > 60)
        {
            Hide("field would not take focus back");
            return;
        }

        if (selected != field.gameObject)
            Select(field.gameObject);

        field.ActivateInputField();
        field.MoveTextEnd(false);
    }

    public static void AllowOpen()
    {
        if (instance != null)
            instance.dismissed = null;
    }

    private static bool PressedOn(TMP_InputField target)
    {
        if (Time.frameCount - VRPointer.PressFrame > 3)
            return false;

        var pressed = VRPointer.PressTarget;
        return pressed != null && pressed.transform.IsChildOf(target.transform);
    }

    private static void Verbose(string message)
    {
        if (Plugin.Config != null && Plugin.Config.EnableVerboseLogging.Value)
            Plugin.Log.LogInfo($"[PeakVR][Keyboard] {message}");
    }

    private static bool Select(GameObject target)
    {
        var events = EventSystem.current;

        if (events == null || events.alreadySelecting)
            return false;

        events.SetSelectedGameObject(target, null);
        return true;
    }

    private void Attach(TMP_InputField target)
    {
        ReturnChat();

        field = target;
        shift = false;
        caps = false;
        refocusAttempts = 0;
        numeric = IsNumeric(target);

        savedSelectAll = target.onFocusSelectAll;
        target.onFocusSelectAll = false;
        target.MoveTextEnd(false);

        openedFrame = Time.frameCount;

        if (numeric)
        {
            numericBuffer = Numeric(target.text);
            numericFresh = true;
        }

        RefreshLayouts();
        Build();
        Rebuild();

        open = true;
        panel.SetActive(true);
        VRPointer.Extra = canvas;
        VRHands.SetPointersActive(true);
        Place();

        isChat = !numeric && VRModHandUI.OwnsChatField(target.transform);

        if (isChat)
        {
            chatLent = VRModHandUI.LendChat(panelRect,
                new Vector2(0f, panelRect.sizeDelta.y / 2f + ChatLift * ChatScale), ChatScale);

            if (chatLent)
            {
                UIOverlay.MakeAlwaysVisible(canvas, UIOverlay.KeyboardQueue);
                VRLayers.HideFromMirror(panel);
            }
        }

        Verbose($"typing into '{target.name}' ({(numeric ? "numpad" : "keyboard")})");
    }

    private void Hide(string reason)
    {
        if (open)
            Verbose($"closed ({reason})");

        if (field != null)
            field.onFocusSelectAll = savedSelectAll;

        if (isChat)
            PeakTextChatPatch.StopBlocking();

        isChat = false;

        ReturnChat();

        open = false;
        field = null;
        refocusAttempts = 0;

        if (panel != null)
            panel.SetActive(false);

        if (VRPointer.Extra == canvas)
            VRPointer.Extra = null;
    }

    private void ReturnChat()
    {
        if (!chatLent)
            return;

        chatLent = false;
        VRModHandUI.ReturnChat();
    }

    private static bool IsNumeric(TMP_InputField target)
    {
        if (target.contentType == TMP_InputField.ContentType.DecimalNumber
            || target.contentType == TMP_InputField.ContentType.IntegerNumber)
            return true;

        return target.GetComponentInParent<SettingInputUICell>() != null;
    }

    private void Build()
    {
        if (panel != null)
            return;

        var prefab = PeakAssets.Keyboard;

        if (prefab != null)
        {
            panel = Instantiate(prefab);
            keyTemplate = FindChild(panel.transform, "KeyTemplate");
            keyRoot = FindChild(panel.transform, "Keys")?.GetComponent<RectTransform>();
        }
        else
        {
            panel = BuildFallbackPanel();
            keyTemplate = FindChild(panel.transform, "KeyTemplate");
            keyRoot = FindChild(panel.transform, "Keys").GetComponent<RectTransform>();
            Plugin.Log.LogWarning("[PeakVR][Keyboard] keyboard prefab not in the bundle - using the built-in look");
        }

        panel.name = "PeakVR Keyboard Panel";
        panel.transform.SetParent(transform, false);
        panelRect = (RectTransform)panel.transform;

        canvas = panel.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 30000;

        if (keyRoot == null)
            keyRoot = panelRect;

        if (keyTemplate != null)
            keyTemplate.SetActive(false);

        font = FindFont();
        panel.SetActive(false);
    }

    private GameObject BuildFallbackPanel()
    {
        var root = new GameObject("Keyboard", typeof(RectTransform));
        root.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        root.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 3f;

        var bg = new GameObject("Background", typeof(RectTransform));
        bg.transform.SetParent(root.transform, false);
        Stretch((RectTransform)bg.transform);
        bg.AddComponent<Image>().color = new Color(0.04f, 0.05f, 0.07f, 0.88f);

        var container = new GameObject("Keys", typeof(RectTransform));
        container.transform.SetParent(root.transform, false);
        Stretch((RectTransform)container.transform);

        var key = new GameObject("KeyTemplate", typeof(RectTransform));
        key.transform.SetParent(root.transform, false);
        key.AddComponent<Image>().color = Color.white;
        key.SetActive(false);

        return root;
    }

    private void RefreshLayouts()
    {
        layouts = VRKeyboardLanguages.Enabled();
        layoutIndex = 0;

        if (layoutName != null)
            for (var i = 0; i < layouts.Count; i++)
                if (layouts[i].Name == layoutName)
                {
                    layoutIndex = i;
                    break;
                }

        layoutName = layouts[layoutIndex].Name;
    }

    private void Rebuild()
    {
        if (layouts == null)
            RefreshLayouts();

        var rows = numeric ? VRKeyboardLayout.Numbers : layouts[layoutIndex].Rows;

        foreach (var key in keys)
            if (key != null)
                Destroy(key.gameObject);

        keys.Clear();
        keyLabels.Clear();
        keyDefs.Clear();

        var widest = 0f;
        foreach (var row in rows)
            widest = Mathf.Max(widest, RowWidth(row));

        var contentHeight = rows.Length * Unit + (rows.Length - 1) * Gap;
        panelRect.sizeDelta = new Vector2(widest + Pad * 2f, contentHeight + Pad * 2f);

        var y = contentHeight / 2f - Unit / 2f;

        foreach (var row in rows)
        {
            var x = -widest / 2f;

            foreach (var def in row)
            {
                var width = def.Width * Unit;
                CreateKey(def, new Vector2(x + width / 2f, y), width);
                x += width + Gap;
            }

            y -= Unit + Gap;
        }

        UIOverlay.MakeAlwaysVisible(canvas, UIOverlay.KeyboardQueue);
        VRLayers.HideFromMirror(panel);
    }

    private void CreateKey(VRKeyDef def, Vector2 position, float width)
    {
        var go = Instantiate(keyTemplate, keyRoot);
        go.name = "Key " + def.Label;
        go.SetActive(true);

        var rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(width, Unit);
        rect.anchoredPosition = position;

        var image = go.GetComponent<Image>();
        image.raycastTarget = def.Action != VRKeyAction.Blank;

        if (def.Action == VRKeyAction.Blank)
            image.color = new Color(image.color.r, image.color.g, image.color.b, image.color.a * 0.35f);

        var key = go.AddComponent<VRKeyboardKey>();
        key.target = image;
        key.repeatable = def.Action is VRKeyAction.Char or VRKeyAction.Space or VRKeyAction.Backspace
            or VRKeyAction.Left or VRKeyAction.Right;

        var button = go.GetComponent<Button>();
        if (button != null)
        {
            key.normal = button.colors.normalColor;
            key.highlighted = button.colors.highlightedColor;
            key.pressed = button.colors.pressedColor;
            Destroy(button);
        }

        var captured = def;
        key.onClick = () => Press(captured);
        key.Refresh();

        var label = new GameObject("Label", typeof(RectTransform));
        label.transform.SetParent(rect, false);
        Stretch((RectTransform)label.transform);

        var text = label.AddComponent<TextMeshProUGUI>();
        if (font != null)
            text.font = font;
        text.alignment = TextAlignmentOptions.Center;
        text.fontSize = def.Action == VRKeyAction.Char ? 46f : 30f;
        text.color = Color.white;
        text.raycastTarget = false;
        text.text = LabelFor(def);

        keys.Add(key);
        keyLabels.Add(text);
        keyDefs.Add(def);
    }

    private void Press(VRKeyDef def)
    {
        switch (def.Action)
        {
            case VRKeyAction.Blank:
                return;

            case VRKeyAction.Shift:
                shift = !shift;
                RefreshLabels();
                return;

            case VRKeyAction.CapsLock:
                caps = !caps;
                RefreshLabels();
                return;

            case VRKeyAction.Language:
                if (layouts.Count > 1)
                {
                    layoutIndex = (layoutIndex + 1) % layouts.Count;
                    layoutName = layouts[layoutIndex].Name;
                    rebuildPending = true;
                }
                return;

            case VRKeyAction.Close:
                CloseField();
                return;
        }

        if (field == null)
        {
            Hide("no field");
            return;
        }

        if (numeric)
        {
            PressNumeric(def);
            return;
        }

        switch (def.Action)
        {
            case VRKeyAction.Char:
                SendCharacter(field, def.Resolve(shift, caps));

                if (shift)
                {
                    shift = false;
                    RefreshLabels();
                }
                break;

            case VRKeyAction.Space:
                SendCharacter(field, ' ');
                break;

            case VRKeyAction.Tab:
                SendCharacter(field, '\t');
                break;

            case VRKeyAction.Backspace:
                SendKey(field, KeyCode.Backspace);
                break;

            case VRKeyAction.Left:
                SendKey(field, KeyCode.LeftArrow);
                break;

            case VRKeyAction.Right:
                SendKey(field, KeyCode.RightArrow);
                break;

            case VRKeyAction.Clear:
                field.text = "";
                field.caretPosition = 0;
                field.ForceLabelUpdate();
                break;

            case VRKeyAction.Escape:
                CloseField();
                break;

            case VRKeyAction.Enter:
                if (isChat)
                {
                    field.onSubmit?.Invoke(field.text);
                    refocusAttempts = 0;
                    field.ActivateInputField();
                    field.MoveTextEnd(false);
                    break;
                }

                var target = field;
                Hide("enter");
                Submit(target);

                clearPending = !Select(null);
                break;
        }
    }

    private void LateUpdate()
    {
        if (!open || field == null || VRPointer.PressFrame != Time.frameCount)
            return;

        if (Time.frameCount - openedFrame < OpenGraceFrames)
            return;

        var target = VRPointer.PressTarget;

        if (target != null)
        {
            var t = target.transform;

            if (panelRect != null && t.IsChildOf(panelRect))
                return;

            if (t.IsChildOf(field.transform))
                return;
        }

        CloseField();
    }

    private void PressNumeric(VRKeyDef def)
    {
        switch (def.Action)
        {
            case VRKeyAction.Char:
                if (numericFresh && def.Character != '-')
                    numericBuffer = "";

                numericFresh = false;

                if (def.Character == '-')
                    numericBuffer = numericBuffer.StartsWith("-")
                        ? numericBuffer.Substring(1)
                        : "-" + numericBuffer;
                else if (def.Character != '.' || !numericBuffer.Contains("."))
                    numericBuffer += def.Character;
                break;

            case VRKeyAction.Backspace:
                numericFresh = false;
                if (numericBuffer.Length > 0)
                    numericBuffer = numericBuffer.Substring(0, numericBuffer.Length - 1);
                break;

            case VRKeyAction.Clear:
                numericBuffer = "";
                break;

            case VRKeyAction.Escape:
                CloseField();
                return;

            case VRKeyAction.Enter:
                var target = field;
                var value = numericBuffer;

                Hide("enter");

                target.SetTextWithoutNotify(value);
                target.onValueChanged?.Invoke(value);
                Submit(target);

                clearPending = !Select(null);
                return;

            default:
                return;
        }

        field.SetTextWithoutNotify(numericBuffer);
        field.MoveTextEnd(false);
    }

    private static string Numeric(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        var text = new System.Text.StringBuilder();

        foreach (var c in value)
            if (char.IsDigit(c) || c == '.' || (c == '-' && text.Length == 0))
                text.Append(c);

        return text.ToString();
    }

    private void CloseField()
    {
        var target = field;

        dismissed = target;

        Hide("closed by the player");

        if (target != null)
            target.DeactivateInputField();

        clearPending = !Select(null);
    }

    private void RefreshLabels()
    {
        for (var i = 0; i < keyLabels.Count && i < keyDefs.Count; i++)
        {
            if (keyLabels[i] != null)
                keyLabels[i].text = LabelFor(keyDefs[i]);

            if (keys[i] == null)
                continue;

            keys[i].locked = keyDefs[i].Action == VRKeyAction.Shift ? shift
                : keyDefs[i].Action == VRKeyAction.CapsLock && caps;
            keys[i].Refresh();
        }
    }

    private string LabelFor(VRKeyDef def)
    {
        if (def.Action == VRKeyAction.Language && layouts != null && layouts.Count > 0)
            return layouts[layoutIndex].Name;

        return def.ResolveLabel(shift, caps);
    }

    private void Place()
    {
        if (panelRect == null)
            return;

        var cam = MainCamera.instance != null ? MainCamera.instance.cam : Camera.main;
        if (cam == null)
            return;

        var head = cam.transform;

        var flat = new Vector3(head.forward.x, 0f, head.forward.z);
        if (flat.sqrMagnitude < 0.0001f)
            flat = Vector3.forward;
        flat.Normalize();

        var position = head.position + flat * Distance + Vector3.down * Drop;

        panelRect.SetPositionAndRotation(position, Quaternion.LookRotation(position - head.position, Vector3.up));
        ApplyScale();
    }

    private void ApplyScale()
    {
        if (panelRect == null || panelRect.sizeDelta.x <= 0f)
            return;

        var width = numeric ? NumpadWidth : KeyboardWidth;
        panelRect.localScale = Vector3.one * (width / panelRect.sizeDelta.x);
    }

    private static void SendCharacter(TMP_InputField target, char character)
    {
        target.ProcessEvent(new Event { type = EventType.KeyDown, character = character });
        target.ForceLabelUpdate();
    }

    private static void SendKey(TMP_InputField target, KeyCode code)
    {
        target.ProcessEvent(new Event { type = EventType.KeyDown, keyCode = code });
        target.ForceLabelUpdate();
    }

    private static void Submit(TMP_InputField target)
    {
        target.onSubmit?.Invoke(target.text);
        target.DeactivateInputField();
    }

    private static float RowWidth(VRKeyDef[] row)
    {
        var width = (row.Length - 1) * Gap;
        foreach (var def in row)
            width += def.Width * Unit;
        return width;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static GameObject FindChild(Transform root, string name)
    {
        foreach (var child in root.GetComponentsInChildren<Transform>(true))
            if (child.name == name)
                return child.gameObject;

        return null;
    }

    private static TMP_FontAsset FindFont()
    {
        foreach (var text in Resources.FindObjectsOfTypeAll<TextMeshProUGUI>())
            if (text != null && text.font != null && text.font != PeakAssets.QuestFont)
                return text.font;

        return null;
    }
}

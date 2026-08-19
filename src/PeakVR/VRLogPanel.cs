using PEAKLib.UI;
using Zorro.Core;
using Zorro.Settings;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PeakVR;

internal class VRLogPanel : MonoBehaviour
{
    private const int VisibleLines = 12;

    private TextMeshProUGUI text;

    private Scrollbar bar;
    private Slider slider;

    private int version = -1;
    private bool atEnd = true;
    private int max;

    public static void Build(RectTransform row)
    {
        var backing = new GameObject("Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        backing.transform.SetParent(row, false);

        var image = backing.GetComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0.45f);
        image.raycastTarget = false;

        var backingRt = (RectTransform)backing.transform;
        backingRt.anchorMin = Vector2.zero;
        backingRt.anchorMax = Vector2.one;
        backingRt.offsetMin = new Vector2(0f, 6f);
        backingRt.offsetMax = new Vector2(-62f, -6f);

        var label = MenuAPI.CreateText("").ParentTo(backingRt).ExpandToParent();
        label.SetColor(Color.white);

        var tmp = label.TextMesh;
        tmp.enableAutoSizing = false;
        tmp.fontSize = 17f;
        tmp.richText = true;
        tmp.alignment = TextAlignmentOptions.TopLeft;
        tmp.enableWordWrapping = false;
        tmp.overflowMode = TextOverflowModes.Truncate;
        tmp.raycastTarget = false;
        tmp.margin = new Vector4(14f, 10f, 14f, 10f);

        var panel = row.gameObject.AddComponent<VRLogPanel>();
        panel.text = tmp;

        panel.bar = ZorroScrollbar(row);
        if (panel.bar != null)
            panel.bar.onValueChanged.AddListener(_ => panel.OnMoved());
        else
        {
            panel.slider = NewSlider(row);
            panel.slider.onValueChanged.AddListener(_ => panel.OnMoved());
        }
    }

    private static Scrollbar ZorroScrollbar(RectTransform row)
    {
        var cell = SingletonAsset<InputCellMapper>.Instance?.EnumSettingCell;
        var dropdown = cell != null ? cell.GetComponentInChildren<TMP_Dropdown>(true) : null;
        var source = dropdown?.template != null
            ? dropdown.template.GetComponentInChildren<Scrollbar>(true)
            : null;

        if (source == null)
        {
            Plugin.Log.LogWarning("[PeakVR] No dropdown scrollbar to copy; using a plain slider for the log");
            return null;
        }

        var copy = Instantiate(source.gameObject, row).GetComponent<Scrollbar>();
        copy.gameObject.name = "LogScroll";
        copy.gameObject.SetActive(true);
        copy.onValueChanged.RemoveAllListeners();
        copy.direction = Scrollbar.Direction.BottomToTop;

        var rt = (RectTransform)copy.transform;
        rt.anchorMin = new Vector2(1f, 0f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 0.5f);
        rt.offsetMin = new Vector2(-52f, 6f);
        rt.offsetMax = new Vector2(-20f, -6f);
        return copy;
    }

    private void OnMoved()
    {
        atEnd = First() >= max;
        Render();
    }

    private int First()
    {
        if (bar != null)
            return Mathf.RoundToInt((1f - bar.value) * max);

        return Mathf.RoundToInt(slider.value);
    }

    private void Update()
    {
        if (version == VRLogBuffer.Version)
            return;

        version = VRLogBuffer.Version;
        max = Mathf.Max(0, VRLogBuffer.Count - VisibleLines);

        if (bar != null)
        {
            bar.size = VRLogBuffer.Count > 0
                ? Mathf.Clamp(VisibleLines / (float)VRLogBuffer.Count, 0.08f, 1f)
                : 1f;

            // Follow the tail unless the bar has been pulled up to look at something older.
            if (atEnd)
                bar.SetValueWithoutNotify(0f);
        }
        else
        {
            slider.maxValue = max;
            if (atEnd)
                slider.SetValueWithoutNotify(max);
        }

        Render();
    }

    private void Render()
    {
        if (text != null)
            text.text = VRLogBuffer.Window(First(), VisibleLines);
    }

    private static Slider NewSlider(RectTransform row)
    {
        var go = new GameObject("LogScroll", typeof(RectTransform), typeof(Slider));
        var rt = (RectTransform)go.transform;
        rt.SetParent(row, false);
        rt.anchorMin = new Vector2(1f, 0f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 0.5f);
        rt.offsetMin = new Vector2(-56f, 6f);
        rt.offsetMax = new Vector2(-20f, -6f);

        var track = Panel(rt, new Color(0.16f, 0.16f, 0.18f, 0.9f));
        track.rectTransform.anchorMin = Vector2.zero;
        track.rectTransform.anchorMax = Vector2.one;
        track.rectTransform.offsetMin = Vector2.zero;
        track.rectTransform.offsetMax = Vector2.zero;

        var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
        var areaRt = (RectTransform)handleArea.transform;
        areaRt.SetParent(rt, false);
        areaRt.anchorMin = Vector2.zero;
        areaRt.anchorMax = Vector2.one;
        areaRt.offsetMin = new Vector2(0f, 16f);
        areaRt.offsetMax = new Vector2(0f, -16f);

        var handle = Panel(areaRt, new Color(0.85f, 0.85f, 0.88f));
        handle.rectTransform.sizeDelta = new Vector2(0f, 64f);

        var slider = go.GetComponent<Slider>();
        slider.direction = Slider.Direction.TopToBottom;
        slider.wholeNumbers = true;
        slider.minValue = 0f;
        slider.maxValue = 0f;
        slider.targetGraphic = handle;
        slider.handleRect = handle.rectTransform;
        return slider;
    }

    private static Image Panel(RectTransform parent, Color color)
    {
        var go = new GameObject("Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);

        var image = go.GetComponent<Image>();
        image.color = color;
        return image;
    }
}

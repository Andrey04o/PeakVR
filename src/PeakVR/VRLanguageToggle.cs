using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PeakVR;

internal class VRLanguageToggle : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    public static readonly Color Accent = new(0.185f, 0.394f, 0.6226f, 1f);
    private static readonly Color Idle = new(0.10f, 0.11f, 0.13f, 0.95f);
    private static readonly Color Hover = new(0.27f, 0.50f, 0.74f, 1f);
    private static readonly Color Dim = new(0.62f, 0.64f, 0.68f, 1f);

    private string layout;
    private Image background;
    private TextMeshProUGUI label;
    private Image box;
    private GameObject tick;
    private bool over;

    public void Setup(string name, Image image, TextMeshProUGUI text, Image checkBox, GameObject checkMark)
    {
        layout = name;
        background = image;
        label = text;
        box = checkBox;
        tick = checkMark;
        Paint();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        over = true;
        Paint();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        over = false;
        Paint();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        var entry = Plugin.Config?.KeyboardLayoutEntry(layout);
        if (entry == null)
            return;

        entry.Value = !entry.Value;
        Paint();
    }

    private bool Selected => Plugin.Config != null && Plugin.Config.KeyboardLayoutEnabled(layout);

    private void Paint()
    {
        var on = Selected;

        if (background != null)
            background.color = over ? Hover : on ? Accent : Idle;

        if (label != null)
            label.color = on || over ? Color.white : Dim;

        if (box != null)
            box.color = on ? Color.white : new Color(1f, 1f, 1f, 0.25f);

        if (tick != null)
            tick.SetActive(on);
    }
}

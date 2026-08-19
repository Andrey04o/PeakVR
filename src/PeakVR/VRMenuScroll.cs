using UnityEngine;
using UnityEngine.UI;

namespace PeakVR;

internal class VRMenuScroll : MonoBehaviour
{
    private const float Deadzone = 0.2f;
    private const float ScrollSpeed = 1.1f;
    private const float SliderSpeed = 0.5f;

    private static GameObject host;

    public static void Create()
    {
        if (host != null)
            return;

        host = new GameObject("PeakVR MenuScroll");
        Object.DontDestroyOnLoad(host);
        host.AddComponent<VRMenuScroll>();
    }

    private void Update()
    {
        if (!Plugin.VrEnabled || VRControls.TurnStick == null || VRPointer.Canvas == null)
            return;

        var stick = VRControls.TurnStick.ReadValue<Vector2>();
        var target = VRPointer.Target;
        if (target == null)
            return;

        var slider = target.GetComponentInParent<Slider>();
        if (slider != null && slider.isActiveAndEnabled)
        {
            var vertical = slider.direction is Slider.Direction.BottomToTop or Slider.Direction.TopToBottom;
            var input = vertical
                ? (slider.direction == Slider.Direction.TopToBottom ? -stick.y : stick.y)
                : stick.x;

            if (Mathf.Abs(input) < Deadzone)
                return;

            var span = slider.maxValue - slider.minValue;
            var step = (slider.wholeNumbers ? Mathf.Max(span * SliderSpeed, 1f) : span * SliderSpeed)
                * input * Time.unscaledDeltaTime;

            slider.value = Mathf.Clamp(slider.value + step, slider.minValue, slider.maxValue);
            return;
        }

        var y = stick.y;
        if (Mathf.Abs(y) < Deadzone)
            return;

        var bar = target.GetComponentInParent<Scrollbar>();
        if (bar != null && bar.isActiveAndEnabled)
        {
            var vertical = bar.direction is Scrollbar.Direction.BottomToTop or Scrollbar.Direction.TopToBottom;
            var input = vertical
                ? (bar.direction == Scrollbar.Direction.TopToBottom ? -stick.y : stick.y)
                : stick.x;

            if (Mathf.Abs(input) < Deadzone)
                return;

            bar.value = Mathf.Clamp01(bar.value + input * SliderSpeed * Time.unscaledDeltaTime);
            return;
        }

        var scroll = target.GetComponentInParent<ScrollRect>();
        if (scroll == null || !scroll.vertical || scroll.content == null)
            return;

        var viewport = scroll.viewport != null ? scroll.viewport.rect.height : scroll.GetComponent<RectTransform>().rect.height;
        var hidden = Mathf.Max(scroll.content.rect.height - viewport, 1f);
        var travel = viewport / hidden;

        scroll.verticalNormalizedPosition = Mathf.Clamp01(
            scroll.verticalNormalizedPosition + y * travel * ScrollSpeed * Time.unscaledDeltaTime);
    }
}

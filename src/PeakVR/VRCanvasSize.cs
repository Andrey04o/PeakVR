using UnityEngine;
using UnityEngine.UI;

namespace PeakVR;

internal static class VRCanvasSize
{
    private static readonly Vector2 Screen = new(1920f, 1080f);

    public static void Apply(Canvas canvas)
    {
        if (canvas == null || canvas.renderMode != RenderMode.WorldSpace || !canvas.isRootCanvas)
            return;

        RectTransform rt = (RectTransform)canvas.transform;
        Vector2 size = GetSize(canvas.GetComponent<CanvasScaler>());
        if ((rt.sizeDelta - size).sqrMagnitude > 0.01f)
            rt.sizeDelta = size;
    }

    private static Vector2 GetSize(CanvasScaler scaler)
    {
        if (scaler == null)
            return Screen;

        float scale = 1f;
        if (scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize)
        {
            Vector2 reference = scaler.referenceResolution;
            if (reference.x <= 0f || reference.y <= 0f)
                return Screen;

            float width = Screen.x / reference.x;
            float height = Screen.y / reference.y;
            switch (scaler.screenMatchMode)
            {
                case CanvasScaler.ScreenMatchMode.Expand:
                    scale = Mathf.Min(width, height);
                    break;
                case CanvasScaler.ScreenMatchMode.Shrink:
                    scale = Mathf.Max(width, height);
                    break;
                default:
                    float match = scaler.matchWidthOrHeight;
                    scale = Mathf.Pow(2f, Mathf.Lerp(Mathf.Log(width, 2f), Mathf.Log(height, 2f), match));
                    break;
            }
        }
        else if (scaler.uiScaleMode == CanvasScaler.ScaleMode.ConstantPixelSize)
        {
            scale = scaler.scaleFactor;
        }

        if (scale <= 1e-4f)
            return Screen;
        return Screen / scale;
    }
}

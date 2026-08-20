using UnityEngine;

namespace PeakVR;

internal struct VRCanvasState
{
    public RenderMode Mode;
    public Camera Camera;
    public float PlaneDistance;
    public AdditionalCanvasShaderChannels Channels;

    public static VRCanvasState Capture(Canvas canvas) => new()
    {
        Mode = canvas.renderMode,
        Camera = canvas.worldCamera,
        PlaneDistance = canvas.planeDistance,
        Channels = canvas.additionalShaderChannels,
    };

    public void Apply(Canvas canvas)
    {
        if (canvas == null)
            return;

        canvas.renderMode = Mode;
        canvas.worldCamera = Camera;
        canvas.additionalShaderChannels = Channels;
        canvas.planeDistance = PlaneDistance;

        Refresh(canvas);
    }

    public static void Refresh(Canvas canvas)
    {
        if (canvas == null)
            return;

        var distance = canvas.planeDistance;
        canvas.planeDistance = distance + 1f;
        canvas.planeDistance = distance;
    }
}

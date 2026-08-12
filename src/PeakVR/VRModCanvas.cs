using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PeakVR;

internal class VRModCanvas : MonoBehaviour
{
    private const float Distance = 1.5f;
    private const float Scale = 0.001f;
    private const int ScanInterval = 30;

    private static GameObject host;

    private readonly List<Canvas> adopted = new();
    private int frame;

    public static void Create()
    {
        if (host != null)
            return;

        host = new GameObject("PeakVR ModCanvas");
        Object.DontDestroyOnLoad(host);
        host.AddComponent<VRModCanvas>();
        Plugin.Log.LogInfo("[PeakVR] Mod canvas adopter created");
    }

    private void OnEnable() => Application.onBeforeRender += OnBeforeRender;

    private void OnDisable() => Application.onBeforeRender -= OnBeforeRender;

    [BeforeRenderOrder(1000)]
    private void OnBeforeRender()
    {
        if (!Plugin.VrEnabled || adopted.Count == 0)
            return;

        var cam = MainCamera.instance != null ? MainCamera.instance.cam : null;
        if (cam != null)
            Follow(cam);
    }

    private void LateUpdate()
    {
        if (!Plugin.VrEnabled || ++frame % ScanInterval != 0)
            return;

        var cam = MainCamera.instance != null ? MainCamera.instance.cam : null;
        if (cam == null)
            return;

        Scan(cam);
        Refresh(cam);
    }

    private void Follow(Camera cam)
    {
        var head = cam.transform;

        for (var i = adopted.Count - 1; i >= 0; i--)
        {
            var c = adopted[i];
            if (c == null)
            {
                adopted.RemoveAt(i);
                continue;
            }

            if (c.renderMode != RenderMode.WorldSpace)
            {
                c.renderMode = RenderMode.WorldSpace;
                c.worldCamera = cam;
            }

            var rt = (RectTransform)c.transform;
            rt.SetPositionAndRotation(head.TransformPoint(0f, 0f, Distance), head.rotation);
            rt.localScale = Vector3.one * Scale;
        }
    }

    private void Scan(Camera cam)
    {
        foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (c == null || !c.isRootCanvas || adopted.Contains(c))
                continue;
            if (c.renderMode != RenderMode.ScreenSpaceOverlay && c.renderMode != RenderMode.ScreenSpaceCamera)
                continue;
            if (VRCanvasOwner.IsOurs(c) || VRCanvasOwner.IsGame(c))
                continue;

            var owner = VRCanvasOwner.ModOwner(c);
            if (owner == null)
                continue;

            Adopt(c, cam, owner);
        }
    }

    private void Adopt(Canvas c, Camera cam, string owner)
    {
        c.renderMode = RenderMode.WorldSpace;
        c.worldCamera = cam;

        UIOverlay.MakeAlwaysVisible(c, true);
        adopted.Add(c);

        var rt = (RectTransform)c.transform;
        var graphics = c.GetComponentsInChildren<Graphic>(true).Length;

        Plugin.Log.LogInfo($"[PeakVR] Mod canvas '{c.name}' ({owner}) -> VR foreground UI: " +
            $"rect={rt.rect.width}x{rt.rect.height} layer={c.gameObject.layer} " +
            $"active={c.gameObject.activeInHierarchy} enabled={c.enabled} graphics={graphics} " +
            $"sorting={c.sortingOrder} scaler={(c.GetComponent<CanvasScaler>() != null)}");
    }

    private void Refresh(Camera cam)
    {
        foreach (var c in adopted)
        {
            if (c == null)
                continue;

            if (c.worldCamera == null)
                c.worldCamera = cam;

            UIOverlay.MakeAlwaysVisible(c, true);
        }
    }
}

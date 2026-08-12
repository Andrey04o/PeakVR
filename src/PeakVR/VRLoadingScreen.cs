using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace PeakVR;

[HarmonyPatch(typeof(LoadingScreen), "Awake")]
internal static class LoadingScreenVRPatch
{
    [HarmonyPostfix]
    private static void Postfix(LoadingScreen __instance)
    {
        __instance.gameObject.AddComponent<VRLoadingScreen>();
    }
}

[DefaultExecutionOrder(3000)]
internal class VRLoadingScreen : MonoBehaviour
{
    private const float Scale = 0.003f;
    private const float Distance = 3f;

    private const float CoverScale = 0.004f;
    private const float CoverDistance = 0.4f;
    private const int CoverQueue = 3004;

    private LoadingScreen loadingScreen;
    private bool converted;

    private Canvas cover;
    private Image coverFill;
    private Color background = Color.black;
    private bool sampled;

    private void Awake()
    {
        loadingScreen = GetComponent<LoadingScreen>();
    }

    private void LateUpdate()
    {
        if (!Plugin.VrEnabled || loadingScreen == null || loadingScreen.canvas == null)
            return;

        var cam = Camera.main;
        if (cam == null && MainCamera.instance != null)
            cam = MainCamera.instance.cam;
        if (cam == null)
            return;

        var showing = loadingScreen.canvas.enabled;
        var alpha = !showing ? 0f
            : loadingScreen.group != null ? loadingScreen.group.alpha
            : 1f;

        UpdateCover(cam, alpha);

        if (!showing)
            return;

        if (!converted)
        {
            loadingScreen.canvas.renderMode = RenderMode.WorldSpace;
            loadingScreen.canvas.worldCamera = cam;
            UIOverlay.MakeAlwaysVisible(loadingScreen.canvas, true);
            converted = true;
        }

        var head = cam.transform;
        var rt = (RectTransform)loadingScreen.canvas.transform;
        rt.localScale = Vector3.one * Scale;
        rt.position = head.position + head.forward * Distance;
        rt.rotation = head.rotation;
    }

    private void UpdateCover(Camera cam, float alpha)
    {
        if (alpha <= 0.001f)
        {
            if (cover != null && cover.enabled)
                cover.enabled = false;
            return;
        }

        if (!EnsureCover(cam))
            return;

        var color = BackgroundColor();
        color.a = alpha;
        coverFill.color = color;

        if (!cover.enabled)
            cover.enabled = true;
    }

    private bool EnsureCover(Camera cam)
    {
        if (cover != null)
            return true;

        var go = new GameObject("PeakVR LoadingCover");
        go.transform.SetParent(cam.transform, false);
        go.transform.localPosition = new Vector3(0f, 0f, CoverDistance);
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one * CoverScale;

        cover = go.AddComponent<Canvas>();
        cover.renderMode = RenderMode.WorldSpace;
        cover.worldCamera = cam;

        var rt = (RectTransform)cover.transform;
        rt.sizeDelta = new Vector2(2000f, 2000f);

        var fill = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        fill.transform.SetParent(rt, false);

        coverFill = fill.GetComponent<Image>();
        coverFill.raycastTarget = false;

        var frt = coverFill.rectTransform;
        frt.anchorMin = Vector2.zero;
        frt.anchorMax = Vector2.one;
        frt.sizeDelta = Vector2.zero;
        frt.anchoredPosition = Vector2.zero;

        UIOverlay.MakeAlwaysVisible(cover, CoverQueue);
        Plugin.Log.LogInfo($"[PeakVR] Loading screen cover created (colour {BackgroundColor()})");
        return true;
    }

    private Color BackgroundColor()
    {
        if (sampled)
            return background;

        sampled = true;
        background = Color.black;

        Image best = null;
        var bestArea = 0f;

        foreach (var img in loadingScreen.canvas.GetComponentsInChildren<Image>(true))
        {
            if (img == null || img.color.a < 0.1f)
                continue;

            var rect = img.rectTransform.rect;
            var area = rect.width * rect.height;
            if (area <= bestArea)
                continue;

            bestArea = area;
            best = img;
        }

        if (best == null)
            return background;

        background = best.sprite != null ? Sample(best.sprite) * best.color : best.color;
        background.a = 1f;

        Plugin.Log.LogInfo($"[PeakVR] Loading cover colour {background} from '{best.name}' " +
            $"(sprite={(best.sprite != null ? best.sprite.name : "none")})");

        return background;
    }

    private static Color Sample(Sprite sprite)
    {
        RenderTexture rt = null;
        Texture2D readback = null;
        var previous = RenderTexture.active;

        try
        {
            var texture = sprite.texture;
            var region = sprite.textureRect;

            var scale = new Vector2(region.width / texture.width, region.height / texture.height);
            var offset = new Vector2(region.x / texture.width, region.y / texture.height);

            rt = RenderTexture.GetTemporary(8, 8, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(texture, rt, scale, offset);

            RenderTexture.active = rt;
            readback = new Texture2D(8, 8, TextureFormat.RGBA32, false);
            readback.ReadPixels(new Rect(0f, 0f, 8f, 8f), 0, 0);
            readback.Apply();

            var sum = Color.clear;
            var pixels = readback.GetPixels();
            foreach (var p in pixels)
                sum += p;

            return sum / pixels.Length;
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"[PeakVR] Could not sample loading sprite '{sprite.name}': {e.Message}");
            return Color.black;
        }
        finally
        {
            RenderTexture.active = previous;
            if (rt != null)
                RenderTexture.ReleaseTemporary(rt);
            if (readback != null)
                Destroy(readback);
        }
    }
}

using PEAKLib.UI;
using PEAKLib.UI.Elements;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PeakVR;

internal static class VRAboutPanel
{
    private const string GithubUrl = "https://github.com/Andrey04o/PeakVR";
    private const string ItchUrl = "https://andrey04o.itch.io/";

    private static PeakCustomPage page;

    public static void Open()
    {
        try
        {
            if (page == null)
                page = BuildPage();
            page.Open();
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"[PeakVR] About panel unavailable (PEAKLib.UI missing?): {e.Message}");
        }
    }

    private static PeakCustomPage BuildPage()
    {
        var page = MenuAPI.CreatePageWithBackground("About PeakVR");
        page.SelectOnOpen = false;
        page.CloseOnUICancel = true;
        page.CloseOnPause = true;

        if (PeakAssets.Logo != null)
            Picture(page, PeakAssets.Logo, new Vector2(0f, 320f), 150f);
        else
            Label(page, "PeakVR", 320f, 62f, Color.white);

        Label(page, $"Version {Plugin.Version}", 248f, 28f, new Color(0.7f, 0.7f, 0.72f));
        Label(page, "Created by Andrey04o", 188f, 34f, Color.white);
        Label(page, "Forked from LCVR by DaXcess", 150f, 24f, new Color(0.8f, 0.8f, 0.82f));

        LinkButton(page, "GitHub   —   github.com/Andrey04o/PeakVR", 100f, GithubUrl);
        LinkButton(page, "My other games   —   andrey04o.itch.io", 10f, ItchUrl);
        Label(page, "Links open in your desktop web browser.", -50f, 22f, new Color(0.6f, 0.6f, 0.62f));

        var close = MenuAPI.CreateButton("Close");
        close.ParentTo(page.transform);
        Place(close.RectTransform, new Vector2(0f, -135f), new Vector2(320f, 72f));
        close.OnClick(() => page.Close());

        Label(page, "Used Assets", -240f, 28f, Color.white);
        Label(page, "VR controller FBX model — Unity VR Template",
            -288f, 22f, new Color(0.72f, 0.72f, 0.74f));

        return page;
    }

    private static void LinkButton(PeakCustomPage page, string text, float y, string url)
    {
        var button = MenuAPI.CreateButton(text);
        button.ParentTo(page.transform);
        Place(button.RectTransform, new Vector2(0f, y), new Vector2(820f, 74f));
        button.OnClick(() => Application.OpenURL(url));
    }

    private static void Picture(PeakCustomPage page, Sprite sprite, Vector2 pos, float height)
    {
        var go = new GameObject("Picture", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(page.transform, false);

        var img = go.GetComponent<Image>();
        img.sprite = sprite;
        img.preserveAspect = true;
        img.raycastTarget = false;

        var aspect = sprite.rect.height > 0f ? sprite.rect.width / sprite.rect.height : 1f;
        Place(go.GetComponent<RectTransform>(), pos, new Vector2(height * aspect, height));
    }

    private static void Label(PeakCustomPage page, string text, float y, float size, Color color)
    {
        var label = MenuAPI.CreateText(text);
        label.ParentTo(page.transform);
        label.SetColor(color);

        var tmp = label.TextMesh;
        tmp.enableAutoSizing = false;
        tmp.fontSize = size;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableWordWrapping = true;
        tmp.raycastTarget = false;

        Place(label.RectTransform, new Vector2(0f, y), new Vector2(1500f, 200f));
    }

    private static void Place(RectTransform rt, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
    }
}

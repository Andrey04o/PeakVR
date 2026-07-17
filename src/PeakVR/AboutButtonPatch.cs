using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace PeakVR;

[HarmonyPatch(typeof(MainMenu), nameof(MainMenu.Start))]
internal static class AboutButtonPatch
{
    private const string ButtonName = "PeakVR AboutButton";
    private const float Margin = 40f;

    [HarmonyPostfix]
    private static void Postfix(MainMenu __instance)
    {
        var template = __instance.aggrocrabButton != null ? __instance.aggrocrabButton : __instance.landfallButton;

        var selector = Object.FindObjectOfType<MainMenuPageSelector>();
        var parent = selector != null && selector.mainPage != null
            ? selector.mainPage.transform
            : (template != null ? template.transform.parent : null);
        if (parent == null || parent.Find(ButtonName) != null)
            return;

        var go = new GameObject(ButtonName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);

        var img = go.GetComponent<Image>();
        img.color = Color.white;
        if (PeakAssets.AboutButton != null)
        {
            img.sprite = PeakAssets.AboutButton;
            img.preserveAspect = true;
        }

        var button = go.GetComponent<Button>();
        button.targetGraphic = img;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        if (template != null)
        {
            button.transition = Selectable.Transition.ColorTint;
            button.colors = template.colors;
        }
        button.onClick.AddListener(VRAboutPanel.Open);

        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.zero;
        rt.sizeDelta = template != null
            ? ((RectTransform)template.transform).sizeDelta
            : new Vector2(220f, 110f);
        rt.anchoredPosition = new Vector2(Margin, Margin);
        rt.SetAsLastSibling();

        Plugin.Log.LogInfo("[PeakVR] About button added to main menu (bottom-left)");
    }
}

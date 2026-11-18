using HarmonyLib;
using PEAKLib.UI;
using PEAKLib.UI.Elements;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Zorro.Core;

namespace PeakVR;

internal static class VRInitialSetup
{
    private const float CardOffset = 340f;
    private const float ColumnWidth = 380f;

    private static PeakCustomPage page;

    public static bool IsOpen => page != null && page.isOpen;

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
            Plugin.Log.LogWarning($"[PeakVR] Initial setup page unavailable (PEAKLib.UI missing?): {e.Message}");
        }
    }

    private static PeakCustomPage BuildPage()
    {
        PeakCustomPage built = MenuAPI.CreatePageWithBackground("Initial VR Setup");
        VRMenuFx.AttachFade(built.gameObject);

        Canvas canvas = built.GetComponent<Canvas>();
        if (canvas != null)
        {
            canvas.overrideSorting = true;
            canvas.sortingOrder = 900;
        }
        built.SelectOnOpen = false;
        built.CloseOnUICancel = false;
        built.CloseOnPause = false;

        Label(built, "Initial VR setup", new Vector2(0f, 320f), new Vector2(1500f, 140f), 110f,
            TextAlignmentOptions.Center);

        Choice(built, "I'm bad in VR", -CardOffset,
            new[] { "Tunneling on", "Snap turning", "Camera roll off" }, () => Choose(true));
        Choice(built, "I'm good in VR", CardOffset,
            new[] { "Tunneling off", "Smooth turning", "Camera roll on" }, () => Choose(false));

        PeakButton skip = Button(built, "Skip", new Vector2(0f, -360f), new Vector2(300f, 110f));
        skip.OnClick(Skip);

        return built;
    }

    private static void Choice(PeakCustomPage parent, string title, float x, string[] lines,
        UnityEngine.Events.UnityAction click)
    {
        PeakButton button = Button(parent, title, new Vector2(x, 100f), new Vector2(620f, 120f));
        button.OnClick(click);

        for (int i = 0; i < lines.Length; i++)
            Label(parent, lines[i], new Vector2(x, -20f - i * 80f), new Vector2(ColumnWidth, 70f), 52f,
                TextAlignmentOptions.Left);
    }

    private static void Choose(bool beginner)
    {
        Plugin.Config.MovementTunneling.Value = beginner;
        Plugin.Config.SmoothTurn.Value = !beginner;
        Plugin.Config.CameraRoll.Value = !beginner;
        Finish();
        VRCalibration.Open();
        Plugin.Log.LogInfo($"[PeakVR] Initial setup: {(beginner ? "beginner" : "experienced")} comfort preset");
    }

    private static void Skip()
    {
        Finish();
        Plugin.Log.LogInfo("[PeakVR] Initial setup skipped");
    }

    private static void Finish()
    {
        Plugin.Config.InitialSetupDone.Value = true;
        if (page != null)
            page.Close();
    }

    private static PeakButton Button(PeakCustomPage parent, string text, Vector2 position, Vector2 size)
    {
        PeakButton button = MenuAPI.CreateButton(text);
        button.ParentTo(parent.transform);
        Place(button.RectTransform, position, size);

        TextMeshProUGUI tmp = button.Text.TextMesh;
        tmp.enableAutoSizing = false;
        tmp.fontSize = 60f;
        tmp.fontStyle &= ~FontStyles.UpperCase;
        tmp.text = text;
        return button;
    }

    private static void Label(PeakCustomPage parent, string text, Vector2 position, Vector2 size, float fontSize,
        TextAlignmentOptions alignment)
    {
        PeakText label = MenuAPI.CreateText(text);
        label.ParentTo(parent.transform);
        label.SetColor(Color.white);

        TextMeshProUGUI tmp = label.TextMesh;
        tmp.enableAutoSizing = false;
        tmp.fontSize = fontSize;
        tmp.alignment = alignment;
        tmp.enableWordWrapping = false;
        tmp.raycastTarget = false;

        Place(label.RectTransform, position, size);
    }

    private static void Place(RectTransform rt, Vector2 position, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = position;
    }
}

[HarmonyPatch(typeof(MainMenu), nameof(MainMenu.Start))]
internal static class InitialSetupMenuPatch
{
    [HarmonyPostfix]
    private static void Postfix(MainMenu __instance)
    {
        InitialSetupPrompt.Menu = __instance;
    }
}

internal class InitialSetupPrompt : MonoBehaviour
{
    private const float Delay = 1.5f;
    private const float FadedAlpha = 0.01f;

    public static MainMenu Menu;

    private float waited;

    private void Update()
    {
        if (Plugin.Config == null || Plugin.Config.InitialSetupDone.Value || !Plugin.VrEnabled || !CanShow())
        {
            waited = 0f;
            return;
        }

        waited += Time.unscaledDeltaTime;
        if (waited < Delay)
            return;

        waited = 0f;
        if (!VRInitialSetup.IsOpen)
            VRInitialSetup.Open();
    }

    private static bool CanShow()
    {
        if (LoadingScreenHandler.loading || Menu != null || Character.localCharacter == null)
            return false;

        LoadingScreen screen = RetrievableResourceSingleton<LoadingScreenHandler>.Instance._lastActiveLoadingScreen;
        return screen == null || screen.group == null || screen.group.alpha <= FadedAlpha;
    }
}

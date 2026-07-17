using PEAKLib.UI;
using PEAKLib.UI.Elements;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PeakVR;

internal static class VRCalibration
{
    private const float DefaultArmScale = 1.089f;
    private const float MinScale = 0.6f;
    private const float MaxScale = 1.8f;

    private static PeakCustomPage page;

    public static void Register()
    {
        MenuAPI.AddToPauseMenu(BuildPauseEntry);
        Plugin.Log.LogInfo("[PeakVR] Calibration menu registered (pause menu)");
    }

    private static void BuildPauseEntry(Transform parent)
    {
        var pauseMain = parent.GetComponent<PauseMenuMainPage>();
        var button = MenuAPI.CreatePauseMenuButton("VR Calibration");

        if (pauseMain != null && pauseMain.resumeButton != null)
        {
            var resume = pauseMain.resumeButton.transform;
            button.ParentTo(resume.parent);
            button.transform.SetSiblingIndex(resume.GetSiblingIndex() + 1);
        }
        else
        {
            button.ParentTo(parent);
        }

        button.OnClick(() =>
        {
            Open();
            if (pauseMain != null && pauseMain.resumeButton != null)
                pauseMain.resumeButton.onClick.Invoke();
        });
    }

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
            Plugin.Log.LogWarning($"[PeakVR] Calibration page unavailable (PEAKLib.UI missing?): {e.Message}");
        }
    }

    private static PeakCustomPage BuildPage()
    {
        var page = MenuAPI.CreatePageWithBackground("VR Calibration");
        page.SelectOnOpen = false;
        page.CloseOnUICancel = true;
        page.CloseOnPause = true;

        Label(page, "VR Arm Calibration", 400f, 54f, Color.white);
        Label(page,
            "Stand up straight in a T-pose: arms straight out to your sides,\n" +
            "controllers level with your shoulders.",
            315f, 30f, new Color(0.85f, 0.85f, 0.85f));
        Label(page, "Then squeeze BOTH TRIGGERS at once to save.", 245f, 32f, new Color(1f, 0.85f, 0.3f));

        if (PeakAssets.TPose != null)
            Picture(page, PeakAssets.TPose, new Vector2(0f, 45f), 360f);

        var readout = Label(page, "", -175f, 32f, Color.white);
        var current = Label(page, "", -235f, 26f, new Color(0.7f, 0.8f, 1f));
        var status = Label(page, "", -290f, 30f, new Color(0.4f, 1f, 0.5f));

        var reset = MenuAPI.CreateButton("Reset to Default");
        reset.ParentTo(page.transform);
        Place(reset.RectTransform, new Vector2(-180f, -390f), new Vector2(320f, 72f));
        reset.OnClick(() =>
        {
            VRArmIKPatch.ApplyArmScale(DefaultArmScale);
            status.SetText("Reset to default.");
        });

        var close = MenuAPI.CreateButton("Close");
        close.ParentTo(page.transform);
        Place(close.RectTransform, new Vector2(180f, -390f), new Vector2(320f, 72f));
        close.OnClick(() => page.Close());

        var runner = page.gameObject.AddComponent<Runner>();
        runner.Page = page;
        runner.Readout = readout;
        runner.Current = current;
        runner.Status = status;

        return page;
    }

    private static PeakText Label(PeakCustomPage page, string text, float y, float size, Color color)
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

        Place(label.RectTransform, new Vector2(0f, y), new Vector2(1500f, 220f));
        return label;
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

    private static void Place(RectTransform rt, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
    }

    private static float MeasureVirtualArmSpan()
    {
        var c = Character.localCharacter;
        if (c == null || c.refs == null || c.refs.ikLeft == null || c.refs.ikRight == null)
            return 0f;

        var l = c.refs.ikLeft.data;
        var r = c.refs.ikRight.data;
        if (l.root == null || l.mid == null || l.tip == null ||
            r.root == null || r.mid == null || r.tip == null)
            return 0f;

        var leftArm = Vector3.Distance(l.root.position, l.mid.position) +
                      Vector3.Distance(l.mid.position, l.tip.position);
        var rightArm = Vector3.Distance(r.root.position, r.mid.position) +
                       Vector3.Distance(r.mid.position, r.tip.position);
        var shoulders = Vector3.Distance(l.root.position, r.root.position);

        return leftArm + rightArm + shoulders;
    }

    private static float VirtualArmSpan()
    {
        var span = MeasureVirtualArmSpan();
        if (span > 0.1f)
        {
            if (Plugin.Config != null)
                Plugin.Config.VirtualArmSpan.Value = span;
            return span;
        }

        return Plugin.Config != null ? Plugin.Config.VirtualArmSpan.Value : 1.851f;
    }

    private class Runner : MonoBehaviour
    {
        public PeakCustomPage Page;
        public PeakText Readout;
        public PeakText Current;
        public PeakText Status;

        private Transform left;
        private Transform right;
        private bool prevBoth;

        private void Update()
        {
            if (Page == null || !Page.isOpen)
            {
                prevBoth = false;
                return;
            }

            if (!TryControllers())
            {
                Readout.SetText("Controllers not tracked.");
                prevBoth = false;
                return;
            }

            var wingspan = Vector3.Distance(left.position, right.position);
            var span = VirtualArmSpan();
            var scale = Mathf.Clamp(span / Mathf.Max(wingspan, 0.05f), MinScale, MaxScale);

            Readout.SetText($"Wingspan: {wingspan:0.00} m      New Arm Scale: {scale:0.000}");
            Current.SetText($"Current Arm Scale: {VRArmIKPatch.ArmScale:0.000}");

            var both = VRControls.LeftTrigger != null && VRControls.RightTrigger != null
                && VRControls.LeftTrigger.IsPressed() && VRControls.RightTrigger.IsPressed();

            if (both && !prevBoth)
            {
                VRArmIKPatch.ApplyArmScale(scale);
                Status.SetText($"Saved!   Arm Scale = {scale:0.000}");
            }

            prevBoth = both;
        }

        private bool TryControllers()
        {
            if (left == null)
                left = VRHands.Left != null ? VRHands.Left : Find("PeakVR Left Hand");
            if (right == null)
                right = VRHands.Right != null ? VRHands.Right : Find("PeakVR Right Hand");

            return left != null && right != null;
        }

        private static Transform Find(string name)
        {
            var go = GameObject.Find(name);
            return go != null ? go.transform : null;
        }
    }
}

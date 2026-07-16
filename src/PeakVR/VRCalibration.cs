using PEAKLib.UI;
using PEAKLib.UI.Elements;
using TMPro;
using UnityEngine;

namespace PeakVR;

internal static class VRCalibration
{
    private const float DefaultArmScale = 1.089f;
    private const float MinScale = 0.6f;
    private const float MaxScale = 1.8f;

    public static void Register()
    {
        MenuAPI.AddToMainMenu(parent => BuildEntry(parent, true));
        MenuAPI.AddToPauseMenu(parent => BuildEntry(parent, false));
        Plugin.Log.LogInfo("[PeakVR] Calibration menu registered");
    }

    private static void BuildEntry(Transform parent, bool mainMenu)
    {
        var page = BuildPage();
        var button = mainMenu
            ? MenuAPI.CreateMenuButton("VR Calibration")
            : MenuAPI.CreatePauseMenuButton("VR Calibration");
        button.ParentTo(parent).OnClick(() => page.Open());
    }

    private static PeakCustomPage BuildPage()
    {
        var page = MenuAPI.CreatePageWithBackground("VR Calibration");
        page.SelectOnOpen = false;
        page.CloseOnUICancel = true;
        page.CloseOnPause = true;

        Label(page, "VR Arm Calibration", 340f, 54f, Color.white);
        Label(page,
            "Stand up straight in a T-pose: arms straight out to your sides,\n" +
            "controllers level with your shoulders.",
            210f, 30f, new Color(0.85f, 0.85f, 0.85f));
        Label(page, "Then squeeze BOTH TRIGGERS at once to save.", 110f, 32f, new Color(1f, 0.85f, 0.3f));

        var readout = Label(page, "", 10f, 32f, Color.white);
        var current = Label(page, "", -60f, 26f, new Color(0.7f, 0.8f, 1f));
        var status = Label(page, "", -130f, 30f, new Color(0.4f, 1f, 0.5f));

        var reset = MenuAPI.CreateButton("Reset to Default");
        reset.ParentTo(page.transform);
        Place(reset.RectTransform, new Vector2(-180f, -300f), new Vector2(320f, 72f));
        reset.OnClick(() =>
        {
            VRArmIKPatch.ApplyArmScale(DefaultArmScale);
            status.SetText("Reset to default.");
        });

        var close = MenuAPI.CreateButton("Close");
        close.ParentTo(page.transform);
        Place(close.RectTransform, new Vector2(180f, -300f), new Vector2(320f, 72f));
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

        Place(label.RectTransform, new Vector2(0f, y), new Vector2(1500f, 220f));
        return label;
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

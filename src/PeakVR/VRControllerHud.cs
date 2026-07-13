using UnityEngine;

namespace PeakVR;

[DefaultExecutionOrder(1250)]
internal class VRControllerHud : MonoBehaviour
{
    private const float Scale = 0.0007f;
    private const float ItemSpacing = 90f;

    private static readonly Vector3 LeftPos = new(0f, 0.03f, -0.06f);
    private static readonly Vector3 RightPos = new(0f, 0.03f, -0.06f);
    private static readonly Vector3 LeftEuler = new(0f, 90f, 90f);
    private static readonly Vector3 RightEuler = new(0f, -90f, -90f);

    private Canvas left;
    private Canvas right;
    private bool moved;

    private void LateUpdate()
    {
        if (!Plugin.VrEnabled || VRHands.Left == null || VRHands.Right == null)
            return;

        var gui = GUIManager.instance;
        if (gui == null || gui.staminaCanvasGroup == null || gui.items == null || gui.items.Length == 0)
            return;

        if (!moved || gui.staminaCanvasGroup.transform.parent != left.transform)
            MoveHud(gui);

        UpdateBackface();
    }

    private void MoveHud(GUIManager gui)
    {
        EnsureCanvases();

        Center(gui.staminaCanvasGroup.transform, left, Vector2.zero);

        var startX = -(gui.items.Length - 1) * ItemSpacing * 0.5f;
        for (var i = 0; i < gui.items.Length; i++)
        {
            if (gui.items[i] != null)
                Center(gui.items[i].transform, right, new Vector2(startX + i * ItemSpacing, 0f));
        }

        if (gui.backpack != null)
            Center(gui.backpack.transform, right, new Vector2(startX + gui.items.Length * ItemSpacing, 0f));
        if (gui.temporaryItem != null)
            Center(gui.temporaryItem.transform, right, new Vector2(startX - ItemSpacing, 0f));

        UIOverlay.MakeAlwaysVisible(left, true);
        UIOverlay.MakeAlwaysVisible(right, true);

        moved = true;
        Plugin.Log.LogInfo("[PeakVR] HUD moved onto controllers");
    }

    private void UpdateBackface()
    {
        if (MainCamera.instance == null)
            return;

        var camPos = MainCamera.instance.cam.transform.position;
        SetVisible(left, camPos);
        SetVisible(right, camPos);
    }

    private static void SetVisible(Canvas canvas, Vector3 camPos)
    {
        if (canvas == null)
            return;

        var facing = Vector3.Dot(canvas.transform.forward, camPos - canvas.transform.position) < 0f;
        if (canvas.enabled != facing)
            canvas.enabled = facing;
    }

    private void EnsureCanvases()
    {
        if (left == null)
            left = MakeCanvas("PeakVR LeftHUD", VRHands.Left, "LeftHUDAnchor", LeftPos, LeftEuler);
        if (right == null)
            right = MakeCanvas("PeakVR RightHUD", VRHands.Right, "RightHUDAnchor", RightPos, RightEuler);
    }

    private static Canvas MakeCanvas(string name, Transform hand, string anchorName, Vector3 fallbackPos, Vector3 fallbackEuler)
    {
        var go = new GameObject(name);

        var anchor = FindAnchor(hand, anchorName);
        if (anchor != null)
        {
            go.transform.SetParent(anchor, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
        }
        else
        {
            go.transform.SetParent(hand, false);
            go.transform.localPosition = fallbackPos;
            go.transform.localRotation = Quaternion.Euler(fallbackEuler);
        }

        go.transform.localScale = Vector3.one * Scale;

        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = MainCamera.instance != null ? MainCamera.instance.cam : Camera.main;

        var rt = (RectTransform)canvas.transform;
        rt.sizeDelta = new Vector2(500f, 220f);
        return canvas;
    }

    private static Transform FindAnchor(Transform hand, string anchorName)
    {
        foreach (var t in hand.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == anchorName || t.name == "HUDAnchor")
                return t;
        }
        return null;
    }

    private static void Center(Transform t, Canvas canvas, Vector2 pos)
    {
        t.SetParent(canvas.transform, false);
        if (t is RectTransform rt)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.identity;
        }
    }
}

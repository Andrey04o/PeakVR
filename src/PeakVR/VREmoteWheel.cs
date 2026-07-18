using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PeakVR;

// T6: a button on the LEFT wrist canvas (pressed by the RIGHT controller) opens the emote wheel.
// Tap it -> wheel opens (we hold emoteIsPressed) -> point the right controller at a slice -> pull the
// right trigger -> we release emoteIsPressed and the game plays the hovered emote and closes.
[DefaultExecutionOrder(1260)]
internal class VREmoteWheel : MonoBehaviour
{
    private const int EmoteLayer = 7;
    private const float MaxDistance = 1.5f;
    private const float HoverScale = 1.15f;

    public static bool EmoteActive { get; private set; }
    public static bool RightTriggerConsumed { get; private set; }

    private RectTransform button;
    private Collider buttonCollider;
    private LineRenderer line;
    private Material lineMat;

    private void LateUpdate()
    {
        RightTriggerConsumed = false;

        if (!Plugin.VrEnabled || VRHands.Right == null)
            return;

        EnsureButton();
        if (buttonCollider == null)
            return;

        if (EmoteActive)
        {
            RightTriggerConsumed = true;
            button.localScale = Vector3.one;
            HideLine();

            var ch = Character.localCharacter;
            if (ch == null || ch.data.fullyPassedOut ||
                (VRControls.RightTrigger != null && VRControls.RightTrigger.WasPressedThisFrame()))
                EmoteActive = false;
            return;
        }

        var canOpen = VRControllerHud.LeftHudCanvas != null && VRControllerHud.LeftHudCanvas.enabled
            && VRPointer.Canvas == null && Character.localCharacter != null
            && !Character.localCharacter.data.fullyPassedOut;

        if (!canOpen)
        {
            button.localScale = Vector3.one;
            HideLine();
            return;
        }

        var hovering = Physics.Raycast(VRHands.Right.position, VRHands.Right.forward, out var hit, MaxDistance,
            1 << EmoteLayer, QueryTriggerInteraction.Collide);

        button.localScale = hovering ? Vector3.one * HoverScale : Vector3.one;
        UpdateLine(hovering, hit);

        if (hovering)
        {
            RightTriggerConsumed = true;
            if (VRControls.RightTrigger != null && VRControls.RightTrigger.WasPressedThisFrame())
                EmoteActive = true;
        }
    }

    private void UpdateLine(bool hovering, RaycastHit hit)
    {
        EnsureLine();

        var show = VRLine.ShouldShow(Plugin.Config.HudLine.Value, hovering);
        line.enabled = show;
        if (!show)
            return;

        var origin = VRHands.Right.position;
        var dir = VRHands.Right.forward;
        line.SetPosition(0, origin);
        line.SetPosition(1, hovering ? hit.point : origin + dir * MaxDistance);

        var col = hovering ? VRLine.CharacterColor() : new Color(0.9f, 0.9f, 0.95f);
        if (lineMat.HasProperty("_BaseColor"))
            lineMat.SetColor("_BaseColor", col);
        else
            lineMat.color = col;
    }

    private void HideLine()
    {
        if (line != null)
            line.enabled = false;
    }

    private void EnsureLine()
    {
        if (line != null)
            return;

        var go = new GameObject("PeakVR EmoteLine") { layer = VRLayers.UI };
        go.transform.SetParent(VRHands.Right, false);

        line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.widthMultiplier = 0.004f;
        line.numCapVertices = 4;
        line.positionCount = 2;

        var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
        lineMat = new Material(shader);
        line.material = lineMat;
        line.enabled = false;
    }

    private void EnsureButton()
    {
        if (button != null || VRControllerHud.LeftHudCanvas == null)
            return;

        var go = new GameObject("PeakVR EmoteButton");
        button = go.AddComponent<RectTransform>();
        button.SetParent(VRControllerHud.LeftHudCanvas.transform, false);
        button.sizeDelta = new Vector2(150f, 150f);
        button.anchoredPosition = new Vector2(0f, -170f);

        var img = go.AddComponent<Image>();
        if (PeakAssets.EmoteButton != null)
        {
            img.sprite = PeakAssets.EmoteButton;
            img.color = Color.white;
            img.preserveAspect = true;
        }
        else
        {
            img.color = new Color(1f, 0.78f, 0.2f);

            var labelGo = new GameObject("Label");
            var labelRt = labelGo.AddComponent<RectTransform>();
            labelRt.SetParent(button, false);
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = Vector2.zero;
            labelRt.offsetMax = Vector2.zero;

            var label = labelGo.AddComponent<TextMeshProUGUI>();
            label.text = "EMOTE";
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.black;
            label.fontSize = 42f;
            var font = FindObjectOfType<TextMeshProUGUI>();
            if (font != null && font != label && font.font != null)
                label.font = font.font;
        }

        var colGo = new GameObject("EmoteCollider") { layer = EmoteLayer };
        colGo.transform.SetParent(button, false);
        colGo.transform.localPosition = Vector3.zero;
        colGo.transform.localRotation = Quaternion.identity;
        colGo.transform.localScale = Vector3.one;
        var box = colGo.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(150f, 150f, 40f);
        buttonCollider = box;

        UIOverlay.MakeAlwaysVisible(VRControllerHud.LeftHudCanvas, UIOverlay.HandQueue);
        VRLayers.HideFromMirror(go, EmoteLayer);
        Plugin.Log.LogInfo("[PeakVR] Emote button created on left wrist canvas");
    }
}

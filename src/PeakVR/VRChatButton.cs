using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PeakVR;

// A second left-wrist button, next to the emote button, that opens PeakTextChat's input field.
// Same interaction: point the RIGHT controller at it and pull the right trigger. Only appears when
// PeakTextChat is installed. Typing still needs a real keyboard - there is no VR keyboard.
[DefaultExecutionOrder(1265)]
internal class VRChatButton : MonoBehaviour
{
    private const int ButtonLayer = 7;
    private const float MaxDistance = 1.5f;
    private const float HoverScale = 1.15f;
    private static readonly Vector2 Position = new(190f, -170f);
    private static readonly Vector2 Size = new(150f, 150f);

    public static bool RightTriggerConsumed { get; private set; }

    private RectTransform button;
    private Collider buttonCollider;

    private void LateUpdate()
    {
        RightTriggerConsumed = false;

        if (!Plugin.VrEnabled || VRHands.Right == null || !PeakTextChatPatch.Available)
            return;

        EnsureButton();
        if (buttonCollider == null)
            return;

        var usable = VRControllerHud.LeftHudCanvas != null && VRControllerHud.LeftHudCanvas.enabled
            && VRPointer.Canvas == null && !VREmoteWheel.EmoteActive
            && Character.localCharacter != null && !Character.localCharacter.data.fullyPassedOut;

        if (!usable)
        {
            button.localScale = Vector3.one;
            return;
        }

        var hovering = Physics.Raycast(VRHands.Right.position, VRHands.Right.forward, out var hit, MaxDistance,
            1 << ButtonLayer, QueryTriggerInteraction.Collide) && hit.collider == buttonCollider;

        button.localScale = hovering ? Vector3.one * HoverScale : Vector3.one;

        if (!hovering)
            return;

        RightTriggerConsumed = true;

        if (VRControls.RightTrigger != null && VRControls.RightTrigger.WasPressedThisFrame())
            PeakTextChatPatch.OpenChat();
    }

    private void EnsureButton()
    {
        if (button != null || VRControllerHud.LeftHudCanvas == null)
            return;

        var go = new GameObject("PeakVR ChatButton");
        button = go.AddComponent<RectTransform>();
        button.SetParent(VRControllerHud.LeftHudCanvas.transform, false);
        button.sizeDelta = Size;
        button.anchoredPosition = Position;

        var img = go.AddComponent<Image>();
        if (PeakAssets.ChatButton != null)
        {
            img.sprite = PeakAssets.ChatButton;
            img.color = Color.white;
            img.preserveAspect = true;
        }
        else
        {
            img.color = new Color(0.35f, 0.68f, 1f);

            var labelGo = new GameObject("Label");
            var labelRt = labelGo.AddComponent<RectTransform>();
            labelRt.SetParent(button, false);
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = Vector2.zero;
            labelRt.offsetMax = Vector2.zero;

            var label = labelGo.AddComponent<TextMeshProUGUI>();
            label.text = "CHAT";
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.black;
            label.fontSize = 42f;

            var font = FindObjectOfType<TextMeshProUGUI>();
            if (font != null && font != label && font.font != null)
                label.font = font.font;
        }

        var colGo = new GameObject("ChatCollider") { layer = ButtonLayer };
        colGo.transform.SetParent(button, false);
        colGo.transform.localPosition = Vector3.zero;
        colGo.transform.localRotation = Quaternion.identity;
        colGo.transform.localScale = Vector3.one;

        var box = colGo.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(Size.x, Size.y, 40f);
        buttonCollider = box;

        UIOverlay.MakeAlwaysVisible(VRControllerHud.LeftHudCanvas, UIOverlay.HandQueue);
        VRLayers.HideFromMirror(go, ButtonLayer);
        Plugin.Log.LogInfo("[PeakVR] Chat button created on left wrist canvas");
    }
}

using System.Collections.Generic;
using UnityEngine;
using Zorro.Core;

namespace PeakVR;

[DefaultExecutionOrder(1250)]
internal class VRControllerHud : MonoBehaviour
{
    private const float Scale = 0.0007f;
    private const float ItemSpacing = 90f;
    private const int HudLayer = 3;
    private const float PointerMaxDistance = 1.5f;
    private const float HoverScale = 1.14f;

    private static readonly Vector3 LeftPos = new(0f, 0.03f, -0.06f);
    private static readonly Vector3 RightPos = new(0f, 0.03f, -0.06f);
    private static readonly Vector3 LeftEuler = new(0f, 90f, 90f);
    private static readonly Vector3 RightEuler = new(0f, -90f, -90f);

    public static bool LeftTriggerConsumed { get; private set; }

    internal static Canvas LeftHudCanvas;

    private Canvas left;
    private Canvas right;
    private bool moved;

    private readonly List<CellTarget> targets = new();
    private LineRenderer pointer;
    private Material pointerMat;
    private Transform hoveredCell;

    private struct CellTarget
    {
        public Collider collider;
        public Transform cell;
        public byte slot;
    }

    private void LateUpdate()
    {
        LeftTriggerConsumed = false;

        if (!Plugin.VrEnabled || VRHands.Left == null || VRHands.Right == null)
            return;

        var gui = GUIManager.instance;
        if (gui == null || gui.staminaCanvasGroup == null || gui.items == null || gui.items.Length == 0)
            return;

        if (!moved || gui.staminaCanvasGroup.transform.parent != left.transform)
            MoveHud(gui);

        UpdateBackface();
        UpdateSelection();
    }

    private void MoveHud(GUIManager gui)
    {
        EnsureCanvases();
        ClearTargets();

        Center(gui.staminaCanvasGroup.transform, left, Vector2.zero);

        var startX = -(gui.items.Length - 1) * ItemSpacing * 0.5f;
        for (var i = 0; i < gui.items.Length; i++)
        {
            if (gui.items[i] != null)
            {
                Center(gui.items[i].transform, right, new Vector2(startX + i * ItemSpacing, 0f));
                RegisterCell(gui.items[i].transform, (byte)i);
            }
        }

        if (gui.backpack != null)
        {
            Center(gui.backpack.transform, right, new Vector2(startX + gui.items.Length * ItemSpacing, 0f));
            RegisterCell(gui.backpack.transform, 3);
        }

        if (gui.temporaryItem != null)
        {
            Center(gui.temporaryItem.transform, right, new Vector2(startX - ItemSpacing, 0f));
            RegisterCell(gui.temporaryItem.transform, 250);
        }

        UIOverlay.MakeAlwaysVisible(left, UIOverlay.HandQueue);
        UIOverlay.MakeAlwaysVisible(right, UIOverlay.HandQueue);

        HideInputPrompts(left);
        HideInputPrompts(right);

        // Keep the wrist HUD out of the airport mirror; preserve the raycast collider layers
        // (HudLayer cells, emote-button layer 7) so pointing at them still works.
        VRLayers.HideFromMirror(left.gameObject, HudLayer, 7);
        VRLayers.HideFromMirror(right.gameObject, HudLayer, 7);

        moved = true;
        Plugin.Log.LogInfo("[PeakVR] HUD moved onto controllers");
    }

    private static void HideInputPrompts(Canvas canvas)
    {
        if (canvas == null)
            return;

        // The moved item cells carry inline keyboard/gamepad button prompts — hide them in VR.
        foreach (var prompt in canvas.GetComponentsInChildren<InLineInputPrompts>(true))
            prompt.gameObject.SetActive(false);
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

    private void UpdateSelection()
    {
        EnsurePointer();

        var local = Character.localCharacter;
        var active = right != null && right.enabled && VRPointer.Canvas == null
            && local != null && !local.data.fullyPassedOut;

        if (!active)
        {
            SetHover(null);
            if (pointer != null)
                pointer.enabled = false;
            return;
        }

        if (VRHands.Left == null)
        {
            SetHover(null);
            pointer.enabled = false;
            return;
        }

        var origin = VRHands.Left.position;
        var dir = VRHands.Left.forward;

        var hasHit = Physics.Raycast(origin, dir, out var rayHit, PointerMaxDistance, 1 << HudLayer,
            QueryTriggerInteraction.Collide);

        Transform hitCell = null;
        byte hitSlot = 0;
        if (hasHit)
        {
            foreach (var t in targets)
            {
                if (t.collider == rayHit.collider)
                {
                    hitCell = t.cell;
                    hitSlot = t.slot;
                    break;
                }
            }
        }

        var onCell = hitCell != null;

        var showLine = VRLine.ShouldShow(Plugin.Config.HudLine.Value, onCell);
        pointer.enabled = showLine;
        if (showLine)
        {
            pointer.SetPosition(0, origin);
            pointer.SetPosition(1, onCell ? rayHit.point : origin + dir * PointerMaxDistance);
            SetPointerColor(onCell);
        }

        SetHover(hitCell);

        if (onCell)
        {
            LeftTriggerConsumed = true;
            if (VRControls.LeftTrigger != null && VRControls.LeftTrigger.WasPressedThisFrame())
                SelectSlot(hitSlot);
        }
    }

    private void SetHover(Transform cell)
    {
        if (hoveredCell == cell)
            return;

        if (hoveredCell != null)
            hoveredCell.localScale = Vector3.one;

        hoveredCell = cell;

        if (hoveredCell != null)
            hoveredCell.localScale = Vector3.one * HoverScale;
    }

    private static void SelectSlot(byte slot)
    {
        var ch = Character.localCharacter;
        if (ch == null || ch.refs == null || ch.refs.items == null)
            return;

        var items = ch.refs.items;
        if (items.currentSelectedSlot.IsSome && items.currentSelectedSlot.Value == slot)
            items.EquipSlot(Optionable<byte>.None);
        else
            items.EquipSlot(Optionable<byte>.Some(slot));

        if (slot == 3 && ch.data != null && ch.data.carriedPlayer != null && ch.refs.carriying != null)
            ch.refs.carriying.Drop(ch.data.carriedPlayer);
    }

    private void RegisterCell(Transform cell, byte slot)
    {
        if (cell == null)
            return;

        var size = cell is RectTransform rt ? rt.rect.size : new Vector2(80f, 80f);
        if (size.x < 1f) size.x = 80f;
        if (size.y < 1f) size.y = 80f;

        var go = new GameObject("PeakVR HudCollider") { layer = HudLayer };
        go.transform.SetParent(cell, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        var box = go.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(size.x, size.y, 40f);

        targets.Add(new CellTarget { collider = box, cell = cell, slot = slot });
    }

    private void ClearTargets()
    {
        foreach (var t in targets)
        {
            if (t.cell != null)
                t.cell.localScale = Vector3.one;
            if (t.collider != null)
                Destroy(t.collider.gameObject);
        }
        targets.Clear();
        hoveredCell = null;
    }

    private void EnsurePointer()
    {
        if (pointer != null)
            return;

        var go = new GameObject("PeakVR HudPointer") { layer = VRLayers.UI };
        go.transform.SetParent(VRHands.Left, false);

        pointer = go.AddComponent<LineRenderer>();
        pointer.useWorldSpace = true;
        pointer.widthMultiplier = 0.004f;
        pointer.numCapVertices = 4;
        pointer.positionCount = 2;

        var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
        pointerMat = new Material(shader);
        pointer.material = pointerMat;
        pointer.enabled = false;
    }

    private void SetPointerColor(bool hover)
    {
        var col = hover ? VRLine.CharacterColor() : new Color(0.9f, 0.9f, 0.95f);
        if (pointerMat.HasProperty("_BaseColor"))
            pointerMat.SetColor("_BaseColor", col);
        else
            pointerMat.color = col;
    }

    private void EnsureCanvases()
    {
        if (left == null)
        {
            left = MakeCanvas("PeakVR LeftHUD", VRHands.Left, "LeftHUDAnchor", LeftPos, LeftEuler);
            LeftHudCanvas = left;
        }
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

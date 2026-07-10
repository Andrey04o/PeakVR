using UnityEngine;

namespace PeakVR;

[DefaultExecutionOrder(1200)]
internal class VRInteractPrompt : MonoBehaviour
{
    private const float Scale = 0.0013f;
    private const float ForwardOffset = 0.05f;
    private const float UpOffset = 0.07f;

    private Canvas canvas;
    private RectTransform canvasRt;

    private Transform interactName;
    private Transform interactPrompts;
    private Transform progress;
    private UI_UseItemProgress progressComp;

    private void LateUpdate()
    {
        if (VRHands.Right == null)
            return;

        var gui = GUIManager.instance;
        if (gui == null)
            return;

        if (gui.reticleDefault != null && gui.reticleDefault.activeSelf)
            gui.reticleDefault.SetActive(false);

        EnsureCanvas();

        if (progressComp == null)
            progressComp = Object.FindObjectOfType<UI_UseItemProgress>(true);

        Adopt(ref progress, progressComp != null ? progressComp.transform : null, new Vector2(0f, 155f));
        Adopt(ref interactName, gui.interactName != null ? gui.interactName.transform : null, new Vector2(0f, 70f));
        Adopt(ref interactPrompts, PromptContainer(gui), new Vector2(0f, -70f));

        PlaceCanvas();
    }

    private void EnsureCanvas()
    {
        if (canvas != null)
            return;

        var go = new GameObject("PeakVR Interact Prompt");
        canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = MainCamera.instance != null ? MainCamera.instance.cam : Camera.main;
        canvas.sortingOrder = 50;

        canvasRt = (RectTransform)go.transform;
        canvasRt.sizeDelta = new Vector2(500f, 400f);
        canvasRt.localScale = Vector3.one * Scale;
    }

    private void Adopt(ref Transform held, Transform target, Vector2 anchoredPos)
    {
        if (target == null)
            return;

        if (target.parent != canvasRt)
        {
            target.SetParent(canvasRt, false);
            if (target is RectTransform rt)
            {
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = anchoredPos;
            }
        }

        held = target;
    }

    private static Transform PromptContainer(GUIManager gui)
    {
        if (gui.interactPromptPrimary != null && gui.interactPromptPrimary.transform.parent != null)
            return gui.interactPromptPrimary.transform.parent;
        return null;
    }

    private void PlaceCanvas()
    {
        var cam = MainCamera.instance != null ? MainCamera.instance.cam : Camera.main;
        if (cam == null)
            return;

        var hand = VRHands.Right;
        var head = cam.transform;

        var pos = hand.position + Vector3.up * UpOffset + hand.forward * ForwardOffset;
        canvasRt.position = pos;

        var dir = pos - head.position;
        if (dir.sqrMagnitude < 0.0001f)
            dir = head.forward;

        canvasRt.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
    }
}

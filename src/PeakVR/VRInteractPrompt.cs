using UnityEngine;

namespace PeakVR;

[DefaultExecutionOrder(1200)]
internal class VRInteractPrompt : MonoBehaviour, IVRRestorable
{
    private const float Scale = 0.0013f;
    private const float ForwardOffset = 0.05f;
    private const float UpOffset = 0.07f;

    private const int RefreshInterval = 30;

    private Canvas canvas;
    private RectTransform canvasRt;
    private int frame;

    private Transform interactName;
    private Transform interactPrompts;
    private Transform progress;
    private UI_UseItemProgress progressComp;

    private readonly VRRestore restore = new();
    private bool reticleHidden;

    private void LateUpdate()
    {
        if (!Plugin.VrEnabled || VRHands.Right == null)
            return;

        var gui = GUIManager.instance;
        if (gui == null)
            return;

        if (gui.reticleDefault != null && gui.reticleDefault.activeSelf)
        {
            reticleHidden = true;
            gui.reticleDefault.SetActive(false);
        }

        EnsureCanvas();

        if (progressComp == null)
            progressComp = Object.FindObjectOfType<UI_UseItemProgress>(true);

        var progressT = progressComp != null ? progressComp.transform : null;
        var nameT = gui.interactName != null ? gui.interactName.transform : null;
        var promptsT = PromptContainer(gui);

        restore.Record(progressT);
        restore.Record(nameT);
        restore.Record(promptsT);

        Adopt(ref progress, progressT, new Vector2(0f, 155f));
        Adopt(ref interactName, nameT, new Vector2(0f, 70f));
        Adopt(ref interactPrompts, promptsT, new Vector2(0f, -70f));

        if (++frame % RefreshInterval == 0)
        {
            UIOverlay.SweepForegroundLayer(canvas);
            UIOverlay.MakeAlwaysVisible(canvas, true);
        }

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

    public void RestoreForFlat()
    {
        var count = restore.RestoreAll();

        var gui = GUIManager.instance;
        if (reticleHidden && gui != null && gui.reticleDefault != null)
            gui.reticleDefault.SetActive(true);
        reticleHidden = false;

        interactName = null;
        interactPrompts = null;
        progress = null;

        if (canvas != null)
            Destroy(canvas.gameObject);
        canvas = null;
        canvasRt = null;

        if (count > 0)
            Plugin.Log.LogInfo($"[PeakVR] Interaction prompts returned to the screen ({count})");
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

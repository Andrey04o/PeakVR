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

    private Canvas leftCanvas;
    private RectTransform leftCanvasRt;
    private GameObject leftName;
    private GameObject leftPrompts;
    private GameObject leftPrimary;
    private GameObject leftSecondary;
    private TMPro.TextMeshProUGUI leftNameText;
    private TMPro.TextMeshProUGUI leftPrimaryText;
    private TMPro.TextMeshProUGUI leftSecondaryText;
    private IInteractible leftHovered;

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

        bool isRefresh = ++frame % RefreshInterval == 0;
        if (isRefresh)
        {
            UIOverlay.SweepForegroundLayer(canvas);
            UIOverlay.MakeAlwaysVisible(canvas, true);
        }

        PlaceCanvas(canvasRt, VRHands.Right);
        UpdateLeft(gui, isRefresh);
    }

    private void UpdateLeft(GUIManager gui, bool isRefresh)
    {
        IInteractible target = VRHandItems.Enabled ? HandInteractPatch.LeftHover : null;
        IInteractible right = Interaction.instance != null ? Interaction.instance.currentHovered : null;

        if (!Exists(target) || VRHands.Left == null)
            target = null;

        SetLeftHover(ReferenceEquals(target, right) ? null : target, right);

        if (target == null || !EnsureLeftCanvas(gui))
        {
            if (leftCanvas != null && leftCanvas.gameObject.activeSelf)
                leftCanvas.gameObject.SetActive(false);
            return;
        }

        if (!leftCanvas.gameObject.activeSelf)
            leftCanvas.gameObject.SetActive(true);

        FillLeft(target);

        if (isRefresh)
        {
            UIOverlay.SweepForegroundLayer(leftCanvas);
            UIOverlay.MakeAlwaysVisible(leftCanvas, true);
        }

        PlaceCanvas(leftCanvasRt, VRHands.Left);
    }

    private void SetLeftHover(IInteractible target, IInteractible right)
    {
        if (ReferenceEquals(target, leftHovered))
            return;

        if (Exists(leftHovered) && !ReferenceEquals(leftHovered, right))
            leftHovered.HoverExit();

        leftHovered = target;

        if (Exists(target))
            target.HoverEnter();
    }

    private static bool Exists(IInteractible interactible)
    {
        return interactible is Component component && component != null;
    }

    private bool EnsureLeftCanvas(GUIManager gui)
    {
        if (leftCanvas != null)
            return leftPrimary != null && leftPrimaryText != null;

        Transform nameSource = gui.interactName != null ? gui.interactName.transform : null;
        Transform promptsSource = PromptContainer(gui);
        if (nameSource == null || promptsSource == null || gui.interactNameText == null
            || gui.interactPromptText == null)
            return false;

        GameObject go = new GameObject("PeakVR Interact Prompt Left");
        leftCanvas = go.AddComponent<Canvas>();
        leftCanvas.renderMode = RenderMode.WorldSpace;
        leftCanvas.worldCamera = MainCamera.instance != null ? MainCamera.instance.cam : Camera.main;
        leftCanvas.sortingOrder = 50;

        leftCanvasRt = (RectTransform)go.transform;
        leftCanvasRt.sizeDelta = new Vector2(500f, 400f);
        leftCanvasRt.localScale = Vector3.one * Scale;

        leftName = Clone(nameSource, new Vector2(0f, 70f));
        leftPrompts = Clone(promptsSource, new Vector2(0f, -70f));

        leftNameText = FindCopy<TMPro.TextMeshProUGUI>(nameSource, leftName.transform, gui.interactNameText);
        leftPrimary = FindCopy(promptsSource, leftPrompts.transform, gui.interactPromptPrimary);
        leftPrimaryText = FindCopy<TMPro.TextMeshProUGUI>(promptsSource, leftPrompts.transform, gui.interactPromptText);
        leftSecondary = FindCopy(promptsSource, leftPrompts.transform, gui.interactPromptSecondary);
        leftSecondaryText = FindCopy<TMPro.TextMeshProUGUI>(promptsSource, leftPrompts.transform,
            gui.secondaryInteractPromptText);

        UIOverlay.SweepForegroundLayer(leftCanvas);
        UIOverlay.MakeAlwaysVisible(leftCanvas, true);

        return leftPrimary != null && leftPrimaryText != null;
    }

    private GameObject Clone(Transform source, Vector2 anchoredPos)
    {
        GameObject copy = Instantiate(source.gameObject, leftCanvasRt, false);
        if (copy.transform is RectTransform rt)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.localScale = Vector3.one;
        }
        return copy;
    }

    private static GameObject FindCopy(Transform sourceRoot, Transform copyRoot, GameObject source)
    {
        Transform found = source != null ? FindCopy(sourceRoot, copyRoot, source.transform) : null;
        return found != null ? found.gameObject : null;
    }

    private static T FindCopy<T>(Transform sourceRoot, Transform copyRoot, Component source) where T : Component
    {
        Transform found = source != null ? FindCopy(sourceRoot, copyRoot, source.transform) : null;
        return found != null ? found.GetComponent<T>() : null;
    }

    private static Transform FindCopy(Transform sourceRoot, Transform copyRoot, Transform source)
    {
        string path = "";
        Transform current = source;

        while (current != null && current != sourceRoot)
        {
            path = path.Length == 0 ? current.name : current.name + "/" + path;
            current = current.parent;
        }

        if (current == null)
            return null;

        return path.Length == 0 ? copyRoot : copyRoot.Find(path);
    }

    private void FillLeft(IInteractible target)
    {
        Character local = Character.localCharacter;
        bool isName = true;
        bool isPrimary = true;
        bool isSecondary = false;
        string name = "";

        if (target is Item item)
        {
            name = item.GetItemName();
        }
        else if (target is CharacterInteractible character)
        {
            isName = false;
            isPrimary = local != null && character.IsPrimaryInteractible(local);
            isSecondary = local != null && character.IsSecondaryInteractible(local);
            if (isSecondary && leftSecondaryText != null)
                leftSecondaryText.text = character.GetSecondaryInteractionText();
        }
        else
        {
            name = target.GetName();
        }

        leftPrimaryText.text = target.GetInteractionText();
        if (leftNameText != null && isName)
            leftNameText.text = name;

        SetActive(leftName, isName && leftNameText != null);

        foreach (Transform child in leftPrompts.transform)
        {
            bool want = false;
            if (child.gameObject == leftPrimary)
                want = isPrimary;
            else if (child.gameObject == leftSecondary)
                want = isSecondary;

            SetActive(child.gameObject, want);
        }

        if (leftSecondary != null && leftSecondary.transform.parent != leftPrompts.transform)
            SetActive(leftSecondary, isSecondary);
    }

    private static void SetActive(GameObject go, bool value)
    {
        if (go != null && go.activeSelf != value)
            go.SetActive(value);
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

        if (Exists(leftHovered))
            leftHovered.HoverExit();
        leftHovered = null;

        if (leftCanvas != null)
            Destroy(leftCanvas.gameObject);
        leftCanvas = null;
        leftCanvasRt = null;
        leftName = null;
        leftPrompts = null;

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

    private static void PlaceCanvas(RectTransform target, Transform hand)
    {
        var cam = MainCamera.instance != null ? MainCamera.instance.cam : Camera.main;
        if (cam == null || target == null || hand == null)
            return;

        var head = cam.transform;

        var pos = hand.position + Vector3.up * UpOffset + hand.forward * ForwardOffset;
        target.position = pos;

        var dir = pos - head.position;
        if (dir.sqrMagnitude < 0.0001f)
            dir = head.forward;

        target.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
    }
}

using UnityEngine;

namespace PeakVR;

[DefaultExecutionOrder(1200)]
internal class VRInteractPrompt : MonoBehaviour, IVRRestorable
{
    private const float Scale = 0.0013f;
    private const float ForwardOffset = 0.05f;
    private const float UpOffset = 0.07f;
    private const float ConsumeQuietTime = 0.35f;

    private const int RefreshInterval = 30;

    private class HandPrompt
    {
        public Canvas Canvas;
        public RectTransform Rt;
        public GameObject Name;
        public GameObject Prompts;
        public GameObject Primary;
        public GameObject Secondary;
        public TMPro.TextMeshProUGUI NameText;
        public TMPro.TextMeshProUGUI PrimaryText;
        public TMPro.TextMeshProUGUI SecondaryText;
    }

    private Canvas canvas;
    private RectTransform canvasRt;
    private Canvas progressCanvas;
    private RectTransform progressCanvasRt;
    private int frame;

    private Transform interactName;
    private Transform interactPrompts;
    private Transform progress;
    private UI_UseItemProgress progressComp;

    private readonly VRRestore restore = new();
    private bool reticleHidden;

    private HandPrompt rightPrompt;
    private HandPrompt leftPrompt;
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

        Adopt(ref progress, progressT, new Vector2(0f, 155f), progressCanvasRt);
        Adopt(ref interactName, nameT, new Vector2(0f, 70f), canvasRt);
        Adopt(ref interactPrompts, promptsT, new Vector2(0f, -70f), canvasRt);
        HideVanilla(gui);

        bool isRefresh = ++frame % RefreshInterval == 0;
        if (isRefresh)
        {
            UIOverlay.SweepForegroundLayer(canvas);
            UIOverlay.MakeAlwaysVisible(canvas, true);
            UIOverlay.SweepForegroundLayer(progressCanvas);
            UIOverlay.MakeAlwaysVisible(progressCanvas, true);
        }

        PlaceCanvas(canvasRt, VRHands.Right);
        bool isLeftUse = VRItemUse.IsProgressLeft(Character.localCharacter) && VRHands.Left != null;
        PlaceCanvas(progressCanvasRt, isLeftUse ? VRHands.Left : VRHands.Right);

        bool isQuiet = IsQuiet();
        UpdateRight(gui, isRefresh, isQuiet);
        UpdateLeft(gui, isRefresh, isQuiet);
    }

    internal static bool IsQuiet()
    {
        Character local = Character.localCharacter;
        return local != null && local.data != null && Time.time - local.data.lastConsumedItem < ConsumeQuietTime;
    }

    private static void HideVanilla(GUIManager gui)
    {
        SetActive(gui.interactName, false);
        SetActive(gui.interactPromptPrimary, false);
        SetActive(gui.interactPromptSecondary, false);
    }

    private void UpdateRight(GUIManager gui, bool isRefresh, bool isQuiet)
    {
        IInteractible target = Interaction.instance != null ? Interaction.instance.currentHovered : null;
        if (!Exists(target) || isQuiet)
            target = null;

        bool isPiton = IsPitonPrompt();
        if ((target == null && !isPiton) || !EnsurePrompt(gui, ref rightPrompt, "PeakVR Interact Prompt Right"))
        {
            if (rightPrompt != null && rightPrompt.Canvas.gameObject.activeSelf)
                rightPrompt.Canvas.gameObject.SetActive(false);
            return;
        }

        ShowPrompt(rightPrompt, target, isPiton, isRefresh, VRHands.Right);
    }

    private void UpdateLeft(GUIManager gui, bool isRefresh, bool isQuiet)
    {
        IInteractible target = VRHandItems.Enabled ? HandInteractPatch.LeftHover : null;
        IInteractible right = Interaction.instance != null ? Interaction.instance.currentHovered : null;

        if (!Exists(target) || VRHands.Left == null)
            target = null;

        SetLeftHover(ReferenceEquals(target, right) ? null : target, right);

        if (isQuiet)
            target = null;

        if (target == null || !EnsurePrompt(gui, ref leftPrompt, "PeakVR Interact Prompt Left"))
        {
            if (leftPrompt != null && leftPrompt.Canvas.gameObject.activeSelf)
                leftPrompt.Canvas.gameObject.SetActive(false);
            return;
        }

        ShowPrompt(leftPrompt, target, false, isRefresh, VRHands.Left);
    }

    private void ShowPrompt(HandPrompt prompt, IInteractible target, bool isPiton, bool isRefresh, Transform hand)
    {
        if (!prompt.Canvas.gameObject.activeSelf)
            prompt.Canvas.gameObject.SetActive(true);

        Fill(prompt, target, isPiton);

        if (isRefresh)
        {
            UIOverlay.SweepForegroundLayer(prompt.Canvas);
            UIOverlay.MakeAlwaysVisible(prompt.Canvas, true);
        }

        PlaceCanvas(prompt.Rt, hand);
    }

    private static bool IsPitonPrompt()
    {
        Character local = Character.localCharacter;
        return local != null && local.data != null && local.data.climbingSpikeCount > 0 && local.data.isClimbing;
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

    private bool EnsurePrompt(GUIManager gui, ref HandPrompt prompt, string name)
    {
        if (prompt != null && prompt.Canvas != null)
            return prompt.Primary != null && prompt.PrimaryText != null;

        Transform nameSource = gui.interactName != null ? gui.interactName.transform : null;
        Transform promptsSource = PromptContainer(gui);
        if (nameSource == null || promptsSource == null || gui.interactNameText == null
            || gui.interactPromptText == null)
            return false;

        HandPrompt created = new HandPrompt();
        GameObject go = new GameObject(name);
        created.Canvas = go.AddComponent<Canvas>();
        created.Canvas.renderMode = RenderMode.WorldSpace;
        created.Canvas.worldCamera = MainCamera.instance != null ? MainCamera.instance.cam : Camera.main;
        created.Canvas.sortingOrder = 50;

        created.Rt = (RectTransform)go.transform;
        created.Rt.sizeDelta = new Vector2(500f, 400f);
        created.Rt.localScale = Vector3.one * Scale;

        created.Name = Clone(nameSource, new Vector2(0f, 70f), created.Rt);
        created.Prompts = Clone(promptsSource, new Vector2(0f, -70f), created.Rt);

        created.NameText = FindCopy<TMPro.TextMeshProUGUI>(nameSource, created.Name.transform, gui.interactNameText);
        created.Primary = FindCopy(promptsSource, created.Prompts.transform, gui.interactPromptPrimary);
        created.PrimaryText = FindCopy<TMPro.TextMeshProUGUI>(promptsSource, created.Prompts.transform,
            gui.interactPromptText);
        created.Secondary = FindCopy(promptsSource, created.Prompts.transform, gui.interactPromptSecondary);
        created.SecondaryText = FindCopy<TMPro.TextMeshProUGUI>(promptsSource, created.Prompts.transform,
            gui.secondaryInteractPromptText);

        UIOverlay.SweepForegroundLayer(created.Canvas);
        UIOverlay.MakeAlwaysVisible(created.Canvas, true);

        prompt = created;
        return prompt.Primary != null && prompt.PrimaryText != null;
    }

    private static GameObject Clone(Transform source, Vector2 anchoredPos, RectTransform parent)
    {
        GameObject copy = Instantiate(source.gameObject, parent, false);
        if (copy.transform is RectTransform rt)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.localScale = Vector3.one;
        }
        copy.SetActive(true);
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

    private static void Fill(HandPrompt prompt, IInteractible target, bool isPiton)
    {
        Character local = Character.localCharacter;
        bool isName = target != null;
        bool isPrimary = target != null;
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
            if (isSecondary && prompt.SecondaryText != null)
                prompt.SecondaryText.text = character.GetSecondaryInteractionText();
        }
        else if (target is VRBackpackStashTarget)
        {
            isName = false;
        }
        else if (target != null)
        {
            name = target.GetName();
        }

        if (isPiton)
        {
            isSecondary = true;
            if (prompt.SecondaryText != null)
                prompt.SecondaryText.text = LocalizedText.GetText("SETPITON");
        }

        if (target != null)
            prompt.PrimaryText.text = target.GetInteractionText();
        if (prompt.NameText != null && isName)
            prompt.NameText.text = name;

        SetActive(prompt.Name, isName && prompt.NameText != null);

        foreach (Transform child in prompt.Prompts.transform)
        {
            bool want = false;
            if (child.gameObject == prompt.Primary)
                want = isPrimary;
            else if (child.gameObject == prompt.Secondary)
                want = isSecondary;

            SetActive(child.gameObject, want);
        }

        if (prompt.Secondary != null && prompt.Secondary.transform.parent != prompt.Prompts.transform)
            SetActive(prompt.Secondary, isSecondary);
    }

    private static void SetActive(GameObject go, bool value)
    {
        if (go != null && go.activeSelf != value)
            go.SetActive(value);
    }

    private void EnsureCanvas()
    {
        if (canvas == null)
        {
            canvas = CreateCanvas("PeakVR Interact Prompt");
            canvasRt = (RectTransform)canvas.transform;
        }

        if (progressCanvas == null)
        {
            progressCanvas = CreateCanvas("PeakVR Use Progress");
            progressCanvasRt = (RectTransform)progressCanvas.transform;
        }
    }

    private static Canvas CreateCanvas(string name)
    {
        GameObject go = new GameObject(name);
        Canvas created = go.AddComponent<Canvas>();
        created.renderMode = RenderMode.WorldSpace;
        created.worldCamera = MainCamera.instance != null ? MainCamera.instance.cam : Camera.main;
        created.sortingOrder = 50;

        RectTransform rt = (RectTransform)go.transform;
        rt.sizeDelta = new Vector2(500f, 400f);
        rt.localScale = Vector3.one * Scale;
        return created;
    }

    private void Adopt(ref Transform held, Transform target, Vector2 anchoredPos, RectTransform parent)
    {
        if (target == null || parent == null)
            return;

        if (target.parent != parent)
        {
            target.SetParent(parent, false);
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

        if (gui != null)
            gui.RefreshInteractablePrompt();

        interactName = null;
        interactPrompts = null;
        progress = null;

        if (Exists(leftHovered))
            leftHovered.HoverExit();
        leftHovered = null;

        DestroyPrompt(ref leftPrompt);
        DestroyPrompt(ref rightPrompt);

        if (canvas != null)
            Destroy(canvas.gameObject);
        canvas = null;
        canvasRt = null;

        if (progressCanvas != null)
            Destroy(progressCanvas.gameObject);
        progressCanvas = null;
        progressCanvasRt = null;

        if (count > 0)
            Plugin.Log.LogInfo($"[PeakVR] Interaction prompts returned to the screen ({count})");
    }

    private static void DestroyPrompt(ref HandPrompt prompt)
    {
        if (prompt != null && prompt.Canvas != null)
            Destroy(prompt.Canvas.gameObject);
        prompt = null;
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

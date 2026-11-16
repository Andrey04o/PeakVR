using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace PeakVR;

[DefaultExecutionOrder(1500)]
internal class VRItemAim : MonoBehaviour
{
    private const float MaxDist = 80f;
    private const float ReticlePadding = 0.03f;
    private const float ReticleAngularScale = 0.022f;

    private class AimView
    {
        public LineRenderer Line;
        public SpriteRenderer Reticle;
    }

    private Material lineMat;
    private Sprite reticleSprite;
    private AimView right;
    private AimView left;

    private void Awake()
    {
        CreateMaterial();
        right = CreateView("Right");
        left = CreateView("Left");
    }

    private void Update()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null)
            return;

        if (kb.f9Key.wasPressedThisFrame)
            AdjustOffset(new Vector3(-5f, 0f, 0f));
        else if (kb.f10Key.wasPressedThisFrame)
            AdjustOffset(new Vector3(5f, 0f, 0f));
        else if (kb.f11Key.wasPressedThisFrame)
            AdjustOffset(new Vector3(0f, -5f, 0f));
        else if (kb.f12Key.wasPressedThisFrame)
            AdjustOffset(new Vector3(0f, 5f, 0f));
        else if (kb.f2Key.wasPressedThisFrame)
            AdjustOffset(new Vector3(0f, 0f, -5f));
        else if (kb.f3Key.wasPressedThisFrame)
            AdjustOffset(new Vector3(0f, 0f, 5f));
    }

    private static void AdjustOffset(Vector3 delta)
    {
        Character character = Character.localCharacter;
        Item item = character != null && character.data != null ? character.data.currentItem : null;
        VRGripProfile profile = VRGripProfiles.Get(item);
        if (profile == null)
            return;

        profile.RotationHand += delta;
        Plugin.Log.LogInfo($"[PeakVR] Grip rotation offset '{item.name}' = {profile.RotationHand}");
    }

    private void LateUpdate()
    {
        Character character = Character.localCharacter;
        bool canShow = ItemAim.Enabled && VRPointer.Canvas == null && character != null && character.data != null;

        Item rightItem = canShow ? character.data.currentItem : null;
        Item leftItem = canShow && VRHandItems.Enabled ? VRHandItems.LeftItem : null;
        if (leftItem == rightItem)
            leftItem = null;

        bool isShootable = Show(right, rightItem, true);
        isShootable |= Show(left, leftItem, VRItemUse.IsSpike(leftItem));

        if (isShootable)
            HideGameReticle();
    }

    private bool Show(AimView view, Item item, bool isPlacementAllowed)
    {
        bool isShootable = item != null && item.UIData.isShootable;
        bool isPlacement = isPlacementAllowed && item != null && IsPlacementItem(item);

        if (!(isShootable || isPlacement) || !ItemAim.TryGetAim(item, out Vector3 origin, out Vector3 direction))
        {
            Hide(view);
            return false;
        }

        int mask = HelperFunctions.LayerType.TerrainMap.ToLayerMask();
        bool hasHit = Physics.Raycast(origin, direction, out RaycastHit lineHit, MaxDist, mask,
            QueryTriggerInteraction.Ignore);
        Vector3 end = hasHit ? lineHit.point : origin + direction * MaxDist;

        view.Line.enabled = true;
        view.Line.SetPosition(0, origin);
        view.Line.SetPosition(1, end);

        if (isShootable && item.CanUsePrimary() && TryGetReachHit(item, origin, direction, out RaycastHit reachHit))
        {
            EnsureReticleSprite(view);
            Transform head = MainCamera.instance != null ? MainCamera.instance.cam.transform : null;
            float dist = head != null ? Vector3.Distance(head.position, reachHit.point) : 1f;
            view.Reticle.gameObject.SetActive(true);
            view.Reticle.transform.position = reachHit.point + reachHit.normal * ReticlePadding;
            view.Reticle.transform.rotation = head != null ? head.rotation : Quaternion.LookRotation(reachHit.normal);
            view.Reticle.transform.localScale = Vector3.one * (ReticleAngularScale * dist);
        }
        else
        {
            view.Reticle.gameObject.SetActive(false);
        }

        return isShootable;
    }

    private static bool TryGetReachHit(Item item, Vector3 origin, Vector3 direction, out RaycastHit hit)
    {
        RopeShooter rope = item.GetComponentInChildren<RopeShooter>();
        if (rope != null)
            return rope.WillAttach(out hit);

        VineShooter vine = item.GetComponentInChildren<VineShooter>();
        if (vine != null)
            return vine.WillAttach(out hit);

        return Physics.Raycast(origin, direction, out hit, MaxDist,
            HelperFunctions.LayerType.TerrainMap.ToLayerMask(), QueryTriggerInteraction.Ignore);
    }

    private static void Hide(AimView view)
    {
        if (view.Line != null && view.Line.enabled)
            view.Line.enabled = false;
        if (view.Reticle != null && view.Reticle.gameObject.activeSelf)
            view.Reticle.gameObject.SetActive(false);
    }

    private static void HideGameReticle()
    {
        GUIManager gui = GUIManager.instance;
        if (gui != null && gui.reticleShoot != null && gui.reticleShoot.activeSelf)
            gui.reticleShoot.SetActive(false);
    }

    private void EnsureReticleSprite(AimView view)
    {
        if (reticleSprite == null)
        {
            GUIManager gui = GUIManager.instance;
            if (gui != null && gui.reticleShoot != null)
            {
                Image img = gui.reticleShoot.GetComponentInChildren<Image>(true);
                if (img != null && img.sprite != null)
                    reticleSprite = img.sprite;
            }

            if (reticleSprite == null)
                reticleSprite = PeakAssets.Reticle;
        }

        if (reticleSprite != null && view.Reticle.sprite != reticleSprite)
            view.Reticle.sprite = reticleSprite;
    }

    private static bool IsPlacementItem(Item item)
    {
        return item.GetComponentInChildren<Constructable>() != null
            || item.GetComponentInChildren<ClimbingSpikeComponent>() != null
            || item.GetComponentInChildren<RopeTier>() != null;
    }

    private void CreateMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
        lineMat = new Material(shader);
        if (lineMat.HasProperty("_BaseColor"))
            lineMat.SetColor("_BaseColor", Color.white);
        else
            lineMat.color = Color.white;
    }

    private AimView CreateView(string side)
    {
        GameObject lineObj = new GameObject($"PeakVR ItemAim Line {side}");
        lineObj.transform.SetParent(transform, false);

        LineRenderer line = lineObj.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.widthMultiplier = 0.006f;
        line.numCapVertices = 4;
        line.positionCount = 2;
        line.textureMode = LineTextureMode.Stretch;
        line.material = lineMat;
        line.enabled = false;

        GameObject reticleObj = new GameObject($"PeakVR ItemAim Reticle {side}");
        reticleObj.transform.SetParent(transform, false);

        SpriteRenderer reticle = reticleObj.AddComponent<SpriteRenderer>();
        reticle.gameObject.SetActive(false);

        return new AimView { Line = line, Reticle = reticle };
    }
}

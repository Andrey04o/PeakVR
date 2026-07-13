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

    private LineRenderer line;
    private Material lineMat;
    private SpriteRenderer reticle;
    private bool reticleSpriteSet;

    private void Awake()
    {
        CreateLine();
        CreateReticle();
    }

    private void Update()
    {
        var kb = Keyboard.current;
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
        ShootableAim.RotationOffset += delta;
        Plugin.Log.LogInfo($"[PeakVR] Item rotation offset = {ShootableAim.RotationOffset}");
    }

    private void LateUpdate()
    {
        var character = Character.localCharacter;
        var item = character != null ? character.data.currentItem : null;

        var shootable = item != null && item.UIData.isShootable;

        if (!ItemAim.Enabled || VRHands.Right == null || VRPointer.Canvas != null
            || item == null || !(shootable || IsPlacementItem(item)))
        {
            Hide();
            return;
        }

        if (shootable)
            HideGameReticle();

        var ray = ItemAim.GetMiddleScreenRay();
        var mask = HelperFunctions.LayerType.TerrainMap.ToLayerMask();
        var lineHasHit = Physics.Raycast(ray, out var lineHit, MaxDist, mask, QueryTriggerInteraction.Ignore);

        var end = lineHasHit ? lineHit.point : ray.origin + ray.direction * MaxDist;

        line.enabled = true;
        line.SetPosition(0, ray.origin);
        line.SetPosition(1, end);

        if (shootable && item.CanUsePrimary() && TryGetReachHit(item, out var reachHit))
        {
            EnsureReticleSprite();
            var head = MainCamera.instance != null ? MainCamera.instance.cam.transform : null;
            var dist = head != null ? Vector3.Distance(head.position, reachHit.point) : 1f;
            reticle.gameObject.SetActive(true);
            reticle.transform.position = reachHit.point + reachHit.normal * ReticlePadding;
            reticle.transform.rotation = head != null ? head.rotation : Quaternion.LookRotation(reachHit.normal);
            reticle.transform.localScale = Vector3.one * (ReticleAngularScale * dist);
        }
        else
        {
            reticle.gameObject.SetActive(false);
        }
    }

    private static bool TryGetReachHit(Item item, out RaycastHit hit)
    {
        var rope = item.GetComponentInChildren<RopeShooter>();
        if (rope != null)
            return rope.WillAttach(out hit);

        var vine = item.GetComponentInChildren<VineShooter>();
        if (vine != null)
            return vine.WillAttach(out hit);

        var ray = ItemAim.GetMiddleScreenRay();
        return Physics.Raycast(ray, out hit, MaxDist,
            HelperFunctions.LayerType.TerrainMap.ToLayerMask(), QueryTriggerInteraction.Ignore);
    }

    private void Hide()
    {
        if (line != null && line.enabled)
            line.enabled = false;
        if (reticle != null && reticle.gameObject.activeSelf)
            reticle.gameObject.SetActive(false);
    }

    private static void HideGameReticle()
    {
        var gui = GUIManager.instance;
        if (gui != null && gui.reticleShoot != null && gui.reticleShoot.activeSelf)
            gui.reticleShoot.SetActive(false);
    }

    private void EnsureReticleSprite()
    {
        if (reticleSpriteSet)
            return;

        var gui = GUIManager.instance;
        if (gui != null && gui.reticleShoot != null)
        {
            var img = gui.reticleShoot.GetComponentInChildren<Image>(true);
            if (img != null && img.sprite != null)
            {
                reticle.sprite = img.sprite;
                reticleSpriteSet = true;
                return;
            }
        }

        if (PeakAssets.Reticle != null)
        {
            reticle.sprite = PeakAssets.Reticle;
            reticleSpriteSet = true;
        }
    }

    private static bool IsPlacementItem(Item item)
    {
        return item.GetComponentInChildren<Constructable>() != null
            || item.GetComponentInChildren<ClimbingSpikeComponent>() != null
            || item.GetComponentInChildren<RopeTier>() != null;
    }

    private void CreateLine()
    {
        var obj = new GameObject("PeakVR ItemAim Line");
        obj.transform.SetParent(transform, false);

        line = obj.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.widthMultiplier = 0.006f;
        line.numCapVertices = 4;
        line.positionCount = 2;
        line.textureMode = LineTextureMode.Stretch;

        var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
        lineMat = new Material(shader);
        if (lineMat.HasProperty("_BaseColor"))
            lineMat.SetColor("_BaseColor", Color.white);
        else
            lineMat.color = Color.white;
        line.material = lineMat;
        line.enabled = false;
    }

    private void CreateReticle()
    {
        var obj = new GameObject("PeakVR ItemAim Reticle");
        obj.transform.SetParent(transform, false);

        reticle = obj.AddComponent<SpriteRenderer>();
        reticle.gameObject.SetActive(false);
    }
}

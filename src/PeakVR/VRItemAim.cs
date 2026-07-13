using UnityEngine;

namespace PeakVR;

[DefaultExecutionOrder(1500)]
internal class VRItemAim : MonoBehaviour
{
    private const float MaxDist = 80f;
    private const float ReticlePadding = 0.03f;
    private const float ReticleSize = 0.16f;

    private LineRenderer line;
    private Material lineMat;
    private Transform reticle;
    private Material reticleMat;

    private void Awake()
    {
        CreateLine();
        CreateReticle();
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
        var hasHit = Physics.Raycast(ray, out var hit, MaxDist, mask, QueryTriggerInteraction.Ignore);

        var end = hasHit ? hit.point : ray.origin + ray.direction * MaxDist;

        line.enabled = true;
        line.SetPosition(0, ray.origin);
        line.SetPosition(1, end);
        SetLineColor(hasHit ? new Color(0.3f, 1f, 0.4f) : new Color(0.35f, 0.75f, 1f));

        if (hasHit && shootable)
        {
            reticle.gameObject.SetActive(true);
            reticle.position = hit.point + hit.normal * ReticlePadding;
            reticle.rotation = Quaternion.LookRotation(hit.normal);
            reticle.localScale = Vector3.one * ReticleSize;
        }
        else
        {
            reticle.gameObject.SetActive(false);
        }
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
        line.material = lineMat;
        line.enabled = false;
    }

    private void SetLineColor(Color col)
    {
        if (lineMat.HasProperty("_BaseColor"))
            lineMat.SetColor("_BaseColor", col);
        else
            lineMat.color = col;
    }

    private void CreateReticle()
    {
        var obj = new GameObject("PeakVR ItemAim Reticle");
        obj.transform.SetParent(transform, false);
        obj.AddComponent<MeshFilter>().sharedMesh = BuildQuad();

        var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit");
        reticleMat = new Material(shader);
        if (PeakAssets.Reticle != null)
            reticleMat.mainTexture = PeakAssets.Reticle.texture;
        reticleMat.color = new Color(0.4f, 1f, 0.5f, 0.9f);

        var rend = obj.AddComponent<MeshRenderer>();
        rend.sharedMaterial = reticleMat;
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows = false;

        reticle = obj.transform;
        reticle.gameObject.SetActive(false);
    }

    private static Mesh BuildQuad()
    {
        var mesh = new Mesh();
        mesh.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f),
            new Vector3(0.5f, -0.5f, 0f),
            new Vector3(0.5f, 0.5f, 0f),
            new Vector3(-0.5f, 0.5f, 0f),
        };
        mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
        mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        mesh.RecalculateBounds();
        return mesh;
    }
}

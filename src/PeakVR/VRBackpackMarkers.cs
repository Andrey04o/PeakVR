using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PeakVR;

[DefaultExecutionOrder(5000)]
internal class VRBackpackMarkers : MonoBehaviour
{
    private const float GhostAlpha = 0.35f;
    private const float InPackScale = 0.5f;
    private static readonly int RopeCutoff = Shader.PropertyToID("_RopeCutoff");

    private readonly List<BackpackMark> marks = new List<BackpackMark>();
    private readonly List<Item> outlined = new List<Item>();
    private readonly List<Item> outlineNext = new List<Item>();

    private Material ghostMaterial;
    private bool isRopeGhost;
    private GameObject ghost;
    private Item ghostItem;

    private void OnDisable()
    {
        outlineNext.Clear();
        SetOutlines();
        DestroyGhost();
    }

    private void LateUpdate()
    {
        marks.Clear();
        outlineNext.Clear();
        if (Camera.main != null)
            VRBackpackHands.CollectMarks(Character.localCharacter, marks);

        BackpackMark ghostMark = default;
        for (int i = 0; i < marks.Count; i++)
        {
            BackpackMark mark = marks[i];
            if (mark.Ghost != null)
            {
                ghostMark = mark;
                continue;
            }

            if (mark.Outline != null && !outlineNext.Contains(mark.Outline))
                outlineNext.Add(mark.Outline);
        }

        SetOutlines();
        SetGhost(ghostMark.Ghost, ghostMark.Slot);
        if (ghostMark.Ghost != null && ghostMaterial != null && !isRopeGhost)
        {
            Color color = VRLine.CharacterColor();
            SetColor(ghostMaterial, new Color(color.r, color.g, color.b, GhostAlpha));
        }
    }

    private void SetOutlines()
    {
        for (int i = 0; i < outlined.Count; i++)
        {
            Item item = outlined[i];
            if (item != null && !outlineNext.Contains(item))
                item.HoverExit();
        }

        outlined.Clear();
        for (int i = 0; i < outlineNext.Count; i++)
        {
            Item item = outlineNext[i];
            if (item == null)
                continue;

            item.HoverEnter();
            outlined.Add(item);
        }
    }

    private void SetGhost(Item item, Transform slot)
    {
        if (item == null || slot == null)
        {
            if (ghost != null)
                ghost.SetActive(false);
            return;
        }

        if (ghost == null || ghostItem != item)
            BuildGhost(item);

        Quaternion rotation = slot.rotation;
        ghost.transform.SetPositionAndRotation(slot.position - rotation * item.centerOfMass * 0.5f, rotation);
        ghost.SetActive(true);
    }

    private void BuildGhost(Item item)
    {
        DestroyGhost();
        ghostItem = item;
        ghost = new GameObject("PeakVR Backpack Stash Ghost");
        ghost.transform.localScale = item.forceScale ? Vector3.one * InPackScale : item.transform.lossyScale;
        EnsureGhostMaterial();

        Transform root = item.transform;
        foreach (MeshRenderer renderer in item.GetComponentsInChildren<MeshRenderer>())
        {
            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy)
                continue;

            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null)
                AddGhostPart(root, renderer.transform, filter.sharedMesh);
        }

        foreach (SkinnedMeshRenderer skinned in item.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (skinned.enabled && skinned.gameObject.activeInHierarchy && skinned.sharedMesh != null)
                AddGhostPart(root, skinned.transform, skinned.sharedMesh);
        }

        VRLayers.HideFromMirror(ghost);
    }

    private void AddGhostPart(Transform root, Transform part, Mesh mesh)
    {
        GameObject go = new GameObject("Part");
        go.transform.SetParent(ghost.transform, false);
        go.transform.localPosition = root.InverseTransformPoint(part.position);
        go.transform.localRotation = Quaternion.Inverse(root.rotation) * part.rotation;

        Vector3 rootScale = root.lossyScale;
        Vector3 partScale = part.lossyScale;
        go.transform.localScale = new Vector3(Divide(partScale.x, rootScale.x), Divide(partScale.y, rootScale.y),
            Divide(partScale.z, rootScale.z));

        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = go.AddComponent<MeshRenderer>();
        Material[] materials = new Material[Mathf.Max(mesh.subMeshCount, 1)];
        for (int i = 0; i < materials.Length; i++)
            materials[i] = ghostMaterial;
        renderer.sharedMaterials = materials;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    private static float Divide(float value, float by)
    {
        return Mathf.Abs(by) < 0.0001f ? value : value / by;
    }

    private void DestroyGhost()
    {
        if (ghost != null)
            Destroy(ghost);
        ghost = null;
        ghostItem = null;
    }

    private void EnsureGhostMaterial()
    {
        if (ghostMaterial != null)
            return;

        Material rope = FindRopeGhost();
        if (rope != null)
        {
            ghostMaterial = new Material(rope);
            if (ghostMaterial.HasProperty(RopeCutoff))
                ghostMaterial.SetFloat(RopeCutoff, 1f);
            isRopeGhost = true;
            return;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
        ghostMaterial = new Material(shader);
        ghostMaterial.SetFloat("_Surface", 1f);
        ghostMaterial.SetFloat("_Blend", 0f);
        ghostMaterial.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        ghostMaterial.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        ghostMaterial.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        ghostMaterial.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        ghostMaterial.SetFloat("_ZWrite", 0f);
        ghostMaterial.SetOverrideTag("RenderType", "Transparent");
        ghostMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        ghostMaterial.renderQueue = (int)RenderQueue.Transparent;
    }

    private static Material FindRopeGhost()
    {
        foreach (Item item in Zorro.Core.SingletonAsset<ItemDatabase>.Instance.itemLookup.Values)
        {
            RopeSpool spool = item != null ? item.GetComponent<RopeSpool>() : null;
            if (spool == null || spool.isAntiRope || spool.ropePrefab == null)
                continue;

            RopeBoneVisualizer visualizer = spool.ropePrefab.GetComponentInChildren<RopeBoneVisualizer>(true);
            if (visualizer != null && visualizer.ghostMaterial != null)
                return visualizer.ghostMaterial;
        }

        return null;
    }

    private static void SetColor(Material material, Color color)
    {
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
        else
            material.color = color;
    }
}

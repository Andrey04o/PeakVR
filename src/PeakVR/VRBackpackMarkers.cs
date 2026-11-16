using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PeakVR;

[DefaultExecutionOrder(5000)]
internal class VRBackpackMarkers : MonoBehaviour
{
    private const int RingPoints = 24;
    private const float RingRadius = 0.06f;
    private const float TargetRadius = 0.08f;
    private const float RingWidth = 0.003f;
    private const float TargetWidth = 0.006f;
    private const float GhostAlpha = 0.35f;

    private static readonly Color RingColor = new Color(0.9f, 0.9f, 0.95f);

    private readonly List<BackpackMark> marks = new List<BackpackMark>();
    private readonly List<LineRenderer> rings = new List<LineRenderer>();
    private readonly Vector3[] points = new Vector3[RingPoints];

    private Material ringMaterial;
    private Material targetMaterial;
    private Material ghostMaterial;
    private GameObject ghost;
    private Item ghostItem;

    private void OnDisable()
    {
        HideFrom(0);
        DestroyGhost();
    }

    private void LateUpdate()
    {
        marks.Clear();
        Camera cam = Camera.main;
        if (cam != null)
            VRBackpackHands.CollectMarks(Character.localCharacter, marks);

        if (marks.Count == 0)
        {
            HideFrom(0);
            SetGhost(null, null);
            return;
        }

        EnsureMaterials();
        Color color = VRLine.CharacterColor();
        SetColor(targetMaterial, color);

        BackpackMark ghostMark = default;
        for (int i = 0; i < marks.Count; i++)
        {
            BackpackMark mark = marks[i];
            Draw(GetRing(i), mark, cam.transform.position);
            if (mark.Ghost != null)
                ghostMark = mark;
        }

        HideFrom(marks.Count);
        SetGhost(ghostMark.Ghost, ghostMark.Slot);
        if (ghostMaterial != null)
            SetColor(ghostMaterial, new Color(color.r, color.g, color.b, GhostAlpha));
    }

    private void Draw(LineRenderer ring, BackpackMark mark, Vector3 eye)
    {
        Vector3 normal = mark.Position - eye;
        if (normal.sqrMagnitude < 0.0001f)
            normal = Vector3.forward;
        normal.Normalize();

        Vector3 right = Vector3.Cross(normal, Vector3.up);
        if (right.sqrMagnitude < 0.0001f)
            right = Vector3.Cross(normal, Vector3.forward);
        right.Normalize();
        Vector3 up = Vector3.Cross(right, normal);

        float radius = mark.IsTarget ? TargetRadius : RingRadius;
        for (int k = 0; k < RingPoints; k++)
        {
            float angle = k * Mathf.PI * 2f / RingPoints;
            points[k] = mark.Position + (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * radius;
        }

        ring.SetPositions(points);
        ring.widthMultiplier = mark.IsTarget ? TargetWidth : RingWidth;
        ring.sharedMaterial = mark.IsTarget ? targetMaterial : ringMaterial;
        ring.enabled = true;
    }

    private LineRenderer GetRing(int index)
    {
        while (rings.Count <= index)
        {
            GameObject go = new GameObject("PeakVR Backpack Slot Ring");
            go.transform.SetParent(transform, false);
            LineRenderer ring = go.AddComponent<LineRenderer>();
            ring.useWorldSpace = true;
            ring.loop = true;
            ring.positionCount = RingPoints;
            ring.shadowCastingMode = ShadowCastingMode.Off;
            ring.receiveShadows = false;
            ring.enabled = false;
            VRLayers.HideFromMirror(go);
            rings.Add(ring);
        }

        return rings[index];
    }

    private void HideFrom(int index)
    {
        for (int i = index; i < rings.Count; i++)
        {
            if (rings[i] != null)
                rings[i].enabled = false;
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
        ghost.transform.SetParent(transform, false);
        ghost.transform.localScale = item.transform.lossyScale;
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

    private void EnsureMaterials()
    {
        if (ringMaterial != null)
            return;

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
        ringMaterial = new Material(shader);
        SetColor(ringMaterial, RingColor);
        targetMaterial = new Material(shader);
    }

    private void EnsureGhostMaterial()
    {
        if (ghostMaterial != null)
            return;

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

    private static void SetColor(Material material, Color color)
    {
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
        else
            material.color = color;
    }
}

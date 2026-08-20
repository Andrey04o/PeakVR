using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PeakVR;

internal class VRRestore
{
    private struct Entry
    {
        public Transform Target;
        public Transform Parent;
        public int SiblingIndex;
        public Transform PrevSibling;
        public Vector3 LocalPosition;
        public Quaternion LocalRotation;
        public Vector3 LocalScale;
        public bool IsRect;
        public Vector2 AnchorMin;
        public Vector2 AnchorMax;
        public Vector2 Pivot;
        public Vector2 AnchoredPosition;
        public Vector2 SizeDelta;
    }

    private readonly List<Entry> entries = new();
    private readonly HashSet<RectTransform> rebuild = new();

    public int Count => entries.Count;

    public void Record(Transform target)
    {
        if (target == null)
            return;

        foreach (var existing in entries)
            if (existing.Target == target)
                return;

        var entry = new Entry
        {
            Target = target,
            Parent = target.parent,
            SiblingIndex = target.GetSiblingIndex(),
            PrevSibling = PreviousSibling(target),
            LocalPosition = target.localPosition,
            LocalRotation = target.localRotation,
            LocalScale = target.localScale,
        };

        if (target is RectTransform rt)
        {
            entry.IsRect = true;
            entry.AnchorMin = rt.anchorMin;
            entry.AnchorMax = rt.anchorMax;
            entry.Pivot = rt.pivot;
            entry.AnchoredPosition = rt.anchoredPosition;
            entry.SizeDelta = rt.sizeDelta;
        }

        entries.Add(entry);
    }

    public int RestoreAll()
    {
        var restored = 0;
        entries.Sort(Compare);

        foreach (var entry in entries)
        {
            if (entry.Target == null)
                continue;

            if (entry.Parent != null)
            {
                entry.Target.SetParent(entry.Parent, false);

                if (entry.PrevSibling != null && entry.PrevSibling.parent == entry.Parent)
                    entry.Target.SetSiblingIndex(entry.PrevSibling.GetSiblingIndex() + 1);
                else
                    entry.Target.SetSiblingIndex(entry.SiblingIndex);
            }

            if (entry.IsRect && entry.Target is RectTransform rt)
            {
                rt.anchorMin = entry.AnchorMin;
                rt.anchorMax = entry.AnchorMax;
                rt.pivot = entry.Pivot;
                rt.anchoredPosition = entry.AnchoredPosition;
                rt.sizeDelta = entry.SizeDelta;
            }

            entry.Target.localPosition = entry.LocalPosition;
            entry.Target.localRotation = entry.LocalRotation;
            entry.Target.localScale = entry.LocalScale;
            restored++;

            if (entry.Parent is RectTransform parent)
                rebuild.Add(parent);
        }

        foreach (var parent in rebuild)
        {
            if (parent == null)
                continue;

            LayoutRebuilder.MarkLayoutForRebuild(parent);

            var animator = parent.GetComponentInParent<Animator>();
            if (animator != null && animator.isActiveAndEnabled)
                animator.Rebind();
        }

        rebuild.Clear();
        entries.Clear();
        return restored;
    }

    private static Transform PreviousSibling(Transform target)
    {
        var parent = target.parent;
        var index = target.GetSiblingIndex();
        return parent != null && index > 0 ? parent.GetChild(index - 1) : null;
    }

    private static int Compare(Entry a, Entry b)
    {
        var parentA = a.Parent != null ? a.Parent.GetInstanceID() : 0;
        var parentB = b.Parent != null ? b.Parent.GetInstanceID() : 0;

        return parentA != parentB
            ? parentA.CompareTo(parentB)
            : a.SiblingIndex.CompareTo(b.SiblingIndex);
    }

    public void Clear() => entries.Clear();
}

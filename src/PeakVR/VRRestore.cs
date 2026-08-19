using System.Collections.Generic;
using UnityEngine;

namespace PeakVR;

internal class VRRestore
{
    private struct Entry
    {
        public Transform Target;
        public Transform Parent;
        public int SiblingIndex;
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

        foreach (var entry in entries)
        {
            if (entry.Target == null)
                continue;

            if (entry.Parent != null)
            {
                entry.Target.SetParent(entry.Parent, false);
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
        }

        entries.Clear();
        return restored;
    }

    public void Clear() => entries.Clear();
}

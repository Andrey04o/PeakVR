using System.Collections.Generic;
using UnityEngine;

namespace PeakVR;

internal class VRItemCollision : MonoBehaviour
{
    private Item current;
    private readonly List<Collider> itemColliders = new();
    private readonly List<Collider> bodyColliders = new();

    private void LateUpdate()
    {
        var character = Character.localCharacter;
        var item = character != null ? character.data.currentItem : null;

        if (item == current)
            return;

        Restore();
        current = item;

        if (character == null || item == null)
            return;

        Apply(character, item);
    }

    private void OnDisable() => Restore();

    private void Apply(Character character, Item item)
    {
        var ragdoll = character.refs != null ? character.refs.ragdoll : null;
        if (ragdoll == null || ragdoll.colliderList == null)
            return;

        foreach (var c in item.GetComponentsInChildren<Collider>(true))
            if (c != null)
                itemColliders.Add(c);

        foreach (var c in ragdoll.colliderList)
            if (c != null)
                bodyColliders.Add(c);

        Toggle(true);
    }

    private void Restore()
    {
        if (itemColliders.Count > 0 && bodyColliders.Count > 0)
            Toggle(false);

        itemColliders.Clear();
        bodyColliders.Clear();
    }

    private void Toggle(bool ignore)
    {
        foreach (var a in itemColliders)
        {
            if (a == null)
                continue;

            foreach (var b in bodyColliders)
            {
                if (b == null)
                    continue;

                try
                {
                    Physics.IgnoreCollision(a, b, ignore);
                }
                catch (System.Exception)
                {
                }
            }
        }
    }
}

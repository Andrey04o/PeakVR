using System.Collections.Generic;
using UnityEngine;

namespace PeakVR;

[DefaultExecutionOrder(1300)]
internal class VRRemoteBinoculars : MonoBehaviour
{
    private const float RaisedThreshold = 0.5f;
    private const float RescanInterval = 1f;

    private class Pin
    {
        public bool kinematic;
        public Vector3 scale;
        public Character holder;
        public bool hadHandJoint;
        public readonly List<Collider> colliders = new();
    }

    private readonly List<Action_ShowBinocularOverlay> overlays = new();
    private readonly Dictionary<Item, Pin> pinned = new();

    private Transform raisedHand;
    private float nextScan;

    private void LateUpdate()
    {
        BinocularRig.Load();
        if (!BinocularRig.HasRig)
            return;

        if (Time.time >= nextScan)
        {
            nextScan = Time.time + RescanInterval;
            Rescan();
        }

        raisedHand = null;

        for (var i = 0; i < overlays.Count; i++)
        {
            var overlay = overlays[i];
            if (overlay == null || overlay.isProp)
                continue;

            var item = overlay.GetComponentInParent<Item>();
            if (item == null)
                continue;

            if (item.itemState != ItemState.Held)
            {
                Release(item);
                continue;
            }

            var holder = item.holderCharacter;
            var isVr = holder != null && !holder.IsLocal && VRNetworking.IsActiveRemote(holder);
            var hand = isVr ? RemoteHand(holder) : null;

            if (hand == null || item.defaultPos.y < RaisedThreshold)
            {
                Release(item);
                continue;
            }

            Hold(item, holder);
            BinocularRig.ApplyHold(item.transform, hand.position,
                BinocularRig.ControllerRotationFromIkTarget(hand.rotation));
            raisedHand = hand;

            var body = item.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.position = item.transform.position;
                body.rotation = item.transform.rotation;
            }
        }

        PlaceProps();
    }

    private void PlaceProps()
    {
        for (var i = 0; i < overlays.Count; i++)
        {
            var overlay = overlays[i];
            if (overlay == null || !overlay.isProp || !overlay.gameObject.activeInHierarchy)
                continue;

            var owner = overlay.GetComponentInParent<Character>();
            var hand = owner != null && owner.refs != null ? owner.refs.IKHandTargetRight : raisedHand;
            if (hand == null)
                continue;

            BinocularRig.ApplyHold(overlay.transform.root, hand.position,
                BinocularRig.ControllerRotationFromIkTarget(hand.rotation));
        }
    }

    private void Hold(Item item, Character holder)
    {
        if (pinned.ContainsKey(item))
            return;

        var pin = new Pin { holder = holder };
        var body = item.GetComponent<Rigidbody>();

        pin.hadHandJoint = RemoveHandJoint(holder, body);

        foreach (var col in item.GetComponentsInChildren<Collider>(true))
        {
            if (col == null || !col.enabled)
                continue;

            pin.colliders.Add(col);
            col.enabled = false;
        }

        if (body != null)
        {
            pin.kinematic = body.isKinematic;
            body.isKinematic = true;
        }

        pin.scale = item.transform.localScale;
        pinned[item] = pin;
    }

    private static bool RemoveHandJoint(Character holder, Rigidbody itemBody)
    {
        if (holder == null || itemBody == null)
            return false;

        var hand = holder.GetBodypartRig(BodypartType.Hand_R);
        if (hand == null)
            return false;

        var removed = false;
        foreach (var joint in hand.GetComponents<FixedJoint>())
        {
            if (joint == null || joint.connectedBody != itemBody)
                continue;

            Destroy(joint);
            removed = true;
        }

        return removed;
    }

    private void Release(Item item)
    {
        if (!pinned.TryGetValue(item, out var pin))
            return;

        pinned.Remove(item);
        Restore(item, pin);
    }

    private static void Restore(Item item, Pin pin)
    {
        if (item == null)
            return;

        item.transform.localScale = pin.scale;

        var body = item.GetComponent<Rigidbody>();
        if (body != null)
            body.isKinematic = pin.kinematic;

        foreach (var col in pin.colliders)
            if (col != null)
                col.enabled = true;

        if (!pin.hadHandJoint || pin.holder == null || body == null)
            return;

        var hand = pin.holder.GetBodypartRig(BodypartType.Hand_R);
        if (hand == null)
            return;

        SnapToHandGrip(item, body, hand);

        if (hand.GetComponent<FixedJoint>() == null)
            hand.gameObject.AddComponent<FixedJoint>().connectedBody = body;
    }

    private static void SnapToHandGrip(Item item, Rigidbody body, Rigidbody hand)
    {
        var grip = item.transform.Find("Hand_R");
        if (grip == null)
            return;

        var itemRotInv = Quaternion.Inverse(item.transform.rotation);
        var gripLocalRot = itemRotInv * grip.rotation;
        var gripLocalDir = itemRotInv * (grip.position - item.transform.position);

        var rotation = hand.transform.rotation * Quaternion.Inverse(gripLocalRot);
        var position = hand.transform.position - rotation * gripLocalDir;

        item.transform.SetPositionAndRotation(position, rotation);

        if (body != null)
        {
            body.position = position;
            body.rotation = rotation;
        }
    }

    private void ReleaseAll()
    {
        foreach (var entry in pinned)
            Restore(entry.Key, entry.Value);

        pinned.Clear();
    }

    private void OnDisable()
    {
        ReleaseAll();
    }

    private static Transform RemoteHand(Character holder)
    {
        if (holder == null || holder.refs == null)
            return null;

        var ik = holder.refs.ikRight;
        if (ik != null && ik.data.tip != null)
            return ik.data.tip;

        var bone = holder.GetBodypartRig(BodypartType.Hand_R);
        return bone != null ? bone.transform : null;
    }

    private void Rescan()
    {
        overlays.Clear();
        overlays.AddRange(FindObjectsByType<Action_ShowBinocularOverlay>(FindObjectsSortMode.None));
    }
}

using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

namespace PeakVR;

internal static class VRRemoteGrip
{
    private const float SnapDistance = 0.015f;
    private const float SnapAngle = 4f;

    private class Hold
    {
        public int View;
        public Item Item;
        public Joint Joint;
        public ItemPhysicsSyncer Syncer;
        public bool IsSyncOff;
        public bool IsGravityOff;
        public Vector3 Dir;
        public Quaternion Rot;
        public Collider[] ItemColliders;
        public Collider[] LocalColliders;
    }

    private static readonly Dictionary<Character, Hold[]> Holds = new();
    private static readonly List<Joint> Joints = new();

    public static void Apply(Character character, VRNetworking.RemotePose pose)
    {
        if (character == null || character.data == null)
            return;

        if (!Holds.TryGetValue(character, out Hold[] holds))
        {
            if (!pose.hasGrip || (pose.gripRightView == 0 && pose.leftItemView == 0))
                return;
            holds = new[] { new Hold(), new Hold() };
            Holds[character] = holds;
        }

        int right = pose.hasGrip ? pose.gripRightView : 0;
        int left = pose.hasGrip && pose.leftItemView != right ? pose.leftItemView : 0;
        Step(character, holds[0], BodypartType.Hand_R, right, pose.gripRightPos, pose.gripRightRot);
        Step(character, holds[1], BodypartType.Hand_L, left, pose.gripLeftPos, pose.gripLeftRot);
    }

    public static bool TryGetOffset(Character character, Item item, out Vector3 dir, out Quaternion rot)
    {
        dir = Vector3.zero;
        rot = Quaternion.identity;
        if (character == null || item == null || !Holds.TryGetValue(character, out Hold[] holds))
            return false;

        Hold hold = holds[0];
        if (hold.Item != item)
            return false;

        dir = hold.Dir;
        rot = hold.Rot;
        return true;
    }

    public static Character GetHolder(Item item)
    {
        if (item == null)
            return null;

        foreach (KeyValuePair<Character, Hold[]> pair in Holds)
        {
            if (pair.Key != null && (pair.Value[0].Item == item || pair.Value[1].Item == item))
                return pair.Key;
        }

        return null;
    }

    public static Item GetItem(Character character, bool isLeft)
    {
        if (character == null || !Holds.TryGetValue(character, out Hold[] holds))
            return null;

        return holds[isLeft ? 1 : 0].Item;
    }

    public static void Drop(Character character)
    {
        if (character == null || !Holds.TryGetValue(character, out Hold[] holds))
            return;

        Release(character, holds[0]);
        Release(character, holds[1]);
        Holds.Remove(character);
    }

    private static void Step(Character character, Hold hold, BodypartType part, int view, Vector3 dir, Quaternion rot)
    {
        if (hold.View != view)
        {
            Release(character, hold);
            hold.View = view;
            hold.Item = Find(view);
            SetIgnoreLocal(hold, true);
        }

        Item item = hold.Item;
        if (item == null || item.rig == null || item.rig.isKinematic || item.itemState == ItemState.InBackpack)
            return;

        hold.Dir = dir;
        hold.Rot = rot;

        Rigidbody hand = character.GetBodypartRig(part);
        if (hand == null)
            return;

        if (item.itemState == ItemState.Ground)
            PauseSync(hold, item);

        Joint joint = FindJoint(hand, item.rig);
        Quaternion inverse = Quaternion.Inverse(item.rig.rotation);
        Vector3 seenDir = inverse * (hand.position - item.rig.position);
        Quaternion seenRot = inverse * hand.rotation;
        if (joint != null && (seenDir - dir).sqrMagnitude < SnapDistance * SnapDistance
            && Quaternion.Angle(seenRot, rot) < SnapAngle)
            return;

        DestroyJoints(hand, item.rig);
        Quaternion itemRot = hand.rotation * Quaternion.Inverse(rot);
        Vector3 itemPos = hand.position - itemRot * dir;
        item.transform.SetPositionAndRotation(itemPos, itemRot);
        item.rig.position = itemPos;
        item.rig.rotation = itemRot;
        item.rig.linearVelocity = hand.linearVelocity;
        item.rig.angularVelocity = Vector3.zero;
        hold.Joint = VRHandJoint.AttachRigid(hand, item);
    }

    private static void PauseSync(Hold hold, Item item)
    {
        if (!hold.IsSyncOff)
        {
            if (hold.Syncer == null)
                hold.Syncer = item.GetComponent<ItemPhysicsSyncer>();
            if (hold.Syncer != null && hold.Syncer.shouldSync)
            {
                hold.Syncer.shouldSync = false;
                hold.IsSyncOff = true;
            }
        }

        if (!hold.IsGravityOff && item.rig.useGravity)
        {
            item.rig.useGravity = false;
            hold.IsGravityOff = true;
        }
    }

    private static void Release(Character character, Hold hold)
    {
        Item item = hold.Item;
        bool isCurrent = item != null && character != null && character.data != null && character.data.currentItem == item;

        if (hold.Joint != null && !isCurrent)
            Object.Destroy(hold.Joint);

        if (hold.IsSyncOff && hold.Syncer != null)
            hold.Syncer.shouldSync = true;

        if (hold.IsGravityOff && item != null && item.rig != null && item.itemState == ItemState.Ground)
            item.rig.useGravity = true;

        SetIgnoreLocal(hold, false);
        hold.View = 0;
        hold.Item = null;
        hold.Joint = null;
        hold.Syncer = null;
        hold.IsSyncOff = false;
        hold.IsGravityOff = false;
    }

    private static void SetIgnoreLocal(Hold hold, bool isIgnored)
    {
        if (isIgnored)
        {
            Character local = Character.localCharacter;
            if (hold.Item == null || local == null)
                return;
            hold.ItemColliders = hold.Item.GetComponentsInChildren<Collider>(true);
            hold.LocalColliders = local.GetComponentsInChildren<Collider>(true);
        }

        if (hold.ItemColliders == null || hold.LocalColliders == null)
            return;

        for (int i = 0; i < hold.ItemColliders.Length; i++)
        {
            Collider itemCollider = hold.ItemColliders[i];
            if (itemCollider == null)
                continue;
            for (int k = 0; k < hold.LocalColliders.Length; k++)
            {
                if (hold.LocalColliders[k] != null)
                    Physics.IgnoreCollision(itemCollider, hold.LocalColliders[k], isIgnored);
            }
        }

        if (!isIgnored)
        {
            hold.ItemColliders = null;
            hold.LocalColliders = null;
        }
    }

    private static Item Find(int view)
    {
        if (view == 0)
            return null;

        PhotonView photonView = PhotonNetwork.GetPhotonView(view);
        return photonView != null ? photonView.GetComponent<Item>() : null;
    }

    private static Joint FindJoint(Rigidbody hand, Rigidbody rig)
    {
        Joints.Clear();
        hand.GetComponents(Joints);
        for (int i = 0; i < Joints.Count; i++)
        {
            if (Joints[i] != null && Joints[i].connectedBody == rig)
                return Joints[i];
        }

        return null;
    }

    private static void DestroyJoints(Rigidbody hand, Rigidbody rig)
    {
        Joints.Clear();
        hand.GetComponents(Joints);
        for (int i = 0; i < Joints.Count; i++)
        {
            if (Joints[i] != null && Joints[i].connectedBody == rig)
                Object.DestroyImmediate(Joints[i]);
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace PeakVR;

[DefaultExecutionOrder(1150)]
internal class VRFollowHold : MonoBehaviour
{
    private const float SmoothRate = 8f;
    private const float BinocularForward = 0.06f;

    private static readonly Dictionary<ushort, bool> AntizookaCache = new Dictionary<ushort, bool>();
    private static readonly Dictionary<int, Action_ShowBinocularOverlay> OverlayCache =
        new Dictionary<int, Action_ShowBinocularOverlay>();

    private class HandFollow
    {
        public bool IsLeft;
        public Item Held;
        public bool IsSmoothing;
        public Vector3 SmoothPos;
        public Quaternion SmoothRot = Quaternion.identity;
    }

    private readonly HandFollow right = new HandFollow { IsLeft = false };
    private readonly HandFollow left = new HandFollow { IsLeft = true };

    public static bool IsFollow(Item item)
    {
        return IsAntizooka(item);
    }

    public static bool IsAntizooka(Item item)
    {
        if (item == null)
            return false;

        if (!AntizookaCache.TryGetValue(item.itemID, out bool isAntizooka))
        {
            isAntizooka = item.GetComponentInChildren<Peak.Action_Antizooka>(true) != null;
            AntizookaCache[item.itemID] = isAntizooka;
        }

        return isAntizooka;
    }

    public static Action_ShowBinocularOverlay GetOverlay(Item item)
    {
        if (item == null)
            return null;

        int id = item.GetInstanceID();
        if (!OverlayCache.TryGetValue(id, out Action_ShowBinocularOverlay overlay))
        {
            overlay = item.GetComponentInChildren<Action_ShowBinocularOverlay>(true);
            OverlayCache[id] = overlay;
        }

        return overlay;
    }

    public static bool IsBinocularsActive(Item item)
    {
        Action_ShowBinocularOverlay overlay = GetOverlay(item);
        return overlay != null && overlay.binocularsActive;
    }

    public static Vector3 MirrorX(Vector3 v)
    {
        return new Vector3(-v.x, v.y, v.z);
    }

    public static Quaternion MirrorX(Quaternion q)
    {
        return new Quaternion(q.x, -q.y, -q.z, q.w);
    }

    public static bool IsLeftHeld(Item item)
    {
        Character character = Character.localCharacter;
        return item != null && item == VRHandItems.LeftItem
            && (character == null || character.data == null || character.data.currentItem != item);
    }

    private static bool IsFollowNow(Item item)
    {
        return IsAntizooka(item) || IsBinocularsActive(item);
    }

    private void LateUpdate()
    {
        Character character = Character.localCharacter;
        Item rightItem = null;
        Item leftItem = null;

        if (Plugin.VrEnabled && character != null && character.data != null)
        {
            Item current = character.data.currentItem;
            if (current != null && current.itemState == ItemState.Held && IsFollowNow(current))
                rightItem = current;

            Item held = VRHandItems.LeftItem;
            if (held != null && held != current && IsFollowNow(held))
                leftItem = held;
        }

        Step(right, rightItem, VRHands.Right, character);
        Step(left, leftItem, VRHands.Left, character);
    }

    private void Step(HandFollow state, Item item, Transform hand, Character character)
    {
        if (item != state.Held)
        {
            Release(state, character);
            state.Held = item;
            state.IsSmoothing = false;
        }

        if (item == null || hand == null)
        {
            state.IsSmoothing = false;
            return;
        }

        if (character != null)
            VRHandJoint.DestroyItemJoints(character.GetBodypartRig(state.IsLeft ? BodypartType.Hand_L : BodypartType.Hand_R));

        if (item.rig != null && item.rig.detectCollisions)
            item.rig.detectCollisions = false;

        Quaternion wantRot;
        Vector3 wantPos;

        if (GetOverlay(item) != null)
        {
            BinocularRig.Load();
            if (BinocularRig.HasRig)
            {
                Quaternion gripRot = state.IsLeft ? MirrorX(BinocularRig.GripRot) : BinocularRig.GripRot;
                Vector3 gripPos = state.IsLeft ? MirrorX(BinocularRig.GripPos) : BinocularRig.GripPos;
                wantRot = hand.rotation * Quaternion.Inverse(gripRot);
                wantPos = hand.position - wantRot * gripPos;
            }
            else
            {
                wantRot = hand.rotation;
                wantPos = hand.position + hand.forward * BinocularForward;
            }
        }
        else
        {
            wantRot = hand.rotation;
            wantPos = hand.position;
        }

        Vector3 localWantPos = transform.InverseTransformPoint(wantPos);
        Quaternion localWantRot = Quaternion.Inverse(transform.rotation) * wantRot;

        if (!state.IsSmoothing)
        {
            state.SmoothPos = localWantPos;
            state.SmoothRot = localWantRot;
            state.IsSmoothing = true;
        }
        else
        {
            float t = 1f - Mathf.Exp(-SmoothRate * Time.deltaTime);
            state.SmoothPos = Vector3.Lerp(state.SmoothPos, localWantPos, t);
            state.SmoothRot = Quaternion.Slerp(state.SmoothRot, localWantRot, t);
        }

        Vector3 pos = transform.TransformPoint(state.SmoothPos);
        Quaternion rot = transform.rotation * state.SmoothRot;
        item.transform.SetPositionAndRotation(pos, rot);
        if (item.rig != null)
        {
            item.rig.position = pos;
            item.rig.rotation = rot;
            item.rig.linearVelocity = Vector3.zero;
            item.rig.angularVelocity = Vector3.zero;
        }
    }

    private void OnDisable()
    {
        Character character = Character.localCharacter;
        Release(right, character);
        Release(left, character);
        right.Held = null;
        left.Held = null;
        right.IsSmoothing = false;
        left.IsSmoothing = false;
    }

    private static void Release(HandFollow state, Character character)
    {
        Item item = state.Held;
        if (item == null || item.rig == null)
            return;

        item.rig.detectCollisions = true;

        if (character == null || character.data == null || !IsStillHeld(state, item, character))
            return;

        Rigidbody bone = character.GetBodypartRig(state.IsLeft ? BodypartType.Hand_L : BodypartType.Hand_R);
        if (bone == null)
            return;

        VRGripProfiles.Capture(item, state.IsLeft, bone.transform.position, bone.transform.rotation,
            out Vector3 dir, out Quaternion gripRot);
        Quaternion rot = bone.transform.rotation * Quaternion.Inverse(gripRot);
        Vector3 pos = bone.transform.position - rot * dir;

        item.transform.SetPositionAndRotation(pos, rot);
        item.rig.position = pos;
        item.rig.rotation = rot;
        item.rig.linearVelocity = Vector3.zero;
        item.rig.angularVelocity = Vector3.zero;

        VRHandJoint.DestroyItemJoints(bone);
        VRHandJoint.AttachRigid(bone, item);
    }

    private static bool IsStillHeld(HandFollow state, Item item, Character character)
    {
        if (state.IsLeft)
            return VRHandItems.LeftItem == item && character.data.currentItem != item;

        return character.data.currentItem == item && item.itemState == ItemState.Held;
    }
}

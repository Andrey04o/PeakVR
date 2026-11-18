using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace PeakVR;

[HarmonyPatch(typeof(PlayerGhost), "Update")]
internal static class VRGhostSync
{
    private const float TargetHeight = 0.5f;
    private const float Follow = 12f;

    private struct GhostPose
    {
        public Vector3 offset;
        public Quaternion rotation;
        public float time;
    }

    private static readonly Dictionary<int, GhostPose> Poses = new();

    public static Character GetTarget(Character c)
    {
        if (c == null)
            return null;
        if (c.Ghost != null && c.Ghost.m_target != null)
            return c.Ghost.m_target;
        return MainCameraMovement.specCharacter;
    }

    public static bool TryGetLook(Character c, Vector3 head, out Vector3 forward)
    {
        forward = Vector3.forward;
        Character target = GetTarget(c);
        if (target == null)
            return false;

        Vector3 toTarget = target.Center + Vector3.up * TargetHeight - head;
        if (toTarget.sqrMagnitude < 0.01f)
            return false;

        forward = toTarget;
        return true;
    }

    public static bool TryGetSend(Character c, out Vector3 offset, out Quaternion rotation)
    {
        offset = Vector3.zero;
        rotation = Quaternion.identity;
        if (c.Ghost == null || c.Ghost.m_target == null || MainCamera.instance == null)
            return false;

        Transform head = MainCamera.instance.cam.transform;
        offset = head.position - c.Ghost.m_target.Center;
        rotation = head.rotation;
        return true;
    }

    public static void Receive(int actor, Vector3 offset, Quaternion rotation)
    {
        Poses[actor] = new GhostPose { offset = offset, rotation = rotation, time = Time.time };
    }

    public static void Drop(int actor)
    {
        Poses.Remove(actor);
    }

    [HarmonyPrefix]
    private static bool Prefix(PlayerGhost __instance)
    {
        Character owner = __instance.m_owner;
        Character target = __instance.m_target;
        if (owner == null || target == null || owner.IsLocal || owner.photonView == null)
            return true;
        if (!Poses.TryGetValue(owner.photonView.OwnerActorNr, out GhostPose pose)
            || Time.time - pose.time > VRNetworking.StaleTime)
            return true;

        Transform t = __instance.transform;
        float blend = Mathf.Clamp01(Time.deltaTime * Follow);
        t.SetPositionAndRotation(Vector3.Lerp(t.position, target.Center + pose.offset, blend),
            Quaternion.Slerp(t.rotation, pose.rotation, blend));
        return false;
    }
}

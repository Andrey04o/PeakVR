using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace PeakVR;

[HarmonyPatch(typeof(CharacterRagdoll), nameof(CharacterRagdoll.FixedUpdate))]
internal static class VRArmSmoothing
{
    private static readonly BodypartType[] ArmParts =
    {
        BodypartType.Arm_L, BodypartType.Elbow_L, BodypartType.Hand_L,
        BodypartType.Arm_R, BodypartType.Elbow_R, BodypartType.Hand_R
    };

    private static readonly List<Tracked> Bodies = new List<Tracked>();
    private static readonly Dictionary<Rigidbody, Tracked> ByRig = new Dictionary<Rigidbody, Tracked>();
    private static readonly List<Rigidbody> Current = new List<Rigidbody>();
    private static bool isHooked;
    private static bool isApplied;
    private static Transform torso;

    [HarmonyPostfix]
    private static void Postfix(Character ___character)
    {
        if (___character == null || ___character != Character.localCharacter)
            return;

        Restore();

        if (!Plugin.VrEnabled)
        {
            Clear();
            return;
        }

        Bodypart torsoPart = ___character.GetBodypart(BodypartType.Torso);
        if (torsoPart == null || torsoPart.Rig == null)
            return;

        Hook();
        Collect(___character);
        torso = torsoPart.transform;
        Quaternion inverse = Quaternion.Inverse(torsoPart.Rig.rotation);
        Vector3 origin = torsoPart.Rig.position;

        for (int i = Bodies.Count - 1; i >= 0; i--)
        {
            Tracked tracked = Bodies[i];
            if (tracked.rig == null || !Current.Contains(tracked.rig))
            {
                ByRig.Remove(tracked.rig);
                Bodies.RemoveAt(i);
                continue;
            }

            tracked.position = inverse * (tracked.rig.position - origin);
            tracked.rotation = inverse * tracked.rig.rotation;
            tracked.isValid = true;
        }
    }

    private static void Collect(Character c)
    {
        Current.Clear();
        foreach (BodypartType type in ArmParts)
        {
            Bodypart part = c.GetBodypart(type);
            if (part != null && part.Rig != null)
                Add(part.Rig);
        }

        Item right = c.data != null ? c.data.currentItem : null;
        if (right != null && right.rig != null)
            Add(right.rig);

        Item left = VRHandItems.LeftItem;
        if (left != null && left.rig != null && left != right)
            Add(left.rig);
    }

    private static void Add(Rigidbody rig)
    {
        Current.Add(rig);
        if (ByRig.ContainsKey(rig))
            return;

        Tracked tracked = new Tracked(rig);
        ByRig[rig] = tracked;
        Bodies.Add(tracked);
    }

    private static void Hook()
    {
        if (isHooked)
            return;

        isHooked = true;
        Application.onBeforeRender += Apply;
        RenderPipelineManager.endContextRendering += OnEndRendering;
    }

    private static void Clear()
    {
        Restore();
        Bodies.Clear();
        ByRig.Clear();
        torso = null;
        if (!isHooked)
            return;

        isHooked = false;
        Application.onBeforeRender -= Apply;
        RenderPipelineManager.endContextRendering -= OnEndRendering;
    }

    private static void Apply()
    {
        if (isApplied || !Plugin.VrEnabled || torso == null)
            return;

        Vector3 origin = torso.position;
        Quaternion frame = torso.rotation;
        foreach (Tracked tracked in Bodies)
        {
            if (tracked.rig == null || !tracked.isValid || tracked.rig.isKinematic)
                continue;

            tracked.rig.transform.SetPositionAndRotation(origin + frame * tracked.position, frame * tracked.rotation);
            tracked.isMoved = true;
        }

        isApplied = true;
    }

    private static void OnEndRendering(ScriptableRenderContext context, List<Camera> cameras)
    {
        Restore();
    }

    private static void Restore()
    {
        if (!isApplied)
            return;

        foreach (Tracked tracked in Bodies)
        {
            if (!tracked.isMoved || tracked.rig == null)
                continue;

            tracked.rig.transform.SetPositionAndRotation(tracked.rig.position, tracked.rig.rotation);
            tracked.isMoved = false;
        }

        isApplied = false;
    }

    private class Tracked
    {
        public readonly Rigidbody rig;
        public Vector3 position;
        public Quaternion rotation;
        public bool isValid;
        public bool isMoved;

        public Tracked(Rigidbody rig)
        {
            this.rig = rig;
        }
    }
}

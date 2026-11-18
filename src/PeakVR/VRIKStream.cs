using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Animations.Rigging;

namespace PeakVR;

[HarmonyPatch(typeof(CharacterAnimations), nameof(CharacterAnimations.ConfigureIK))]
internal static class VRIKStream
{
    private const float MissLimit = 0.05f;
    private const int MissFrames = 10;
    private const float RebindDelay = 2f;
    private const float ReachMargin = 0.95f;
    private const int LogLimit = 5;
    private const float IdleSpeed = 0.3f;

    private class Check
    {
        public int misses;
        public float nextBind;
        public int binds;
        public Vector3 lastCenter;
        public readonly List<SFX_PlayOneShot> hidden = new();
        public readonly List<SFX_PlayOneShot> active = new();
    }

    private static readonly Dictionary<Character, Check> Checks = new();
    private static readonly List<SFX_PlayOneShot> Sounds = new();

    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static void Prefix(CharacterAnimations __instance)
    {
        Character c = __instance.character;
        if (c == null || c.refs == null || c.refs.animations != __instance)
            return;

        bool isLocalVr = c == Character.localCharacter && Plugin.VrEnabled;
        if (!isLocalVr && (c == Character.localCharacter || !VRNetworking.IsActiveRemote(c)))
            return;

        Step(c);
    }

    private static void Step(Character c)
    {
        Character.CharacterRefs refs = c.refs;
        if (refs.animator == null || refs.ikLeft == null || refs.ikRight == null
            || refs.IKHandTargetLeft == null || refs.IKHandTargetRight == null)
            return;

        if (!Checks.TryGetValue(c, out Check check))
        {
            if (Checks.Count > 32)
                Prune();
            check = new Check();
            Checks[c] = check;
            Bind(c, check, 0f);
            return;
        }

        if (check.hidden.Count > 0)
            ShowSounds(check);

        Vector3 center = c.Center;
        float speed = Vector3.Distance(center, check.lastCenter) / Mathf.Max(Time.fixedDeltaTime, 1e-4f);
        check.lastCenter = center;
        if (speed > IdleSpeed || !IsIdle(c))
        {
            check.misses = 0;
            return;
        }

        float miss = Mathf.Max(GetMiss(refs.ikLeft, refs.IKHandTargetLeft), GetMiss(refs.ikRight, refs.IKHandTargetRight));
        check.misses = miss > MissLimit ? check.misses + 1 : 0;
        if (check.misses < MissFrames || Time.time < check.nextBind)
            return;

        check.misses = 0;
        Bind(c, check, miss);
    }

    private static bool IsIdle(Character c)
    {
        CharacterData data = c.data;
        if (!data.isGrounded || data.fallSeconds > 0f || data.isClimbingAnything || data.currentClimbHandle != null
            || data.passedOut || data.dead || data.currentItem != null || data.carriedPlayer != null || data.carrier != null)
            return false;
        if (c.IsLocal && VRHandClimb.IsLocalHolding(c))
            return false;
        return VRHandHolder.GetHandItem(c, true) == null && VRHandHolder.GetHandItem(c, false) == null;
    }

    public static void Rebind(Character c)
    {
        if (c != null && Checks.TryGetValue(c, out Check check))
            Bind(c, check, 0f);
    }

    private static float GetMiss(TwoBoneIKConstraint ik, Transform target)
    {
        Transform root = ik.data.root;
        Transform mid = ik.data.mid;
        Transform tip = ik.data.tip;
        if (root == null || mid == null || tip == null || ik.weight < 0.99f)
            return 0f;

        float upper = Vector3.Distance(root.position, mid.position);
        float fore = Vector3.Distance(mid.position, tip.position);
        float distance = Vector3.Distance(root.position, target.position);
        if (distance > (upper + fore) * ReachMargin || distance < Mathf.Abs(upper - fore) + MissLimit)
            return 0f;

        return Vector3.Distance(tip.position, target.position);
    }

    private static void Bind(Character c, Check check, float miss)
    {
        Character.CharacterRefs refs = c.refs;
        Animator animator = refs.animator;
        if (!animator.isInitialized)
            return;

        check.nextBind = Time.time + RebindDelay;
        HideSounds(animator, check);
        Bind(animator, refs.IKHandTargetLeft);
        Bind(animator, refs.IKHandTargetRight);
        Bind(animator, refs.ikLeft.data.hint);
        Bind(animator, refs.ikRight.data.hint);

        if (check.binds++ < LogLimit)
            Plugin.Log.LogInfo($"[PeakVR] re-bound the hand IK targets of '{c.characterName}' to the animation stream (hand missed its target by {miss:F2} m)");
    }

    private static void Bind(Animator animator, Transform target)
    {
        if (target != null && target.IsChildOf(animator.transform))
            animator.BindStreamTransform(target);
    }

    private static void HideSounds(Animator animator, Check check)
    {
        if (check.hidden.Count > 0)
            ShowSounds(check);

        animator.GetComponentsInChildren(true, Sounds);
        for (int i = 0; i < Sounds.Count; i++)
        {
            SFX_PlayOneShot sound = Sounds[i];
            if (!sound.playOnEnable)
                continue;

            check.hidden.Add(sound);
            sound.playOnEnable = false;
            if (!sound.gameObject.activeSelf)
                continue;

            check.active.Add(sound);
            bool isStop = sound.stopOnDisable;
            sound.stopOnDisable = false;
            sound.gameObject.SetActive(false);
            sound.stopOnDisable = isStop;
        }
        Sounds.Clear();
    }

    private static void ShowSounds(Check check)
    {
        for (int i = 0; i < check.active.Count; i++)
        {
            SFX_PlayOneShot sound = check.active[i];
            if (sound != null && !sound.gameObject.activeSelf)
                sound.gameObject.SetActive(true);
        }
        for (int i = 0; i < check.hidden.Count; i++)
        {
            SFX_PlayOneShot sound = check.hidden[i];
            if (sound != null)
                sound.playOnEnable = true;
        }
        check.active.Clear();
        check.hidden.Clear();
    }

    private static void Prune()
    {
        List<Character> gone = new();
        foreach (KeyValuePair<Character, Check> entry in Checks)
        {
            if (entry.Key == null)
                gone.Add(entry.Key);
        }

        foreach (Character character in gone)
            Checks.Remove(character);
    }
}

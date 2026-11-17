using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PeakVR;

internal static class VRFriendHelp
{
    private const float HelpDistance = 4f;
    private const float HelpLookAngle = 60f;
    private const float HandCone = 60f;
    private const float PullDistance = 0.12f;
    private const float PushDistance = 0.15f;

    private class HelpHand
    {
        public bool IsLeft;
        public Character Target;
        public bool IsGripHeld;
        public bool IsArmed;
        public bool IsHelping;
        public bool IsKicked;
        public float StartDistance;
    }

    private static readonly HelpHand Left = new HelpHand { IsLeft = true };
    private static readonly HelpHand Right = new HelpHand { IsLeft = false };
    private static bool isKickRequested;

    public static bool Enabled => Plugin.VrEnabled && VRHandItems.Enabled;

    public static bool IsHelping => Left.IsHelping || Right.IsHelping;

    public static bool IsTargeting(bool isLeft)
    {
        HelpHand hand = isLeft ? Left : Right;
        return hand.Target != null;
    }

    public static bool ConsumeKick()
    {
        bool isKick = isKickRequested;
        isKickRequested = false;
        return isKick;
    }

    public static void Tick(Character local, bool playerMovementActive)
    {
        bool isActive = Enabled && playerMovementActive && !VRKeyboard.IsOpen && local != null && local.data != null
            && local.refs != null && local.refs.grabbing != null && !local.data.dead && !local.data.passedOut
            && !local.data.fullyPassedOut;
        StepHand(local, Left, VRControls.LeftGrip, isActive);
        StepHand(local, Right, VRControls.RightGrip, isActive);
    }

    private static void StepHand(Character local, HelpHand hand, InputAction grip, bool isActive)
    {
        if (!isActive || grip == null)
        {
            Clear(hand);
            hand.IsGripHeld = grip != null && grip.IsPressed();
            return;
        }

        bool isKickMode = local.refs.grabbing.isKickMode;
        bool isPressed = grip.IsPressed();

        if (!isPressed)
        {
            if (hand.IsArmed)
                Log($"{Side(hand)} hand let go of the {(isKickMode ? "kick" : "help")}");
            hand.IsArmed = false;
            hand.IsHelping = false;
            hand.IsKicked = false;
        }

        if (!hand.IsArmed)
            hand.Target = FindTarget(local, hand.IsLeft, isKickMode);
        else if (!IsTargetAlive(hand.Target))
            hand.Target = null;

        if (isPressed && !hand.IsGripHeld && hand.Target != null)
        {
            hand.IsArmed = true;
            hand.StartDistance = HandHeadDistance(hand.IsLeft);
            Log($"{Side(hand)} hand armed {(isKickMode ? "kick" : "help")} on '{hand.Target.characterName}'");
        }
        hand.IsGripHeld = isPressed;

        if (!hand.IsArmed || hand.Target == null)
            return;

        float moved = HandHeadDistance(hand.IsLeft) - hand.StartDistance;
        if (isKickMode)
        {
            if (!hand.IsKicked && moved >= PushDistance)
            {
                hand.IsKicked = true;
                isKickRequested = true;
                Log($"{Side(hand)} hand pushed: kick '{hand.Target.characterName}'");
            }
            return;
        }

        if (!hand.IsHelping && -moved >= PullDistance)
        {
            hand.IsHelping = true;
            Log($"{Side(hand)} hand pulled: helping '{hand.Target.characterName}'");
        }
    }

    private static void Clear(HelpHand hand)
    {
        hand.Target = null;
        hand.IsArmed = false;
        hand.IsHelping = false;
        hand.IsKicked = false;
    }

    private static bool IsTargetAlive(Character target)
    {
        return target != null && target.data != null && !target.data.dead;
    }

    private static Character FindTarget(Character local, bool isLeft, bool isKickMode)
    {
        if (VRHandItems.IsBusy(local, isLeft) || VRHandClimb.IsGripping(isLeft))
            return null;

        IInteractible hover = isLeft ? HandInteractPatch.LeftTarget : HandInteractPatch.RightTarget;
        if (hover != null && hover is not CharacterInteractible)
            return null;

        bool isAimed = isLeft ? VRAim.TryLeft(out Vector3 origin, out Vector3 dir) : VRAim.TryRight(out origin, out dir);
        if (!isAimed)
            return null;

        if (isKickMode && (!local.data.isGrounded || local.data.isClimbingAnything || local.data.isKicking))
            return null;

        CharacterGrabbing grabbing = local.refs.grabbing;
        Character best = null;
        float bestAngle = HandCone;
        foreach (Character other in Character.AllCharacters)
        {
            if (other == null || other == local || other.data == null || other.data.dead)
                continue;

            Vector3 toOther = other.Center - local.Center;
            float distance = toOther.magnitude;
            if (isKickMode)
            {
                Vector3 look = local.data.lookDirection;
                if (look.y > 0f)
                {
                    look.y = 0f;
                    look.Normalize();
                }
                if (distance > grabbing.kickDistance || Vector3.Angle(look, toOther) > grabbing.kickAngle)
                    continue;
            }
            else if (distance > HelpDistance || Vector3.Angle(local.data.lookDirection, toOther) > HelpLookAngle
                || !CharacterGrabbing.TargetCanBeHelped(other, local))
                continue;

            float angle = Vector3.Angle(dir, other.Center - origin);
            if (angle >= bestAngle)
                continue;

            best = other;
            bestAngle = angle;
        }

        return best;
    }

    public static bool TryGetLine(bool isRight, out Vector3 start, out Vector3 end, out Color color)
    {
        start = default;
        end = default;
        color = default;

        HelpHand hand = isRight ? Right : Left;
        if (!Enabled || hand.Target == null)
            return false;

        bool isAimed = isRight ? VRAim.TryRight(out Vector3 origin, out Vector3 dir) : VRAim.TryLeft(out origin, out dir);
        if (!isAimed)
            return false;

        start = origin;
        end = NearestHand(hand.Target, origin);
        color = VRLine.CharacterColor();
        if (hand.IsHelping || hand.IsKicked)
            color = Color.Lerp(color, Color.white, 0.5f);
        return true;
    }

    private static Vector3 NearestHand(Character target, Vector3 origin)
    {
        Vector3 best = target.Center;
        float bestSqr = float.MaxValue;
        Rigidbody left = target.GetBodypartRig(BodypartType.Hand_L);
        Rigidbody right = target.GetBodypartRig(BodypartType.Hand_R);
        if (left != null)
        {
            bestSqr = (left.position - origin).sqrMagnitude;
            best = left.position;
        }
        if (right != null && (right.position - origin).sqrMagnitude < bestSqr)
            best = right.position;
        return best;
    }

    private static float HandHeadDistance(bool isLeft)
    {
        Transform hand = isLeft ? VRHands.Left : VRHands.Right;
        Transform head = MainCamera.instance != null ? MainCamera.instance.transform : null;
        if (hand == null || head == null)
            return 0f;

        return Vector3.Distance(hand.position, head.position);
    }

    private static string Side(HelpHand hand)
    {
        return hand.IsLeft ? "left" : "right";
    }

    private static void Log(string message)
    {
        if (Plugin.Config != null && Plugin.Config.EnableVerboseLogging.Value)
            Plugin.Log.LogInfo($"[PeakVR][Help] {message}");
    }
}

[HarmonyPatch(typeof(CharacterGrabbing), "Update")]
internal static class FriendHelpReachPatch
{
    [HarmonyPrefix]
    private static void Prefix(Character ___character)
    {
        if (___character == null || ___character != Character.localCharacter || !VRFriendHelp.Enabled)
            return;

        if (VRFriendHelp.IsHelping || VRFriendHelp.ConsumeKick())
            ___character.data.sincePressReach = 0f;
    }
}

[HarmonyPatch(typeof(CharacterGrabbing), nameof(CharacterGrabbing.TargetCanBeHelped), typeof(Character), typeof(Vector3))]
internal static class FriendHelpClimberPatch
{
    [HarmonyPostfix]
    private static void Postfix(Character target, Vector3 pullTarget, ref bool __result)
    {
        if (!__result && target != null && VRHandClimb.IsLocalHolding(target) && target.Center.y < pullTarget.y + 1f)
            __result = true;
    }
}

[HarmonyPatch(typeof(CharacterGrabbing), "CanGrab")]
internal static class FriendHelpCanGrabPatch
{
    [HarmonyPostfix]
    private static void Postfix(Character ___character, ref bool __result)
    {
        if (!__result && VRFriendHelp.IsHelping && ___character != null && ___character == Character.localCharacter)
            __result = true;
    }
}

using HarmonyLib;
using UnityEngine;

namespace PeakVR;

[HarmonyPatch(typeof(CharacterAnimations), "Update")]
internal static class LegAnimationPatch
{
    private static readonly int InputX = Animator.StringToHash("Input X");
    private static readonly int InputY = Animator.StringToHash("Input Y");

    private const float VelocityScale = 0.5f;
    private const float Damp = 0.1f;

    [HarmonyPostfix]
    private static void Postfix(CharacterAnimations __instance)
    {
        if (!VRHeadRig.RoomMoving)
            return;

        var c = Character.localCharacter;
        if (c == null || c.refs.animations != __instance)
            return;

        var animator = c.refs.animator;
        if (animator == null)
            return;

        var look = c.data.lookDirection;
        look.y = 0f;
        if (look.sqrMagnitude < 1e-4f)
            return;
        look.Normalize();

        var right = new Vector3(look.z, 0f, -look.x);
        var vel = c.data.avarageVelocity;
        vel.y = 0f;

        var forward = Mathf.Clamp(Vector3.Dot(vel, look) * VelocityScale, -1f, 1f);
        var strafe = Mathf.Clamp(Vector3.Dot(vel, right) * VelocityScale, -1f, 1f);

        animator.SetFloat(InputX, strafe, Damp, Time.deltaTime);
        animator.SetFloat(InputY, forward, Damp, Time.deltaTime);
    }
}

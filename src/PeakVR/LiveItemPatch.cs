using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace PeakVR;

[HarmonyPatch(typeof(CharacterItems), "EquipSlotRpc")]
internal static class StashDestroyPatch
{
    private static readonly System.Reflection.MethodInfo Destroy =
        AccessTools.Method(typeof(PhotonNetwork), "Destroy", new[] { typeof(PhotonView) });

    private static readonly System.Reflection.MethodInfo Guarded =
        AccessTools.Method(typeof(StashDestroyPatch), nameof(DestroyUnlessLive));

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var replaced = 0;

        foreach (var code in instructions)
        {
            if (Destroy != null && Guarded != null && code.opcode == OpCodes.Call
                && ReferenceEquals(code.operand, Destroy))
            {
                replaced++;
                yield return new CodeInstruction(OpCodes.Call, Guarded);
                continue;
            }

            yield return code;
        }

        if (replaced == 0)
            Plugin.Log.LogError("[PeakVR] EquipSlotRpc stash-destroy not found - live throws will destroy the item");
    }

    private static void DestroyUnlessLive(PhotonView view)
    {
        if (VRGrab.SuppressStashDestroy)
            return;

        PhotonNetwork.Destroy(view);
    }
}

[HarmonyPatch(typeof(Bonkable), "Bonk")]
internal static class CarriedBonkPatch
{
    [HarmonyPrefix]
    private static bool Prefix(Bonkable __instance, Collision coll)
    {
        if (!VRGrab.Enabled || __instance.item == null || VRLeftHand.Carried != __instance.item)
            return true;

        var hit = coll.gameObject.GetComponentInParent<Character>();
        return hit == null || hit != Character.localCharacter;
    }
}

[HarmonyPatch(typeof(Item), "UpdateCollisionDetectionMode")]
internal static class HeldItemCollisionPatch
{
    [HarmonyPostfix]
    private static void Postfix(Item __instance)
    {
        if (!VRGrab.Enabled || __instance.rig == null || __instance.rig.isKinematic)
            return;

        if (__instance.holderCharacter != Character.localCharacter && VRLeftHand.Carried != __instance)
            return;

        __instance.rig.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        __instance.rig.excludeLayers = 0;
    }
}

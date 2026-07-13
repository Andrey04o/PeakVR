using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace PeakVR;

[HarmonyPatch]
internal static class ItemAimRedirectPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        var names = new (System.Type type, string method)[]
        {
            (typeof(RopeShooter), "OnPrimaryFinishedCast"),
            (typeof(RopeShooter), nameof(RopeShooter.WillAttach)),
            (typeof(VineShooter), "OnPrimaryFinishedCast"),
            (typeof(VineShooter), nameof(VineShooter.WillAttach)),
            (typeof(RescueHook), "GetHit"),
            (typeof(RopeTier), "Update"),
            (typeof(Action_RaycastDart), "RunAction"),
            (typeof(Action_RaycastDart), "FireDart"),
            (typeof(Constructable), "Update"),
            (typeof(Constructable), nameof(Constructable.TryUpdatePreview)),
            (typeof(Constructable), "CreateOrMovePreview"),
            (typeof(CharacterItems), "RaycastClimbingSpikeStart"),
            (typeof(CharacterItems), "WithinClimbingSpikePreviewRange"),
        };

        foreach (var (type, method) in names)
        {
            var m = AccessTools.Method(type, method);
            if (m != null)
                yield return m;
        }
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var getMethod = AccessTools.Method(typeof(ItemAim), nameof(ItemAim.Get));
        var getRayMethod = AccessTools.Method(typeof(ItemAim), nameof(ItemAim.GetMiddleScreenRay));

        var codes = new List<CodeInstruction>(instructions);
        for (var i = 0; i < codes.Count; i++)
        {
            var c = codes[i];

            if (c.opcode == OpCodes.Ldsfld
                && c.operand is FieldInfo f && f.Name == "instance" && f.DeclaringType == typeof(MainCamera)
                && i + 1 < codes.Count
                && codes[i + 1].operand is MethodInfo tm && tm.Name == "get_transform")
            {
                c.opcode = OpCodes.Call;
                c.operand = getMethod;
                codes[i + 1].opcode = OpCodes.Nop;
                codes[i + 1].operand = null;
            }
            else if (c.operand is MethodInfo m && m.Name == "GetMiddleScreenRay")
            {
                c.opcode = OpCodes.Call;
                c.operand = getRayMethod;
            }
        }

        return codes;
    }
}

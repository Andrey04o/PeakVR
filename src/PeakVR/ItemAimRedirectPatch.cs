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
            (typeof(Peak.Action_RaycastSpawnSomething), "RunAction"),
            (typeof(Peak.Action_RaycastSpawnSomething), "FixedUpdate"),
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

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
    {
        MethodInfo getMethod = AccessTools.Method(typeof(ItemAim), nameof(ItemAim.Get), new[] { typeof(object) });
        MethodInfo getRayMethod = AccessTools.Method(typeof(ItemAim), nameof(ItemAim.GetMiddleScreenRay),
            new[] { typeof(object) });
        bool hasSelf = !original.IsStatic;

        List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
        List<CodeInstruction> result = new List<CodeInstruction>(codes.Count + 8);
        for (int i = 0; i < codes.Count; i++)
        {
            CodeInstruction c = codes[i];

            if (c.opcode == OpCodes.Ldsfld
                && c.operand is FieldInfo f && f.Name == "instance" && f.DeclaringType == typeof(MainCamera)
                && i + 1 < codes.Count
                && codes[i + 1].operand is MethodInfo tm && tm.Name == "get_transform")
            {
                c.opcode = hasSelf ? OpCodes.Ldarg_0 : OpCodes.Ldnull;
                c.operand = null;
                codes[i + 1].opcode = OpCodes.Call;
                codes[i + 1].operand = getMethod;
                result.Add(c);
                continue;
            }

            if (c.operand is MethodInfo m && m.Name == "GetMiddleScreenRay")
            {
                if (!m.IsStatic)
                {
                    if (i == 0 || codes[i - 1].opcode != OpCodes.Ldarg_0)
                    {
                        result.Add(c);
                        continue;
                    }

                    if (!hasSelf)
                        result[result.Count - 1].opcode = OpCodes.Ldnull;
                }
                else
                {
                    CodeInstruction load = new CodeInstruction(hasSelf ? OpCodes.Ldarg_0 : OpCodes.Ldnull);
                    load.labels.AddRange(c.labels);
                    c.labels.Clear();
                    result.Add(load);
                }

                c.opcode = OpCodes.Call;
                c.operand = getRayMethod;
            }

            result.Add(c);
        }

        return result;
    }
}

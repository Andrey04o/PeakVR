using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace PeakVR;

[HarmonyPatch]
internal static class LensFlareMultipassPatch
{
    private static readonly FieldInfo MultipassIdField =
        typeof(XRPass).GetField("<multipassId>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);

    private static IEnumerable<MethodBase> TargetMethods()
    {
        return AccessTools.GetDeclaredMethods(typeof(LensFlareCommonSRP))
            .Where(m => m.Name is "ComputeOcclusion" or "DoLensFlareDataDrivenCommon"
                        && m.GetParameters().Any(p => p.ParameterType == typeof(XRPass) && p.Name == "xr"));
    }

    [HarmonyPrefix]
    private static void Prefix(XRPass xr, ref int __state)
    {
        __state = -1;

        if (MultipassIdField == null || xr == null || !xr.enabled || xr.singlePassEnabled)
            return;

        var id = xr.multipassId;
        if (id == 0)
            return;

        __state = id;
        MultipassIdField.SetValue(xr, 0);
    }

    [HarmonyPostfix]
    private static void Postfix(XRPass xr, int __state)
    {
        if (__state >= 0)
            MultipassIdField.SetValue(xr, __state);
    }
}

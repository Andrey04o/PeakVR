using HarmonyLib;
using Zorro.ControllerSupport;

namespace PeakVR;

[HarmonyPatch(typeof(InputHandler), nameof(InputHandler.GetCurrentUsedInputScheme))]
internal static class InputSchemePatch
{
    [HarmonyPostfix]
    private static void Postfix(ref InputScheme __result)
    {
        if (Plugin.VrEnabled)
            __result = InputScheme.KeyboardMouse;
    }
}

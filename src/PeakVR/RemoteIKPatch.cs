using HarmonyLib;

namespace PeakVR;

[HarmonyPatch(typeof(CharacterAnimations), "ConfigureIK")]
internal static class RemoteIKPatch
{
    [HarmonyPrefix]
    private static bool Prefix(CharacterAnimations __instance)
    {
        var c = __instance.character;
        if (c == null || c == Character.localCharacter)
            return true;

        return !VRNetworking.IsActiveRemote(c);
    }
}

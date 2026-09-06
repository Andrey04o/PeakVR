using HarmonyLib;

namespace PeakVR;

[HarmonyPatch(typeof(CharacterAnimations))]
internal static class RemoteIKPatch
{
    [HarmonyPatch("ConfigureIK")]
    [HarmonyPrefix]
    private static bool ConfigurePrefix(CharacterAnimations __instance)
    {
        var c = __instance.character;
        if (c == null || c == Character.localCharacter)
            return true;

        return !VRNetworking.IsActiveRemote(c);
    }

    [HarmonyPatch("ConfigureIK")]
    [HarmonyPostfix]
    private static void ConfigurePostfix(CharacterAnimations __instance) => Apply(__instance);

    [HarmonyPatch("HandleIK")]
    [HarmonyPostfix]
    private static void HandlePostfix(CharacterAnimations __instance)
    {
        if (!IsVrRemote(__instance, out var c))
            return;

        c.refs.ikRig.weight = 1f;
        c.refs.ikLeft.weight = 1f;
        c.refs.ikRight.weight = 1f;
    }

    private static void Apply(CharacterAnimations animations)
    {
        if (IsVrRemote(animations, out var c))
            VRNetReceiver.ApplyTo(c);
    }

    private static bool IsVrRemote(CharacterAnimations animations, out Character c)
    {
        c = animations.character;
        if (c == null || c == Character.localCharacter || c.refs.animations != animations)
            return false;
        if (c.refs.ikRig == null || c.refs.ikLeft == null || c.refs.ikRight == null)
            return false;

        return VRNetworking.IsActiveRemote(c);
    }
}

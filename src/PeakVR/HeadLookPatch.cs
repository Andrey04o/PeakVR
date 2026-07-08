using HarmonyLib;

namespace PeakVR;

[HarmonyPatch(typeof(CharacterMovement), "CameraLook")]
internal static class HeadLookPatch
{
    [HarmonyPostfix]
    private static void Postfix(CharacterMovement __instance)
    {
        var character = Character.localCharacter;
        if (character == null || MainCamera.instance == null)
            return;

        if (__instance.GetComponent<Character>() != character)
            return;

        character.data.lookValues = HelperFunctions.DirectionToLook(MainCamera.instance.cam.transform.forward);
        character.RecalculateLookDirections();
    }
}

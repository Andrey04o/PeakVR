using System.Collections;
using PEAKLib.UI.Elements;
using UnityEngine;

namespace PeakVR;

internal static class VRMenuFx
{
    public static void PlayOpenSound()
    {
        var prefab = Templates.SettingsCellPrefab;
        if (prefab == null)
            return;

        var cell = prefab.GetComponent<SettingsUICell>();
        if (cell != null && cell.fadeInSFX != null)
            cell.fadeInSFX.Play();
    }

    public static void AttachFade(GameObject target)
    {
        if (target != null && target.GetComponent<VRFadeIn>() == null)
            target.AddComponent<VRFadeIn>();
    }
}

internal class VRFadeIn : MonoBehaviour
{
    private const float Duration = 0.22f;

    private CanvasGroup group;

    private void OnEnable()
    {
        if (group == null)
            group = gameObject.GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();

        StopAllCoroutines();
        StartCoroutine(Fade());
    }

    private IEnumerator Fade()
    {
        VRMenuFx.PlayOpenSound();

        var elapsed = 0f;
        while (elapsed < Duration)
        {
            elapsed += Time.unscaledDeltaTime;
            group.alpha = Mathf.Clamp01(elapsed / Duration);
            yield return null;
        }

        group.alpha = 1f;
    }
}

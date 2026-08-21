using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PeakVR;

internal class VRKeyboardKey : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler,
    IPointerDownHandler, IPointerUpHandler
{
    public Image target;
    public Action onClick;

    public Color normal = new(0.17f, 0.19f, 0.23f, 0.98f);
    public Color highlighted = new(0.31f, 0.56f, 0.86f, 1f);
    public Color pressed = new(0.16f, 0.36f, 0.60f, 1f);

    public bool locked;
    public bool repeatable;

    private bool over;
    private bool down;
    private float nextRepeat;

    public void Refresh() => Paint();

    private void Update()
    {
        if (!down || !over || !repeatable)
            return;

        var rate = Plugin.Config != null ? Plugin.Config.KeyRepeatRate.Value : 0f;
        if (rate <= 0f)
            return;

        if (Time.unscaledTime < nextRepeat)
            return;

        nextRepeat = Time.unscaledTime + 1f / rate;
        Fire();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        over = true;
        Paint();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        over = false;
        down = false;
        Paint();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        down = true;

        var delay = Plugin.Config != null ? Plugin.Config.KeyRepeatDelay.Value : 0.4f;
        nextRepeat = Time.unscaledTime + delay;

        Paint();
        Fire();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        down = false;
        Paint();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
    }

    private void Fire()
    {
        if (onClick == null)
            return;

        try
        {
            onClick();
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"[PeakVR][Keyboard] key failed: {e.Message}");
        }
    }

    private void Paint()
    {
        if (target == null)
            return;

        target.color = down ? pressed : over ? highlighted : locked ? pressed : normal;
    }
}

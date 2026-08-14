using Photon.Pun;
using UnityEngine;
using Zorro.Core;

namespace PeakVR;

[DefaultExecutionOrder(1200)]
internal class VRGrab : MonoBehaviour
{
    private const int SampleCount = 16;
    private const int Baseline = 2;
    private const int DirectionBaseline = 3;
    private const float DirectionMinSpeed = 0.3f;
    private const float Window = 0.12f;
    private const float MaxThrowSpeed = 25f;
    private const float FullChargeSpeed = 8f;
    private const float MinThrowSpeed = 0.5f;
    private const float ChargeMarker = 1f;
    private const float AnimateChargeAbove = 0.1f;

    private static VRGrab instance;

    private readonly Vector3[] positions = new Vector3[SampleCount];
    private readonly float[] times = new float[SampleCount];
    private int head = -1;
    private int filled;

    private Item latched;
    private Item heldAtPress;
    private bool watching;
    private bool relatchCheck;
    private bool interactAllowed = true;
    private int threwFrame = -1;

    public static bool Enabled => Plugin.Config != null && Plugin.Config.ImmersiveHands.Value;

    public static bool AllowInteract => instance == null || !Enabled || instance.interactAllowed;

    public static bool AllowVanillaDrop => instance == null || !Enabled;

    private void OnEnable() => instance = this;

    private void OnDisable()
    {
        if (instance == this)
            instance = null;

        Clear();
        filled = 0;
        head = -1;
    }

    private void Clear()
    {
        latched = null;
        watching = false;
        relatchCheck = false;
    }

    private void Update()
    {
        if (!Enabled)
            return;

        var hand = VRHands.Right;
        if (hand == null)
            return;

        head = (head + 1) % SampleCount;
        positions[head] = transform.InverseTransformPoint(hand.position);
        times[head] = Time.time;

        if (filled < SampleCount)
            filled++;
    }

    public static void Step()
    {
        if (instance != null)
            instance.Tick();
    }

    private void Tick()
    {
        interactAllowed = true;

        if (!Enabled || VRControls.RightGrip == null)
        {
            Clear();
            return;
        }

        var character = Character.localCharacter;
        if (character == null || character.data == null)
        {
            Clear();
            return;
        }

        var held = character.data.currentItem;

        if (latched != null && latched != held)
            latched = null;

        var pressed = VRControls.RightGrip.WasPressedThisFrame();

        if (pressed)
        {
            watching = true;
            relatchCheck = held != null;
            heldAtPress = held;
        }
        else if (!VRControls.RightGrip.IsPressed())
        {
            watching = false;
            relatchCheck = false;
        }

        if (watching && held != null && held != heldAtPress)
        {
            latched = held;
            watching = false;
            relatchCheck = false;
        }
        else if (relatchCheck && !pressed)
        {
            relatchCheck = false;

            if (held != null && Hovered() == null && NothingWasInteracted())
                latched = held;
        }

        if (VRControls.RightGrip.WasReleasedThisFrame() && latched != null)
        {
            Throw(character, latched);
            latched = null;
        }

        if (VRControls.LeftGrip != null && VRControls.LeftGrip.WasReleasedThisFrame()
            && held != null && threwFrame != Time.frameCount)
        {
            Throw(character, held);
            latched = null;
        }

        interactAllowed = latched == null;
    }

    private static bool NothingWasInteracted()
    {
        var interaction = Interaction.instance;
        return interaction == null || (interaction.readyToInteract && interaction.currentHeldInteractible == null);
    }

    private static IInteractible Hovered()
    {
        var interaction = Interaction.instance;
        return interaction != null ? interaction.currentHovered : null;
    }

    private void Throw(Character character, Item item)
    {
        if (item == null || item.UIData == null || !item.UIData.canDrop)
            return;

        var items = character.refs != null ? character.refs.items : null;
        if (items == null || !items.currentSelectedSlot.IsSome)
            return;

        var slot = character.player != null
            ? character.player.GetItemSlot(items.currentSelectedSlot.Value)
            : null;
        if (slot == null)
            return;

        var hand = PeakVelocity();
        var speed = Mathf.Min(hand.magnitude * Plugin.Config.ThrowStrength.Value, MaxThrowSpeed);
        var charge = speed < MinThrowSpeed ? 0f : Mathf.Clamp01(speed / FullChargeSpeed);

        var sticky = character.data.currentStickyItem;
        if (sticky != null && charge < sticky.throwChargeRequirement)
        {
            Animate(character);
            return;
        }

        var velocity = hand.normalized * speed + character.data.avarageVelocity;
        if (item is Backpack)
            velocity = Vector3.zero;

        if (charge > AnimateChargeAbove)
            Animate(character);

        character.photonView.RPC("DropItemRpc", RpcTarget.All, -(charge + ChargeMarker),
            items.currentSelectedSlot.Value, item.transform.position, velocity,
            item.transform.rotation, slot.data, false);

        items.throwChargeLevel = 0f;
        items.EquipSlot(Optionable<byte>.None);
        threwFrame = Time.frameCount;
    }

    private static void Animate(Character character)
    {
        var animations = character.refs != null ? character.refs.animations : null;
        if (animations != null)
            animations.throwTime = 0.125f;
    }

    private Vector3 PeakVelocity()
    {
        if (filled <= Baseline)
            return Vector3.zero;

        var now = Time.time;
        var best = Vector3.zero;
        var bestSqr = 0f;

        for (var i = 0; i + Baseline < filled; i++)
        {
            var newer = (head - i + SampleCount) % SampleCount;
            var older = (head - i - Baseline + SampleCount) % SampleCount;

            if (now - times[older] > Window)
                break;

            var dt = times[newer] - times[older];
            if (dt <= 0f)
                continue;

            var v = (positions[newer] - positions[older]) / dt;
            var sqr = v.sqrMagnitude;
            if (sqr <= bestSqr)
                continue;

            bestSqr = sqr;
            best = v;
        }

        var release = ReleaseDirection();
        if (release != Vector3.zero)
            best = release * best.magnitude;

        return transform.TransformVector(best);
    }

    private Vector3 ReleaseDirection()
    {
        if (filled <= DirectionBaseline)
            return Vector3.zero;

        var older = (head - DirectionBaseline + SampleCount) % SampleCount;
        var dt = times[head] - times[older];
        if (dt <= 0f)
            return Vector3.zero;

        var v = (positions[head] - positions[older]) / dt;
        return v.magnitude < DirectionMinSpeed ? Vector3.zero : v.normalized;
    }
}

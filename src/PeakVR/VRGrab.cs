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
    private const float GrabRange = 0.3f;
    private const float MaxGripOffset = 0.35f;
    private const float CaptureTimeout = 3f;

    private static VRGrab instance;

    private static bool captured;
    private static ushort capturedId;
    private static float capturedAt;
    private static Vector3 capturedDir;
    private static Quaternion capturedRot;

    private static Item gripItem;
    private static Vector3 gripDir;
    private static Quaternion gripRot;

    private readonly Vector3[] positions = new Vector3[SampleCount];
    private readonly float[] times = new float[SampleCount];
    private int head = -1;
    private int filled;

    private Item latched;
    private Item heldAtPress;
    private Item lastHeld;
    private bool watching;
    private bool relatchCheck;
    private bool interactAllowed = true;
    private int threwFrame = -1;

    public static bool Enabled => Plugin.Config != null && Plugin.Config.ImmersiveHands.Value;

    public static bool AllowInteract => instance == null || !Enabled || instance.interactAllowed;

    public static bool AllowVanillaDrop => instance == null || !Enabled;

    public static bool LeftHandEnabled => Enabled;

    public static void LeftLog(string message) => Log($"left: {message}");

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

        if (held != lastHeld)
        {
            lastHeld = held;
            if (held != gripItem)
                gripItem = null;
        }

        if (captured && Time.time - capturedAt > CaptureTimeout)
            captured = false;

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

        if (!LeftHandEnabled && VRControls.LeftGrip != null && VRControls.LeftGrip.WasReleasedThisFrame()
            && held != null && threwFrame != Time.frameCount)
        {
            Throw(character, held);
            latched = null;
        }

        interactAllowed = latched == null;
    }

    public static void Capture(Item target)
    {
        if (!Enabled || target == null)
            return;

        if (Excluded(target))
        {
            Log($"skip '{target.name}' - excluded item");
            return;
        }

        var character = Character.localCharacter;
        var bone = character != null ? character.GetBodypartRig(BodypartType.Hand_R) : null;
        if (bone == null)
        {
            Log($"skip '{target.name}' - no hand bone");
            return;
        }

        var handPos = bone.transform.position;
        var handRot = bone.transform.rotation;
        var distance = Distance(target, handPos);

        if (distance > GrabRange)
        {
            Log($"skip '{target.name}' - {distance:F2}m away (range {GrabRange:F2})");
            return;
        }

        var inv = Quaternion.Inverse(target.transform.rotation);
        var dir = inv * (handPos - target.transform.position);
        var raw = dir.magnitude;

        var limit = Mathf.Max(MaxGripOffset, Radius(target) + GrabRange);
        var authored = target.transform.Find("Hand_R");
        if (authored != null)
        {
            var authoredDir = inv * (authored.position - target.transform.position);
            dir = authoredDir + Vector3.ClampMagnitude(dir - authoredDir, limit);
        }

        capturedDir = dir;
        capturedRot = inv * handRot;
        capturedId = target.itemID;
        capturedAt = Time.time;
        captured = true;

        var controller = VRHands.Right;
        var lag = controller != null ? Vector3.Distance(controller.position, handPos) : 0f;

        Log($"captured '{target.name}' id={capturedId} surface={distance:F2}m offset={raw:F2}->{dir.magnitude:F2}m "
            + $"limit={limit:F2}m rot={capturedRot.eulerAngles} boneLag={lag:F2}m");
    }

    private static void Log(string message)
    {
        if (Plugin.Config != null && Plugin.Config.EnableVerboseLogging.Value)
            Plugin.Log.LogInfo($"[PeakVR][Grab] {message}");
    }

    public static bool TryGripOffset(Item item, out Vector3 dir, out Quaternion rot)
    {
        dir = default;
        rot = default;

        if (!Enabled || item == null)
            return false;

        if (gripItem != item)
        {
            if (!captured || item.itemID != capturedId || Time.time - capturedAt > CaptureTimeout)
                return false;

            gripItem = item;
            gripDir = capturedDir;
            gripRot = capturedRot;
            captured = false;

            Log($"bound '{item.name}' id={item.itemID} after {Time.time - capturedAt:F2}s");
        }

        dir = gripDir;
        rot = gripRot;
        return true;
    }

    private static bool Excluded(Item item)
    {
        if (item.UIData != null && item.UIData.isShootable)
            return true;

        if (item.GetComponentInChildren<Peak.Action_Antizooka>(true) != null)
            return true;

        if (item.GetComponentInChildren<Peak.Action_RaycastSpawnSomething>(true) != null)
            return true;

        return item.GetComponentInChildren<Action_ShowBinocularOverlay>(true) != null;
    }

    private static float Radius(Item item)
    {
        var found = false;
        var bounds = new Bounds();

        foreach (var col in item.GetComponentsInChildren<Collider>(true))
        {
            if (col == null || !col.enabled || col.isTrigger)
                continue;

            if (!found)
            {
                bounds = col.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(col.bounds);
            }
        }

        return found ? bounds.extents.magnitude : 0f;
    }

    private static float Distance(Item item, Vector3 point)
    {
        var best = float.MaxValue;

        foreach (var col in item.GetComponentsInChildren<Collider>(true))
        {
            if (col == null || !col.enabled || col.isTrigger)
                continue;

            var sqr = col is MeshCollider mesh && !mesh.convex
                ? col.bounds.SqrDistance(point)
                : (col.ClosestPoint(point) - point).sqrMagnitude;

            if (sqr < best)
                best = sqr;
        }

        return best == float.MaxValue ? float.MaxValue : Mathf.Sqrt(best);
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

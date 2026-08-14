using Photon.Pun;
using UnityEngine;

namespace PeakVR;

[DefaultExecutionOrder(1200)]
internal class VRLeftHand : MonoBehaviour
{
    private const int SampleCount = 16;
    private const int Baseline = 2;
    private const int DirectionBaseline = 3;
    private const float DirectionMinSpeed = 0.3f;
    private const float Window = 0.12f;
    private const float MaxThrowSpeed = 25f;
    private const float MinThrowSpeed = 0.5f;
    private const float OwnershipTimeout = 1.5f;
    private const float GrabRange = 0.3f;
    private const float MaxGripOffset = 0.35f;
    private const float RagdollControl = 0.01f;

    private static VRLeftHand instance;

    private readonly Vector3[] positions = new Vector3[SampleCount];
    private readonly float[] times = new float[SampleCount];
    private int head = -1;
    private int filled;

    private Item carried;
    private Item pending;
    private float pendingSince;
    private FixedJoint joint;
    private Vector3 gripDir;
    private Quaternion gripRot;
    private bool gripCaptured;

    public static Item Carried => instance != null ? instance.carried : null;

    public static bool? ActingHand { get; private set; }

    public static bool InteractAllowed { get; private set; } = true;

    private void OnEnable() => instance = this;

    private void OnDisable()
    {
        if (carried != null)
            Release(Vector3.zero);

        if (instance == this)
            instance = null;

        filled = 0;
        head = -1;
    }

    private void Update()
    {
        if (!VRGrab.LeftHandEnabled)
            return;

        var hand = VRHands.Left;
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
        InteractAllowed = true;

        var character = Character.localCharacter;

        if (!VRGrab.LeftHandEnabled || character == null || VRControls.LeftGrip == null)
        {
            if (carried != null)
                Release(Vector3.zero);
            ActingHand = null;
            return;
        }

        if (carried != null && Incapacitated(character))
        {
            VRGrab.LeftLog($"forced release of '{carried.name}'");
            Release(Vector3.zero);
        }

        if (pending != null)
        {
            if (pending == null || Time.time - pendingSince > OwnershipTimeout)
            {
                VRGrab.LeftLog("ownership request timed out");
                pending = null;
            }
            else if (pending.photonView.IsMine)
            {
                Attach(character, pending);
                pending = null;
            }
        }

        if (VRControls.LeftGrip.WasPressedThisFrame() && !TryGrab(character))
            ActingHand = false;

        if (VRControls.LeftGrip.WasReleasedThisFrame())
        {
            pending = null;

            if (carried != null)
                Release(ThrowVelocity(character));

            if (ActingHand == false)
                ActingHand = null;
        }

        if (VRControls.RightGrip != null)
        {
            if (VRControls.RightGrip.WasPressedThisFrame())
                ActingHand = true;
            else if (VRControls.RightGrip.WasReleasedThisFrame() && ActingHand == true)
                ActingHand = null;
        }

        InteractAllowed = carried == null && pending == null;
    }

    private static bool Incapacitated(Character character)
    {
        var data = character.data;
        if (data == null)
            return true;

        return data.dead || data.fullyPassedOut || data.passedOut
            || data.ragdollControlClamp <= RagdollControl;
    }

    private bool TryGrab(Character character)
    {
        if (carried != null || pending != null)
            return false;

        if (HandInteractPatch.LeftTarget is not Item target || target == null)
            return false;

        if (target.itemState != ItemState.Ground || !target.IsInteractible(character))
            return false;

        if (target == character.data.currentItem || target.photonView == null)
            return false;

        Capture(character, target);

        if (target.photonView.IsMine)
        {
            Attach(character, target);
            return true;
        }

        pending = target;
        pendingSince = Time.time;
        target.photonView.RequestOwnership();
        VRGrab.LeftLog($"requesting ownership of '{target.name}'");
        return true;
    }

    private void Capture(Character character, Item target)
    {
        gripCaptured = false;

        var bone = character.GetBodypartRig(BodypartType.Hand_L);
        if (bone == null)
            return;

        var inv = Quaternion.Inverse(target.transform.rotation);
        var dir = inv * (bone.transform.position - target.transform.position);
        var limit = Mathf.Max(MaxGripOffset, Radius(target) + GrabRange);

        var authored = target.transform.Find("Hand_L") ?? target.transform.Find("Hand_R");
        if (authored != null)
        {
            var authoredDir = inv * (authored.position - target.transform.position);
            dir = authoredDir + Vector3.ClampMagnitude(dir - authoredDir, limit);
        }

        gripDir = dir;
        gripRot = inv * bone.transform.rotation;
        gripCaptured = true;
    }

    private void Attach(Character character, Item item)
    {
        var bone = character.GetBodypartRig(BodypartType.Hand_L);
        if (bone == null || item == null || item.rig == null)
            return;

        var wantRot = item.transform.rotation;
        var wantPos = item.transform.position;

        if (gripCaptured)
        {
            wantRot = bone.transform.rotation * Quaternion.Inverse(gripRot);
            wantPos = bone.transform.position - wantRot * gripDir;
        }

        if (item.rig.isKinematic)
        {
            item.SetKinematicNetworked(false, wantPos, wantRot);
            VRGrab.LeftLog($"woke kinematic '{item.name}'");
        }

        item.transform.SetPositionAndRotation(wantPos, wantRot);

        item.rig.linearVelocity = Vector3.zero;
        item.rig.angularVelocity = Vector3.zero;
        item.rig.useGravity = false;

        joint = bone.gameObject.AddComponent<FixedJoint>();
        joint.connectedBody = item.rig;

        IgnoreBody(character, item, true);

        carried = item;
        VRLeftSlot.Fill(character, item);
        PlayGrabSound(character, item);
        VRGrab.LeftLog($"carrying '{item.name}' id={item.itemID}");
    }

    private static void PlayGrabSound(Character character, Item item)
    {
        if (item.TryGetComponent<ItemUseFeedback>(out var feedback) && feedback.equip != null
            && feedback.equip.Length > 0)
        {
            foreach (var sfx in feedback.equip)
                sfx?.Play(item.transform.position);
            return;
        }

        var audio = character.GetComponentInChildren<ItemAudioManager>(true);
        if (audio == null || audio.switchGeneric == null)
            return;

        foreach (var sfx in audio.switchGeneric)
            sfx?.Play(item.transform.position);
    }

    private void Release(Vector3 velocity)
    {
        var item = carried;
        carried = null;

        if (joint != null)
            Destroy(joint);
        joint = null;

        var character = Character.localCharacter;
        VRLeftSlot.Empty(character);

        if (item == null)
            return;

        IgnoreBody(character, item, false);

        if (item.rig != null)
        {
            item.rig.useGravity = true;
            if (!float.IsNaN(velocity.sqrMagnitude) && !float.IsInfinity(velocity.sqrMagnitude))
                item.rig.linearVelocity = velocity;
        }

        item.GetComponent<ItemPhysicsSyncer>()?.ForceSyncForFrames();
        VRGrab.LeftLog($"released '{item.name}' at {velocity.magnitude:F1}m/s");
    }

    private static void IgnoreBody(Character character, Item item, bool ignore)
    {
        var ragdoll = character != null && character.refs != null ? character.refs.ragdoll : null;
        if (ragdoll == null || ragdoll.colliderList == null || item == null)
            return;

        foreach (var a in item.GetComponentsInChildren<Collider>(true))
        {
            if (a == null)
                continue;

            foreach (var b in ragdoll.colliderList)
            {
                if (b == null)
                    continue;

                Physics.IgnoreCollision(a, b, ignore);
            }
        }
    }

    private Vector3 ThrowVelocity(Character character)
    {
        var hand = PeakVelocity();
        var speed = Mathf.Min(hand.magnitude * Plugin.Config.ThrowStrength.Value, MaxThrowSpeed);

        if (speed < MinThrowSpeed)
            speed = 0f;

        return hand.normalized * speed + character.data.avarageVelocity;
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
}

using Photon.Pun;
using UnityEngine;
using Zorro.Core;

namespace PeakVR;

internal class VRHandVelocity
{
    private const int SampleCount = 16;
    private const int Baseline = 2;
    private const int DirectionBaseline = 3;
    private const float DirectionMinSpeed = 0.3f;
    private const float Window = 0.12f;

    private readonly Vector3[] positions = new Vector3[SampleCount];
    private readonly float[] times = new float[SampleCount];
    private int head = -1;
    private int filled;

    public void Reset()
    {
        head = -1;
        filled = 0;
    }

    public void Add(Vector3 local, float time)
    {
        head = (head + 1) % SampleCount;
        positions[head] = local;
        times[head] = time;

        if (filled < SampleCount)
            filled++;
    }

    public Vector3 Peak(float now)
    {
        if (filled <= Baseline)
            return Vector3.zero;

        Vector3 best = Vector3.zero;
        float bestSqr = 0f;

        for (int i = 0; i + Baseline < filled; i++)
        {
            int newer = (head - i + SampleCount) % SampleCount;
            int older = (head - i - Baseline + SampleCount) % SampleCount;

            if (now - times[older] > Window)
                break;

            float dt = times[newer] - times[older];
            if (dt <= 0f)
                continue;

            Vector3 v = (positions[newer] - positions[older]) / dt;
            float sqr = v.sqrMagnitude;
            if (sqr <= bestSqr)
                continue;

            bestSqr = sqr;
            best = v;
        }

        Vector3 release = ReleaseDirection();
        if (release != Vector3.zero)
            best = release * best.magnitude;

        return best;
    }

    private Vector3 ReleaseDirection()
    {
        if (filled <= DirectionBaseline)
            return Vector3.zero;

        int older = (head - DirectionBaseline + SampleCount) % SampleCount;
        float dt = times[head] - times[older];
        if (dt <= 0f)
            return Vector3.zero;

        Vector3 v = (positions[head] - positions[older]) / dt;
        return v.magnitude < DirectionMinSpeed ? Vector3.zero : v.normalized;
    }
}

[DefaultExecutionOrder(1200)]
internal class VRHandItems : MonoBehaviour
{
    private const float MaxThrowSpeed = 25f;
    private const float FullChargeSpeed = 8f;
    private const float MinThrowSpeed = 0.5f;
    private const float ChargeMarker = 1f;
    private const float AnimateChargeAbove = 0.1f;
    private const float CaptureTimeout = 3f;
    private const float OwnershipTimeout = 1.5f;
    private const float ArrivalTimeout = 2f;
    private const float RagdollControl = 0.01f;
    private const float EquipCooldown = 0.25f;
    private const float AutoGrabTime = 1f;
    private const float RestoreDelay = 0.5f;
    private const int IgnoreRefreshFrames = 15;

    private static readonly System.Collections.Generic.List<Collider> ArmBuffer =
        new System.Collections.Generic.List<Collider>();

    private Item ignoredRight;
    private Item ignoredLeft;
    private int ignoreFrame;

    private static VRHandItems instance;

    private static bool captured;
    private static ushort capturedId;
    private static float capturedAt;
    private static Vector3 capturedDir;
    private static Quaternion capturedRot;

    private static Item gripItem;
    private static Vector3 gripDir;
    private static Quaternion gripRot;

    private static Item bypass;

    private readonly VRHandVelocity velocityLeft = new VRHandVelocity();
    private readonly VRHandVelocity velocityRight = new VRHandVelocity();

    private bool isRightGripped;
    private bool isRightActing;
    private bool isRightRequested;
    private int rightActingReleasedFrame = -1;
    private Item rightAtPress;
    private Item rightLast;
    private Item rightPending;
    private float rightPendingSince;

    private Item leftItem;
    private Item leftPending;
    private float leftPendingSince;
    private Joint leftJoint;
    private bool isLeftActing;
    private int leftActingReleasedFrame = -1;
    private bool isLeftDeferred;
    private float leftDeferredSince;
    private Item leftDeferredFrom;
    private Vector3 leftGripDir;
    private Quaternion leftGripRot;
    private bool isLeftGripCaptured;
    private bool isLeftSnapped;
    private float rightAutoLeft;
    private float leftAutoLeft;

    public static bool Enabled => Plugin.Config != null && Plugin.Config.ImmersiveHands.Value;

    public static bool SuppressStashDestroy { get; private set; }

    public static bool? ActingHand { get; private set; }

    public static Item LeftItem => instance != null ? instance.leftItem : null;

    public static bool IsLeftSnapped => instance != null && instance.isLeftSnapped;

    public static bool IsHolding(bool isLeft)
    {
        if (instance == null)
            return false;

        if (isLeft)
            return instance.leftItem != null || instance.leftPending != null;

        return instance.isRightGripped || instance.rightPending != null;
    }

    public static bool IsBusy(Character character, bool isLeft)
    {
        if (instance == null || !Enabled)
            return false;

        if (isLeft)
            return instance.leftItem != null || instance.leftPending != null;

        if (instance.rightPending != null)
            return true;

        return character != null && character.data != null && character.data.currentItem != null;
    }

    public static bool IsInteractInput(bool isLeft)
    {
        if (instance == null)
            return false;

        if (isLeft)
            return instance.isLeftActing || instance.leftActingReleasedFrame == Time.frameCount;

        return instance.isRightActing || instance.rightActingReleasedFrame == Time.frameCount;
    }

    public static bool IsGripHolding(Character character, Item item)
    {
        if (instance == null || item == null)
            return false;

        if (instance.leftItem == item)
            return true;

        return instance.isRightGripped && character != null && character.data != null
            && character.data.currentItem == item;
    }

    private void OnEnable() => instance = this;

    private void OnDisable()
    {
        if (leftItem != null)
            ReleaseLeft(Character.localCharacter, Vector3.zero);

        ClearRight();
        ClearLeftInput();

        if (instance == this)
            instance = null;

        velocityLeft.Reset();
        velocityRight.Reset();
    }

    private void Update()
    {
        if (!Enabled)
            return;

        float now = Time.time;
        if (VRHands.Right != null)
            velocityRight.Add(transform.InverseTransformPoint(VRHands.Right.position), now);
        if (VRHands.Left != null)
            velocityLeft.Add(transform.InverseTransformPoint(VRHands.Left.position), now);
    }

    public static void Step()
    {
        if (instance != null)
            instance.Tick();
    }

    private void Tick()
    {
        Character character = Character.localCharacter;

        if (!Enabled || character == null || character.data == null || VRControls.RightGrip == null
            || VRControls.LeftGrip == null)
        {
            if (leftItem != null)
                ReleaseLeft(character, Vector3.zero);
            ClearRight();
            ClearLeftInput();
            return;
        }

        if (captured && Time.time - capturedAt > CaptureTimeout)
            captured = false;

        TickRight(character);
        TickLeft(character);
        StepIgnore(character);
    }

    private void StepIgnore(Character character)
    {
        Item right = character.data.currentItem;
        Item left = leftItem;
        bool isRefresh = ++ignoreFrame % IgnoreRefreshFrames == 0;

        if (ignoredRight != right)
        {
            if (ignoredRight != left)
                RestoreArmCollisions(character, ignoredRight);
            ignoredRight = right;
            IgnoreArmCollisions(character, right);
        }
        else if (isRefresh)
        {
            IgnoreArmCollisions(character, right);
        }

        if (ignoredLeft != left)
        {
            if (ignoredLeft != right)
                RestoreArmCollisions(character, ignoredLeft);
            ignoredLeft = left;
            IgnoreArmCollisions(character, left);
        }
        else if (isRefresh)
        {
            IgnoreArmCollisions(character, left);
        }
    }

    private static void IgnoreArmCollisions(Character character, Item item)
    {
        if (item == null)
            return;

        foreach (Collider arm in ArmColliders(character))
        {
            foreach (Collider col in item.GetComponentsInChildren<Collider>(true))
            {
                if (arm != null && col != null)
                    Physics.IgnoreCollision(arm, col, true);
            }
        }
    }

    private static void RestoreArmCollisions(Character character, Item item)
    {
        if (item == null || GameUtils.instance == null)
            return;

        GameUtils.instance.IgnoreCollisions(ArmColliders(character).ToArray(),
            item.GetComponentsInChildren<Collider>(true), RestoreDelay);
    }

    private static System.Collections.Generic.List<Collider> ArmColliders(Character character)
    {
        ArmBuffer.Clear();
        if (character.refs != null && character.refs.ragdoll != null)
            ArmBuffer.AddRange(character.refs.ragdoll.colliderList);
        return ArmBuffer;
    }

    private void ClearRight()
    {
        isRightGripped = false;
        isRightActing = false;
        isRightRequested = false;
        rightPending = null;
        rightAtPress = null;
        if (ActingHand == true)
            ActingHand = isLeftActing ? false : null;
    }

    private void ClearLeftInput()
    {
        isLeftActing = false;
        isLeftDeferred = false;
        leftPending = null;
        if (ActingHand == false)
            ActingHand = isRightActing ? true : null;
    }

    private void TickRight(Character character)
    {
        Item held = character.data.currentItem;
        UnityEngine.InputSystem.InputAction grip = VRControls.RightGrip;

        if (held != rightLast)
        {
            bool isArrival = held != null && isRightRequested && grip.IsPressed() && held != rightAtPress;
            if (held != gripItem)
                gripItem = null;

            isRightGripped = isArrival;
            if (isArrival)
            {
                isRightRequested = false;
                Log($"right holds '{held.name}'");
            }

            rightLast = held;
        }

        StepRightPending(character);

        if (grip.WasPressedThisFrame())
        {
            rightAtPress = held;
            isRightRequested = false;
            isRightActing = false;
            rightAutoLeft = 0f;

            if (!VRHandClimb.IsConsumed(false))
                PressRight(character, held);
        }

        StepRightAuto(character, grip);

        if (grip.WasReleasedThisFrame())
        {
            rightPending = null;
            isRightRequested = false;
            rightAutoLeft = 0f;
            StopActingRight();

            if (isRightGripped)
            {
                isRightGripped = false;
                if (held != null)
                    ReleaseRight(character, held);
            }
        }
    }

    private void PressRight(Character character, Item held)
    {
        IInteractible target = HandInteractPatch.RightTarget;
        Item item = target as Item;

        if (held != null && !isRightGripped)
        {
            if (target != null && item == null)
            {
                ActRight();
                return;
            }

            isRightGripped = true;
            Log($"right grips equipped '{held.name}'");
            return;
        }

        if (item != null)
        {
            if (TryGrabRight(character, item))
                return;

            if (item is Backpack || item.isSecretlyOtherItemPrefab != null)
            {
                ActRight();
                isRightRequested = true;
            }
            return;
        }

        ActRight();
        rightAutoLeft = AutoGrabTime;
        if (target is FakeItem)
            isRightRequested = true;
    }

    private bool TryGrabRight(Character character, Item item)
    {
        if (item == leftItem)
        {
            GrabFromLeft(character, item);
            return true;
        }

        if (!IsGrabbable(item) || !CanPickUp(character, item))
            return false;

        RequestRight(character, item);
        return true;
    }

    private void StepRightAuto(Character character, UnityEngine.InputSystem.InputAction grip)
    {
        if (rightAutoLeft <= 0f)
            return;

        rightAutoLeft -= Time.deltaTime;

        if (!grip.IsPressed() || isRightGripped || rightPending != null || character.data.currentItem != null)
        {
            rightAutoLeft = 0f;
            return;
        }

        if (HandInteractPatch.RightTarget is not Item item || item == null || !IsNear(character, item, false))
            return;

        if (!TryGrabRight(character, item))
            return;

        rightAutoLeft = 0f;
        StopActingRight();
        Log($"right auto-grip '{item.name}'");
    }

    private void ActRight()
    {
        isRightActing = true;
        ActingHand = true;
    }

    private void StopActingRight()
    {
        if (!isRightActing)
            return;

        isRightActing = false;
        rightActingReleasedFrame = Time.frameCount;
        if (ActingHand == true)
            ActingHand = isLeftActing ? false : null;
    }

    public static bool IsGrabbable(IInteractible target)
    {
        return target is Item item && item != null && item is not Backpack && item.isSecretlyOtherItemPrefab == null;
    }

    private static bool IsNear(Character character, Item item, bool isLeft)
    {
        Rigidbody bone = character.GetBodypartRig(isLeft ? BodypartType.Hand_L : BodypartType.Hand_R);
        if (bone == null)
            return false;

        Vector3 hand = bone.transform.position;
        return Vector3.Distance(VRGripProfiles.ClosestPoint(item, hand), hand) <= VRGripProfiles.GrabRange;
    }

    private static bool CanPickUp(Character character, Item item)
    {
        if (item.photonView == null || item.itemState != ItemState.Ground || !item.IsInteractible(character))
            return false;

        if (character.player == null || !character.player.HasEmptySlot(item.itemID))
            return false;

        CharacterItems items = character.refs != null ? character.refs.items : null;
        return items != null && items.lastEquippedSlotTime + EquipCooldown <= Time.time;
    }

    private void RequestRight(Character character, Item item)
    {
        Capture(item);
        isRightRequested = true;

        if (item.photonView.IsMine)
        {
            Equip(character, item);
            return;
        }

        rightPending = item;
        rightPendingSince = Time.time;
        item.photonView.RequestOwnership();
        Log($"right requesting ownership of '{item.name}'");
    }

    private void StepRightPending(Character character)
    {
        if (rightPending == null)
        {
            rightPending = null;
            return;
        }

        if (rightPending.photonView.IsMine)
        {
            Item item = rightPending;
            rightPending = null;
            Equip(character, item);
            return;
        }

        if (Time.time - rightPendingSince <= OwnershipTimeout)
            return;

        Item fallback = rightPending;
        rightPending = null;
        Log($"right ownership of '{fallback.name}' timed out - vanilla pickup");
        bypass = fallback;
        fallback.Interact(character);
        bypass = null;
    }

    private void GrabFromLeft(Character character, Item item)
    {
        if (character.player == null || !character.player.HasEmptySlot(item.itemID))
            return;

        CaptureExact(character, item);
        isRightRequested = true;
        VRLeftSlot.Empty(character);
        Equip(character, item);
    }

    private static void Equip(Character character, Item item)
    {
        CharacterItems items = character.refs != null ? character.refs.items : null;
        if (items == null || character.player == null
            || !character.player.AddItem(item.itemID, item.data, out ItemSlot slot))
        {
            Log($"no slot for '{item.name}' - vanilla pickup");
            bypass = item;
            item.Interact(character);
            bypass = null;
            return;
        }

        items.currentSelectedSlot = Optionable<byte>.Some(slot.itemSlotID);
        items.lastSelectedSlot = items.currentSelectedSlot;
        items.lastEquippedSlotTime = Time.time;

        character.photonView.RPC("EquipSlotRpc", RpcTarget.All, (int)slot.itemSlotID, item.photonView.ViewID);
        character.refs.afflictions?.UpdateWeight();

        Log($"equipped '{item.name}' to slot {slot.itemSlotID}");
    }

    private void ReleaseRight(Character character, Item item)
    {
        if (item == leftItem)
        {
            MoveRightToLeft(character, item, true);
            return;
        }

        Throw(character, item);
    }

    private void TickLeft(Character character)
    {
        UnityEngine.InputSystem.InputAction grip = VRControls.LeftGrip;

        if (leftItem == null && !ReferenceEquals(leftItem, null))
            DropLeftState(character);

        if (leftItem != null && Incapacitated(character))
        {
            Log($"left forced release of '{leftItem.name}'");
            ReleaseLeft(character, Vector3.zero);
        }

        if (leftItem != null && leftItem.rig != null && leftItem.rig.isKinematic
            && leftItem.itemState == ItemState.Ground)
            leftItem.SetKinematic(false);

        StepLeftPending(character);
        StepLeftDeferred(character, grip);

        if (grip.WasPressedThisFrame())
        {
            isLeftActing = false;
            isLeftDeferred = false;
            leftAutoLeft = 0f;

            if (!VRHandClimb.IsConsumed(true) && !VRControllerHud.LeftPointing)
                PressLeft(character);
        }

        StepLeftAuto(character, grip);

        if (grip.WasReleasedThisFrame())
        {
            leftPending = null;
            isLeftDeferred = false;
            leftAutoLeft = 0f;
            StopActingLeft();

            if (leftItem != null)
                ReleaseLeft(character, ThrowVelocity(character, velocityLeft));
        }
    }

    private void PressLeft(Character character)
    {
        if (leftItem != null || leftPending != null)
            return;

        IInteractible target = HandInteractPatch.LeftTarget;
        Item held = character.data.currentItem;

        if (target is Item item && item != null)
        {
            if (TryGrabLeft(character, item))
                return;

            if (item is Backpack)
            {
                ActLeft();
            }
            else if (item.isSecretlyOtherItemPrefab != null)
            {
                ActLeft();
                Defer(held);
            }
            return;
        }

        ActLeft();
        leftAutoLeft = AutoGrabTime;
        if (target is FakeItem)
            Defer(held);
    }

    private bool TryGrabLeft(Character character, Item item)
    {
        if (leftItem != null || leftPending != null)
            return false;

        if (item == character.data.currentItem)
        {
            if (!isRightGripped && !VRControls.RightGrip.IsPressed() && IsGrabbable(item))
                MoveRightToLeft(character, item, false);
            else
                AttachLeft(character, item, true);
            return true;
        }

        if (!IsGrabbable(item) || item.photonView == null || item.itemState != ItemState.Ground
            || !item.IsInteractible(character))
            return false;

        CaptureLeft(character, item);

        if (item.photonView.IsMine)
        {
            AttachLeft(character, item, false);
            return true;
        }

        leftPending = item;
        leftPendingSince = Time.time;
        item.photonView.RequestOwnership();
        Log($"left requesting ownership of '{item.name}'");
        return true;
    }

    private void StepLeftAuto(Character character, UnityEngine.InputSystem.InputAction grip)
    {
        if (leftAutoLeft <= 0f)
            return;

        leftAutoLeft -= Time.deltaTime;

        if (!grip.IsPressed() || leftItem != null || leftPending != null || isLeftDeferred)
        {
            leftAutoLeft = 0f;
            return;
        }

        if (HandInteractPatch.LeftTarget is not Item item || item == null || !IsNear(character, item, true))
            return;

        if (!TryGrabLeft(character, item))
            return;

        leftAutoLeft = 0f;
        StopActingLeft();
        Log($"left auto-grip '{item.name}'");
    }

    private void ActLeft()
    {
        isLeftActing = true;
        ActingHand = false;
    }

    private void StopActingLeft()
    {
        if (!isLeftActing)
            return;

        isLeftActing = false;
        leftActingReleasedFrame = Time.frameCount;
        if (ActingHand == false)
            ActingHand = isRightActing ? true : null;
    }

    private void Defer(Item held)
    {
        isLeftDeferred = true;
        leftDeferredSince = Time.time;
        leftDeferredFrom = held;
    }

    private void StepLeftPending(Character character)
    {
        if (leftPending == null)
        {
            leftPending = null;
            return;
        }

        if (Time.time - leftPendingSince > OwnershipTimeout)
        {
            Log($"left ownership of '{leftPending.name}' timed out");
            leftPending = null;
            return;
        }

        if (!leftPending.photonView.IsMine)
            return;

        Item item = leftPending;
        leftPending = null;

        if (item.itemState == ItemState.Ground)
            AttachLeft(character, item, false);
    }

    private void StepLeftDeferred(Character character, UnityEngine.InputSystem.InputAction grip)
    {
        if (!isLeftDeferred)
            return;

        if (!grip.IsPressed() || leftItem != null || Time.time - leftDeferredSince > ArrivalTimeout)
        {
            isLeftDeferred = false;
            return;
        }

        Item held = character.data.currentItem;
        if (held == null || held == leftDeferredFrom)
            return;

        isLeftDeferred = false;
        Log($"moving picked '{held.name}' to the left hand");
        MoveRightToLeft(character, held, false);
    }

    private static bool Incapacitated(Character character)
    {
        CharacterData data = character.data;
        if (data == null)
            return true;

        return data.dead || data.fullyPassedOut || data.passedOut
            || data.ragdollControlClamp <= RagdollControl;
    }

    private void CaptureLeft(Character character, Item item)
    {
        isLeftGripCaptured = false;

        Rigidbody bone = character.GetBodypartRig(BodypartType.Hand_L);
        if (bone == null)
            return;

        string mode = VRGripProfiles.Capture(item, true, bone.transform.position, bone.transform.rotation,
            out leftGripDir, out leftGripRot);
        isLeftGripCaptured = true;
        Log($"left captured '{item.name}' {mode} offset={leftGripDir.magnitude:F2}m");
    }

    private void AttachLeft(Character character, Item item, bool isSecond)
    {
        Rigidbody bone = character.GetBodypartRig(BodypartType.Hand_L);
        if (bone == null || item == null || item.rig == null)
            return;

        if (!isSecond)
        {
            Quaternion wantRot = item.transform.rotation;
            Vector3 wantPos = item.transform.position;

            if (isLeftGripCaptured)
            {
                wantRot = bone.transform.rotation * Quaternion.Inverse(leftGripRot);
                wantPos = bone.transform.position - wantRot * leftGripDir;
            }

            if (item.rig.isKinematic)
                item.SetKinematicNetworked(false, wantPos, wantRot);

            item.transform.SetPositionAndRotation(wantPos, wantRot);
            item.rig.position = wantPos;
            item.rig.rotation = wantRot;
            item.rig.linearVelocity = Vector3.zero;
            item.rig.angularVelocity = Vector3.zero;
            item.rig.useGravity = false;
        }

        bool isSnapped = false;
        if (VRGripProfiles.TryGetRotationOffset(item, true, out Quaternion handOffset))
        {
            Quaternion rotated = bone.transform.rotation * handOffset;
            bone.transform.rotation = rotated;
            bone.rotation = rotated;
            isSnapped = true;
        }

        if (leftJoint != null)
            Destroy(leftJoint);
        leftJoint = VRHandJoint.AttachRigid(bone, item);

        leftItem = item;
        isLeftSnapped = isSnapped;
        isLeftGripCaptured = false;

        if (!isSecond)
            VRLeftSlot.Fill(character, item);

        PlayGrabSound(character, item);
        Log($"left holds '{item.name}'{(isSecond ? " with the right hand" : "")}");
    }

    private void RestoreLeftHold(Character character, Item item)
    {
        Rigidbody bone = character.GetBodypartRig(BodypartType.Hand_L);
        if (bone == null || item.rig == null)
            return;

        if (leftJoint != null)
            Destroy(leftJoint);

        leftJoint = VRHandJoint.AttachRigid(bone, item);
        VRLeftSlot.Fill(character, item);
    }

    private void DropLeftState(Character character)
    {
        leftItem = null;
        isLeftSnapped = false;
        if (leftJoint != null)
            Destroy(leftJoint);
        leftJoint = null;
        VRLeftSlot.Empty(character);
    }

    private void ReleaseLeft(Character character, Vector3 velocity)
    {
        Item item = leftItem;
        DropLeftState(character);

        if (item == null)
            return;

        if (character != null && character.data != null && character.data.currentItem == item)
        {
            RestoreRightHold(character, item);
            Log($"left let go of '{item.name}', right keeps it");
            return;
        }

        if (GameUtils.instance != null && character != null)
            GameUtils.instance.IgnoreCollisions(character, item, 0.5f);

        if (item.rig != null)
        {
            item.rig.useGravity = true;
            if (!float.IsNaN(velocity.sqrMagnitude) && !float.IsInfinity(velocity.sqrMagnitude))
                item.rig.linearVelocity = velocity;
        }

        item.GetComponent<ItemPhysicsSyncer>()?.ForceSyncForFrames();
        Log($"left released '{item.name}' at {velocity.magnitude:F1}m/s");
    }

    private void MoveRightToLeft(Character character, Item item, bool isKeepPose)
    {
        CharacterItems items = character.refs != null ? character.refs.items : null;
        if (items == null || !items.currentSelectedSlot.IsSome)
            return;

        byte slotId = items.currentSelectedSlot.Value;

        if (!isKeepPose)
            CaptureLeft(character, item);

        SuppressStashDestroy = true;
        character.photonView.RPC("EquipSlotRpc", RpcTarget.All, -1, -1);
        SuppressStashDestroy = false;

        character.player.EmptySlot(Optionable<byte>.Some(slotId));
        item.SetState(ItemState.Ground);

        if (item.rig != null)
            item.rig.useGravity = false;

        if (isKeepPose && leftItem == item)
            RestoreLeftHold(character, item);
        else
            AttachLeft(character, item, false);

        character.refs.afflictions?.UpdateWeight();
        Log($"moved '{item.name}' from the right to the left hand");
    }

    private static void RestoreRightHold(Character character, Item item)
    {
        Rigidbody hand = character.GetBodypartRig(BodypartType.Hand_R);
        if (hand == null || item == null || item.rig == null)
            return;

        foreach (Joint existing in hand.gameObject.GetComponents<Joint>())
        {
            if (existing.connectedBody == item.rig)
                Destroy(existing);
        }

        VRHandJoint.AttachRigid(hand, item);
    }

    private static void PlayGrabSound(Character character, Item item)
    {
        if (item.TryGetComponent<ItemUseFeedback>(out ItemUseFeedback feedback) && feedback.equip != null
            && feedback.equip.Length > 0)
        {
            foreach (SFX_Instance sfx in feedback.equip)
                sfx?.Play(item.transform.position);
            return;
        }

        ItemAudioManager audio = character.GetComponentInChildren<ItemAudioManager>(true);
        if (audio == null || audio.switchGeneric == null)
            return;

        foreach (SFX_Instance sfx in audio.switchGeneric)
            sfx?.Play(item.transform.position);
    }

    private Vector3 ThrowVelocity(Character character, VRHandVelocity velocity)
    {
        Vector3 hand = transform.TransformVector(velocity.Peak(Time.time));
        float speed = Mathf.Min(hand.magnitude * Plugin.Config.ThrowStrength.Value, MaxThrowSpeed);

        if (speed < MinThrowSpeed)
            speed = 0f;

        return hand.normalized * speed + character.data.avarageVelocity;
    }

    private void Throw(Character character, Item item)
    {
        if (item == null || item.UIData == null || !item.UIData.canDrop)
            return;

        CharacterItems items = character.refs != null ? character.refs.items : null;
        if (items == null || !items.currentSelectedSlot.IsSome)
            return;

        ItemSlot slot = character.player != null
            ? character.player.GetItemSlot(items.currentSelectedSlot.Value)
            : null;
        if (slot == null)
            return;

        Vector3 hand = transform.TransformVector(velocityRight.Peak(Time.time));
        float speed = Mathf.Min(hand.magnitude * Plugin.Config.ThrowStrength.Value, MaxThrowSpeed);
        float charge = speed < MinThrowSpeed ? 0f : Mathf.Clamp01(speed / FullChargeSpeed);

        StickyItemComponent sticky = character.data.currentStickyItem;
        if (sticky != null && charge < sticky.throwChargeRequirement)
        {
            Animate(character);
            return;
        }

        Vector3 velocity = hand.normalized * speed + character.data.avarageVelocity;
        if (item is Backpack)
            velocity = Vector3.zero;

        if (charge > AnimateChargeAbove)
            Animate(character);

        items.throwChargeLevel = 0f;

        if (item.photonView.IsMine)
        {
            ThrowLive(character, items, item, velocity, charge);
            return;
        }

        character.photonView.RPC("DropItemRpc", RpcTarget.All, -(charge + ChargeMarker),
            items.currentSelectedSlot.Value, item.transform.position, velocity,
            item.transform.rotation, slot.data, false);

        items.EquipSlot(Optionable<byte>.None);
    }

    private static void ThrowLive(Character character, CharacterItems items, Item item, Vector3 velocity, float charge)
    {
        byte slotId = items.currentSelectedSlot.Value;

        SuppressStashDestroy = true;
        character.photonView.RPC("EquipSlotRpc", RpcTarget.All, -1, -1);
        SuppressStashDestroy = false;

        character.player.EmptySlot(Optionable<byte>.Some(slotId));
        item.SetState(ItemState.Ground);

        if (item.rig != null)
        {
            item.rig.linearVelocity = velocity;
            item.rig.angularVelocity = Vector3.zero;
        }

        item.photonView.RPC("RPC_SetThrownData", RpcTarget.All, character.photonView.ViewID, charge);
        item.GetComponent<ItemPhysicsSyncer>()?.ForceSyncForFrames();

        if (GameUtils.instance != null)
            GameUtils.instance.IgnoreCollisions(character, item, 0.5f);

        character.refs.afflictions?.UpdateWeight();
        Log($"threw '{item.name}' at {velocity.magnitude:F1}m/s charge={charge:F2}");
    }

    private static void Animate(Character character)
    {
        CharacterAnimations animations = character.refs != null ? character.refs.animations : null;
        if (animations != null)
            animations.throwTime = 0.125f;
    }

    public static bool RunVanillaInteract(Character interactor, Item item)
    {
        if (instance == null || !Enabled || item == null || bypass == item)
            return true;

        if (!IsGrabbable(item))
            return true;

        if (ActingHand == false)
        {
            instance.TryGrabLeft(interactor, item);
            return false;
        }

        if (!CanPickUp(interactor, item))
            return true;

        instance.RequestRight(interactor, item);
        return false;
    }

    private static void Capture(Item target)
    {
        Character character = Character.localCharacter;
        Rigidbody bone = character != null ? character.GetBodypartRig(BodypartType.Hand_R) : null;
        if (bone == null)
            return;

        string mode = VRGripProfiles.Capture(target, false, bone.transform.position, bone.transform.rotation,
            out Vector3 dir, out Quaternion rot);
        SetCaptured(target, dir, rot);
        Log($"right captured '{target.name}' {mode} offset={dir.magnitude:F2}m");
    }

    private static void CaptureExact(Character character, Item target)
    {
        Rigidbody bone = character.GetBodypartRig(BodypartType.Hand_R);
        if (bone == null)
            return;

        Quaternion inv = Quaternion.Inverse(target.transform.rotation);
        SetCaptured(target, inv * (bone.transform.position - target.transform.position), inv * bone.transform.rotation);
    }

    private static void SetCaptured(Item target, Vector3 dir, Quaternion rot)
    {
        capturedDir = dir;
        capturedRot = rot;
        capturedId = target.itemID;
        capturedAt = Time.time;
        captured = true;
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
        }

        dir = gripDir;
        rot = gripRot;
        return true;
    }

    private static void Log(string message)
    {
        if (Plugin.Config != null && Plugin.Config.EnableVerboseLogging.Value)
            Plugin.Log.LogInfo($"[PeakVR][Hands] {message}");
    }
}

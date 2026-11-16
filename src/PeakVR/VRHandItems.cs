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
    private const byte WornSlot = 3;

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
    private bool isRightSliding;
    private bool isRightSlideRequested;
    private bool isLeftSliding;
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
    private bool isLeftPickExpected;
    private float leftPickSince;
    private Item leftPickFrom;
    private Optionable<byte> leftPickSlot = Optionable<byte>.None;
    private bool isLeftPickReequip;
    private bool isLeftPickFromHeld;
    private bool isLeftPickRightGripped;
    private int leftPickItemId = -1;
    private bool isLeftTakeExpected;
    private float leftTakeSince;
    private int leftTakeItemId = -1;
    private bool isRightPackExpected;
    private float rightPackSince;

    public static bool Enabled => Plugin.Config != null && Plugin.Config.ImmersiveHands.Value;

    public static bool SuppressStashDestroy { get; private set; }

    public static bool? ActingHand { get; private set; }

    public static Item LeftItem => instance != null ? instance.leftItem : null;

    public static bool IsLeftSnapped => instance != null && instance.isLeftSnapped;

    public static void SetBypass(Item item)
    {
        bypass = item;
    }

    public static void ExpectLeftPick(Character character)
    {
        ExpectLeftPick(character, -1);
    }

    public static void ExpectLeftPick(Character character, int itemId)
    {
        if (instance == null || !Enabled || character == null || character.data == null
            || instance.leftItem != null || instance.leftPending != null)
            return;

        instance.isLeftPickExpected = true;
        instance.leftPickItemId = itemId;
        instance.leftPickSince = Time.time;
        instance.leftPickFrom = character.data.currentItem;
        instance.isLeftPickFromHeld = character.data.currentItem != null;
        instance.isLeftPickRightGripped = instance.isRightGripped;
        instance.leftPickSlot = character.refs.items.currentSelectedSlot;
        instance.isLeftPickReequip = false;
    }

    public static void ExpectLeftTake(Character character, Item item)
    {
        if (instance == null || !Enabled || character == null || item == null
            || instance.leftItem != null || instance.leftPending != null)
            return;

        instance.isLeftTakeExpected = true;
        instance.leftTakeSince = Time.time;
        instance.leftTakeItemId = item.isSecretlyOtherItemPrefab != null ? item.isSecretlyOtherItemPrefab.itemID : item.itemID;
    }

    public static bool TryCompleteLeftTake(CharacterItems items, byte slotID)
    {
        if (instance == null || !instance.isLeftTakeExpected)
            return false;

        instance.isLeftTakeExpected = false;
        Character character = Character.localCharacter;
        if (character == null || items.character != character || character.player == null || slotID == WornSlot
            || Time.time - instance.leftTakeSince > ArrivalTimeout || instance.leftItem != null
            || instance.leftPending != null)
            return false;

        ItemSlot slot = character.player.GetItemSlot(slotID);
        if (slot == null || slot.IsEmpty() || slot.prefab == null || slot.prefab.itemID != instance.leftTakeItemId)
            return false;

        return instance.SpawnLeft(character, items, slotID, slot);
    }

    private bool SpawnLeft(Character character, CharacterItems items, byte slotID, ItemSlot slot)
    {
        Rigidbody bone = character.GetBodypartRig(BodypartType.Hand_L);
        if (bone == null)
            return false;

        GameObject spawned = PhotonNetwork.Instantiate("0_Items/" + slot.GetPrefabName(), bone.position, bone.rotation, 0);
        Item item = spawned != null ? spawned.GetComponent<Item>() : null;
        if (item == null || item.photonView == null)
            return false;

        item.photonView.RPC("SetItemInstanceDataRPC", RpcTarget.All, slot.data);
        character.player.EmptySlot(Optionable<byte>.Some(slotID));

        Vector3 offset = item.Center() - item.transform.position;
        Vector3 position = bone.position - offset;
        item.transform.position = position;
        if (item.rig != null)
            item.rig.position = position;

        isLeftGripCaptured = false;
        AttachLeft(character, item, false);
        character.refs.afflictions?.UpdateWeight();
        items.RefreshAllCharacterCarryWeight();
        Log($"left took '{item.name}' straight from slot {slotID}");
        return true;
    }

    public static bool IsLeftPickItem(Item item)
    {
        if (instance == null || item == null || !instance.isLeftPickExpected || item == instance.leftPickFrom
            || Time.time - instance.leftPickSince > ArrivalTimeout)
            return false;

        return instance.leftPickItemId < 0 || item.itemID == instance.leftPickItemId;
    }

    private void StepLeftPick(Character character)
    {
        if (isLeftPickReequip)
        {
            isLeftPickReequip = false;
            CharacterItems items = character.refs.items;
            if (leftPickSlot.IsSome && character.data.currentItem == null
                && !character.player.GetItemSlot(leftPickSlot.Value).IsEmpty())
            {
                items.EquipSlot(leftPickSlot);
                isRightRequested = isLeftPickRightGripped && VRControls.RightGrip.IsPressed();
            }
            leftPickSlot = Optionable<byte>.None;
            return;
        }

        if (!isLeftPickExpected)
            return;

        if (leftItem != null || leftPending != null || Time.time - leftPickSince > ArrivalTimeout)
        {
            isLeftPickExpected = false;
            return;
        }

        Item held = character.data.currentItem;
        if (held == null || held == leftPickFrom || held.itemState != ItemState.Held
            || (leftPickItemId >= 0 && held.itemID != leftPickItemId))
            return;

        isLeftPickExpected = false;
        Log($"moving the expected pick '{held.name}' to the left hand");
        MoveRightToLeft(character, held, false);
        isLeftPickReequip = isLeftPickFromHeld && leftPickSlot.IsSome;
    }

    public static bool IsHolding(bool isLeft)
    {
        if (instance == null)
            return false;

        if (isLeft)
            return instance.leftItem != null || instance.leftPending != null;

        return instance.isRightGripped || instance.isRightSliding || instance.rightPending != null;
    }

    public static bool IsSliding(bool isLeft)
    {
        if (instance == null)
            return false;

        return isLeft ? instance.isLeftSliding : instance.isRightSliding;
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

        if (instance.leftItem == item && !instance.isLeftSliding)
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
            VRBackpackHands.StepCollisions(null);
            return;
        }

        if (captured && Time.time - capturedAt > CaptureTimeout)
            captured = false;

        TickRight(character);
        TickLeft(character);
        VRBackpackHands.StepCollisions(character);
        StepJoints(character);
        StepIgnore(character);
    }

    private void StepJoints(Character character)
    {
        Item held = character.data.currentItem;
        if (held != null)
            VRHandJoint.Ensure(character.GetBodypartRig(BodypartType.Hand_R), held, isRightSliding);

        if (leftItem == null)
            return;

        Joint joint = VRHandJoint.Ensure(character.GetBodypartRig(BodypartType.Hand_L), leftItem, isLeftSliding);
        if (joint != null)
            leftJoint = joint;
    }

    private static bool IsTriggerBlocked(bool isLeft)
    {
        if (isLeft)
            return VRControllerHud.LeftTriggerConsumed || VRControllerHud.LeftPointing;

        return VREmoteWheel.RightTriggerConsumed || VRChatButton.RightTriggerConsumed;
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
        isRightSliding = false;
        isRightSlideRequested = false;
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
        isLeftSliding = false;
        leftPending = null;
        if (ActingHand == false)
            ActingHand = isRightActing ? true : null;
    }

    private void TickRight(Character character)
    {
        Item held = character.data.currentItem;
        UnityEngine.InputSystem.InputAction grip = VRControls.RightGrip;
        UnityEngine.InputSystem.InputAction trigger = VRControls.RightTrigger;

        if (held != rightLast)
        {
            bool isArrival = held != null && isRightRequested && grip.IsPressed() && held != rightAtPress;
            bool isSlideArrival = held != null && isRightSlideRequested && trigger != null && trigger.IsPressed()
                && !grip.IsPressed();
            if (held != gripItem)
                gripItem = null;

            isRightGripped = isArrival;
            isRightSliding = isSlideArrival && !isArrival;
            if (isArrival)
            {
                isRightRequested = false;
                Log($"right holds '{held.name}'");
            }

            if (isRightSliding)
            {
                isRightRequested = false;
                isRightSlideRequested = false;
                Log($"right slide-holds '{held.name}'");
            }

            rightLast = held;
        }

        StepRightPending(character);
        StepRightPack(character);

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
            if (!isRightSlideRequested)
                rightPending = null;
            isRightRequested = false;
            rightAutoLeft = 0f;
            StopActingRight();

            if (isRightGripped)
            {
                isRightGripped = false;
                if (held != null && IsSlideKept(false, held))
                {
                    isRightSliding = true;
                    Log($"right slides '{held.name}'");
                }
                else if (held != null && !VRBackpackHands.TryWearRight(character, held)
                    && !VRBackpackHands.TryStashRight(character, held))
                {
                    ReleaseRight(character, held);
                }
            }
        }

        if (trigger == null)
            return;

        if (trigger.WasPressedThisFrame() && !grip.IsPressed() && !IsTriggerBlocked(false))
            PressRightTrigger(character, held);

        if (trigger.WasReleasedThisFrame())
        {
            if (isRightSlideRequested)
            {
                isRightSlideRequested = false;
                isRightRequested = false;
                rightPending = null;
            }

            if (isRightSliding && !grip.IsPressed())
            {
                isRightSliding = false;
                if (held != null)
                    ReleaseRight(character, held);
            }
        }
    }

    private bool IsSlideKept(bool isLeft, Item item)
    {
        UnityEngine.InputSystem.InputAction trigger = isLeft ? VRControls.LeftTrigger : VRControls.RightTrigger;
        return IsTriggerGrab && trigger != null && trigger.IsPressed() && !IsTriggerBlocked(isLeft)
            && VRGripProfiles.CanSlide(item);
    }

    private static bool IsTriggerGrab => Plugin.Config != null && Plugin.Config.TriggerGrab.Value;

    private void PressRightTrigger(Character character, Item held)
    {
        if (!IsTriggerGrab || held != null || rightPending != null || VRHandClimb.IsConsumed(false))
            return;

        if (HandInteractPatch.RightTarget is not Item item || item == null || !IsGrabbable(item))
            return;

        if (!TryGrabRight(character, item))
            return;

        isRightSlideRequested = true;
        rightAtPress = null;
        Log($"right trigger grab '{item.name}'");
    }

    private void PressRight(Character character, Item held)
    {
        IInteractible target = HandInteractPatch.RightTarget;
        Item item = target as Item;

        if (held != null && isRightSliding)
        {
            isRightSliding = false;
            isRightGripped = true;
            Log($"right locks '{held.name}'");
            return;
        }

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

        if (held == null && rightPending == null && VRBackpackHands.TryTakeOff(character, true))
        {
            isRightRequested = true;
            return;
        }

        if (item != null)
        {
            if (held == null && VRBackpackHands.TryTake(character, item, false))
            {
                isRightRequested = true;
                return;
            }

            if (TryGrabRight(character, item))
                return;

            if (item.isSecretlyOtherItemPrefab != null)
            {
                ActRight();
                isRightRequested = true;
            }
            else if (item is Backpack)
            {
                Log($"right can't hold '{item.name}': already wearing a pack");
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
        return target is Item item && item != null && item.isSecretlyOtherItemPrefab == null;
    }

    public static bool HasNearTarget(Character character, bool isLeft)
    {
        if (instance == null || !Enabled || character == null || character.data == null || IsBusy(character, isLeft)
            || !HandInteractPatch.IsRecent)
            return false;

        IInteractible target = isLeft ? HandInteractPatch.LeftTarget : HandInteractPatch.RightTarget;
        if (VRBackpackHands.IsPackTarget(target))
            return true;

        return target is Item item && item != null && IsGrabbable(item);
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
        PickUpVanilla(character, fallback);
    }

    private static void PickUpVanilla(Character character, Item item)
    {
        if (item is Backpack && instance != null)
        {
            VRBackpackHands.Wear(character, item);
            instance.isRightPackExpected = true;
            instance.rightPackSince = Time.time;
            return;
        }

        bypass = item;
        item.Interact(character);
        bypass = null;
    }

    private void StepRightPack(Character character)
    {
        if (!isRightPackExpected)
            return;

        if (Time.time - rightPackSince > ArrivalTimeout || character.data.currentItem != null)
        {
            isRightPackExpected = false;
            return;
        }

        if (character.player == null || character.player.backpackSlot.IsEmpty())
            return;

        isRightPackExpected = false;
        character.refs.items.EquipSlot(Optionable<byte>.Some(WornSlot));
        Log("equipping the picked pack into the right hand");
    }

    private void GrabFromLeft(Character character, Item item)
    {
        if (character.player == null || !character.player.HasEmptySlot(item.itemID))
            return;

        if (item is not Backpack && VRGripProfiles.TryGetLine(item, out _, out _))
            Capture(item);
        else
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
            PickUpVanilla(character, item);
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

        if (leftItem != null && !leftItem.gameObject.activeInHierarchy && leftItem.consuming)
            FinishLeftConsume(character);

        if ((leftItem == null && !ReferenceEquals(leftItem, null))
            || (leftItem != null && !leftItem.gameObject.activeInHierarchy))
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
        StepLeftPick(character);

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
            if (!isLeftSliding)
                leftPending = null;
            isLeftDeferred = false;
            leftAutoLeft = 0f;
            StopActingLeft();

            if (leftItem != null && !isLeftSliding && IsSlideKept(true, leftItem))
            {
                isLeftSliding = true;
                Log($"left slides '{leftItem.name}'");
            }
            else if (leftItem != null && !isLeftSliding && VRBackpackHands.CanWearLeft(character, leftItem))
            {
                Item worn = leftItem;
                ReleaseLeft(character, Vector3.zero);
                VRBackpackHands.Wear(character, worn);
            }
            else if (leftItem != null && !isLeftSliding
                && VRBackpackHands.TryGetLeftStash(character, leftItem, out BackpackSlotTarget stash))
            {
                Item stashed = leftItem;
                ReleaseLeft(character, Vector3.zero);
                VRBackpackHands.StashLeft(character, stashed, stash);
            }
            else if (leftItem != null && !isLeftSliding)
            {
                ReleaseLeft(character, ThrowVelocity(character, velocityLeft));
            }
        }

        UnityEngine.InputSystem.InputAction trigger = VRControls.LeftTrigger;
        if (trigger == null)
            return;

        if (trigger.WasPressedThisFrame() && !grip.IsPressed() && !IsTriggerBlocked(true))
            PressLeftTrigger(character);

        if (trigger.WasReleasedThisFrame() && isLeftSliding && !grip.IsPressed())
        {
            isLeftSliding = false;
            leftPending = null;
            if (leftItem != null)
                ReleaseLeft(character, ThrowVelocity(character, velocityLeft));
        }
    }

    private void PressLeftTrigger(Character character)
    {
        if (!IsTriggerGrab || leftItem != null || leftPending != null || VRHandClimb.IsConsumed(true))
            return;

        if (HandInteractPatch.LeftTarget is not Item item || item == null || !IsGrabbable(item))
            return;

        isLeftSliding = true;
        if (!TryGrabLeft(character, item))
        {
            isLeftSliding = false;
            return;
        }

        if (leftItem == null && leftPending == null)
            isLeftSliding = false;
        else
            Log($"left trigger grab '{item.name}'");
    }

    private void PressLeft(Character character)
    {
        if (leftItem != null && isLeftSliding)
        {
            isLeftSliding = false;
            Log($"left locks '{leftItem.name}'");
            return;
        }

        if (leftItem != null || leftPending != null)
            return;

        if (VRBackpackHands.TryTakeOff(character, false))
            return;

        IInteractible target = HandInteractPatch.LeftTarget;
        Item held = character.data.currentItem;

        if (target is Item item && item != null)
        {
            if (VRBackpackHands.TryTake(character, item, true))
                return;

            if (TryGrabLeft(character, item))
                return;

            if (item.isSecretlyOtherItemPrefab != null)
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
            if (!isRightGripped && !isRightSliding && !VRControls.RightGrip.IsPressed() && IsGrabbable(item))
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
            if (leftItem == null)
                isLeftSliding = false;
            return;
        }

        if (!leftPending.photonView.IsMine)
            return;

        Item item = leftPending;
        leftPending = null;

        if (item.itemState == ItemState.Ground)
            AttachLeft(character, item, false);

        if (leftItem == null)
            isLeftSliding = false;
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

        Transform handle = item is Backpack ? item.transform.Find("Hand_L") : null;
        if (handle != null)
        {
            Quaternion inv = Quaternion.Inverse(item.transform.rotation);
            leftGripDir = inv * (handle.position - item.transform.position);
            leftGripRot = inv * handle.rotation;
            isLeftGripCaptured = true;
            Log($"left takes '{item.name}' by its handle");
            return;
        }

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

        if (isSecond && VRGripProfiles.TryGetLine(item, out _, out _))
        {
            VRGripProfiles.Capture(item, true, bone.transform.position, bone.transform.rotation,
                out Vector3 lineDir, out Quaternion lineRot);
            Vector3 handPos = item.rig.position + item.rig.rotation * lineDir;
            Quaternion handRot = item.rig.rotation * lineRot;
            bone.transform.SetPositionAndRotation(handPos, handRot);
            bone.position = handPos;
            bone.rotation = handRot;
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
        isLeftSliding = false;
        if (leftJoint != null)
            Destroy(leftJoint);
        leftJoint = null;
        VRLeftSlot.Empty(character);
    }

    private void FinishLeftConsume(Character character)
    {
        Item item = leftItem;
        DropLeftState(character);

        Action_ConsumeAndSpawn spawn = item.GetComponent<Action_ConsumeAndSpawn>();
        if (spawn != null && spawn.itemToSpawn != null)
            ExpectLeftPick(character, spawn.itemToSpawn.itemID);

        bool isDestroy = item.itemState == ItemState.Ground && item.photonView != null && item.photonView.IsMine;
        Log($"left consumed '{item.name}' destroy={isDestroy} leftover={(spawn != null && spawn.itemToSpawn != null ? spawn.itemToSpawn.name : "none")}");
        if (isDestroy)
            PhotonNetwork.Destroy(item.photonView);
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

        VRItemUse.ClearRopeHold(item);
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
        if (item.TryGetComponent<BackPackAudio>(out BackPackAudio packAudio) && packAudio.holdSFX != null
            && packAudio.holdSFX.Length > 0)
        {
            foreach (SFX_Instance sfx in packAudio.holdSFX)
                sfx?.Play(item.transform.position);
            return;
        }

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
        VRItemUse.ClearRopeHold(item);

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

        if (item is Backpack || !IsGrabbable(item))
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

        if (target is Backpack)
        {
            captured = false;
            Log($"right takes '{target.name}' by its handle");
            return;
        }

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

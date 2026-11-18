using System.Collections.Generic;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace PeakVR;

internal static class VRNetworking
{
    private const byte EventCode = 199;
    private const byte ExitCode = 198;
    private const byte GroundCode = 197;
    private const byte GhostCode = 196;
    public const float StaleTime = 1f;

    private static bool registered;
    private static GameObject receiver;

    private static readonly RaiseEventOptions SendOptionsToOthers = new() { Receivers = ReceiverGroup.Others };

    public struct RemotePose
    {
        public float headRoll;
        public Vector3 leftPos;
        public Quaternion leftRot;
        public Vector3 rightPos;
        public Quaternion rightRot;
        public Vector3 leftHint;
        public Vector3 rightHint;
        public Vector3 leftShoulder;
        public Vector3 rightShoulder;
        public bool hasShoulders;
        public bool hasHands;
        public int leftItemView;
        public int rightItemView;
        public byte[] climbState;
        public Vector3 legInto;
        public Vector3 ropeOffset;
        public float sinceReceived;
    }

    public static readonly Dictionary<int, RemotePose> Remotes = new();

    public static void CreateReceiver()
    {
        if (receiver != null)
            return;

        receiver = new GameObject("PeakVR NetReceiver");
        Object.DontDestroyOnLoad(receiver);
        receiver.AddComponent<VRNetReceiver>();
        Plugin.Log.LogInfo("[PeakVR][Net] receiver created (runs on all clients)");
    }

    public static bool EnsureRegistered()
    {
        if (registered)
            return true;
        if (PhotonNetwork.NetworkingClient == null)
            return false;

        PhotonNetwork.NetworkingClient.EventReceived += OnEvent;
        registered = true;
        Plugin.Log.LogInfo("[PeakVR][Net] event handler registered");
        return true;
    }

    public static void SendPose(Character c)
    {
        if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom.PlayerCount < 2)
            return;
        var refs = c.refs;
        if (refs.IKHandTargetLeft == null || refs.IKHandTargetRight == null)
            return;
        if (c.data.fullyPassedOut)
            return;
        if (refs.ikLeft == null || refs.ikRight == null
            || refs.ikLeft.data.hint == null || refs.ikRight.data.hint == null)
            return;

        if (refs.ikLeft.data.root == null || refs.ikRight.data.root == null)
            return;
        if (!VRArmIKPatch.HasTargets)
            return;

        Transform root = c.transform;
        Quaternion invRoot = Quaternion.Inverse(root.rotation);

        Vector3 lp = root.InverseTransformPoint(VRArmIKPatch.LastTargetLeft);
        Quaternion lr = invRoot * VRArmIKPatch.LastRotLeft;
        Vector3 rp = root.InverseTransformPoint(VRArmIKPatch.LastTargetRight);
        Quaternion rr = invRoot * VRArmIKPatch.LastRotRight;
        Vector3 lh = root.InverseTransformPoint(VRArmIKPatch.LastHintLeft);
        Vector3 rh = root.InverseTransformPoint(VRArmIKPatch.LastHintRight);

        Vector3 ls = root.InverseTransformPoint(VRArmIKPatch.LastShoulderLeft);
        Vector3 rs = root.InverseTransformPoint(VRArmIKPatch.LastShoulderRight);

        VRClimbPose.TryGetNetState(c, out byte[] climbState, out Vector3 legInto, out Vector3 ropeOffset);
        object[] content = { VRHeadRoll.LocalRoll, lp, lr, rp, rr, lh, rh, ls, rs,
            GetViewID(VRHandItems.LeftItem), GetViewID(VRHandItems.RightLoose),
            climbState ?? new byte[0], legInto, ropeOffset };
        PhotonNetwork.RaiseEvent(EventCode, content, SendOptionsToOthers, SendOptions.SendUnreliable);
    }

    public static void SendGhost(Character c)
    {
        if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom.PlayerCount < 2)
            return;
        if (!VRGhostSync.TryGetSend(c, out Vector3 offset, out Quaternion rotation))
            return;

        object[] content = { offset, rotation };
        PhotonNetwork.RaiseEvent(GhostCode, content, SendOptionsToOthers, SendOptions.SendUnreliable);
    }

    public static void SendVrOff()
    {
        if (!PhotonNetwork.InRoom)
            return;

        PhotonNetwork.RaiseEvent(ExitCode, null, SendOptionsToOthers, SendOptions.SendReliable);
        Plugin.Log.LogInfo("[PeakVR][Net] told the lobby this player left VR");
    }

    public static void SendGround(Item item)
    {
        if (!PhotonNetwork.InRoom || item == null || item.photonView == null)
            return;

        PhotonNetwork.RaiseEvent(GroundCode, item.photonView.ViewID, SendOptionsToOthers, SendOptions.SendReliable);
    }

    private static void ReceiveGround(int view)
    {
        PhotonView photonView = PhotonNetwork.GetPhotonView(view);
        Item item = photonView != null ? photonView.GetComponent<Item>() : null;
        if (item == null || item.itemState != ItemState.Held)
            return;

        Character holder = item.holderCharacter;
        if (holder != null && holder != Character.localCharacter && holder.data != null && holder.data.currentItem == item
            && holder.refs != null && holder.refs.items != null)
            holder.refs.items.EquipSlotRpc(-1, -1);

        if (item.itemState != ItemState.Held)
            return;

        item.SetState(ItemState.Ground);
        Plugin.Log.LogInfo($"[PeakVR][Net] '{item.name}' left a remote VR hand slot - now a ground item");
    }

    public static bool IsHeldByRemote(Item item)
    {
        if (Remotes.Count == 0 || item == null || item.photonView == null)
            return false;

        int view = item.photonView.ViewID;
        if (view == 0)
            return false;

        foreach (KeyValuePair<int, RemotePose> remote in Remotes)
        {
            RemotePose pose = remote.Value;
            if (pose.sinceReceived <= StaleTime && (pose.leftItemView == view || pose.rightItemView == view))
                return true;
        }

        return false;
    }

    private static int GetViewID(Item item)
    {
        return item != null && item.photonView != null ? item.photonView.ViewID : 0;
    }

    public static bool IsActiveRemote(Character c)
    {
        if (c == null || c.photonView == null)
            return false;
        return Remotes.TryGetValue(c.photonView.OwnerActorNr, out var p)
            && p.hasHands && p.sinceReceived <= StaleTime;
    }

    public static bool TryGetClimbPose(Character c, out byte[] state, out Vector3 legInto, out Vector3 ropeOffset)
    {
        state = null;
        legInto = Vector3.zero;
        ropeOffset = Vector3.zero;
        if (c == null || c.photonView == null || !Remotes.TryGetValue(c.photonView.OwnerActorNr, out RemotePose pose))
            return false;
        if (!pose.hasHands || pose.sinceReceived > StaleTime || pose.climbState == null)
            return false;

        state = pose.climbState;
        legInto = pose.legInto;
        ropeOffset = pose.ropeOffset;
        return true;
    }

    private static void OnEvent(EventData e)
    {
        if (e.Code == ExitCode)
        {
            Remotes.Remove(e.Sender);
            VRGhostSync.Drop(e.Sender);
            Plugin.Log.LogInfo($"[PeakVR][Net] actor {e.Sender} left VR — dropping their pose");
            VRNetReceiver.Drop(e.Sender);
            return;
        }

        if (e.Code == GroundCode)
        {
            if (e.CustomData is int groundView)
                ReceiveGround(groundView);
            return;
        }

        if (e.Code == GhostCode)
        {
            if (e.CustomData is object[] ghost && ghost.Length >= 2)
                VRGhostSync.Receive(e.Sender, (Vector3)ghost[0], (Quaternion)ghost[1]);
            return;
        }

        if (e.Code != EventCode)
            return;

        object[] content = (object[])e.CustomData;
        RemotePose pose = new RemotePose
        {
            headRoll = (float)content[0],
            leftPos = (Vector3)content[1],
            leftRot = (Quaternion)content[2],
            rightPos = (Vector3)content[3],
            rightRot = (Quaternion)content[4],
            leftHint = (Vector3)content[5],
            rightHint = (Vector3)content[6],
            leftShoulder = content.Length >= 9 ? (Vector3)content[7] : Vector3.zero,
            rightShoulder = content.Length >= 9 ? (Vector3)content[8] : Vector3.zero,
            hasShoulders = content.Length >= 9,
            hasHands = true,
            leftItemView = content.Length >= 11 ? (int)content[9] : 0,
            rightItemView = content.Length >= 11 ? (int)content[10] : 0,
            climbState = content.Length >= 14 ? content[11] as byte[] : null,
            legInto = content.Length >= 14 ? (Vector3)content[12] : Vector3.zero,
            ropeOffset = content.Length >= 14 ? (Vector3)content[13] : Vector3.zero,
            sinceReceived = 0f,
        };
        Remotes[e.Sender] = pose;
    }
}

[HarmonyLib.HarmonyPatch(typeof(Item), nameof(Item.IsInteractible))]
internal static class RemoteHeldItemPatch
{
    [HarmonyLib.HarmonyPostfix]
    private static void Postfix(Item __instance, ref bool __result)
    {
        if (__result && VRNetworking.IsHeldByRemote(__instance))
            __result = false;
    }
}

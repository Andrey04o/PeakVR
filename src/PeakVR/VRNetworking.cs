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

        object[] content = { VRHeadRoll.LocalRoll, lp, lr, rp, rr, lh, rh, ls, rs,
            GetViewID(VRHandItems.LeftItem), GetViewID(VRHandItems.RightLoose) };
        PhotonNetwork.RaiseEvent(EventCode, content, SendOptionsToOthers, SendOptions.SendUnreliable);
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

    private static void OnEvent(EventData e)
    {
        if (e.Code == ExitCode)
        {
            Remotes.Remove(e.Sender);
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

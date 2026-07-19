using System.Collections.Generic;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace PeakVR;

internal static class VRNetworking
{
    private const byte EventCode = 199;
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
        public bool hasHands;
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
        if (c.refs.IKHandTargetLeft == null || c.refs.IKHandTargetRight == null)
            return;
        if (c.data.fullyPassedOut)
            return;

        var root = c.transform;

        var lp = root.InverseTransformPoint(c.refs.IKHandTargetLeft.position);
        var lr = Quaternion.Inverse(root.rotation) * c.refs.IKHandTargetLeft.rotation;
        var rp = root.InverseTransformPoint(c.refs.IKHandTargetRight.position);
        var rr = Quaternion.Inverse(root.rotation) * c.refs.IKHandTargetRight.rotation;

        var content = new object[] { VRHeadRoll.LocalRoll, lp, lr, rp, rr };
        PhotonNetwork.RaiseEvent(EventCode, content, SendOptionsToOthers, SendOptions.SendUnreliable);
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
        if (e.Code != EventCode)
            return;

        var content = (object[])e.CustomData;
        var pose = new RemotePose
        {
            headRoll = (float)content[0],
            leftPos = (Vector3)content[1],
            leftRot = (Quaternion)content[2],
            rightPos = (Vector3)content[3],
            rightRot = (Quaternion)content[4],
            hasHands = true,
            sinceReceived = 0f,
        };
        Remotes[e.Sender] = pose;
    }
}

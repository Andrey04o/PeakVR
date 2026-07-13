using System.Collections.Generic;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace PeakVR;

internal static class VRNetworking
{
    private const byte EventCode = 199;

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

        GetFrame(c, out var origin, out var frameRot);
        var inv = Quaternion.Inverse(frameRot);

        var lp = inv * (c.refs.IKHandTargetLeft.position - origin);
        var lr = inv * c.refs.IKHandTargetLeft.rotation;
        var rp = inv * (c.refs.IKHandTargetRight.position - origin);
        var rr = inv * c.refs.IKHandTargetRight.rotation;

        var content = new object[] { VRHeadRoll.LocalRoll, lp, lr, rp, rr };
        PhotonNetwork.RaiseEvent(EventCode, content, SendOptionsToOthers, SendOptions.SendUnreliable);
    }

    public static void GetFrame(Character c, out Vector3 origin, out Quaternion rot)
    {
        origin = c.refs.head.transform.position;

        var look = c.data.lookDirection;
        look.y = 0f;
        rot = look.sqrMagnitude < 1e-4f
            ? Quaternion.identity
            : Quaternion.LookRotation(look.normalized, Vector3.up);
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

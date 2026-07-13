using UnityEngine;

namespace PeakVR;

internal class VRNetSync : MonoBehaviour
{
    private const float SendInterval = 1f / 20f;

    private float sinceSend;

    private void Update()
    {
        sinceSend += Time.deltaTime;
        if (sinceSend < SendInterval)
            return;

        sinceSend = 0f;

        var c = Character.localCharacter;
        if (c == null || c.refs == null || c.refs.head == null
            || c.refs.IKHandTargetLeft == null || c.refs.IKHandTargetRight == null)
            return;

        VRNetworking.SendPose(c);
    }
}

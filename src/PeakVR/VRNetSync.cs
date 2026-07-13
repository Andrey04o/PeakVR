using UnityEngine;

namespace PeakVR;

internal class VRNetSync : MonoBehaviour
{
    private const float SendInterval = 1f / 15f;

    private float sinceSend;

    private void Update()
    {
        sinceSend += Time.deltaTime;
        if (sinceSend < SendInterval)
            return;

        sinceSend = 0f;
        VRNetworking.SendHeadRoll(VRHeadRoll.LocalRoll);
    }
}
